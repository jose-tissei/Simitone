# Tasks: render upgrades

Gate for every task: `Tools/ci.ps1` passes; for in-game tasks also `Tools/e2e/Run-E2E.ps1` (new section per feature).
Each task ends with an atomic commit.

## Phase 0: toolchain and measurements (spikes)
| ID | Task | Depends | Done when |
|---|---|---|---|
| S1 | **DONE** Get an MGFX v8 compiler (build `FreeSO/Other/libs/FSOMonoGame/Tools` 2MGFX, or find a compatible one). Replace `Content\DX\Effects\SSAA.xnb` with a test shader (tint) | - | Verified: tint shows in the real game (`s1-tint-proof.png`); stock SSAA rebuilds byte-identical; script `Tools/Render/Build-Effects.ps1` (RND-01) |
| S3 | **DONE** Record the GPU model; time a full-screen test pass at 1920x1080 inside the game (GPU timer or frame-time log) | S1 | Numbers in `notes/measurements.md`; budget confirmed or revised |
| S2 | **DONE** Test MSAA (`-aa`) with the MRT depth target in Hybrid2D plus a post pass | S1 | Result recorded; decision on MSAA/post-AA coexistence |

## Phase 1: anti-aliasing (RND-02..05, 10, 13)
| ID | Task | Depends | Done when |
|---|---|---|---|
| A1 | **DONE** `Tools/Render` project (net461, `AppDir` references like `Conversations`), loader from `SpriteCards.Startup`, settings file `render.profile` | S1 | builds, loads, logs | 
| A2 | **DONE** `PostChain` + `FxaaPass`; hook `DrawBackbuffer` (or `SSAAFunc`) by Cecil in `SpriteCardsPatch` (optional arg like Conversations) | A1 | e2e: edge metric on a screenshot of a sprite silhouette improves; UI pixels unchanged | 
| A3 | **DONE** Survive mode switches and resizes (recreate targets, re-apply delegate after `ChangeAAMode`) | A2 | e2e cycles all four modes, game responds, pass still on |
| A4 | **DONE** Options menu entry: Off / FXAA / SMAA, persisted; Off is bit-identical to before | A2 | e2e toggles and checks |
| A5 | **DONE** `SmaaPass` (MIT port, lookup textures embedded), pick default by measurement | A2, S3 | quality compared in screenshots; cost <= 1 ms |
| A6 | **DONE** Patch tests for the new hooks; unit tests for settings and pass ordering | A2 | `Tools/ci.ps1` green |
| A7 | (P1) Sprite sampling hygiene, render-scale slider replacing SSAA | A5 | optional |

## Phase 2: lighting (RND-07..09)
| ID | Task | Depends | Done when |
|---|---|---|---|
| L1 | Depth-to-normal for the 2D path; debug view of normals | S3 | normals visible and stable on the Newbie lot |
| L2 | N.L shading from room lights in the sprite shaders (effect replacement) | L1 | a lamp visibly lights facing surfaces more than the back |
| L3 | `LightGrid` from the lot: occluders, emitters, updated on change; unit tests for rasterisation | S3 | grid debug overlay matches the floor plan |
| L4 | `RadianceCascades` pixel-shader merge from `sparse_rc_2d` / `solis_2d` ideas; write into the light-map inputs via `PrepareLighting` | L3 | soft shadow behind a wall; colour bleed from a coloured lamp; <= 4 ms |
| L5 | Options entry "Dynamic lighting" with off switch; e2e comparing screenshots | L4 | toggles live |
| L6 | Vertical occlusion decision (height layers or coarse 3D grid) | L4 | note + prototype, or documented limit |

## Phase 3: optional
| ID | Task | Depends | Done when |
|---|---|---|---|
| G1 | GTAO (XeGTAO, MIT) for the 2D path | L1 | contact darkening visible, <= 1 ms |
| R1 | DXR proof of concept in Full 3D: Vortice D3D12 device, proxy BLAS/TLAS, shadow term in a shared texture | A-phase done | one visible ray-traced shadow in Full 3D; or a written no-go with evidence |

## Progress (2026-11-22)
- S1 done. GPU recorded: NVIDIA GeForce RTX 4070 (S3 budget numbers still to measure).
- A1, A2 done: `Tools/Render` (`PostAA`, `RenderSettings`, embedded `Fxaa.mgfxo`), Cecil hook on `PPXDepthEngine.DrawBackbuffer` (`TryDraw`), `simtool render aa off|fxaa`. Measured in the real game with the game paused: FXAA changes about 15% of pixels (edges and texture detail), mean brightness change 0, turning it off restores the original frame exactly; e2e 51 checks, patch tests 21, unit tests 159.
- A4 done: Options button "Anti-aliasing: Off / FXAA / SMAA" (AA icon, green when on, label updates live), saved in `render.profile`.
- A3 done: e2e switches 2D, arch, Full 3D and list with FXAA on; the pass keeps drawing and never marks itself broken.
- A5 done: SMAA 1x (reference SMAA.hlsl, MIT) as three passes with embedded lookup tables. **Finding:** the shipped game runs at the Reach graphics profile (Direct3D feature level 9_3), which cannot run the SMAA shaders, so the render library now makes the game ask for HiDef when the adapter supports it (`Render.Boot.SetVsync` replaces one call in the SimitoneGame constructor; `hidef=off` in `render.profile` undoes it). Without HiDef, SMAA falls back to FXAA. Measured (game paused): SMAA changes 12.6% of pixels at edges and keeps texture detail (gravel stays crisp), FXAA changes 33% and blurs texture detail. SMAA is the better mode.
- e2e 63 checks, patch tests 23, unit tests 164.
- S3 done: FXAA 0.02 ms, SMAA 0.07-0.08 ms per frame at 1080p on the RTX 4070 (details in `notes/measurements.md`).
- S2 done: engine MSAA and the post pass coexist in Full 3D and list mode; decision: keep engine MSAA off by default.
- Next: Phase 2 (lighting): L1 depth-to-normal, L3 light grid.
- Finding: the game saves `GlobalGraphicsMode` in `config.ini` when a mode is picked in the menu; the e2e now resets and restores it (and `render.profile`).

## Order and parallelism
S1 -> (S3, S2) -> Phase 1 (A1..A6) -> Phase 2 (L1..L6, L1 and L3 parallel) -> Phase 3. Phase 1 alone already delivers a visible win.
