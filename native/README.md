# Native doomgeneric bridge

This directory contains only the ADOOMFAI platform glue.

The upstream doomgeneric source is **not vendored in this zip**. `fetch-doomgeneric.ps1`
checks out this pinned upstream commit:

    dcb7a8dbc7a16ce3dda29382ac9aae9d77d21284

The bridge exports a small C ABI:

- `ADOOM_Init(iwad_path)`
- `ADOOM_Tick()`
- `ADOOM_KeyEvent(pressed, doom_key)`
- `ADOOM_GetFrameRGBA()`
- `ADOOM_GetFrameId()`

The platform callbacks required by doomgeneric are implemented in `adoom_platform.c`:

- `DG_Init`
- `DG_DrawFrame`
- `DG_SleepMs`
- `DG_GetTicksMs`
- `DG_GetKey`
- `DG_SetWindowTitle`

Resolution is fixed at 320x200.

The first PoC disables native audio (`-nosound -nomusic`); ADOFAI remains responsible
for its own audio.

## Build

From PowerShell:

    .\fetch-doomgeneric.ps1
    .\build-native.ps1

The current build script expects a MinGW-w64 GCC/Clang toolchain and Ninja/CMake.

## Data

No commercial IWAD is included. Put a user-owned IWAD or a compatible free-data WAD
under the mod's `iwad` directory.
