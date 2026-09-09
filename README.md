# ADOOMFAI

**DOOM hosted inside A Dance of Fire and Ice.**

ADOOMFAI started as a PACL2 renderer experiment: first thousands of individual
`MoveDecorations` calls, then batched framebuffer updates, and finally a native
`doomgeneric` host.

The final version runs the DOOM-compatible engine in native C, connects it to an
ADOFAI/Unity mod written in C#, and displays the original **320x200** framebuffer
through a single ADOFAI decoration.

## Architecture

```text
ADOFAI chart
    |
    | one PACL2 attach command
    v
ADOOMFAIAccelerator.dll (C# / JALib / UMM)
    |
    | P/Invoke
    v
adoom_native.dll (C)
    |
    v
doomgeneric / DOOM engine
    |
    | 320x200 RGBA framebuffer
    v
Unity Texture2D
    |
    v
ADOFAI decoration
```

After the initial attach, PACL2 is no longer in the per-frame DOOM render loop.

## Current status

- Native doomgeneric engine: working
- Original 320x200 framebuffer: working
- IWAD loading: working
- Keyboard input: working
- ADOFAI display integration: working
- Point-filtered framebuffer: working
- Vertical orientation fix: included
- Native sound/music: not implemented in this PoC

## Requirements

- Windows
- A Dance of Fire and Ice with Unity Mod Manager / JALib setup
- PACL2
- .NET SDK capable of building the mod project
- MSYS2 UCRT64 with GCC, CMake and Ninja
- A compatible IWAD

The build helper looks for the common MSYS2 path:

```text
C:\msys64\ucrt64\bin
```

## Build

```powershell
.\build.ps1 -ManagedPath "C:\Program Files (x86)\Steam\steamapps\common\A Dance of Fire and Ice\A Dance of Fire and Ice_Data\Managed"
```

The build process:

1. Fetches the pinned `doomgeneric` revision.
2. Builds `adoom_native.dll`.
3. Builds `ADOOMFAIAccelerator.dll`.
4. Packages the mod.

## IWAD

**No commercial IWAD is included.**

Place one compatible IWAD in:

```text
Mods\ADOOMFAIAccelerator\iwad\
```

Examples:

```text
DOOM.WAD
DOOM2.WAD
freedoom1.wad
freedoom2.wad
```

You can also set the `ADOOMFAI_IWAD` environment variable to an explicit WAD path.

## Controls

| Key | Action |
|---|---|
| Up / W | Forward |
| Down / S | Backward |
| Left / A | Turn left |
| Right / D | Turn right |
| Ctrl | Fire |
| Space | Use / open |
| Shift | Run |
| 1-7 | Weapon keys |
| Tab | Automap |
| Esc | Menu |
| Enter | Confirm |

## Chart

The minimal wrapper chart is in [`chart/`](chart/). It contains one 320x200
framebuffer decoration and one PACL2 attach program.

## How this project evolved

See [`docs/CHALLENGE.md`](docs/CHALLENGE.md) for the progression from a
2,048-decoration renderer to the native DOOM host.

## License

ADOOMFAI source is distributed under **GPL-2.0-or-later** to remain compatible with
the DOOM-derived native engine code it links with. See [`LICENSE`](LICENSE).

Commercial DOOM game data is not covered by this repository's source-code license
and is not distributed here.
