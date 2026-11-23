# Render upgrades (anti-aliasing, lighting): toolchain

Plan: `.specs/features/render-upgrades/`. This folder holds what is needed to ship custom shaders into the released game.

| Piece | What |
|---|---|
| `Mgfx/` | The effect compiler of the FSOMonoGame fork the game ships with (writes MGFX version 8). `gen_csproj.py` builds an SDK-style project from the fork's Protobuild definition; build with `dotnet build Tools/Render/Mgfx -c Release -o Tools/Render/Mgfx/out`. |
| `Build-Effects.ps1` | `-Fx my.fx -Template <game>\Content\DX\Effects\SSAA.xnb -Out my.xnb`: compiles the HLSL and wraps it in the game's `.xnb` container. |
| `Shaders/TestTint.fx` | The S1 proof: the stock SSAA shader with a red tint. |

## S1 result (done)
- Rebuilding the stock `SSAA.fx` with this compiler reproduces the shipped `SSAA.xnb` **byte for byte** (3261 bytes), so the toolchain matches the game's format exactly.
- A tinted `SSAA.xnb` dropped into `Content\DX\Effects` is loaded by the game: the whole world renders red and the UI does not (`s1-tint-proof.png` in the spec folder). That also shows the SSAA full-screen pass sits between the world and the UI, which is where the anti-aliasing pass will go.
- The SSAA effect is only used when the engine's SSAA mode is on, and the engine refuses SSAA in the 2D camera modes (it uses 8x MSAA instead). `simtool aa [0|1|2]` switches the mode at run time (Full 3D needed for SSAA).

## Anti-aliasing (built)
`Render.dll` adds an optional post pass at the world-to-screen blit (before the UI is drawn): **FXAA** and **SMAA 1x**.
- Options menu button "Anti-aliasing: Off / FXAA / SMAA" (AA icon, green when on); the choice is saved in `Documents\Simitone\render.profile`
  (`aa=off|fxaa|smaa`, `hidef=on|off`).
- SMAA needs the HiDef graphics profile, which the shipped game does not request; the library asks for it when the adapter supports it
  (without it SMAA falls back to FXAA). Costs on an RTX 4070 at 1080p: FXAA 0.02 ms, SMAA 0.08 ms per frame.
- `simtool render status | aa off|fxaa|smaa | frames | repeat N | vsync on|off`, `simtool aa [0|1|2]` (engine MSAA/SSAA).
- Shaders: `Shaders/Fxaa.fx`, `Shaders/Smaa.fx` (+ reference `SMAA.hlsl`, MIT, lookup tables from `make_smaa_tables.py`), compiled with
  `Build-Effects.ps1 -Raw` into the `.mgfxo` files that are embedded in the library.
- Measure: `Measure-Frames.ps1`. Results and decisions: `.specs/features/render-upgrades/notes/measurements.md`.

## Lighting (prototype)
The engine bakes the lot's lighting into a plan-view light map and gives it to every world shader in `WorldState.PrepareLighting`. With `light=bounce`
(`Documents\Simitone\render.profile`, or `simtool render light mode bounce`) `Render.Lighting` runs a gather pass over that light map and adds a share of the
surrounding light to texels that are not fully lit, then replaces the texture the shaders sample. Needs the HiDef profile. Off by default. Tune with
`simtool render light tune <tiles> <gain>` (defaults 3 tiles, 0.6). Known limits: no occlusion yet (light can bleed through walls), recomputed every call.

## Radiance cascades (Options > "Dynamic lighting")
- `light=rc` in `render.profile`; `simtool render light mode off|bounce|rc`, `render light rc <gain> <startPx>` (defaults 150 and 4; optional 3rd and 4th values: lamp brightness 1 and glow disc radius 3 tiles), `render light walls on|off` (off = ignore all walls, to see what
  they block), `render light view walls|gather|off`, `render light probe|dirprobe|wallmap` (dump light map, direction map, wall tiles as text). Needs the HiDef profile.
- `Logic/LightGrid.cs` rasterises the lot's walls at 8 px per tile into an atlas shaped like the engine's light map. `Shaders/Cascades.fx` runs three cascades of pixel-shader ray
  marching; only the engine's lamp lights (its list of room point lights, not the sun and moon that fill the light map) are sources, as small glowing discs, so lamps spill into dark areas and stop at walls. Plan view only: no vertical occlusion.

## Anti-aliasing (Options > "Anti-aliasing")
SMAA 1x (`aa=smaa`). Edge sensitivity `simtool render smaa <threshold> <contrast>` (default 0.2, 2). FXAA (`aa=fxaa`) is still there but blurs textures. `Measure-AA.py off.png aa.png [whole]`
prints texture sharpness and hard-edge ratios from two screenshots of the paused game.

## Directional sprite shading (experimental, not installed)
`Make-WorldEffect.py` re-enables the engine's own disabled third sprite pass (normals from the depth sprite through `ddx/ddy`, shaded by the engine's direction light map) and
`Shaders/World2D.xnb` is the compiled result. To try it: copy it over `Content\DX\EffectsDWorldBatch.xnb` (keep the original) and set `shade=on`. Measured effect: almost nothing visible.
The hardware depth buffer cannot be sampled by shaders in this engine, which is why normals come from the sprite shader's derivatives instead of a depth pre-pass.
