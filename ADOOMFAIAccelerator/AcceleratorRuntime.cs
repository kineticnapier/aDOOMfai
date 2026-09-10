using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace ADOOMFAIAccelerator;

internal static class AcceleratorRuntime
{
    private const string HarmonyId = "kineticnapier.ADOOMFAIAccelerator";
    private static Harmony? _harmony;
    private static Main? _main;
    private static Assembly? _pacl2;
    private static Type? _moveEffectType;
    private static MethodInfo? _startEffectOnceClosed;
    private static GameObject? _workerObject;
    private static Component? _worker;
    private static bool _fastPathAvailable;
    private static bool _fastPathDisabledAfterError;
    private static int _fastPathErrors;
    private static bool _enabled;
    private static bool _initialized;
    private static bool _initializationFailed;
    private static bool _waitingForPacl2Logged;
    private static float _initializeRetryTimer;
    private static float _logTimer;

    // Cached hot-path metadata/delegates. Nothing below is discovered per pixel.
    private static int _durationArgIndex = -1;
    private static int _setupArgIndex = -1;
    private static Action<object>? _resetUsed;
    private static Func<object, int>? _getUsedMask;
    private static Func<object, object?>? _getTags;
    private static Func<object, object?>? _getScale;
    private static Func<object, object?>? _getOpacity;
    private static Func<object, object?>? _getRotation;
    private static Func<object, object?>? _getWorkerManager;
    private static readonly Dictionary<Type, Action<Delegate, object>> SetupInvokerCache = new();
    private static readonly Dictionary<string, TargetEntry> TargetCache = new(StringComparer.Ordinal);

    // v0.3 framebuffer batch path. One PACL2 MoveDecorations call becomes one Texture2D upload.
    private const string FramebufferTag = "adoom_framebuffer";
    private const string DoomFramebufferTag = "adoom_doom_framebuffer";
    private const int FrameWidth = 320;
    private const int FrameHeight = 200;
    private const float CameraPlane = 0.7002075382f; // tan(35 deg), FOV ~= 70 deg
    // Tuned to preserve the v13 projection while scaling resolution.
    private const float WallProjectionPerHeight = 61f / 96f;
    private const float SurfaceDepthPerHeight = 44.6f / 96f;
    private static Texture2D? _frameTexture;
    private static Sprite? _frameSprite;
    private static SpriteRenderer? _frameRenderer;
    private static Color32[]? _framePixels;
    private static float[]? _rowDepths;
    private static long _batchFrames;
    private static long _framebufferDispatches;
    private static long _batchPixels;
    private static long _batchTicks;
    private static object? _cachedManager;
    private static MethodInfo? _getTaggedDecorationsMethod;

    // cheap counters; PACL2 executes this path on Unity's main thread
    private static long _fastHits;
    private static long _fastFallbacks;
    private static long _fastDecorations;
    private static long _fastTicks;

    internal static void Enable(Main main)
    {
        _main = main;
        _enabled = true;
        _initialized = false;
        _initializationFailed = false;
        _waitingForPacl2Logged = false;
        _initializeRetryTimer = 0f;
        _fastPathDisabledAfterError = false;
        _fastPathErrors = 0;
        _harmony = new Harmony(HarmonyId);
        NativeDoomHost.Enable(main);
        TryInitialize();
    }

    private static void TryInitialize()
    {
        if (!_enabled || _initialized || _initializationFailed) return;
        _pacl2 = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => string.Equals(a.GetName().Name, "PACL2", StringComparison.OrdinalIgnoreCase));

        if (_pacl2 == null)
        {
            if (!_waitingForPacl2Logged)
            {
                _main?.Info("PACL2 runtime is not loaded yet; waiting for JALib to initialize it...");
                _waitingForPacl2Logged = true;
            }
            return;
        }

        try
        {
            _main?.Info($"PACL2 runtime detected: {_pacl2.GetName().Name} {_pacl2.GetName().Version}");
            SetupFastMoveDecorationsPatch();
            _initialized = true;
            _main?.Info($"Accelerator v0.5 initialized. batchFramebuffer={_fastPathAvailable}, hotMove0={_fastPathAvailable}");
        }
        catch (Exception e)
        {
            _initializationFailed = true;
            _main?.Warn("Accelerator initialization failed; leaving PACL2 unmodified for this session.");
            _main?.Fail(e);
        }
    }

    internal static void Disable()
    {
        _enabled = false;
        _initialized = false;
        _initializationFailed = false;
        try { _harmony?.UnpatchAll(HarmonyId); } catch { }
        _harmony = null;
        _pacl2 = null;
        _moveEffectType = null;
        _startEffectOnceClosed = null;
        _fastPathAvailable = false;
        TargetCache.Clear();
        SetupInvokerCache.Clear();
        _cachedManager = null;
        _getTaggedDecorationsMethod = null;
        DestroyFramebufferResources();
        NativeDoomHost.Disable();
        if (_workerObject != null)
        {
            UnityEngine.Object.Destroy(_workerObject);
            _workerObject = null;
            _worker = null;
        }
    }

    internal static void Tick(float deltaTime)
    {
        NativeDoomHost.Tick(deltaTime);
        if (_enabled && !_initialized && !_initializationFailed)
        {
            _initializeRetryTimer += deltaTime;
            if (_initializeRetryTimer >= 0.25f)
            {
                _initializeRetryTimer = 0f;
                TryInitialize();
            }
        }

        _logTimer += deltaTime;
        if (_logTimer < 2f) return;
        _logTimer = 0f;
        if (_fastHits == 0 && _fastFallbacks == 0) return;

        double ms = _fastTicks * 1000.0 / Stopwatch.Frequency;
        double us = _fastHits == 0 ? 0 : ms * 1000.0 / _fastHits;
        double batchMs = _batchTicks * 1000.0 / Stopwatch.Frequency;
        double batchPer = _batchFrames == 0 ? 0 : batchMs / _batchFrames;
        _main?.Info($"[perf3] fbDispatch={_framebufferDispatches}, batchFrames={_batchFrames}, batchPixels={_batchPixels}, batch={batchMs:F1}ms ({batchPer:F2}ms/frame), hotMove0={_fastHits}, fallback={_fastFallbacks}, directDecorations={_fastDecorations}, hotPath={ms:F1}ms ({us:F1}us/call)");
        _fastHits = _fastFallbacks = _fastDecorations = _fastTicks = 0;
        _framebufferDispatches = 0;
        _batchFrames = _batchPixels = _batchTicks = 0;
    }

    private static Type? FindType(string simpleName)
    {
        if (_pacl2 == null) return null;
        Type[] types;
        try { types = _pacl2.GetTypes(); }
        catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).Cast<Type>().ToArray(); }
        return types.FirstOrDefault(t => t.Name == simpleName);
    }

    private static void SetupFastMoveDecorationsPatch()
    {
        if (_pacl2 == null || _harmony == null) return;
        _moveEffectType = AppDomain.CurrentDomain.GetAssemblies().SelectMany(SafeGetTypes)
            .FirstOrDefault(t => t.Name == "ffxMoveDecorationsPlus");
        Type? programMethodType = FindType("ProgramMethod");
        if (_moveEffectType == null || programMethodType == null)
        {
            _main?.Warn("Hot path: ffxMoveDecorationsPlus or ProgramMethod not found.");
            return;
        }

        MethodInfo? generic = programMethodType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Where(m => m.Name == "StartEffectOnce" && m.IsGenericMethodDefinition)
            .FirstOrDefault(m => m.GetGenericArguments().Length == 1 && m.ReturnType == typeof(void));
        if (generic == null)
        {
            _main?.Warn("Hot path: StartEffectOnce<T> void method not found.");
            return;
        }

        _startEffectOnceClosed = generic.MakeGenericMethod(_moveEffectType);
        ParameterInfo[] ps = _startEffectOnceClosed.GetParameters();
        _durationArgIndex = Array.FindIndex(ps, p => p.Name?.IndexOf("duration", StringComparison.OrdinalIgnoreCase) >= 0);
        _setupArgIndex = Array.FindIndex(ps, p => typeof(Delegate).IsAssignableFrom(p.ParameterType));
        if (_durationArgIndex < 0 || _setupArgIndex < 0)
            throw new MissingMethodException("Could not identify StartEffectOnce duration/setup parameters.");

        BuildWorkerAccessors();
        _harmony.Patch(_startEffectOnceClosed,
            prefix: new HarmonyMethod(typeof(AcceleratorRuntime), nameof(StartEffectOnceHotPrefix)) { priority = Priority.First });
        _fastPathAvailable = true;
        _main?.Info("Hot path patched: StartEffectOnce<ffxMoveDecorationsPlus>");
        _main?.Info("v0.5 adds adoom_doom_framebuffer: PACL2 attaches once, then doomgeneric runs natively and streams 320x200 RGBA frames.");
    }

    // false => skip PACL2 AddComponent + StartEffect. The Run0 parser still executes; v0.3 will target that boundary.
    private static bool StartEffectOnceHotPrefix(object[] __args)
    {
        long started = Stopwatch.GetTimestamp();
        if (!_fastPathAvailable || _fastPathDisabledAfterError || _moveEffectType == null)
            return true;

        try
        {
            if (__args.Length <= Math.Max(_durationArgIndex, _setupArgIndex)) return Fallback(started);
            if (!TryDouble(__args[_durationArgIndex], out double duration) || Math.Abs(duration) > 1e-9)
                return Fallback(started);
            if (__args[_setupArgIndex] is not Delegate setup)
                return Fallback(started);

            Component worker = GetWorker();
            _resetUsed!(worker);
            GetSetupInvoker(setup.GetType())(setup, worker);

            int mask = _getUsedMask!(worker);
            // bit 1 = scale, bit 2 = opacity, bit 4 = any non-scale/opacity Used field.
            //
            // IMPORTANT:
            // adoom_framebuffer intentionally uses rotationOffset as a transport field for yaw.
            // Therefore framebuffer dispatch MUST happen before the generic unsupported-field test.
            object? tagContainer = _getTags!(worker);
            if (!TryGetSingleAdoomTag(tagContainer, out string tag))
                return Fallback(started);

            // DOOMGeneric host attach command. It only runs once at chart start.
            // After attachment, NativeDoomHost polls keyboard input and advances
            // the native engine from Main.OnUpdate; PACL2 is no longer in the frame loop.
            if (string.Equals(tag, DoomFramebufferTag, StringComparison.OrdinalIgnoreCase))
            {
                TargetEntry doomEntry = GetTargetEntry(worker, tag, tagContainer!);
                if (doomEntry.Targets.Length == 0)
                    return Fallback(started);

                NativeDoomHost.Attach(doomEntry.Targets[0].Target);
                _fastHits++;
                _fastDecorations++;
                _fastTicks += Stopwatch.GetTimestamp() - started;
                return false;
            }

            // Dedicated framebuffer batch command. The chart encodes:
            // scale = [px*100, py*100] -> PACL2 targetScaleV2 becomes [px,py]
            // rotationOffset = yaw degrees.
            //
            // One PACL2 call renders the full framebuffer in C#.
            if (string.Equals(tag, FramebufferTag, StringComparison.OrdinalIgnoreCase))
            {
                object? state = _getScale!(worker);
                object? yawObj = _getRotation!(worker);
                if (!TryVector2(state, out Vector2 pos) || !TryFloat(yawObj, out float yaw))
                    return Fallback(started);

                TargetEntry fbEntry = GetTargetEntry(worker, tag, tagContainer!);
                if (fbEntry.Targets.Length == 0)
                    return Fallback(started);

                RenderFramebuffer(fbEntry.Targets[0].Target, pos.x, pos.y, yaw);
                _framebufferDispatches++;
                _fastHits++;
                _fastDecorations++;
                _fastTicks += Stopwatch.GetTimestamp() - started;
                return false;
            }

            // Generic hot path remains conservative.
            if ((mask & 4) != 0 || (mask & 3) == 0)
                return Fallback(started);

            object? scale = (mask & 1) != 0 ? _getScale!(worker) : null;
            object? opacity = (mask & 2) != 0 ? _getOpacity!(worker) : null;
            if (((mask & 1) != 0 && scale == null) || ((mask & 2) != 0 && opacity == null))
                return Fallback(started);

            TargetEntry entry = GetTargetEntry(worker, tag, tagContainer!);
            foreach (TargetHandle h in entry.Targets)
            {
                if ((mask & 1) != 0) h.SetScale!(h.Target, scale!);
                if ((mask & 2) != 0) h.SetOpacity!(h.Target, opacity!);
            }

            _fastHits++;
            _fastDecorations += entry.Targets.Length;
            _fastTicks += Stopwatch.GetTimestamp() - started;
            return false;
        }
        catch (Exception e)
        {
            _fastFallbacks++;
            _fastTicks += Stopwatch.GetTimestamp() - started;
            _fastPathErrors++;
            _main?.Warn("HotMove0 failed; falling back: " + (e.InnerException?.Message ?? e.Message));
            if (_fastPathErrors >= 3)
            {
                _fastPathDisabledAfterError = true;
                _main?.Warn("HotMove0 disabled after 3 errors.");
            }
            return true;
        }
    }

    private static bool Fallback(long started)
    {
        _fastFallbacks++;
        _fastTicks += Stopwatch.GetTimestamp() - started;
        return true;
    }

    private static bool TryDouble(object? value, out double d)
    {
        try { if (value != null) { d = Convert.ToDouble(value); return true; } }
        catch { }
        d = 0; return false;
    }

    private static Component GetWorker()
    {
        if (_worker != null && _workerObject != null) return _worker;
        if (_moveEffectType == null) throw new InvalidOperationException("move effect type missing");
        _workerObject = new GameObject("ADOOMFAIAccelerator.HotMoveWorker") { hideFlags = HideFlags.HideAndDontSave };
        _worker = (Component)_workerObject.AddComponent(_moveEffectType);
        return _worker;
    }

    private static void BuildWorkerAccessors()
    {
        if (_moveEffectType == null) throw new InvalidOperationException();
        FieldInfo[] fields = EnumerateInstanceFields(_moveEffectType).ToArray();
        FieldInfo[] used = fields.Where(f => f.FieldType == typeof(bool) && f.Name.IndexOf("Used", StringComparison.OrdinalIgnoreCase) >= 0).ToArray();
        if (used.Length == 0) throw new MissingFieldException(_moveEffectType.FullName, "*Used");

        _resetUsed = BuildResetUsed(_moveEffectType, used);
        _getUsedMask = BuildUsedMask(_moveEffectType, used);
        _getTags = BuildObjectGetter(_moveEffectType, FindField(fields, "targetTags"));
        _getScale = BuildObjectGetter(_moveEffectType, FindField(fields, "targetScaleV2"));
        _getOpacity = BuildObjectGetter(_moveEffectType, FindField(fields, "targetOpacity"));
        _getRotation = BuildObjectGetter(_moveEffectType, FindField(fields, "targetRot"));
        FieldInfo? dm = fields.FirstOrDefault(f => f.Name.Equals("decManager", StringComparison.OrdinalIgnoreCase));
        if (dm != null) _getWorkerManager = BuildObjectGetter(_moveEffectType, dm);
    }

    private static FieldInfo FindField(FieldInfo[] fields, string name)
        => fields.FirstOrDefault(f => f.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
           ?? throw new MissingFieldException(_moveEffectType?.FullName, name);

    private static Action<object> BuildResetUsed(Type owner, IEnumerable<FieldInfo> fields)
    {
        DynamicMethod dm = new("ADOOM_ResetUsed", typeof(void), new[] { typeof(object) }, typeof(AcceleratorRuntime), true);
        ILGenerator il = dm.GetILGenerator();
        foreach (FieldInfo f in fields)
        {
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Castclass, owner); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Stfld, f);
        }
        il.Emit(OpCodes.Ret);
        return (Action<object>)dm.CreateDelegate(typeof(Action<object>));
    }

    private static Func<object, int> BuildUsedMask(Type owner, IEnumerable<FieldInfo> fields)
    {
        DynamicMethod dm = new("ADOOM_UsedMask", typeof(int), new[] { typeof(object) }, typeof(AcceleratorRuntime), true);
        ILGenerator il = dm.GetILGenerator();
        LocalBuilder mask = il.DeclareLocal(typeof(int));
        il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Stloc, mask);
        int n = 0;
        foreach (FieldInfo f in fields)
        {
            int bit = f.Name.IndexOf("scale", StringComparison.OrdinalIgnoreCase) >= 0 ? 1
                : f.Name.IndexOf("opacity", StringComparison.OrdinalIgnoreCase) >= 0 ? 2 : 4;
            Label skip = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Castclass, owner); il.Emit(OpCodes.Ldfld, f); il.Emit(OpCodes.Brfalse_S, skip);
            il.Emit(OpCodes.Ldloc, mask); il.Emit(OpCodes.Ldc_I4, bit); il.Emit(OpCodes.Or); il.Emit(OpCodes.Stloc, mask);
            il.MarkLabel(skip); n++;
        }
        il.Emit(OpCodes.Ldloc, mask); il.Emit(OpCodes.Ret);
        return (Func<object, int>)dm.CreateDelegate(typeof(Func<object, int>));
    }

    private static Func<object, object?> BuildObjectGetter(Type owner, FieldInfo field)
    {
        DynamicMethod dm = new("ADOOM_Get_" + field.Name, typeof(object), new[] { typeof(object) }, typeof(AcceleratorRuntime), true);
        ILGenerator il = dm.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Castclass, owner); il.Emit(OpCodes.Ldfld, field);
        if (field.FieldType.IsValueType) il.Emit(OpCodes.Box, field.FieldType);
        il.Emit(OpCodes.Ret);
        return (Func<object, object?>)dm.CreateDelegate(typeof(Func<object, object?>));
    }

    private static Action<Delegate, object> GetSetupInvoker(Type delegateType)
    {
        if (SetupInvokerCache.TryGetValue(delegateType, out Action<Delegate, object>? inv)) return inv;
        MethodInfo invoke = delegateType.GetMethod("Invoke") ?? throw new MissingMethodException(delegateType.FullName, "Invoke");
        ParameterInfo[] ps = invoke.GetParameters();
        if (ps.Length != 1 || _moveEffectType == null || !ps[0].ParameterType.IsAssignableFrom(_moveEffectType))
            throw new InvalidOperationException("Unexpected setup delegate: " + delegateType.FullName);
        DynamicMethod dm = new("ADOOM_InvokeSetup", typeof(void), new[] { typeof(Delegate), typeof(object) }, typeof(AcceleratorRuntime), true);
        ILGenerator il = dm.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Castclass, delegateType);
        il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Castclass, ps[0].ParameterType);
        il.Emit(OpCodes.Callvirt, invoke); il.Emit(OpCodes.Ret);
        inv = (Action<Delegate, object>)dm.CreateDelegate(typeof(Action<Delegate, object>));
        SetupInvokerCache[delegateType] = inv;
        return inv;
    }

    private static bool TryGetSingleAdoomTag(object? container, out string tag)
    {
        tag = string.Empty;
        if (container is IList list)
        {
            if (list.Count != 1 || list[0] == null) return false;
            tag = list[0]!.ToString() ?? string.Empty;
        }
        else if (container is IEnumerable e)
        {
            IEnumerator it = e.GetEnumerator();
            if (!it.MoveNext() || it.Current == null) return false;
            tag = it.Current.ToString() ?? string.Empty;
            if (it.MoveNext()) return false;
        }
        else return false;

        return tag.StartsWith("adoom_", StringComparison.OrdinalIgnoreCase)
            || tag.StartsWith("wall_", StringComparison.OrdinalIgnoreCase)
            || tag.StartsWith("floor_", StringComparison.OrdinalIgnoreCase)
            || tag.StartsWith("ceil_", StringComparison.OrdinalIgnoreCase)
            || tag.StartsWith("px_", StringComparison.OrdinalIgnoreCase)
            || tag.StartsWith("pack_", StringComparison.OrdinalIgnoreCase);
    }

    private static TargetEntry GetTargetEntry(object worker, string tag, object originalTagContainer)
    {
        if (TargetCache.TryGetValue(tag, out TargetEntry? cached) && cached.IsAlive()) return cached;
        object manager = ResolveManager(worker) ?? throw new InvalidOperationException("Decoration manager missing");
        if (!ReferenceEquals(manager, _cachedManager))
        {
            _cachedManager = manager;
            TargetCache.Clear();
            _getTaggedDecorationsMethod = null;
        }
        _getTaggedDecorationsMethod ??= manager.GetType().GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .FirstOrDefault(m => m.Name == "GetTaggedDecorations" && m.GetParameters().Length == 1)
            ?? throw new MissingMethodException(manager.GetType().FullName, "GetTaggedDecorations");
        object? arg = ConvertArgument(originalTagContainer, _getTaggedDecorationsMethod.GetParameters()[0].ParameterType);
        object? result = _getTaggedDecorationsMethod.Invoke(manager, new[] { arg });
        if (result is not IEnumerable enumerable) return new TargetEntry(Array.Empty<TargetHandle>());
        List<TargetHandle> handles = new();
        foreach (object? o in enumerable)
        {
            if (o == null || !IsAliveUnityObject(o)) continue;
            handles.Add(BuildHandle(o));
        }
        TargetEntry entry = new(handles.ToArray());
        TargetCache[tag] = entry;
        return entry;
    }

    private static object? ResolveManager(object worker)
    {
        if (_cachedManager != null) return _cachedManager;
        object? manager = _getWorkerManager?.Invoke(worker);
        if (manager != null) return manager;
        FieldInfo? f = AccessTools.Field(typeof(scnGame), "suitableDecManager");
        if (f != null) return f.GetValue(f.IsStatic ? null : scnGame.instance);
        PropertyInfo? p = AccessTools.Property(typeof(scnGame), "suitableDecManager");
        MethodInfo? getter = p?.GetGetMethod(true);
        return getter == null ? null : p!.GetValue(getter.IsStatic ? null : scnGame.instance, null);
    }

    private static TargetHandle BuildHandle(object target)
    {
        Type t = target.GetType();
        MethodInfo? scale = FindUnary(t, "SetScale");
        MethodInfo? opacity = FindUnary(t, "SetOpacity");
        return new TargetHandle(target,
            scale == null ? null : BuildUnaryInvoker(t, scale),
            opacity == null ? null : BuildUnaryInvoker(t, opacity));
    }

    private static MethodInfo? FindUnary(Type t, string name)
        => t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .FirstOrDefault(m => m.Name == name && m.GetParameters().Length == 1);

    private static Action<object, object> BuildUnaryInvoker(Type owner, MethodInfo method)
    {
        Type p = method.GetParameters()[0].ParameterType;
        DynamicMethod dm = new("ADOOM_" + method.Name, typeof(void), new[] { typeof(object), typeof(object) }, typeof(AcceleratorRuntime), true);
        ILGenerator il = dm.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Castclass, owner);
        il.Emit(OpCodes.Ldarg_1);
        if (p.IsValueType) il.Emit(OpCodes.Unbox_Any, p); else il.Emit(OpCodes.Castclass, p);
        il.Emit(method.IsVirtual ? OpCodes.Callvirt : OpCodes.Call, method); il.Emit(OpCodes.Ret);
        return (Action<object, object>)dm.CreateDelegate(typeof(Action<object, object>));
    }

    private static bool TryVector2(object? value, out Vector2 v)
    {
        if (value is Vector2 vv) { v = vv; return true; }
        v = default; return false;
    }

    private static bool TryFloat(object? value, out float f)
    {
        try { if (value != null) { f = Convert.ToSingle(value); return true; } }
        catch { }
        f = 0f; return false;
    }

    private static void RenderFramebuffer(object target, float px, float py, float yawDegrees)
    {
        long started = Stopwatch.GetTimestamp();
        SpriteRenderer sr = ResolveSpriteRenderer(target) ?? throw new InvalidOperationException("adoom_framebuffer SpriteRenderer not found");
        EnsureFramebuffer(sr);

        float rad = yawDegrees * (Mathf.PI / 180f);
        float dirX = Mathf.Cos(rad);
        float dirY = Mathf.Sin(rad);
        float planeX = -dirY * CameraPlane;
        float planeY = dirX * CameraPlane;
        Color32[] pixels = _framePixels!;

        float center = (FrameHeight - 1) * 0.5f;
        for (int x = 0; x < FrameWidth; x++)
        {
            float cameraX = 2f * (x + 0.5f) / FrameWidth - 1f;
            float rayX = dirX + planeX * cameraX;
            float rayY = dirY + planeY * cameraX;

            float tx = rayX > 0f ? (10f - px) / rayX : rayX < 0f ? (0f - px) / rayX : 1e9f;
            float ty = rayY > 0f ? (10f - py) / rayY : rayY < 0f ? (0f - py) / rayY : 1e9f;
            float dist = Mathf.Min(tx, ty);
            if (dist < 0.02f) dist = 0.02f;
            bool xSide = tx < ty;
            float halfWallPx = Mathf.Min(FrameHeight * 0.5f, (FrameHeight * WallProjectionPerHeight) / dist);

            Color32 wall = WallColor(xSide, dist);
            for (int y = 0; y < FrameHeight; y++)
            {
                float dy = y - center;
                int i = y * FrameWidth + x;
                if (Mathf.Abs(dy) <= halfWallPx)
                {
                    pixels[i] = wall;
                    continue;
                }

                float depth = _rowDepths![y];
                float wx = px + rayX * depth;
                float wy = py + rayY * depth;
                int checker = (((int)Math.Floor(wx / 1.5f)) + ((int)Math.Floor(wy / 1.5f))) & 1;
                pixels[i] = dy < 0f ? FloorColor(checker, depth) : CeilingColor(checker, depth);
            }
        }

        _frameTexture!.SetPixels32(pixels);
        _frameTexture.Apply(false, false);
        _batchFrames++;
        _batchPixels += FrameWidth * FrameHeight;
        _batchTicks += Stopwatch.GetTimestamp() - started;
    }

    private static float Clamp(float v, float lo, float hi) => v < lo ? lo : v > hi ? hi : v;
    private static byte B(float v) => (byte)(v < 0 ? 0 : v > 255 ? 255 : v);

    private static Color32 WallColor(bool xSide, float dist)
    {
        float fade = Clamp(1f - dist * 0.035f, 0.50f, 1f);
        if (xSide) return new Color32(B(205 * fade), B(235 * fade), B(250 * fade), 255);
        return new Color32(B(178 * fade), B(214 * fade), B(235 * fade), 255);
    }

    private static Color32 FloorColor(int checker, float depth)
    {
        float fog = Clamp(1f - depth / 35f, 0.55f, 1f);
        int b = checker == 0 ? 72 : 108;
        return new Color32(B(b * fog), B((b + 5) * fog), B((b + 9) * fog), 255);
    }

    private static Color32 CeilingColor(int checker, float depth)
    {
        float fog = Clamp(1f - depth / 42f, 0.60f, 1f);
        int b = checker == 0 ? 32 : 52;
        return new Color32(B(b * fog), B((b + 5) * fog), B((b + 10) * fog), 255);
    }

    private static SpriteRenderer? ResolveSpriteRenderer(object target)
    {
        if (target is Component c)
            return c.GetComponent<SpriteRenderer>() ?? c.GetComponentInChildren<SpriteRenderer>(true);
        if (target is GameObject go)
            return go.GetComponent<SpriteRenderer>() ?? go.GetComponentInChildren<SpriteRenderer>(true);
        return null;
    }

    private static void EnsureFramebuffer(SpriteRenderer sr)
    {
        if (_frameTexture != null && _frameSprite != null && _frameRenderer == sr)
        {
            if (sr.sprite != _frameSprite) sr.sprite = _frameSprite;
            return;
        }

        DestroyFramebufferResources();
        _frameRenderer = sr;
        _framePixels = new Color32[FrameWidth * FrameHeight];
        _rowDepths = new float[FrameHeight];
        float center = (FrameHeight - 1) * 0.5f;
        for (int y = 0; y < FrameHeight; y++)
        {
            float ady = Mathf.Max(Mathf.Abs(y - center), 0.5f);
            _rowDepths[y] = Clamp((FrameHeight * SurfaceDepthPerHeight) / ady, 0.8f, 22f);
        }
        _frameTexture = new Texture2D(FrameWidth, FrameHeight, TextureFormat.RGBA32, false)
        {
            name = "ADOOMFAI.Framebuffer320x200",
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave
        };
        _frameSprite = Sprite.Create(_frameTexture, new Rect(0, 0, FrameWidth, FrameHeight), new Vector2(0.5f, 0.5f), 100f);
        _frameSprite.name = "ADOOMFAI.FramebufferSprite";
        _frameSprite.hideFlags = HideFlags.HideAndDontSave;
        sr.sprite = _frameSprite;
        sr.color = Color.white;
    }

    private static void DestroyFramebufferResources()
    {
        if (_frameSprite != null) UnityEngine.Object.Destroy(_frameSprite);
        if (_frameTexture != null) UnityEngine.Object.Destroy(_frameTexture);
        _frameSprite = null;
        _frameTexture = null;
        _frameRenderer = null;
        _framePixels = null;
        _rowDepths = null;
    }

    private static bool IsAliveUnityObject(object value) => value is not UnityEngine.Object u || u != null;

    private static object? ConvertArgument(object? value, Type targetType)
    {
        if (value == null) return null;
        if (targetType.IsInstanceOfType(value)) return value;
        return value;
    }

    private static IEnumerable<FieldInfo> EnumerateInstanceFields(Type type)
    {
        for (Type? t = type; t != null && t != typeof(MonoBehaviour); t = t.BaseType)
            foreach (FieldInfo f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                yield return f;
    }

    private static IEnumerable<Type> SafeGetTypes(Assembly a)
    {
        try { return a.GetTypes(); }
        catch (ReflectionTypeLoadException e) { return e.Types.Where(t => t != null).Cast<Type>(); }
        catch { return Array.Empty<Type>(); }
    }

    private sealed class TargetEntry
    {
        internal readonly TargetHandle[] Targets;
        internal TargetEntry(TargetHandle[] targets) => Targets = targets;
        internal bool IsAlive()
        {
            foreach (TargetHandle h in Targets) if (!IsAliveUnityObject(h.Target)) return false;
            return true;
        }
    }

    private sealed class TargetHandle
    {
        internal readonly object Target;
        internal readonly Action<object, object>? SetScale;
        internal readonly Action<object, object>? SetOpacity;
        internal TargetHandle(object target, Action<object, object>? scale, Action<object, object>? opacity)
        { Target = target; SetScale = scale; SetOpacity = opacity; }
    }
}
