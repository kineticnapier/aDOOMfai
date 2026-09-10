using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;

namespace ADOOMFAIAccelerator;

internal static class NativeDoomHost
{
    private const int Width = 320;
    private const int Height = 200;
    private const int FrameBytes = Width * Height * 4;

    // DOOM key constants from doomkeys.h. These are game input codes, not OS scan codes.
    private const byte KeyRight = 0xae;
    private const byte KeyLeft = 0xac;
    private const byte KeyUp = 0xad;
    private const byte KeyDown = 0xaf;
    private const byte KeyUse = 0xa2;
    private const byte KeyAction = 0xa3;
    private const byte KeyShift = 0xb6;
    private const byte KeyEscape = 27;
    private const byte KeyEnter = 13;
    private const byte KeyTab = 9;

    private static Main? _main;
    private static SpriteRenderer? _renderer;
    private static Texture2D? _texture;
    private static Sprite? _sprite;
    private static byte[]? _managedFrame;

    private static bool _enabled;
    private static bool _attached;
    private static bool _nativeLoaded;
    private static bool _running;
    private static bool _startAttempted;
    private static bool _fatalDisabled;
    private static string? _wadPath;
    private static long _lastFrameId = -1;
    private static float _retryTimer;
    private static float _perfTimer;
    private static int _uploadedFrames;
    private static double _uploadMs;

    private static readonly Dictionary<byte, bool> KeyStates = new();

    internal static void Enable(Main main)
    {
        _main = main;
        _enabled = true;
        _fatalDisabled = false;
        _startAttempted = false;
        _retryTimer = 0f;
        _perfTimer = 0f;
    }

    internal static void Disable()
    {
        if (_running)
        {
            try { ReleaseAllKeys(); } catch { }
        }

        _enabled = false;
        _attached = false;
        _running = false;
        _startAttempted = false;
        _wadPath = null;
        _renderer = null;
        _lastFrameId = -1;
        KeyStates.Clear();

        if (_sprite != null) UnityEngine.Object.Destroy(_sprite);
        if (_texture != null) UnityEngine.Object.Destroy(_texture);
        _sprite = null;
        _texture = null;
        _managedFrame = null;
    }

    internal static bool Attach(object target)
    {
        SpriteRenderer? sr = ResolveSpriteRenderer(target);
        if (sr == null)
        {
            _main?.Warn("DOOM host: adoom_doom_framebuffer has no SpriteRenderer.");
            return true; // consume the PACL2 command anyway
        }

        _renderer = sr;
        _attached = true;
        EnsureTexture(sr);
        _main?.Info("DOOM host attached to adoom_doom_framebuffer.");
        TryStart();
        return true;
    }

    internal static void Tick(float deltaTime)
    {
        if (!_enabled || !_attached || _fatalDisabled) return;

        if (!_running)
        {
            _retryTimer += deltaTime;
            if (_retryTimer >= 1f)
            {
                _retryTimer = 0f;
                TryStart();
            }
            return;
        }

        try
        {
            PollInput();

            // doomgeneric_Tick() is designed to be called repeatedly; its own timer
            // keeps the original 35 Hz game tic rate.
            Native.ADOOM_Tick();

            long id = Native.ADOOM_GetFrameId();
            if (id != _lastFrameId)
            {
                UploadFrame(id);
            }

            _perfTimer += deltaTime;
            if (_perfTimer >= 2f)
            {
                _perfTimer = 0f;
                if (_uploadedFrames > 0)
                {
                    _main?.Info($"[doom] frames={_uploadedFrames}, upload={_uploadMs:F1}ms ({_uploadMs / _uploadedFrames:F2}ms/frame), wad={Path.GetFileName(_wadPath)}");
                }
                _uploadedFrames = 0;
                _uploadMs = 0;
            }
        }
        catch (Exception e)
        {
            _fatalDisabled = true;
            _main?.Warn("DOOM host disabled after runtime failure.");
            _main?.Fail(e);
        }
    }

    private static void TryStart()
    {
        if (!_enabled || !_attached || _running || _fatalDisabled) return;

        if (!EnsureNativeLoaded())
            return;

        string? wad = FindIwad();
        if (wad == null)
        {
            if (!_startAttempted)
            {
                _startAttempted = true;
                _main?.Warn("DOOM host: no IWAD found. Put a user-owned IWAD or Freedoom WAD in Mods/ADOOMFAIAccelerator/iwad/, or set ADOOMFAI_IWAD.");
            }
            return;
        }

        _wadPath = wad;
        _startAttempted = true;

        try
        {
            int ok = Native.ADOOM_Init(wad);
            if (ok == 0)
            {
                _main?.Warn("DOOM host: native initialization returned failure.");
                return;
            }

            _running = true;
            _lastFrameId = -1;
            _main?.Info($"DOOM native engine started: {Path.GetFileName(wad)} ({Width}x{Height}).");
        }
        catch (Exception e)
        {
            _fatalDisabled = true;
            _main?.Warn("DOOM host: native initialization failed.");
            _main?.Fail(e);
        }
    }

    private static bool EnsureNativeLoaded()
    {
        if (_nativeLoaded) return true;
        if (_main == null) return false;

        string dll = Path.Combine(_main.Path, "adoom_native.dll");
        if (!File.Exists(dll))
        {
            if (!_startAttempted)
                _main.Warn("DOOM host: adoom_native.dll not found next to the mod DLL.");
            return false;
        }

        IntPtr h = Native.LoadLibraryW(dll);
        if (h == IntPtr.Zero)
        {
            int err = Marshal.GetLastWin32Error();
            _main.Warn($"DOOM host: LoadLibrary failed for adoom_native.dll (Win32 error {err}).");
            return false;
        }

        _nativeLoaded = true;
        _main.Info("DOOM host: adoom_native.dll loaded.");
        return true;
    }

    private static string? FindIwad()
    {
        string? env = Environment.GetEnvironmentVariable("ADOOMFAI_IWAD");
        if (!string.IsNullOrWhiteSpace(env) && File.Exists(env))
            return Path.GetFullPath(env);

        if (_main == null) return null;

        string dir = Path.Combine(_main.Path, "iwad");
        Directory.CreateDirectory(dir);

        // Prefer libre data if present, then common user-owned IWAD names.
        string[] preferred =
        {
            "freedoom1.wad",
            "freedoom2.wad",
            "doom1.wad",
            "doom.wad",
            "doom2.wad",
            "plutonia.wad",
            "tnt.wad"
        };

        foreach (string name in preferred)
        {
            string p = Path.Combine(dir, name);
            if (File.Exists(p)) return p;
        }

        string[] any = Directory.GetFiles(dir, "*.wad");
        return any.Length > 0 ? any[0] : null;
    }

    private static void PollInput()
    {
        // Logical movement keys. WASD mirrors arrows.
        SetLogical(KeyUp, Input.GetKey(KeyCode.UpArrow) || Input.GetKey(KeyCode.W));
        SetLogical(KeyDown, Input.GetKey(KeyCode.DownArrow) || Input.GetKey(KeyCode.S));
        SetLogical(KeyLeft, Input.GetKey(KeyCode.LeftArrow) || Input.GetKey(KeyCode.A));
        SetLogical(KeyRight, Input.GetKey(KeyCode.RightArrow) || Input.GetKey(KeyCode.D));

        SetLogical(KeyUse, Input.GetKey(KeyCode.Space));
        SetLogical(KeyAction, Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl));
        SetLogical(KeyShift, Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift));
        SetLogical(KeyEscape, Input.GetKey(KeyCode.Escape));
        SetLogical(KeyEnter, Input.GetKey(KeyCode.Return) || Input.GetKey(KeyCode.KeypadEnter));
        SetLogical(KeyTab, Input.GetKey(KeyCode.Tab));

        // Number keys are ASCII in DOOM's input layer.
        for (int n = 1; n <= 7; n++)
        {
            KeyCode kc = (KeyCode)((int)KeyCode.Alpha0 + n);
            SetLogical((byte)('0' + n), Input.GetKey(kc));
        }
    }

    private static void SetLogical(byte doomKey, bool down)
    {
        KeyStates.TryGetValue(doomKey, out bool old);
        if (old == down) return;
        KeyStates[doomKey] = down;
        Native.ADOOM_KeyEvent(down ? 1 : 0, doomKey);
    }

    private static void ReleaseAllKeys()
    {
        foreach (KeyValuePair<byte, bool> kv in KeyStates)
        {
            if (kv.Value) Native.ADOOM_KeyEvent(0, kv.Key);
        }
        KeyStates.Clear();
    }

    private static void UploadFrame(long id)
    {
        if (_texture == null || _renderer == null) return;

        IntPtr ptr = Native.ADOOM_GetFrameRGBA();
        if (ptr == IntPtr.Zero) return;

        _managedFrame ??= new byte[FrameBytes];

        var sw = System.Diagnostics.Stopwatch.StartNew();
        Marshal.Copy(ptr, _managedFrame, 0, FrameBytes);
        _texture.LoadRawTextureData(_managedFrame);
        _texture.Apply(false, false);
        sw.Stop();

        _lastFrameId = id;
        _uploadedFrames++;
        _uploadMs += sw.Elapsed.TotalMilliseconds;
    }

    private static void EnsureTexture(SpriteRenderer sr)
    {
        if (_texture != null && _sprite != null)
        {
            sr.sprite = _sprite;
            sr.color = Color.white;
            return;
        }

        _texture = new Texture2D(Width, Height, TextureFormat.RGBA32, false)
        {
            name = "ADOOMFAI.DoomGeneric320x200",
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave
        };
        _sprite = Sprite.Create(_texture, new Rect(0, 0, Width, Height), new Vector2(0.5f, 0.5f), 100f);
        _sprite.name = "ADOOMFAI.DoomGenericSprite";
        _sprite.hideFlags = HideFlags.HideAndDontSave;
        sr.sprite = _sprite;
        sr.color = Color.white;
    }

    private static SpriteRenderer? ResolveSpriteRenderer(object target)
    {
        if (target is Component c)
            return c.GetComponent<SpriteRenderer>() ?? c.GetComponentInChildren<SpriteRenderer>(true);
        if (target is GameObject go)
            return go.GetComponent<SpriteRenderer>() ?? go.GetComponentInChildren<SpriteRenderer>(true);
        return null;
    }

    private static class Native
    {
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        internal static extern IntPtr LoadLibraryW(string lpFileName);

        [DllImport("adoom_native.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        internal static extern int ADOOM_Init(string iwadPath);

        [DllImport("adoom_native.dll", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int ADOOM_Tick();

        [DllImport("adoom_native.dll", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void ADOOM_KeyEvent(int pressed, byte doomKey);

        [DllImport("adoom_native.dll", CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr ADOOM_GetFrameRGBA();

        [DllImport("adoom_native.dll", CallingConvention = CallingConvention.Cdecl)]
        internal static extern long ADOOM_GetFrameId();
    }
}
