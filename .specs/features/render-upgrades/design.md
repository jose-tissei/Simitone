# Design: render upgrades

All file references are from the research notes (`notes/engine_pipeline.md`); line numbers are from the repo source and were
checked against the shipped DLL with Cecil, but nothing here has been run yet.

## Frame structure (verified by reading)
`GameScreen.Draw`: every layer's `PreDraw`, then every layer's `Draw`; world first, UI after.
`World.PreDraw` renders the lot into `PPXDepthEngine.Backbuffer` via `InternalDraw` (sky dome, architecture, static buffer, avatars, objects, ghosts).
`World.Draw` only blits that target through `PPXDepthEngine.DrawBackbuffer` (+ delegate `SSAAFunc`).

```
InternalDraw -> Backbuffer (colour + packed depth/ID MRT)
   -> [NEW] post chain: AA (FXAA / SMAA) on colour only
   -> DrawBackbuffer blit -> screen -> UI layers (untouched)
Lighting: LMapBatch bakes per-floor light map -> WorldState.PrepareLighting -> shaders
   -> [NEW] RC pass writes an extra light texture merged in PrepareLighting
```

## Components
| Component | Role | Hook |
|---|---|---|
| `Tools/Render` library (`Simitone.Render`) | Owns effects, targets, settings, passes; no game types in `Logic/` so it is unit tested | loaded like `Conversations` (reflection loader from `SpriteCards.Startup`) |
| `PostChain` | Creates the full-screen targets (recreated on `InitScreenTargets`), runs passes in order | Cecil prefix on `PPXDepthEngine.DrawBackbuffer`, or sets `SSAAFunc` (re-applied after every `World.ChangeAAMode`) |
| `FxaaPass`, `SmaaPass` | Shader passes on the colour target | called by `PostChain` |
| `EffectLoader` | `new Effect(device, bytes)` from blobs embedded in the library (avoids the content pipeline) | none |
| `SpriteNormals` | depth to normal, N.L in the sprite shaders | replacement `.xnb` of the object effects, or a pre-pass |
| `LightGrid` | Builds occluder and emitter grid (walls, floors, room ids, object footprints, lamps, windows) from `VMArchitecture` | read on lot change |
| `RadianceCascades` | Pixel-shader cascades over `LightGrid`; output texture merged through `WorldState.PrepareLighting` | Cecil postfix on `PrepareLighting` |
| Options menu entries | AA mode, lighting mode | reuse the `SpriteCardsPatch` Options-menu extension (as for "AI conversations") |

## Decisions
- **Injection, not rebuild.** The source tree (net9, stock MonoGame 3.8.4) does not match the release (net4.x, FSOMonoGame fork, `.xnb` v8 shaders).
- **AA = post-process on the colour target.** MSAA misses alpha-edged sprites and averages colour-encoded depth. SMAA is MIT; FXAA is the quick first step.
- **Software GI (Radiance Cascades), not DXR.** The fork has no compute; RC runs in pixel shaders, needs no denoiser, matches a tile grid.
- **DXR deferred.** Needs a second D3D12 device (Vortice) sharing textures with the D3D11 device; no MonoGame precedent; little visible gain in sprite mode.
- **Two depth paths.** 2D camera: packed depth target; 3D camera: hardware depth not readable. Depth-based effects (normals, GTAO) are 2D-camera only at first; Full 3D gets lighting through its own mesh shaders later.
- **Every target is rebuilt on `SetGraphicsMode`** (the SpriteCards menu triggers it), and every effect honours a runtime off switch.

## Risks
| Risk | Mitigation |
|---|---|
| No MGFX v8 compiler | S1 first; fallback: hand-port effect bytecode via the fork's 2MGFX source, or compile with a pinned MonoGame 3.8.x `mgfxc` and check format version |
| Hooks depend on exact release names | start every patch from the pristine DLLs; patch tests per hook in `Tools/PatchTests` |
| MSAA + MRT interaction in Hybrid2D | S2 measures it; fall back to disabling MSAA when a post-AA mode is on |
| Normals from depth are noisy at silhouettes | edge-aware kernel; clamp; S4 decides |
| Vertical occlusion not representable in plan view | S5: height layers or coarse 3D grid; accept plan-view only for v1 |
| Frame budget unknown | S3 measures on the real GPU before any feature work is sized |
| Licences | check FXAA file; RC code only from MIT/Apache |
