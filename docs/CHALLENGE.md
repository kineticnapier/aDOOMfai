# ADOOMFAI challenge log

The project began with a deliberately unreasonable question:

> Can DOOM be made to run inside A Dance of Fire and Ice?

## 1. PACL2 pixel renderer

The first versions treated ADOFAI decorations as pixels. A 64x32 framebuffer meant
2,048 separate decorations and thousands of PACL2 `MoveDecorations` calls.

It worked, but input could stall for around a second because every pixel update crossed
the PACL2/Unity effect path.

## 2. PACL2 accelerator

Inspection of PACL2 showed that each `MoveDecorations` call created and started an
effect component. A JALib/UMM accelerator was written to intercept the zero-duration
ADOOM update path and update decorations directly.

This made the 2,048-pixel renderer substantially faster.

## 3. Batch framebuffer

The real breakthrough was removing the 2,048 PACL2-to-C# crossings entirely.

A special framebuffer command sends state once, then C# renders the whole frame and
uploads it to one `Texture2D`.

The resolution jumped from:

```text
64x32 = 2,048 pixels
```

to:

```text
171x96 = 16,416 pixels
```

and finally:

```text
320x200 = 64,000 pixels
```

The 320x200 software renderer measured only a few milliseconds per frame on the test PC.

## 4. Stop reimplementing DOOM

At this point the display pipeline was fast enough, so the custom raycaster was replaced
with `doomgeneric`.

The final architecture uses:

- C / doomgeneric for the DOOM-compatible engine
- C# for Unity/ADOFAI integration
- PACL2 only for the initial framebuffer attachment
- one ADOFAI decoration as the display

The result is best described as:

> A native DOOM-compatible engine hosted inside ADOFAI.

The rendering, game logic, BSP, enemies, weapons, doors, collision and WAD parsing are
handled by the DOOM-derived engine rather than being recreated in PACL2.

## Final result

Challenge complete:

- 320x200 original DOOM framebuffer resolution
- interactive keyboard input
- IWAD loading
- native DOOM-compatible game logic and renderer
- output displayed inside ADOFAI
