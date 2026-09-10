# ADOOMFAI doomgeneric v15

This chart contains only:

- one 320x200 framebuffer decoration
- one PACL2 Program that attaches ADOOMFAIAccelerator to it

There are no PACL2 movement/render loops.

After the attach command, ADOOMFAIAccelerator v0.5+ runs doomgeneric from `OnUpdate`,
polls keyboard input directly, and uploads the native 320x200 framebuffer.

## Required runtime pieces

- PACL2
- ADOOMFAIAccelerator v0.5+
- `adoom_native.dll`
- a compatible IWAD placed in `Mods/ADOOMFAIAccelerator/iwad/`
  or selected using the `ADOOMFAI_IWAD` environment variable

The Accelerator is intentionally not placed in the chart's `requiredMods` field because
ADOFAI's local-mod dependency check previously reported a false missing-mod warning.
