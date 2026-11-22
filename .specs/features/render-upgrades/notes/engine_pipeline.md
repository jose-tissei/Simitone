# Simitone / FreeSO rendering pipeline: where lighting, shadows and anti-aliasing can be added, and whether injection suffices

Conventions. "R" = repo root `C:\Users\Zé\.t3\projects\the-sims\Simitone`. "APP" = `C:\Users\Zé\.t3\projects\the-sims-run\app` (patched running game, v0.8.12 release). Every "Source" below is a local file:line (no web sources used). Items marked VERIFIED were read in the source or in the shipped DLL/xnb via Mono.Cecil (read-only). Items marked INFERENCE are not directly verified. No files in the repo were modified.

Source tree facts: renderer = `R\FreeSO\TSOClient\tso.world` (assembly FSO.LotView), shared target helper = `R\FreeSO\TSOClient\tso.common\Utils\PPXDepthEngine.cs`, shaders (HLSL source) = `R\FreeSO\TSOClient\tso.content\ContentSrc\Effects\*.fx`, UI/game wiring = `R\Client\Simitone\Simitone.Client\SimitoneGame.cs`, `R\Client\Simitone\Simitone.Windows\Program.cs`.

---

## Q1. How does the LotView world draw a frame (targets, passes, 2D vs 3D, post effects, backbuffer scale/supersampling)?

### Takeaway
One class, `FSO.LotView.World`, owns the frame. `PreDraw` renders the whole lot into a private full-screen render target (`PPXDepthEngine.Backbuffer`); `Draw` then only blits that target to the real backbuffer (optionally through the SSAA downsample shader). That blit is the single, natural place for a post pass, and it already has a replaceable delegate. Lighting is not a screen pass: it is a baked top-down light map per floor sampled by world position inside the sprite/mesh shaders.

### Cited Findings
- Frame order in `GameScreen.Draw`: every layer's `PreDraw` (in reverse), then `SetRenderTarget(null)` + `Clear(ClearColor)`, then every layer's `Draw` in order. Layers are `[SceneMgr (3D world), uiLayer]`, so the world is drawn first and the UI on top. Source: `R\FreeSO\TSOClient\tso.common\Rendering\Framework\GameScreen.cs:203-226`; layer add order `R\Client\Simitone\Simitone.Client\SimitoneGame.cs:182-183`. VERIFIED
- `World.PreDraw` (virtual): camera + `OutsideColor`, `State.PrepareLighting()`, `Blueprint.Changes.PreDraw` (rebuilds dirty tiles/static surface and the light maps), `Static.PreDraw`; then if `UseBackbuffer && Visible`: `PPXDepthEngine.SetPPXTarget(null,null,true)` (binds the private Backbuffer and clears it to transparent black), `InternalDraw(device)`, `device.SetRenderTarget(null)`. Source: `R\FreeSO\TSOClient\tso.world\World.cs:646-683`. VERIFIED
- `World.InternalDraw` (protected virtual) is the actual scene: clear (3D only), `PrepareLighting`, `_2D.Begin`, `ResetMatrices`, `DrawBg` sky dome (3D camera only), `Architecture.Draw2D` (floors, walls, roofs, terrain), `Static.Draw` (cached static scroll buffer, 2D), `PrepareCamera`, `Entities.DrawAvatars`, `Entities.Draw` (objects), `Entities.DrawAvatarTransparency` (ghosts, headlines), `SM64` extra. Source: `World.cs:704-728`. The shipped DLL has the identical call sequence (read with Cecil from `APP\FSO.LotView.dll`: `InternalDraw` calls RasterizerState, Clear, PrepareLighting, Begin, ResetMatrices, DepthStencilState, DrawBg, Draw2D, WorldStatic.Draw, PrepareCamera, DrawAvatars, Draw, DrawAvatarTransparency, SM64Component.Draw). VERIFIED
- `World.Draw` (virtual): when `UseBackbuffer` it only calls `PPXDepthEngine.DrawBackbuffer(Opacity, BackbufferScale)`. Source: `World.cs:686-702`. VERIFIED
- `PPXDepthEngine.DrawBackbuffer`: if `SSAA > 1` it calls the delegate `SSAAFunc(GD, Backbuffer)`; otherwise a `SpriteBatch` blit with `Opacity` and a centered `scale` (so `BackbufferScale` zooms by drawing the target smaller/larger). Source: `R\FreeSO\TSOClient\tso.common\Utils\PPXDepthEngine.cs:141-164`. VERIFIED
- Render targets: `Backbuffer` = `Color`, `Depth24Stencil8`, size `SSAA * viewport`, multisample = `MSAA`, `RenderTargetUsage.PreserveContents`; a second `BackbufferDepth` (color RT used as a depth map) is created only when `!FSOEnvironment.Enable3D`. Source: `PPXDepthEngine.cs:20-30, 171-210`. In Simitone `FSOEnvironment.UseMRT = true` and `SoftwareDepth = false` (Program.cs:128-129) so the 2D sprite shaders write a second color output (depth channel) instead of the software-depth path. Source: `R\Client\Simitone\Simitone.Windows\Program.cs:128-129`; `PPXDepthEngine.SetPPXTarget` lines 41-66. VERIFIED
- 2D vs 3D platform: `World.SetGraphicsMode` picks `WorldPlatform2D` + 2D camera for Full2D/Hybrid2D and `WorldPlatform3D` + 3D camera for Full3D, and calls `ChangeAAMode` every time. Source: `World.cs:366-397`; `R\FreeSO\TSOClient\tso.world\Platform\WorldPlatform2D.cs`, `WorldPlatform3D.cs`. VERIFIED. The project's "Full 3D" mode = `GlobalGraphicsMode.Full3D` via `GraphicsModeControl.ChangeMode(...)` from `R\Tools\SpriteCards\SpriteCards.cs:117-125` (`EngineOf`, lines 38-41). VERIFIED
- In Full3D, `WorldPlatform3D` draws terrain, `bp.WCRC` (3D walls), objects (`obj.Draw`, `RCObject` effect), roof, sorted by `UpdateDrawOrder`; real GPU depth buffer is used. Source: `R\FreeSO\TSOClient\tso.world\Platform\WorldPlatform3D.cs:94-133`. VERIFIED. In 2D/Hybrid modes sprites are drawn through `_2DWorldBatch` (`2DWorldBatch.fx` technique `drawZSpriteDepthChannel`), writing color + a packed depth channel (+ `DEPTH0`), so 2D sprites get per-pixel depth. Source: `R\FreeSO\TSOClient\tso.content\ContentSrc\Effects\2DWorldBatch.fx:378-445`. VERIFIED
- Sprite vertex format `_2DSpriteVertex`: `Position(3)`, `TextureCoordinate`, `WorldPosition(3)`, `ObjectID(+level)`, `Room(2)`. Per-sprite CPU data `_2DSprite`: `Pixel`, `Depth`, `Mask` textures, `WorldPosition`, `ObjectID`, `Room`, `Floor`. So per sprite there is color, a depth texture and a world position; there is NO normal map and no material data. Source: `R\FreeSO\TSOClient\tso.world\Utils\_2DSpriteVertex.cs:10-47`, `_2DSprite.cs:12-47`. VERIFIED
- Existing post-effect: only the SSAA box downsample (`SSAA.fx`, 4-tap, converts sRGB<->linear via polynomials, `SSAASize`, `tex`), driven by `R\FreeSO\TSOClient\tso.world\Utils\SSAADownsample.cs`. There is no bloom/tonemap/SSAO/FXAA anywhere in tso.world/Effects (7 files: GradEffect, GrassEffect, LightMap2DEffect, LightMappedEffect, RCObjectEffect, SpriteEffect, WorldBatchEffect, WorldEffect). VERIFIED by listing `R\FreeSO\TSOClient\tso.world\Effects`.

### Inferences
- Because `Draw` is only a blit of a render target that already contains the finished lot, a post pass at that point sees lit, anti-aliased-at-source color but would not see per-pixel depth in Full3D (the depth is the RT's own depth buffer, not a readable texture) and in 2D modes would need `BackbufferDepth` (private).
- Hybrid2D keeps `CameraMode < _3D`, so 2D-camera modes depth-test sprites with the MRT depth channel; any injected effect that wants depth in those modes must read that channel.

### Gaps
- Exact draw order of floor vs wall vs terrain inside `Architecture.Draw2D` (WorldArchitecture.cs 146 lines) was not traced line by line.
- I did not trace `WorldPlatform2D` internals (static-surface path) beyond `WorldStatic`.

---

## Q2. What lighting exists (room lights, per-object light, sun, shadows) and what per-sprite data is available?

### Takeaway
Lighting is a CPU/GPU-baked, per-floor, top-down 2D light map (`LMapBatch`) containing room lights, window/outdoor sun light and 2D shadow volumes (walls, object footprints, and in Ultra mode 3D object shadow maps). Shaders sample it by world position. Time of day comes from the VM clock into `Blueprint.OutsideTime/OutsideColor`. There is no per-pixel dynamic lighting, no normals, no deferred G-buffer.

### Cited Findings
- Light-mode ladder: `WorldConfig.LightingMode` 0 off / 1 advanced / 2 +3D wall shadows / 3 ultra; `AdvancedLighting = Mode>0`, `Shadow3D = >1`, `UltraLighting = >2`. Other flags: `AA` (0/1/2), `Directional`, `Complex`. Source: `R\FreeSO\TSOClient\tso.world\WorldConfig.cs:11-50`. VERIFIED
- Simitone forces `LightingMode = 3` and `ComplexShaders = true` at every launch (so Ultra is on already); `AntiAlias = aa ? 1 : 0` where `aa` is set only by the `-aa` command-line flag. Source: `R\Client\Simitone\Simitone.Windows\Program.cs:132-135`, flag parse around line 41-42 of the flag block. The release IL confirms: `Simitone.Windows.exe Main` calls `GlobalSettings::set_LightingMode`, `set_AntiAlias`, `set_ComplexShaders` (Cecil). VERIFIED
- Light map machinery: `LMapBatch` renders into `LightMap` (and `LightMapDirection`) with `LightMap2D.fx`; `DrawRoom` draws, for each room, its outdoors/sun contribution then each point light with shadow geometry. Sources: `R\FreeSO\TSOClient\tso.world\LMap\LMapBatch.cs:445-640` (`DrawRoom`), wall shadow volumes `DrawWallShadows` `:813`, object footprint shadows `DrawObjShadows` `:925`, 3D object shadow map via the `RCObject` effect `Draw3DObjShadows` `:949-1000` (used when Ultra), geometry builders `LMap\ShadowGeometry.cs`, per-room point lights `LMap\LightData.cs`. VERIFIED
- Sun/moon: `LMapBatch.BuildOutdoorsLight(tod)` derives `SunVector`, `LightPos/LightDir`, `ShadowMultiplier` from time of day (day = 0.25..0.85 of the day, night shadows x1.33; sun at 45 degrees peak). Source: `LMapBatch.cs:371-441`. VERIFIED
- Time of day source: `VMArchitecture.SetTimeOfDay` reads the VM clock and sets `WorldUI.OutsideColor = TimeOfDayConfig.ColorFromTime(time)` and `WorldUI.OutsideTime = time`; the world copies them each `PreDraw`. Source: `R\FreeSO\TSOClient\tso.simantics\VMArchitecture.cs:155-185`, `World.cs:652`. The light map rebuilds when `OutsideTime` moves more than 0.001: `R\FreeSO\TSOClient\tso.world\Model\BlueprintChanges.cs:44-55`. VERIFIED
- How lights reach pixels: `WorldState.PrepareLighting` pushes `LightMap`, `LightMapDirection`, `LightOffset`, `LightingAdjust` into every effect in `WorldContent.LightEffects` (`_2DWorldBatchEffect`, `GrassEffect`, `RCObject`, `ParticleEffect`, `AvatarEffect`), plus `ambientLight` (256x256 per-room color texture, when advanced lighting is off). Source: `R\FreeSO\TSOClient\tso.world\WorldState.cs:425-460`; list built in `R\FreeSO\TSOClient\tso.world\WorldContent.cs:35-46`. The shared HLSL include `LightingCommon.fx` samples the map at `worldPos * WorldToLightFactor` (`lightProcess*`, `lightInterp`, `lightColorFloor` etc.). Source: `R\FreeSO\TSOClient\tso.content\ContentSrc\Effects\LightingCommon.fx:1-120`; sprite shader use `2DWorldBatch.fx:173-260, 378-445`. VERIFIED
- Avatars (3D Vitaboy meshes) use the same light map via `Vitaboy.fx`, plus a CPU-side list of up to a few nearby point-light positions: `CloseLightPositions` (`R\FreeSO\TSOClient\tso.world\Components\AvatarComponent.cs:148-170, 202-232`) and an `AmbientLight` constant when advanced light is off. VERIFIED
- 3D object meshes (`RCObject.fx`, `DGRPRenderer`) pick pass 1 (directional light) when `Directional && AdvancedLighting && Room < 65533`. Source: `R\FreeSO\TSOClient\tso.world\Utils\DGRPRenderer.cs:286, 314, 330`. VERIFIED
- Per-sprite data: color texture, depth texture, mask, world position, object id, room id, floor. No normals/albedo/emissive buffers; "Luminous" DGRP sprites are marked by assigning room 65535 (unlit). Source: `_2DSprite.cs:12-24`; `DGRPRenderer.cs:227`. VERIFIED
- Sky: `SkyDomeComponent` (3D camera only) uses `Blueprint.OutsideTime`; Source: `R\FreeSO\TSOClient\tso.world\Components\SkyDomeComponent.cs:15-28`, drawn in `WorldStatic.DrawBg` (`WorldStatic.cs:98-112`). VERIFIED

### Inferences
- Because shadows exist only as 2D footprints/volumes baked into a per-floor map (plus an optional 3D object shadow map), contact shadows, soft area shadows, AO, or true ray-traced/GI lighting would be new work; the existing map can be re-weighted or replaced, but it carries no height/normal data.
- Real "ray tracing" in this engine would have to be a full-screen compute/raymarch against a reconstructed height field (the world is a tile grid with per-tile heights/walls in `Blueprint`), not against the sprites themselves.

### Gaps
- Exact contents/format of `LightMap` channels beyond the HLSL comment ("RGBA: LightIntensity, OutdoorsIntensity, LightIntensityShad, OutdoorsIntensityShad", `LightingCommon.fx:35`).
- Roof/terrain lighting paths (`RoofComponent`, `TerrainComponent`) not traced.

---

## Q3. Which graphics settings exist and how does the user toggle them?

### Takeaway
Engine supports AA 0 = none, 1 = MSAA 4x, 2 = SSAA 2x (with an MSAA-8 fallback in 2D camera modes). Simitone only exposes it as the `-aa` launch flag and never offers SSAA; `GlobalSettings` values are overwritten at startup. There is no in-game graphics options UI for AA or lighting.

### Cited Findings
- `World.ChangeAAMode`: `WorldConfig.Current.AA` 0 -> MSAA 0/SSAA 1; 1 -> MSAA 4/SSAA 1; 2 -> MSAA 0/SSAA 2; if SSAA>1 and camera is 2D (`CameraMode < _3D`) it falls back to MSAA 8/SSAA 1. It sets `PPXDepthEngine.SSAAFunc = SSAADownsample.Draw` and rebuilds the targets (`InitScreenTargets`) when MSAA/SSAA changed. It runs from `ChangedWorldConfig` and from every `SetGraphicsMode`. Source: `World.cs:969-996, 998-1055, 366-397`. VERIFIED
- `GameResized` rebuilds screen targets and the 2D buffers at `newSize / SSAA`. Source: `World.cs:117-126`. VERIFIED
- World config is assembled in `SimitoneGame.Initialize` from `GlobalSettings` (LightingMode, SmoothZoom, SurroundingLotMode, AntiAlias, DirectionalLight3D, ComplexShaders, EnableTransitions). Source: `R\Client\Simitone\Simitone.Client\SimitoneGame.cs:113-125`. VERIFIED
- But `Program.Main` overrides `LightingMode=3`, `AntiAlias = aa?1:0`, `ComplexShaders=true`, `EnableTransitions=true` after reading config, so `config.ini` values for these are ignored. Source: `Program.cs:132-137`; release IL has the same setters. VERIFIED. Documented flags: `-ide -3d -aa -jit -dx|-dx11 -gl|-ogl -touch -nosound -lang<N> -hz<N> -path<dir>`, plus patched `-2d`. Source: `R\docs\architecture\AI-CONTEXT.md` (line 16). VERIFIED (doc); flag parser `Program.cs:70-95`.
- `BackbufferScale` (World.cs:49) is a float zoom applied in the final blit (`PPXDepthEngine.DrawBackbuffer`), reset to 1 on mode switch (`World.cs:378`, 554); it is not a render-resolution scale. `PPXDepthEngine.SSAA` is the supersample integer (fields `SSAA`, `MSAA` are public static). VERIFIED
- Other consumers of AA: UI/head/pie-menu 3D scenes use `GlobalSettings.AntiAlias > 0` -> 8x MSAA (`UIIconCache.cs:67`, `UIPieMenu.cs:148`, `UISelectSkinAlert.cs:129`); `UIExternalContainer.cs:83`. VERIFIED
- The only in-lot Options menu additions are our four display-mode buttons + reload + AI conversations toggle (patched into `UIMainPanel.Switcher_OnCategorySelect`), there is no AA/lighting toggle. Source: `R\Tools\SpriteCardsPatch\Program.cs:58-114`. VERIFIED

### Inferences
- Setting `PPXDepthEngine.SSAA`/`MSAA` and calling `PPXDepthEngine.InitScreenTargets()` at runtime from an injected library is enough to enable SSAA/MSAA live (the fields and method are public in `APP\FSO.Common.dll`, verified with Cecil), but `ChangeAAMode` will reset them on the next graphics-mode change unless `WorldConfig.Current.AA` is also set (it is a public static instance field, `WorldConfig.Current`).

### Gaps
- Whether MSAA on the DirectX 11 swap chain interacts with MRT depth output in 2D modes (known FreeSO behavior: they fall back to 8x MSAA instead of SSAA in 2D) was not tested at runtime.

---

## Q4. Cheapest hook points for a full-screen post pass and a lighting-composite replacement; can shaders ship without rebuilding?

### Takeaway
Best post-pass hook: the `PPXDepthEngine.SSAAFunc` delegate (already invoked at the exact "compose world RT to screen" moment, before UI), or Cecil-patch `World.Draw` / `PPXDepthEngine.DrawBackbuffer`. Effects are compiled `.xnb` files on disk under `Content\DX\Effects` (and `OGL\Effects`) and are loaded through a ContentManager at startup, so replacing them by file works without rebuilding any DLL, but only if the new shader is compiled with a matching MonoGame effect compiler. New effects can also be loaded by the injected library itself.

### Cited Findings
- Post-pass seam 1 (no Cecil needed for the call itself): `PPXDepthEngine.SSAAFunc` is `public static Action<GraphicsDevice, RenderTarget2D>`; `DrawBackbuffer` calls it instead of the plain blit only when `SSAA > 1`; `World.ChangeAAMode` reassigns it to `SSAADownsample.Draw` on every call (graphics-mode change, config change). Source: `PPXDepthEngine.cs:141-152`, `World.cs:969-973`. VERIFIED. So an injected library must either set `PPXDepthEngine.SSAA = 2` (or a custom 1x path) and re-assign `SSAAFunc` after each `ChangeAAMode`, or Cecil-patch `ChangeAAMode`/`DrawBackbuffer`.
- Post-pass seam 2 (patch): `World.Draw(GraphicsDevice)` (`APP\FSO.LotView.dll`, `System.Void FSO.LotView.World::Draw(GraphicsDevice)`, virtual public) or `PPXDepthEngine.DrawBackbuffer(Single,Single)` (public static in `APP\FSO.Common.dll`). At that point only the world has been drawn on the real backbuffer; UI layer comes after (GameScreen.cs:203-226). A post-pass hook there will not affect the UI. VERIFIED
- Scene-insert seam: `World.InternalDraw(GraphicsDevice)` is `protected virtual` in the DLL (Cecil: `pub=False fam=True virt=True`); a prefix/suffix call inserted with the same pattern as `SpriteCardsPatch` (insert `call` at the first instruction) lets a library run code inside the world render target, e.g. after `DrawAvatarTransparency` and before `SM64`, while the world RT (`PPXDepthEngine.GetBackbuffer()`, public static) is still bound. VERIFIED (signature); INFERENCE (practical effect).
- Lighting-composite seam: `WorldState.PrepareLighting(WorldState)` (virtual public; sets all light textures/offsets every frame, called in `PreDraw` and `InternalDraw`) is the one place the light map textures are handed to the shaders; replacing `LightEffect.AdvancedLight`/`AdvancedDirection` here (or the textures `State.Light.LightMap`) changes lighting for floors, walls, sprites, meshes and avatars at once. `WorldState.ClearLighting(bool)` is the other (used for thumbnails). VERIFIED (source `WorldState.cs:425-476`; signatures in DLL).
- Effects are static public fields of `WorldContent`, loaded by `LoadEffects` from `ContentManager.Load<Effect>("Effects/<name>")` with `RootDirectory = Content.RootDirectory` (= `Content/DX/` or `Content/OGL/`, set via `FSOEnvironment.GFXContentDir` `Program.cs:119-120`). Fields (all `public static` in the shipped DLL): `_2DWorldBatchEffect`, `Grad2DEffect`, `Light2DEffect`, `GrassEffect`, `RCObject`, `SSAA`(raw `Effect`), `SpriteEffect`, `ParticleEffect`, `AvatarEffect`, `LightEffects`, `ContentManager`. Source: `WorldContent.cs:13-70`; DLL fields verified with Cecil. `LoadEffects(bool reload)` is public and re-loadable. VERIFIED
- On-disk effect files in APP: `Content\DX\Effects\*.xnb` (14 files: 2DWorldBatch, colorpoly2D, gradpoly2D, GrassShader(+iOS), LightMap2D, MSDFFont, ParticleShader, PixShader, RCObject, SpriteEffects, SSAA, VerShader, Vitaboy) and `Content\OGL\Effects\*.xnb` (17). All dated 2025-01-16 (original release). No loose `.fx` ships (0 `.fx` under APP). XNB header bytes: DX files start `XNBw`, OGL `XNBd`; inner effect blob has `MGFX` magic, version byte 8, profile byte 1 (DX11) / 0 (OpenGL). Verified by reading bytes of `SSAA.xnb`, `2DWorldBatch.xnb` in both folders. VERIFIED
- HLSL sources for all effects are in the repo: `R\FreeSO\TSOClient\tso.content\ContentSrc\Effects\*.fx` (2DWorldBatch, LightMap2D, LightingCommon, RCObject, SpriteEffects, SSAA, Vitaboy, ParticleShader, GrassShader, PixShader, VerShader, gradpoly2D, colorpoly2D, MSDFFont) with content lists `TSOClientContent.mgcb`, `TSOClientContentDX.mgcb`. The old build helper `compileForMono.bat` calls `C:\Program Files (x86)\MSBuild\MonoGame\v3.0\2MGFX.exe` (absent on this machine: that path does not exist). VERIFIED
- The release's `MonoGame.Framework.dll` is a custom FSOMonoGame fork (`APP\Monogame\Windows\MonoGame.Framework.dll`, .NET Framework 4.5; fork source in `R\FreeSO\Other\libs\FSOMonoGame`). Its effect reader expects MGFX version 8. VERIFIED. `~/.nuget` has no MonoGame packages and no global `mgcb`/`mgfxc` tool is installed (`dotnet tool list -g` = dotnet-script, ilspycmd only). VERIFIED

### Inferences
- Shipping a replaced `.xnb` is feasible without touching DLLs only if its MGFX version/profile matches the fork's reader (version 8; the fork has its own `2MGFX` tool source under `R\FreeSO\Other\libs\FSOMonoGame\Tools`, not verified to build). An alternative that avoids the toolchain issue: the library compiles a custom `Effect` at runtime from a pre-built blob it embeds, or constructs `new Effect(device, bytes)` from an `.mgfxo` it carries.
- Replacing e.g. `SSAA.xnb` in place gives an immediate no-code post effect for the `SSAA>1` path (technique 0 is applied, parameters `SSAASize`, `tex`), since `SSAADownsample.Draw` just sets those two parameters and draws a fullscreen quad (`SSAADownsample.cs:8-21`). Keeping those parameter names makes it drop-in.

### Gaps
- Not verified: whether any build of the fork's `2MGFX` can be produced here; not verified at runtime that a replaced `.xnb` loads.

---

## Q5. Can a Cecil-injected library reach GraphicsDevice, the world render target, WorldState? Candidate methods and signatures.

### Takeaway
Yes. All needed members are public static or reachable from `World`/`WorldState` instance arguments of methods that can be hooked using exactly the pattern in `Tools/SpriteCardsPatch`. The existing hook already receives a `WorldState` (has `Device`, `Light`, `Cameras`, `OutsideColor`).

### Cited Findings (signatures read from `APP\*.dll` with Mono.Cecil; the DLLs in APP are the patched ones, the signatures are original)
- `FSO.LotView.World::PreDraw(Microsoft.Xna.Framework.Graphics.GraphicsDevice)` public virtual
- `FSO.LotView.World::Draw(Microsoft.Xna.Framework.Graphics.GraphicsDevice)` public virtual
- `FSO.LotView.World::InternalDraw(Microsoft.Xna.Framework.Graphics.GraphicsDevice)` protected virtual
- `FSO.LotView.World::ChangeAAMode(GraphicsDevice)` public; `World::GameResized()`; `World::Initialize(FSO.Common.Rendering.Framework._3DLayer)`; `World::InitDefaultGraphicsMode()`
- `FSO.LotView.WorldStatic::PreDraw(GraphicsDevice, WorldState)`, `Draw(WorldState)`, `DrawBg(GraphicsDevice, WorldState, BoundingBox[], bool)`
- `FSO.LotView.WorldEntities::DrawAvatars(GraphicsDevice, WorldState)`, `Draw(GraphicsDevice, WorldState)`, `DrawAvatarTransparency(GraphicsDevice, WorldState)`, `StaticDraw(GraphicsDevice, WorldState, Vector2)`
- `FSO.LotView.WorldArchitecture::Draw2D(GraphicsDevice, WorldState)`, `DrawDynamic(GraphicsDevice, WorldState, bool)`, `StaticDraw(...)`
- `FSO.LotView.WorldState::PrepareLighting(WorldState)` virtual, `ClearLighting(bool)`
- `FSO.Common.Utils.PPXDepthEngine`: public static `InitGD(GraphicsDevice)`, `InitScreenTargets()`, `SetPPXTarget(RT,RT,bool[,Color])`, `GetBackbuffer()` returns the world render target, `DrawBackbuffer(float opacity, float scale)`, `GetWidthHeight()`, `CreateRenderTarget(...)`; public static fields `SSAA`, `MSAA`, `SSAAFunc`, `WithOpacity`; private `GD`, `Backbuffer`, `BackbufferDepth`, `SB` (reachable by reflection).
- `FSO.LotView.WorldContent` public statics (see Q4).
- GraphicsDevice access: `WorldState.Device` (WorldState.cs, used in `PrepareLighting`), `World.m_Device`, `GameFacade.GraphicsDevice` (used throughout Simitone.Client, e.g. `TS1GameScreen.cs:456`), `PPXDepthEngine.GD` (private).
- The existing patcher already shows the three insertion styles needed: prefix call at method start with `ldarg.0`/`ldarg <param>`, replace of a call/field load, and "before every `ret`" for constructors. Source: `R\Tools\SpriteCardsPatch\Program.cs:25-56, 116-131, 161-214`. Hook library pattern: `R\Tools\SpriteCards\SpriteCards.cs` targets `net461` and references `FSO.LotView`, `FSO.Common`, `FSO.UI`, `FSO.SimAntics`, `Simitone.Client`, `MonoGame.Framework`, `FSO.Files` straight from `APP` (`R\Tools\SpriteCards\SpriteCards.csproj`). `Startup(string[])` (called from `Main`) can register everything early. VERIFIED
- Hook already running per frame: `SpriteCards.Apply(ObjectComponent, WorldState)` at the start of `ObjectComponent.{DoDraw,Draw,Update,Preload}` so an injected library already receives a `WorldState` once per object per frame; a library therefore does not even need a new patch to obtain `WorldState` (use it to capture `state.Device`, `state.Light`, `state.Camera`). VERIFIED (`SpriteCardsPatch/Program.cs:31-42`).

### Candidate hook plan (INFERENCE built on the verified signatures)
1. Post pass / AA: prefix or suffix `PPXDepthEngine.DrawBackbuffer(float,float)` or `World.Draw` with a call into the library that gets `PPXDepthEngine.GetBackbuffer()` and draws a full-screen FXAA/SMAA/TAA-like effect. Or set `SSAAFunc` from the library.
2. Lighting replacement: suffix `WorldState.PrepareLighting(WorldState)`, `LMapBatch` redraw, or `WorldContent.LoadEffects(bool)` to swap `WorldContent._2DWorldBatchEffect.AdvancedLight` / effect instances.
3. Scene overlay (extra lights, volumetric, SSAO): suffix `World.InternalDraw` before `device.SetRenderTarget(null)` in `PreDraw`.
4. Shadow map from 3D meshes: reuse `LMapBatch.Draw3DObjShadows` (Ultra mode already on).

### Gaps
- Depth texture access in Full3D requires making the RT's depth readable (the world Backbuffer is `Depth24Stencil8`, not shader-readable in MonoGame DX11 fork) or adding an extra depth-output pass; not verified.

---

## Q6. How is the project built; can FreeSO source be compiled and does it match the release? Patch vs rebuild.

### Takeaway
The source tree targets .NET 9 and the stock MonoGame 3.8.4 NuGet, whereas the running game is the prebuilt v0.8.12 release on .NET Framework 4.x with a custom MonoGame fork and compiled `.xnb` effects. A source build would therefore not match the released binaries (different runtime, different MonoGame, different shader compiler/format); the repo's documented strategy is patching release DLLs. Injection can cover everything in Q1-Q5; a rebuild would be needed only for deep changes (new vertex data, new render targets inside shaders).

### Cited Findings
- Source csproj targets: `FSO.LotView.csproj` net9.0 with `MonoGame.Framework.DesktopGL 3.8.4`; `Simitone.Client.csproj` net9.0; `Simitone.Windows.csproj` net9.0-windows with `MonoGame.Framework.DesktopGL` and `WindowsDX` 3.8.4. Source: `R\FreeSO\TSOClient\tso.world\FSO.LotView.csproj:4,19`, `R\Client\Simitone\Simitone.Client\Simitone.Client.csproj:4`, `R\Client\Simitone\Simitone.Windows\Simitone.Windows.csproj:4,23-24`. VERIFIED
- Release binaries: `APP\FSO.LotView.dll` TargetFramework `.NETFramework,Version=v4.5`; `APP\Monogame\Windows\MonoGame.Framework.dll` `.NETFramework,Version=v4.5` (assembly version 0.0.0.0, the FSOMonoGame fork); `APP\Simitone.Windows.exe` `.NETFramework,Version=v4.6.1` with `supportedRuntime v4.0`. VERIFIED (Cecil).
- Repo docs state the same: "source and release differ" (`R\docs\architecture\AI-CONTEXT.md:11, 77`); CI pins release v0.8.12 and builds only the Tools against it (`R\Tools\ci.ps1`, `.github/workflows/tools-ci.yml`). VERIFIED
- .NET SDKs installed: 9.0.203, 9.0.321, 10.0.400, 11.0.100-preview. Only SDK builds were possible; building source was NOT attempted (no project built in this session). `R\FreeSO` is a git submodule at `f86bbd34 (beta-update-90-120-gf86bbd34)`. VERIFIED
- The shipped DLL code structure matches source for the pieces examined (World.InternalDraw call sequence, PPXDepthEngine members, WorldContent fields all identical to the source read here). VERIFIED for those. The release's `Program.Main` namespace is `FSO.Client.GlobalSettings` where master source has it under `FSO.UI` (IL: `FSO.Client.GlobalSettings::set_LightingMode`), a small proof that source != release at the type-layout level. VERIFIED
- Mod toolchain: Mono.Cecil 0.11.6 patcher on .NET 9 (`R\Tools\SpriteCardsPatch\SpriteCardsPatch.csproj`), hook libs on net461 (`R\Tools\SpriteCards\SpriteCards.csproj`) against `AppDir` = APP; patching always starts from the original DLLs; output written to a separate dir. Source: `R\Tools\SpriteCardsPatch\Program.cs:1-23`; `R\docs\architecture\AI-CONTEXT.md:21`. VERIFIED
- 28 unit + patch tests exist that check the patcher against the real release (`R\Tools\PatchTests`) and 26+ e2e checks drive the game with `simtool` (screenshots via Windows Graphics Capture): `R\Tools\SimTool`, `R\Tools\e2e\Run-E2E.ps1`. Useful to validate visual results. VERIFIED (doc).

### Inferences
- The custom shaders/tools needed (HLSL -> MGFX v8) are the only part of the pipeline with no ready compiler on this machine (no `2MGFX.exe`/`mgfxc`); the fork source in `R\FreeSO\Other\libs\FSOMonoGame` likely contains it but was not built. A rebuild-from-source route would additionally need a net461/FSOMonoGame toolchain or a re-port to MonoGame 3.8.4, and could not be dropped into the existing release.

### Gaps
- Not tested: whether source `master` actually builds with SDK 9/10 here, and whether a source build would run against the user's Sims data in the same way.

---

## Q7. Risks: determinism, multiplayer, performance, interaction with 2D/3D modes and SpriteCards patches

### Takeaway
Rendering is separated from simulation, so lighting/AA changes are render-only and do not touch VM determinism; the real risks are mode interactions (AA 2D fallback, depth path), per-frame cost, and the fragility of hooks to the exact release.

### Cited Findings
- Determinism: lighting inputs come from the VM clock (`VMArchitecture.SetTimeOfDay`) one way (VM -> `Blueprint.OutsideTime/OutsideColor`); the renderer only reads. A render-only change (post pass, light map replacement) does not write VM state. The existing hook `SpriteCards.Apply` is also render-only (it changes `ForceImmediate` and each object's `DrawGroup` mode, not VM state; `SpriteCards.cs`, "every object's render mode and `WorldState.ForceImmediate` are set explicitly every frame", AI-CONTEXT.md:70). VERIFIED (source)
- Multiplayer: FreeSO's online servers were shut down Dec 2024 and Simitone is single-player/offline; the server projects are not referenced by the Simitone projects (AI-CONTEXT.md:58-62, ARCHITECTURE.md). VERIFIED (doc). No lockstep render dependence exists to break.
- Performance facts: light maps are rebuilt only when rooms/lights/time change (`BlueprintChanges` flags, `LMapBatch.ParseInvalidated`, `LMapBatch.cs:266`) and are cached; shadow generation is on the GPU with scissor rects. Ultra lighting (mode 3) is already on, which adds `Draw3DObjShadows` per room light. SSAA 2 = 4x pixels for the whole world RT and every shader pass, but the 2D camera mode refuses SSAA (falls back to MSAA 8) (World.cs:990-994). VERIFIED (source)
- 2D/3D mode interactions with SpriteCards: `SpriteCards.ApplyEngineMode` calls `GraphicsModeControl.ChangeMode`, which triggers `World.SetGraphicsMode` -> `ChangeAAMode` -> `InitScreenTargets` (targets are disposed and recreated, `PPXDepthEngine.cs:20-30`). Any injected render targets must survive that event. Hybrid modes set `ForceImmediate` so sprites and meshes are interleaved in one pass and rely on `_2DWorldBatch.DrawImmediate` always rebinding the quad index buffer (the DX path patched by `SpriteCardsPatch/Program.cs:43-54`). A post effect placed after `InternalDraw` is unaffected by that interleaving; a scene-level injection inside `InternalDraw` must not leave the index buffer/depth state altered (the exact bug fixed by the DrawImmediate patch). VERIFIED (source/patch)
- The depth path differs per camera: 2D camera uses `BackbufferDepth` + MRT depth channel; 3D camera has `BackbufferDepth == null` and relies on the hardware depth buffer (`PPXDepthEngine.cs:27-29, 41-66`). An effect needing scene depth needs two code paths. VERIFIED
- Fragility: hooks depend on exact method names in the release (AI-CONTEXT.md:77). Patching must always run from the original DLLs (SpriteCardsPatch Program.cs header; AI-CONTEXT.md:79). A second hook library for rendering should follow the same "pure logic + game hook" split and patch tests. VERIFIED (doc)
- APP currently contains patched: Conversations.dll, FSO.Client.dll, FSO.Common.dll, FSO.LotView.dll, FSO.SimAntics.dll, FSO.UI.dll, Simitone.Client.dll, SimTestHook.dll, SpriteCards.dll (modified after 2025-02-01 by file time); FSO.Common.dll is patched (by the InputHook/other patches), so a render hook must coordinate with those patchers (all input = original DLLs; patchers chained via backup folders `the-sims-patch\backup\...`). VERIFIED (file times); the exact chain is in AI-CONTEXT.md:79.

### Inferences
- A post-process AA (FXAA/SMAA-style) applied at `DrawBackbuffer` is the lowest-risk way to improve edges in 2D-camera modes where SSAA is blocked and MSAA does not smooth MRT/depth-tested sprite edges well; true supersampling remains available only in Full 3D (engine already supports `AA=2`).
- Lighting overhaul at the light-map level (feeding richer `LightMap` data) is deterministic and cheap; per-pixel shading, shadows from 3D meshes, or ray marching require a depth/normal source that the 2D sprite path does not provide (only a 16-bit packed depth), so they are best limited to Full 3D.

### Gaps
- No runtime profiling was performed; nothing was run. Frame-time numbers and GPU memory impact are unknown.
- Interaction between MSAA on the DX11 backbuffer and the MRT depth render target in Hybrid2D was not tested.
