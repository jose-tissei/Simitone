# Anti-aliasing options for a mixed 2D-sprite + 3D-mesh MonoGame renderer (Simitone / FreeSO), 1080p and up

Legend: [V] = verified from a fetched source or from the local repo. [I] = inferred or from general graphics knowledge, not verified in this session. Date context: Nov 2026. Web search results were thin on several points (flagged in Gaps).

## 1. What Simitone/FreeSO already has for AA (local repo)

### Takeaway
Simitone already ships a three-state AA setting (off / 4x MSAA / 2x2 SSAA) built on a custom MSAA-capable MonoGame fork, with a SSAA box-downsample shader. There is NO SMAA, FXAA, TAA, or upscaler anywhere in the repo. MSAA is applied to the whole-world backbuffer render target, but several UI paths disable MSAA on DirectX.

### Cited Findings
All paths below are under `C:\Users\Zé\.t3\projects\the-sims\Simitone\` (abbrev `S\`). [V] for all.
- Search for `SMAA`, `FXAA`, `SuperSample`, `SSAA` found no SMAA/FXAA code. The `SSAA` hits are FSO's own supersampling: `S\FreeSO\TSOClient\tso.world\Utils\SSAADownsample.cs`, `S\FreeSO\TSOClient\tso.content\ContentSrc\Effects\SSAA.fx`, `WorldContent.cs` (loads `Effects/SSAA`), `PPXDepthEngine.cs`. Other hits (`SM64Component`, `Vitaboy.fx`, `LightMap2D.fx`, `LMapBatch`, `LotFacadeGenerator` with `SUPERSAMPLE_COUNT`) are unrelated or local supersampling of facades/lightmaps. No `TAA`, `FSR`, `DLSS` search was run on the repo beyond the terms the task listed.
- Setting: `GlobalSettings.AntiAlias` int, default "0" (`S\FreeSO\TSOClient\FSO.UI\GlobalSettings.cs` lines ~62, 134). Persisted in config.ini.
- Simitone launcher: `-aa` flag sets `GlobalSettings.Default.AntiAlias = aa ? 1 : 0` (`S\Client\Simitone\Simitone.Windows\Program.cs` line 133). `SimitoneGame.cs` line 121 passes `AA = settings.AntiAlias` into `WorldConfig`.
- `WorldConfig.AA` int (`S\FreeSO\TSOClient\tso.world\WorldConfig.cs` line 47).
- Mode mapping, `World.ChangeAAMode` (`S\FreeSO\TSOClient\tso.world\World.cs` lines 969-996): AA 0 = no AA; AA 1 = `PPXDepthEngine.MSAA = 4`; AA 2 = `SSAA = 2` (2x2 supersample, i.e. 4x pixels). Special case: if SSAA>1 and camera mode is below `CameraRenderMode._3D` (i.e. the 2D camera), it swaps to MSAA 8 and SSAA 1. Calls `PPXDepthEngine.InitScreenTargets()` when changed.
- `PPXDepthEngine` (`S\FreeSO\TSOClient\tso.common\Utils\PPXDepthEngine.cs`): world is rendered into a `Backbuffer` render target created via `CreateRenderTarget(GD, 1, MSAA, SurfaceFormat.Color, SSAA*W, SSAA*H, Depth24Stencil8)` with `RenderTargetUsage.PreserveContents`. When 3D is not enabled a separate `BackbufferDepth` target (color-encoded depth for the 2D sprite depth pass) is also created with the same MSAA value. `DrawBackbuffer` either calls `SSAAFunc` (SSAA path) or draws the backbuffer with a SpriteBatch (point/default sampler) to the screen.
- SSAA downsample shader: `SSAA.fx` header says "Currently uses box filter". It takes 4 POINT samples (offset by half a texel) and averages them in approximate linear light (sRGB<->linear polynomial approximations), shaders SM3 / `ps_4_0_level_9_1`. So SSAA is limited to exactly 2x2 and a 4-tap box filter.
- MSAA capability probe: `S\FreeSO\TSOClient\tso.common\Utils\FeatureLevelTest.cs` lines 43-63 creates a 1x1 8x MSAA target, clears red, copies with `TextureUtils.CopyAccelerated`, reads back; sets `FSOEnvironment.MSAASupport`. `FSOEnvironment.MSAASupport` defaults true (`FSOEnvironment.cs` line 34). `TSOGame.cs` line 155 checks `!FSOEnvironment.MSAASupport`.
- UI options: `UIGraphicsOptionsDialog.cs` (FSO client) has AA Low/Med/High buttons (`AALowButton` -> 0, `AAMedButton` -> 1, `AAHighButton` -> 2, lines ~341-343) and disables the AA check button if `!FSOEnvironment.MSAASupport` (line 136). Older `UIOptions.cs` has a simple AntiAlias on/off toggle (`settings.AntiAlias ^ 1`, line 514).
- Presentation-parameter MSAA is NOT used: `TSOGame.cs` line 59 contains `//Graphics.PreferMultiSampling = true;` (commented out). No active `PreferMultiSampling` use in the game code. The MonoGame fork itself has the property (`FSOMonoGame\MonoGame.Framework\GraphicsDeviceManager.cs` lines 266-277, 439-448: when true, sets `MultiSampleCount` to `MaxMultiSampleCount`).
- MSAA used for 3D head/UI scene render targets: `_3DTargetScene` takes a multisample arg (`3DTargetScene.cs` line 24, Depth24Stencil8, PreserveContents); callers pass `(AntiAlias > 0) ? 8 : 0` (Simitone `UIIconCache.cs:67`, `UIPieMenu.cs:148`, `UISelectSkinAlert.cs:129`; FSO `UIPieMenu.cs:147`). `UIHouseMode.cs:621` uses 4. `UISpriteBatch` / `UIExternalContainer.cs:83` uses `(AntiAlias > 0 && !FSOEnvironment.DirectX) ? 4 : 0`; `UICachedContainer.cs:47` uses MSAA only when `UseMultisample && !FSOEnvironment.DirectX`. So on DirectX, cached UI containers are never MSAA (the UI is not anti-aliased by MSAA there, which is what you want for UI).
- `BackbufferScale` (`World.cs:49`, used at `World.cs:699` in `DrawBackbuffer(Opacity, BackbufferScale)`) is NOT an AA/render-scale feature: it is the zoom factor (`TargetZoom / BaseScale` in `UILotControl.cs:748`) that scales the backbuffer sprite during zoom. It is reset to 1 elsewhere. Note it as a pre-existing zoom-by-scaling-the-final-image mechanism (bilinear/point resampling blur risk when zoomed; [I]).
- MonoGame fork (`S\FreeSO\Other\libs\FSOMonoGame`) supports MSAA render targets on DirectX with an automatic resolve: `RenderTarget2D.DirectX.cs` lines 133-182 creates a second non-MSAA `_resolvedTexture`, `ResolveSubresource()` calls `ID3D11DeviceContext.ResolveSubresource`, and the shader resource view points at the resolved texture; the MSAA texture has the ShaderResource bind flag removed (line 189-190). Depth view uses `Texture2DMultisampled`. `GetSupportedSampleDescription` clamps count.
- Running game caveat: AI-CONTEXT.md (`S\docs\architecture\AI-CONTEXT.md`) states the running game is the prebuilt release v0.8.12 (.NET Framework 4.x, MonoGame DirectX), binary-patched with Mono.Cecil via `Tools/SpriteCardsPatch`; source on master is net9.0. Repo source and the running binary can differ, so verify the MSAA behaviour in the actual DLLs.

### Inferences
- [I] The existing MSAA applies to the world backbuffer, so it only anti-aliases 3D geometry edges (walls/floors/characters). Sprites drawn as textured quads will not get inside-texture edge AA from MSAA, but the quad's own silhouette is only anti-aliased where it is geometric (see section 2). The AA toggle effectively works well for 3D modes and gives little for sprite silhouettes in 2D mode.
- [I] The 2D-camera special case (SSAA swapped for MSAA 8) suggests the original authors found SSAA unaffordable or incompatible with the 2D depth-sprite path; worth understanding before adding SSAA for 2D.
- [I] `PPXDepthEngine.InitScreenTargets` is the single choke point for adding a render scale, a post-AA pass, or a TAA history buffer.

### Gaps
- Not checked: whether `ChangeAAMode` is callable at runtime in the patched v0.8.12 binary (only source reviewed). Not checked: what the `Simitone.Client` options UI exposes for AA (only the `-aa` flag and FSO option dialogs were found). Not checked: sampler states used by `DrawBackbuffer` SpriteBatch (default is LinearClamp for SpriteBatch default unless overridden; unverified).

## 2. Which AA works on a frame with hard-silhouette sprites AND 3D meshes; MSAA failure modes; MonoGame MSAA support

### Takeaway
Rank for this mixed frame: post-process morphological AA (SMAA 1x, or FXAA as the cheap fallback) over the scene before UI, plus fixing sprite sampling/edges, covers everything including alpha-tested edges. MSAA only helps true geometry edges (walls, floors, meshes, and quad borders) and is mostly wasted on alpha-tested sprite interiors. MonoGame does support MSAA render targets on DX in this fork (and via the PreferMultiSampling/PresentationParameters path in stock MonoGame), but needs a resolve and does not give per-sample access.

### Cited Findings
- MonoGame `RenderTarget2D` constructors take `preferredMultiSampleCount`, so the API exists — [MonoGame API doc mirror](https://gnsgamedesign.github.io/monogame.github.io/api/Microsoft.Xna.Framework.Graphics.RenderTarget2D.html) (documented via search result summary).
- Backbuffer MSAA (`GraphicsDeviceManager.PreferMultiSampling = true`, sample count set in the `PreparingDeviceSettings` event) works on both DX and GL; render-target MSAA on DirectX historically required an engine patch (PR #5538 at the time) plus a manual `ResolveSubresource` into a non-MSAA target; the post doesn't confirm current behaviour — [MonoGame forum: Quick Overview: How to work with MSAA](https://community.monogame.net/t/quick-overview-how-to-work-with-msaa-directx-too/8570).
- Same source: the depth buffer's multisample settings must match the colour texture; 4 samples is described as safe, 8 almost always safe; "multisampling computes textures once per pixel, so point-sampled textures on quads can still look jagged".
- DesktopGL: issue #6199 "PreferMultiSampling does not enable anti-aliasing"; a later PR #7338 "Fix multisampling in DesktopGL" is reported merged; some users on older builds still saw no difference — [forum/issue index from search](https://github.com/MonoGame/MonoGame/pull/7338/files), [community.monogame.net/t/msaa-in-cross-platform-open-gl-not-working/19050](https://community.monogame.net/t/msaa-in-cross-platform-open-gl-not-working/19050). Details not fetched.
- Fork-specific [V]: see section 1: the FSOMonoGame fork creates the resolved texture automatically and exposes it as the shader resource, so MSAA RTs can be sampled directly by later passes (but only after the implicit resolve; no custom resolve shader, no per-sample access).
- Multiple MSAA render targets (MRT) caused an exception in at least one Nez/MonoGame report — [MonoGame forum thread](https://community.monogame.net/t/exception-when-using-multiple-rendertargets-with-preferredmultisamplelevel-0-nez/17897) (title only; not fetched). Relevant because Simitone uses `FSOEnvironment.UseMRT = true` on Windows (Program.cs:114) to write colour + depth together.
- Local repo [V]: the world pass uses a colour+depth pair (`SetPPXTarget(color, depth)`, MRT when `UseMRT`) where depth is encoded into a colour target (`BackbufferDepth`, `SurfaceFormat.Color`/Vector4/Single in places).

### Inferences
- [I] Why MSAA fails here:
  1. Alpha-tested sprites: MSAA shades once per pixel, so `clip()`/alpha-discard edges and texture-internal silhouettes stay aliased. Only alpha-to-coverage (not exposed in MonoGame's BlendState; would need a custom blend/rasterizer path and MSAA target) fixes that; with premultiplied alpha-blended edges you get soft edges only if the art contains them.
  2. Depth sprites: the 2D depth is a second, colour-encoded target. MSAA-resolving a colour-encoded depth value averages depth values at edges, producing incorrect depth (halos/z-fighting at sprite-vs-mesh boundaries). Averaging encoded depth is wrong; resolve of the depth target would need a per-sample min/max custom resolve, which MonoGame does not expose.
  3. Deferred/MRT: with MRT, all targets must have equal sample counts; MSAA on a G-buffer-like set multiplies memory/bandwidth (4x MSAA at 3840x2160 RGBA8 + depth is roughly 4 x (33+33) MB, [I] arithmetic).
  4. Shader-time aliasing (specular, point-sampled texture edges, lighting maps) is not touched by MSAA.
- [I] Because sprites are quads, MSAA does smooth the quad rectangle border but sprite silhouettes are alpha-defined inside the quad, so little visible benefit in 2D mode. That fits the repo's behaviour of using SSAA or MSAA 8 depending on mode.
- [I] DesktopGL vs DX11: MSAA RT support depends on MonoGame version/fork; the repo's FSOMonoGame fork is the safe known quantity on DX; unknown for current stock MonoGame 3.8.x (not verified).

### Gaps
- Could not confirm via fetched docs the current stock MonoGame 3.8.x behaviour for MSAA render targets on DX11 (WebFetch of the MonoGame docs/PR was not done; forum info is 2016-2018). Alpha-to-coverage exposure in MonoGame was not researched (I believe `BlendState` has no AlphaToCoverageEnable; [I], unverified).

## 3. Post-process AA: FXAA and SMAA — licensing, ports, quality/cost, interaction with UI and text

### Takeaway
SMAA reference code is MIT-style licensed with a binary-distribution exception, so it is safe to reuse in a MonoGame shader (needs two precomputed textures, 3 passes). FXAA 3.11 is easier to port but its licence history needs checking (public domain for v3, BSD-3 reported for 3.11). The cleanest UI strategy is to run post AA on the world render target only, before UI is composited, which avoids blurring text. Existing MonoGame ports: one MIT-licensed post-processing library lists SMAA and MLAA (DesktopGL-targeted), but no dedicated MonoGame SMAA repo was found.

### Cited Findings
- SMAA (iryoku/smaa) LICENSE: standard MIT terms, five copyright holders (2013), with the clarification that the copyright/permission notice need not appear in binary distributions; no warranty — [iryoku/smaa LICENSE.txt](https://raw.githubusercontent.com/iryoku/smaa/master/LICENSE.txt) (fetched, summarized). [V]
- SMAA repo description: GPU MLAA-style implementation for DX9/DX10/DX11/OpenGL; look-up tables to reduce false positives; designed to combine with multi/supersampling; requires two precomputed textures `AreaTex` and `SearchTex`; demo integrations only for DX9 and DX10; `SMAA.hlsl` holds integration info — [iryoku/smaa](https://github.com/iryoku/smaa). [V]
- A Urho3D D3D11 port reported weight calculation was wrong under D3D11 and suggested DX9 or OpenGL3 paths — [Urho3D forum thread](https://discourse-urho3d.github.io/t/smaa-implementation/5646) (snippet only). Treat as a porting pitfall to watch, unverified.
- MonoGame content builder issue with an SMAA shader (built in XNA, failed in MonoGame content builder); the same thread says FXAA 3.11 migrates easily and works — [MonoGame forum thread](https://community.monogame.net/t/monogame-content-builder-fails-to-build-valid-shader/6885) (snippet only). The SMAA HLSL uses SM4/SM5 features (macros `SMAA_HLSL_4`, `SMAA_HLSL_4_1`) and texture Load/Gather [I]; MonoGame's 2MGFX compiles `ps_4_0_level_9_1`/`ps_4_0`+ profiles on DX, GLSL via MojoShader/SPIRV-cross on GL [I]. Simitone's own `SSAA.fx` already uses `#if SM4 ... ps_4_0_level_9_1` [V].
- MonoGame port: `MerjTek.MonoGame.PostProcessing` NuGet, MIT, Windows DirectX and OpenGL, lists MLAA and SMAA among effects; package targets DesktopGL so the dependency may need fixing for DirectX — [NuGet page](https://www-0.nuget.org/packages/MerjTek.MonoGame.PostProcessing.OpenGL). Found only via search listing; not inspected for correctness.
- FXAA licensing: FXAA 3 public domain; FXAA 3.11 BSD 3-clause per Wikipedia (citation traced to a Stack Overflow question; not retrieved) — [Wikipedia: FXAA](https://en.wikipedia.com/wiki/FXAA). A Godot port cites NVIDIA's licence file for 3.11. Verify against NVIDIA's repository before shipping. Ready HLSL: [NVIDIA GameWorks FXAA 3.11 sample](https://docs.nvidia.com/gameworks/content/gameworkslibrary/graphicssamples/d3d_samples/fxaa311sample.htm).
- Catlike Coding: post-process AA has no true sub-pixel data, so it analyses the image and selectively blurs — [Catlike Coding FXAA tutorial](https://catlikecoding.com/unity/tutorials/advanced-rendering/fxaa/).
- Intel CMAA 2.0 article compares CMAA2 against FXAA 3.11 and SMAA — [Intel CMAA2](https://www.intel.com/content/www/us/en/developer/articles/technical/conservative-morphological-anti-aliasing-20.html) (quality/cost numbers not retrieved). CMAA2 is a further MIT-licensed option [I] (licence not verified in session).
- No source found comparing SMAA and FXAA specifically on UI text — see Gaps.

### Inferences
- [I] Cost: FXAA is one full-screen pass, typically well under 1 ms at 1080p on discrete GPUs; SMAA 1x is three passes (edge detect, blend-weight calc, neighbourhood blend) plus two small LUT textures, typically ~1 ms or less at 1080p, higher at 4K. Not measured in this session. Quality: SMAA 1x preserves text and sharp pixel details better than FXAA, which blurs the whole image slightly (general knowledge from Jimenez et al., not fetched).
- [I] Sprite silhouettes: both operate on colour (or luma) edges, so hard alpha-cut sprite edges against terrain are detected and smoothed, which MSAA cannot do. With pre-rendered 3D-looking sprite art, internal texture detail may get slightly softened by FXAA; SMAA with a modest threshold (0.1) tends to leave texture detail alone.
- [I] Colour-encoded depth/ID buffers must not be AA'd; SMAA edge detection can optionally use depth (predication) but here depth is in a colour-encoded 2D target, so prefer luma/colour edge detection.
- [I] Integration point: in `PPXDepthEngine.DrawBackbuffer` the world is blitted from `Backbuffer` to the screen; insert SMAA/FXAA there (world->AA RT->screen) before the UI layer is drawn, so UI/text are untouched. In this codebase the UI is drawn after the world (UI containers, `UISpriteBatch` etc.), which fits (not traced in full).
- [I] Shader build: need to ship SMAA as .fx compiled by the repo's mgcb (`TSOClientContentDX.mgcb`, already has an `SSAA.fx`) and add AreaTex/SearchTex as content textures; or generate AreaTex/SearchTex at build. The release v0.8.12 binary is Cecil-patched, so adding a shader means shipping an extra .xnb and a patch hook; harder than editing source [I].
- [I] Gamma: SMAA works best on a gamma-correct or perceptually encoded input (it uses luma thresholds); the repo's world RT is sRGB-encoded Color, which is fine.

### Gaps
- No verified empirical comparison of FXAA vs SMAA on small UI text or on pixel-art-like sprites. No inspected, tested MonoGame SMAA port (only MerjTek NuGet listing). SMAA 1x/T2x/S2x requirement details (T2x needs jitter + previous frame/velocity, S2x needs a 2x MSAA source with sample-position-specific resolve, 4x = T2x+S2x) are from general knowledge [I]; the iryoku README fetch did not return them.

## 4. TAA / TAAU requirements and fit for a 2D scrolling world with static sprites

### Takeaway
TAA needs per-pixel motion vectors, sub-pixel camera jitter, a history buffer and history rejection/clamping; for a mostly static camera it gives excellent sprite and mesh AA, but for a scrolling/zooming 2D world with depth sprites it adds ghosting, blur and non-trivial engineering. Not recommended early; consider after SMAA, mainly as a base for DLAA-like quality.

### Cited Findings
- Streamline's DLSS integration (an example of a temporal technique) requires: depth, motion vectors, colour in/out tagged; common constants with jitter-free matrices; pixel-space jitter offset; MV scale factors — [Streamline Programming Guide](https://github.com/NVIDIA-RTX/Streamline/blob/main/docs/ProgrammingGuide.md) (fetched; summarised). [V]
- SMAA repo says its morphological approach can combine with multi/supersampling, which is the T2x/S2x variants concept — [iryoku/smaa](https://github.com/iryoku/smaa). [V]
- Karis (UE4 high-quality TAA, SIGGRAPH 2014) and Salvi (Intel/NVIDIA, variance clipping) GDC talks are the canonical TAA references; not fetched in this session. [I]

### Inferences
- [I] 2D scrolling: camera pans are pure translations, so motion vectors can be computed analytically from the camera delta (screen-space offset of previous vs current frame), not stored per-pixel, for static sprites. Moving objects (Sims, animated sprites) need per-object velocity written to a velocity buffer or a rect-based approach. With a zoom change (this game scales via `BackbufferScale`), the reprojection is a uniform scale — also analytic.
- [I] Jitter: for sprites drawn as pre-rendered textured quads, sub-pixel jitter shifts the sprite's rasterization; with point sampling, jitter would change which texel is picked and cause shimmer unless texels are also resampled bilinear; with bilinear sprite sampling, jitter effectively supersamples the sprite, which is the main benefit. So TAA fights with pixel-exact sprite rendering and the existing point-filtered pipeline.
- [I] Depth sprites: the colour-encoded depth buffer is not TAA'd; history rejection can use it for disocclusion, but reprojection of sprite-depth is approximate. Disocclusion when Sims walk in front of walls will produce ghosting unless velocity and depth tests are correct.
- [I] Cost: one extra full-screen pass plus two history RTs (e.g. ping-pong colour). Cheap computationally; the cost is engineering and tuning (clamping, sharpening).
- [I] UI must be composited after the TAA resolve (as for SMAA).
- [I] Simitone-specific: the pipeline is built from a single `PPXDepthEngine.Backbuffer`, so history ping-pong would be a localized change; but the `BackbufferScale` zoom and lighting-map updates (`LMapBatch`) would produce full-screen changes (flicker) that defeat history.

### Gaps
- No fetched source for Karis/Salvi talk details or for TAA in 2D sprite games. Did not find a verified example of TAA in a 2D sprite/depth-sprite engine.

## 5. DLSS / DLAA / FSR / XeSS integration realities from .NET/MonoGame

### Takeaway
Not realistic for this project. DLSS/DLAA go through native C/C++ NVIDIA Streamline (or NGX), which needs motion vectors and depth, signed NVIDIA DLLs, per-GPU hardware support, and D3D device/swap-chain interposition; FSR 4 requires DX12. Even if technically possible through P/Invoke, the MonoGame DX11 device is owned by SharpDX inside the framework, and sprites with colour-encoded depth are poor inputs for ML upscalers. Cheaper alternatives (SMAA, render scale) give most of the visible gain.

### Cited Findings
- Streamline SDK provides C/C++ headers (`sl.h`, `sl_dlss.h`); examples are all C++; integration is by linking `sl.interposer.lib` or loading `sl.interposer.dll` dynamically — [Streamline Programming Guide](https://github.com/NVIDIA-RTX/Streamline/blob/main/docs/ProgrammingGuide.md). [V]
- Streamline modules are dual-signed by NVIDIA; validate `sl.interposer.dll` with `WinVerifyTrust` / `sl::security::verifyEmbeddedSignature`; unsigned/dev DLLs only for debugging, not for shipping — same guide. [V]
- Requirements listed: Windows 10 RS3 64-bit or newer, GPU with DirectX 11 and Vulkan 1.2+; support checked at runtime via `slGetFeatureRequirements`; common constants (jitter-free matrices), jitter offset, motion vector scale; `slDLSSSetOptions` for mode/output size; `slDLSSGetOptimalSettings` for render resolution — same guide. [V] (page does not say which API DLSS SR supports)
- NVIDIA says Streamline supports DirectX 11 and 12 (Vulkan "coming soon" at the time; later 2.4.10 notes list Vulkan fixes) — [NVIDIA Streamline page](https://developer.nvidia.com/rtx/streamline). Streamline has a "Manual Hooking" mode (advanced guide) for apps that need to avoid the interposer; DLSS-G intercepts `IDXGISwapChain::Present` — NVIDIA developer blog and forum, see [How to integrate DLSS 4 with Streamline](https://developer.nvidia.com/blog/how-to-integrate-nvidia-dlss-4-into-your-game-with-nvidia-streamline).
- A developer-forum thread documents a D3D11 mod failing "DLSS fails to enable for D3D11 / NGX DLSS Context cannot run" — [NVIDIA forum](https://forums.developer.nvidia.com/t/dlss-fails-to-enable-for-d3d11-ngx-dlss-context-cannot-run/307531) (troubleshooting report, not official support statement).
- FSR: FidelityFX SDK is described as MIT-licensed (FSR 3 branch); FSR 4 ships as prebuilt signed DLLs in SDK 2.0 (not MIT source); FSR 4 requires DirectX 12; FSR 3 release lists DX12/UE5/Vulkan, no source found confirming D3D11 support. FSR 4 hardware sources disagree (RX 9000 only vs RX 6000/7000/9000 series) — [GPUOpen SDK 1](https://gpuopen.com/amd-fidelityfx-sdk-1/), [GamingOnLinux on SDK 2.0](https://www.gamingonlinux.com/2025/08/amd-fidelityfx-super-resolution-4-fsr-4-now-available-for-all-developers-with-fidelityfx-sdk-2-0), [GPUOpen FSR4 release](https://gpuopen.com/learn/amd-fsr4-gpuopen-release/), [GPUOpen FSR4 page](https://gpuopen.com/fidelityfx-superresolution-4/); one Yahoo report claims FSR 4 source was accidentally published under MIT (single secondary source, unverified) — [Yahoo](https://tech.yahoo.com/gaming/articles/latest-fsr-4-source-code-174711776.html).
- OptiScaler (third-party mod) lists improved DX11 support (DLSS upscaling only) — from the NGX/Streamline search results ([NexusMods listing](https://www.nexusmods.com/site/mods/757)); a mod, not official.

### Inferences
- [I] From .NET/MonoGame: you would P/Invoke a thin native C++ shim that links Streamline, grab the `ID3D11Device`/context from MonoGame's SharpDX `GraphicsDevice` (fork exposes `_d3dDevice`/`_d3dContext` internally, e.g. `GraphicsDevice._d3dContext` used in `RenderTarget2D.DirectX.cs`), tag native `ID3D11Texture2D` pointers for colour, depth and motion vectors, and call evaluate each frame. The interposer must be loaded BEFORE device creation (for the default interposer path) or use manual hooking; with MonoGame this means loading before `GraphicsDeviceManager` creates the device — feasible but fragile [I; the Streamline interposer-timing rule is from general knowledge, not confirmed in the fetched page].
- [I] Input quality: DLSS needs a real depth buffer and per-pixel motion vectors. Simitone's 2D sprites use colour-encoded pre-rendered depth in separate RTs and the camera is a 2D orthographic scroll; you would have to synthesise a hardware-style depth/MV (analytic camera MV is possible for static sprites). ML upscalers trained on 3D game content may smear sprite art; DLAA specifically (native resolution) would be the only sensible mode. Not verified in a test.
- [I] Distribution: NVIDIA-only (DLSS), AMD RDNA-only/DX12 (FSR 4); XeSS has a DX11 path via Intel's SDK [I, not researched]. The game is distributed as a fan mod patching a Sims 1 reimplementation; licence/redistribution terms for NVIDIA DLLs not researched here. Would not cover all users.
- [I] Verdict: not worth it. A smaller step toward the same goal is SMAA 1x + optional render-scale SSAA.

### Gaps
- Not verified: DLSS Super Resolution plugin's D3D11 support specifically (the fetched guide does not say); DLSS/NGX licence terms; XeSS requirements and licence; whether anyone has shipped DLSS/FSR in a MonoGame title. Did not fetch the DLSS Programming Guide PDF.

## 6. Supersampling / render scale and sprite-specific techniques

### Takeaway
Supersampling (render scale) is already half-built in the repo (2x2 SSAA, 4-tap box). It is the highest-quality option but costs 4x fill; a variable render-scale + better downsample filter, together with sprite sampling fixes (bilinear + sub-pixel alignment, mipmaps, edge padding, soft alpha edges), gives most of the visible quality cheaply for the 2D sprite portion.

### Cited Findings
- [V] Repo: `SSAA.fx` header "A shader for SSAA style downscaling. Currently uses box filer."; 4 POINT taps averaged in linear space; `PPXDepthEngine.SSAA` multiplies backbuffer width/height (so the 2D-colour and the colour-encoded depth target are both 4x pixels). `FeatureLevelTest` sets `EnableNPOTMip`, `TexCompressSupport` flags — mipmapped NPOT textures supported on DX [V, lines 11-28].
- [V] Repo: `TextureUtils.UploadWithMips` exists (`tso.common\Utils\TextureUtils.cs`) and `CreateRenderTarget(... numberLevels ...)` supports mip RTs; object thumbnails use mipmapped RTs (`WorldPlatform3D.cs:243`: `new RenderTarget2D(gd,1024,1024,true,...)`).
- [V] MonoGame forum: MSAA does not smooth point-sampled texture detail — [MonoGame forum](https://community.monogame.net/t/quick-overview-how-to-work-with-msaa-directx-too/8570).

### Inferences
- [I] Render scale: allowing arbitrary scales (1.25x, 1.5x, 2x) and a better downsampler (bilinear hardware downsample for scale<=1.5, or a Lanczos/tent filter) is cheap to implement in `SSAADownsample`. Depth-sprite RT also needs scale; the 2D sprites' point-filtered texels at 2x produce 2x2 blocks that do NOT gain detail (nothing to resolve) — SSAA of point-sampled pre-rendered sprites only anti-aliases geometry/alpha edges, not sprite texture content. For pre-rendered sprites that are already authored at one resolution, supersampling's benefit comes from the quad edges and the 3D layer.
- [I] Sprite-specific techniques (all untested here, general graphics practice):
  - Edge padding/dilation: extend colour outward into transparent texels (RGB bleed) so bilinear sampling at silhouettes doesn't pull in black/white fringes; mandatory if switching sprites from point to bilinear. Use premultiplied alpha.
  - Bilinear + sub-pixel alignment: with ortho 2D and integer zoom, align quads to pixel grid and use point; at fractional zoom use bilinear (or sharper bicubic/"smooth pixel" shader that blends only at texel boundaries) to avoid shimmer while scrolling. Keeps pixel-art-like look but removes jaggies across non-integer scale.
  - Mipmapped sprite sampling: generate mips for sprite atlases (the engine already can upload mips, see above) and use trilinear when zoomed out (zoom < 1); prevents shimmering on minification. Needs padding between atlas entries to avoid bleeding.
  - Per-sprite alpha edge smoothing: soften the alpha channel edge in the art/loader (1-px blur of alpha mask or distance-field alpha with smoothstep in the pixel shader) so silhouettes are anti-aliased without a full-screen pass; changes depth-sprite behaviour because depth is binary — keep the depth write alpha-tested while colour alpha is blended (needs sort/two-pass; uncertain).
  - Pixel-art-aware filters (e.g. sharp-bilinear, "smooth pixel scaling"): useful for the 2D sprite layer if upscaling; not an AA of silhouettes.
- [I] BackbufferScale zoom (Simitone's zoom resamples the backbuffer) is a pre-existing source of blur at non-1x; rendering sprites at the target zoom with proper filtering is cleaner than scaling the finished frame.

### Gaps
- No external source retrieved on sprite alpha-edge techniques or "smooth pixel" filters; everything in the sprite bullet list is general practice, not cited. Not verified: what sampler (point/bilinear) the 2D sprite draw path currently uses (`_2DWorldBatch`, `PPXDepthEngine`); needs a look at `tso.world\Utils\_2DWorldBatch.cs` and the 2D effect samplers.

## 7. Recommended implementation order (cheapest big win first)

### Takeaway
1) Verify/restore the existing MSAA toggle and fix UI exclusion; 2) add a SMAA 1x (or FXAA as fast stopgap) pass on the world RT before UI; 3) fix sprite sampling (padding, premultiplied alpha, bilinear/mips) and add flexible render scale with a better downsample; 4) optional S2x/T2x-style temporal later; 5) skip DLSS/FSR.

### Cited Findings
(Order below is my synthesis from the sections above, based on cited facts there; not a sourced claim.)

### Inferences
1. [I] Confirm what the running v0.8.12 binary does with `AntiAlias` (1 = 4x MSAA) and check visually which parts it fixes (3D edges only). Effort: hours. Cost: none. Fixes: nothing new but defines baseline; ensure MSAA is not used for UI containers on DX (already the case per repo code).
2. [I] FXAA 3.11 pass (one .fx, ~1 pass, trivial to port per the MonoGame forum note) inserted in `PPXDepthEngine.DrawBackbuffer` between world RT and screen, before UI. Effort: ~1 day including a quick toggle. Cost: small. Fixes: sprite silhouettes AND mesh edges at once; slightly soft. Check FXAA 3.11 licence first.
3. [I] Replace/augment with SMAA 1x (MIT, needs AreaTex/SearchTex, 3 passes, watch the D3D11 and MGFX porting pitfalls above). Effort: ~2-4 days. Quality: sharper than FXAA, better on text-like detail. Existing `MerjTek.MonoGame.PostProcessing` may shorten this; inspect its quality and DX build first. Keep MSAA as optional on top ("SMAA + 4xMSAA" = closest to SMAA S2x idea) for 3D modes.
4. [I] Sprite sampling hygiene: premultiplied alpha, RGB dilation/padding, bilinear (or smooth-pixel) sampling at fractional zoom, mipmaps for zoom-out. Effort: ~2-5 days (atlas padding is the cost). Fixes: shimmer while scrolling/zooming, fringes. Pairs with step 2/3.
5. [I] Generalise SSAA into a render-scale slider (1.0-2.0) with a bilinear/tent downsample; keep the linear-light average. Effort: ~1-2 days. Cost: fill-rate (4x at 2x scale; roughly 8.3 MPix -> 33 MPix at 4K/2x [I arithmetic]).
6. [I] Only if still unsatisfied: TAA / SMAA T2x (analytic camera MVs, jitter, history). Effort: weeks, tuning-heavy; risk: ghosting on moving Sims and lighting-map changes.
7. [I] Skip DLSS/DLAA/FSR/XeSS: native C++ SDKs, motion-vector/depth requirements poorly matched to sprite rendering, vendor/hardware-limited, patched-binary distribution complicates shipping.

### Gaps
- Costs quoted are not measured; no benchmark was run. Timing estimates are judgment, not sourced.

## Source reliability notes
- Local repo findings are primary and verified (file/line given). Web sources: GitHub/NVIDIA docs fetched directly are reliable; forum posts (MonoGame, Urho3D, NVIDIA devs) are anecdotal and mostly 2013-2019 for MonoGame; WebSearch summaries were produced by a search layer, with some details (MonoGame PR #7338, issue #6199, FXAA licence history) only seen as snippets and not independently opened. The SMAA mode requirements (1x/T2x/S2x/4x) and TAA recipes are from general knowledge.
