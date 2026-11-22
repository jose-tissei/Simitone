# Render upgrades: anti-aliasing, dynamic lighting / GI, ray tracing

Scope: Large/Complex (new domain, no precedent in this repo). Research: [research.md](research.md), raw notes in `notes/`.
Delivery model: injection into the v0.8.12 release (Mono.Cecil patch plus a separately built library), same as `Tools/SpriteCards` and
`Tools/Conversations`. No engine source rebuild (research: the source tree is net9/stock MonoGame, the running game is net4.x/FSOMonoGame fork).

## Goals
1. Smooth jagged edges of sprites and meshes (today: only 4x MSAA via `-aa`, which misses alpha-edged sprites).
2. Lighting that reacts in real time: surfaces facing a lamp are lit, light bounces and colour bleeds, soft shadows.
3. "Ray tracing" as the visible result (soft shadows, bounce light), by software methods; hardware DXR only as a research track.

## Requirements

| ID | Requirement | Priority |
|---|---|---|
| RND-01 | A compiled custom effect can be loaded by the game without rebuilding the engine (toolchain proven) | P0 |
| RND-02 | A post-process AA pass (FXAA first, then SMAA 1x) runs on the world colour target at the world-to-screen blit, before the UI | P0 |
| RND-03 | UI and text are never anti-aliased by the pass | P0 |
| RND-04 | AA is selectable in the Options menu (Off / FXAA / SMAA) and the choice persists; Off restores the original frame exactly | P0 |
| RND-05 | The pass survives graphics-mode switches (2D, 3D arch, Full 3D, list) and resizes: targets are recreated, the delegate re-applied | P0 |
| RND-06 | Sprite sampling hygiene: premultiplied alpha, edge dilation, smooth sampling at fractional zoom (optional follow-up) | P1 |
| RND-07 | Normals derived from the sprite depth, and N.L shading from the game's lights in the sprite shaders | P1 |
| RND-08 | Plan-view occluder and emitter grid built from walls, floors, rooms and objects, updated when the lot changes | P1 |
| RND-09 | 2D Radiance Cascades (pixel shaders only) over that grid gives soft shadows and bounce light, fed into the existing light-map shader inputs | P1 |
| RND-10 | Every new effect has an Options toggle and can be turned off at runtime; frame time is measured and stays within budget (see below) | P0 |
| RND-11 | Optional GTAO contact darkening | P2 |
| RND-12 | DXR proof of concept in Full 3D only (second D3D12 device via Vortice, shared texture) | P3, research |
| RND-13 | Tests: patch tests against the real release, unit tests for pure logic, e2e checks with screenshots (edge-quality metric, lighting change) | P0 |

## Budget and constraints
- 1920x1080, the user's NVIDIA RTX-class GPU (model to be recorded in spike 3). Target: AA <= 1 ms, GI <= 4 ms, total added frame time <= 6 ms.
- Fork limits: DX11 path, no compute shaders, no readable depth in 3D mode, effects are MGFX v8 `.xnb`.
- Must not change the simulation (rendering only reads it), so no determinism risk.
- Must coexist with the SpriteCards modes and never leave the index buffer or depth state changed (the `DrawImmediate` bug).
- Licences: SMAA is MIT; verify the FXAA 3.11 file; reuse RC code only from MIT/Apache repos (sparse_rc_2d, solis_2d).

## Out of scope
DLSS / FSR / XeSS (native SDKs, DX12, motion vectors), TAA (ghosting, weeks of tuning), switching MonoGame fork, source rebuild.

## Open questions (decided by spikes, see tasks)
- Can an MGFX v8 compiler be produced from `FreeSO/Other/libs/FSOMonoGame/Tools`? (S1)
- Does MSAA plus the MRT depth target survive a post pass in Hybrid2D? (S2)
- Is the packed depth precise enough for usable normals? (S4)
- How to handle vertical occlusion (an object shading a wall) in a plan-view grid? (S5)
