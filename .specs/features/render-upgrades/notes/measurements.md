# Measurements (2026-11-22)

Machine: Windows 11, NVIDIA GeForce RTX 4070, game window 1920x1080, DirectX 11 via the game's MonoGame fork.

## Graphics profile (found while building SMAA)
The shipped Simitone release runs at the **Reach** profile (Direct3D feature level 9_3) because `SimitoneGame` never asks for HiDef
(the older FreeSO `TSOGame` did). SMAA's shaders need feature level 10+. The render library therefore makes the game request HiDef when
the adapter supports it (`Render.Boot.SetVsync` replaces one call in the constructor); `hidef=off` in `render.profile` restores Reach.
The game's own shaders (compiled for level 9_1) keep working at HiDef; the whole end-to-end suite passes with it.

## S3: cost of the passes (`Tools/Render/Measure-Frames.ps1`)
The game's frame loop stays near 1000 frames per second even with vsync off (the game re-enables its fixed time step every frame and the
loop is clamped by timer resolution), and the passes are far cheaper than that, so each pass is run 40 times per frame in the measurement and
the difference to "off" is divided by 40. Game paused, so the Sims do not matter.

| Mode of the game | FXAA | SMAA 1x |
|---|---|---|
| 3D with 2D list (default) | 0.02 ms | 0.08 ms |
| Full 3D | 0.02 ms | 0.07 ms |

Both are far inside the 1 ms anti-aliasing budget of the spec (RND-10). Caveat: this is wall-clock frame time, not a GPU timer
(MonoGame exposes none); with the CPU waiting on the GPU it is a good proxy, but it is not a GPU profile.

## S2: engine MSAA together with the post pass
`simtool aa 1` (4x MSAA) plus `render aa smaa`, in Full 3D and in 3D with 2D list: both run, no error, the pass stays healthy.
MSAA alone changes 3.9% (Full 3D) and 2.4% (list) of the pixels, SMAA on top changes a further 3.7% (Full 3D) and 12.4% (list).
In list mode MSAA does little because the picture is mostly sprites, which is the reason to use a post pass.
Decision: keep the engine's MSAA off by default; SMAA gives the better result at lower cost, and the two can be combined if someone wants.

## Quality (zoomed crops, game paused)
- FXAA changes about 15-33% of the pixels and softens texture detail (gravel, grass).
- SMAA changes about 12% of the pixels, concentrated at edges, and keeps texture detail crisp. SMAA is the better mode.
