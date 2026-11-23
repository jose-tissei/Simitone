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
| L1 | **DROPPED, see Correction** (depth buffer unreadable; engine derivative pass built) Depth-to-normal for the 2D path; debug view of normals | S3 | normals visible and stable on the Newbie lot |
| L2 | **DROPPED, see Correction** (measured: almost no visible effect) N.L shading from room lights in the sprite shaders (effect replacement) | L1 | a lamp visibly lights facing surfaces more than the back |
| L3 | **DONE** `LightGrid` from the lot: occluders, emitters, updated on change; unit tests for rasterisation | S3 | grid debug overlay matches the floor plan |
| L4 | **DONE** `RadianceCascades` pixel-shader merge from `sparse_rc_2d` / `solis_2d` ideas; write into the light-map inputs via `PrepareLighting` | L3 | soft shadow behind a wall; colour bleed from a coloured lamp; <= 4 ms |
| L5 | **DONE** Options entry "Dynamic lighting" with off switch; e2e comparing screenshots | L4 | toggles live |
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
- Lighting prototype (not one of the listed tasks, a quick way to see what is possible): `Render.Lighting` runs a bounce-light pass over the engine's own light map
  and hands the result to every world shader through a hook at the end of `WorldState.PrepareLighting`. Off by default (`light=bounce` in `render.profile`,
  `simtool render light mode bounce`, `render light tune <tiles> <gain>`). Visible effect: softer, slightly brighter indirect light on floors and walls, coloured light
  spreads a little. It has no occlusion (light can bleed through walls) and no normals; e2e 67 checks, patch tests 25, unit tests 173.
- **Finding that changes the plan:** the engine already has real-time lighting (a per-room light map that updates with lamps and time of day, soft shadows,
  and a direction field used for meshes). Sprites carry no normals, so L1/L2 would need normals reconstructed from the packed depth first.
- L3, L4, L5 built (2026-11-22, e2e result below): `LightGrid` rasterises the lot's walls (doors left open) into an atlas laid out like the engine's light map (4 px per tile);
  `Cascades.fx` runs three radiance cascades in pixel shaders (scene pass, one pass per cascade merged top-down, combine pass) and adds the result to the light map
  where it is not fully lit, so light now stops at walls. Options button "Dynamic lighting: Off / On" (x = 1724), `render.profile` keys `light=rc`, `shade=on`.
  Limits: plan view only (no vertical occlusion, L6 stays open), 3 cascades reach about 10 tiles, wall diagonals not checked against a diagonal-wall lot.
- L1 + L2 built differently from the plan: the **hardware depth buffer cannot be read** (checked with a probe: D24S8, bind flag DepthStencil only, and the packed depth target
  only exists in the pure 2D launch), so no depth-to-normal pre-pass. The engine's own HLSL already contains a commented-out third pass (`psZDepthSpriteDirLight`) that
  rebuilds normals from the depth sprite with `ddx/ddy` and shades by the direction light map. `Make-WorldEffect.py` re-enables it, the result is `Shaders/World2D.xnb`
  (installed over `2DWorldBatch.xnb`, original backed up), and a Cecil patch on `WorldConfig.get_DirPassOffset` calls `Normals.DirPass` so the engine picks pass 2.
  Effect: sprites facing away from the light get darker, facing sprites keep their colour (visible on bushes, fences, wall tops). Modest, and it is the original authors'
  disabled feature, so some surfaces may look faceted.
- Not done: L6 vertical occlusion, G1 GTAO, R1 ray tracing (research only).
- Finding: the game saves `GlobalGraphicsMode` in `config.ini` when a mode is picked in the menu; the e2e now resets and restores it (and `render.profile`).

## Correction after looking at the pictures (2026-11-22, later)
The first round of this work was reported as done on the strength of tests that only proved the code ran. Looking at before/after images showed otherwise, and the
fixes below were checked against aligned frames of the paused game (two screenshots of the same state are pixel-identical, so any difference is real).
- **SMAA had two bugs.** (1) The area lookup table is stored in red/green but the HLSL 3 path of the reference reads red/alpha, so every blend weight was wrong and edges came out
  stair-stepped (fixed by `#define SMAA_AREATEX_SELECT(sample) sample.rg`). (2) The pass switched render targets in the middle of the frame, and switching back to the
  screen made the driver discard what the game had already drawn there (its clear colour, which sprite edges blend over), so edge pixels and the void area lost their
  background (fixed by asking for `RenderTargetUsage.PreserveContents` when the device is created). Regression check: with edge detection off the pass now reproduces the
  original frame with 0 changed pixels. Edge sensitivity is now a run-time setting; 0.2 (reference preset: 0.1) keeps 97% of lawn texture detail while the number of hard-edge
  pixels drops to about half. FXAA keeps 74% of the lawn detail and visibly blurs, so the Options button now offers SMAA only (FXAA stays in `render.profile`).
  The honest limit: the game's sprites and its 3D mode are already soft-edged (pre-rendered alpha edges, hardware MSAA), so SMAA's improvement is subtle.
- **Radiance cascades were spreading the night ambient light**, which is the same everywhere in the light map (measured 36,52,90 of 255 over the whole lot), so the result
  was a uniform brightening. Now only light above that ambient level is a source. The scene is 8 px per tile (was 4) to keep the top cascade's probes one tile apart.
  Measured at night with the default gain 50: the lamp light spills across floors and walls, a warm glow reaches the lawn, and the bedroom behind a wall stays dark.
  Ignoring every wall (a debug switch) changed 39% of the pixels against 8% with walls (measured at gain 25), which is the proof that walls block the light.
- **Directional sprite shading did not earn its place.** At night with the cascades on it changes 174 pixels of 2 million; by day about 1.6%, mostly small darkening of outdoor objects.
  It is therefore no longer part of the "Dynamic lighting" button and the installer no longer replaces the game's `2DWorldBatch.xnb`. The code, `Make-WorldEffect.py` and
  `Shaders/World2D.xnb` stay as an experimental option (`shade=on` plus copying the file by hand).
- **Depth buffer finding stands:** the hardware depth buffer cannot be sampled (D24S8, DepthStencil bind flag only).

## Order and parallelism
S1 -> (S3, S2) -> Phase 1 (A1..A6) -> Phase 2 (L1..L6, L1 and L3 parallel) -> Phase 3. Phase 1 alone already delivers a visible win.
