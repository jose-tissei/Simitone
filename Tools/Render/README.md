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
