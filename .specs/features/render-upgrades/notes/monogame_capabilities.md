# MonoGame graphics capabilities for Simitone/FreeSO (advanced rendering feasibility)

Conventions: "Verified-local" = read from the user's files/source on disk. "Cited" = found at a web source. "Inference" = my reasoning, not found in a source. Today: 2026-11-22.

## 1. Which backend does Simitone run on, and what shader model / feature level does that give?

### Takeaway
Simitone ships both MonoGame 3.8.4 WindowsDX (Direct3D 11 via SharpDX) and DesktopGL (OpenGL via MojoShader) and picks one at startup; on Windows it defaults to DX11. Stock effects compile with MGFXC to at most SM 5.0 (HiDef profile), and the game already requests HiDef when the adapter supports it.

### Cited Findings
- Verified-local: Simitone.Windows.csproj targets `net9.0-windows` (not .NET Framework 4.x) and references `MonoGame.Framework.DesktopGL` 3.8.4 and `MonoGame.Framework.WindowsDX` 3.8.4. A post-build step copies each package's MonoGame.Framework.dll into `Monogame\Windows` (DX) and `Monogame\WindowsGL` (GL). File: C:\Users\Zé\.t3\projects\the-sims\Simitone\Client\Simitone\Simitone.Windows\Simitone.Windows.csproj
- Verified-local: `Program.cs` sets `useDX = !linux` (true on Windows by default, switchable by a "dx11" argument/option). `MonogameLinker.Link` then selects `Monogame/Windows/` + `Content/DX/` or `Monogame/WindowsGL/` + `Content/OGL/`, and an AssemblyResolve handler loads the chosen framework DLL. Files: ...\Simitone.Windows\Program.cs, ...\Utils\MonogameLinker.cs
- Verified-local: the run folder C:\Users\Zé\.t3\projects\the-sims-run\app has `Simitone.Windows.exe`, `Monogame\Windows`, `Monogame\WindowsGL`, `Monogame\MacOS`, and `Content\DX\Effects` containing precompiled .xnb effects (2DWorldBatch, GrassShader, LightMap2D, ParticleShader, PixShader, RCObject, SSAA, SpriteEffects, VerShader, Vitaboy, MSDFFont, colorpoly2D, gradpoly2D). The Windows folder holds MonoGame.Framework.dll (894,464 bytes, assembly version 0.0.0.0, CLR v4.0.30319 header) plus SharpDX 4.x-style assemblies: SharpDX, Direct3D11, DXGI, D3DCompiler, Direct2D1, XAudio2, XInput, MediaFoundation. There is no Direct3D12 or Vortice assembly.
- Verified-local: TSOGame.cs sets `GraphicsProfile.HiDef` when `IsProfileSupported(HiDef)`; `PreferMultiSampling` is commented out; vsync on. Effect loading goes through `Content.Load<Effect>("Effects/...")` plus an `EffectSuffix` (WorldContent.cs).
- Verified-local: the repo also vendors an old MonoGame fork (FreeSO\Other\libs\FSOMonoGame; DX11 via SharpDX, requests feature levels 11_1, 11_0, 10_1, 10_0). The running build uses the NuGet 3.8.4 packages, not this fork, per the csproj. The fork's HiDef check maps to FeatureLevel 10_0 (GraphicsAdapter.DirectX.cs) and Reach to 9_1. The fork is a useful reference for 3.8.x-era behavior, but treat it as not what executes.
- Cited: MonoGame docs, custom effects: WindowsDX supports `vs/ps_4_0_level_9_1`, `_9_3`, `4_0`, `4_1`, `5_0`; the last three require `GraphicsProfile.HiDef`. DesktopGL translates HLSL to GLSL through MojoShader and only supports `vs/ps_2_0` and `3_0`. Preshaders unsupported; the compiler strips unused parameters; on GL, parameter defaults do not work. `2MGFX` is defined for all builds, `HLSL`/`SM4` on DX, `OpenGL`/`GLSL` on GL. — [MonoGame docs: Custom Effects](https://docs.monogame.net/articles/getting_started/content_pipeline/custom_effects.html)
- Cited: MonoGame 3.8.4 was released 2025 (CHANGELOG lists 3.8.4 on April 2, 2025; 3.8.4.1 patch announced Oct 2025). — [CHANGELOG](https://www.github.com/MonoGame/MonoGame/blob/develop/CHANGELOG.md), [3.8.4.1 post](https://monogame.net/blog/2025-10-20-MonoGame3841-release/)
- Cited: MonoGame 3.8.5 (blog dated 2026-07-15) adds new targets: DesktopVK (SDL2 + Vulkan) and WindowsDX12 (Direct3D 12, SDL2, XAudio2), both described as eventual replacements for DesktopGL and the DX11 target; the C# side moves away from third-party wrappers (OpenTK, SharpDX) to a native library. Shader changes: none in 3.8.5; a "unified shader compiler" is planned for 3.9. 3.8.6 is hardening. — [MonoGame 3.8.5 release](https://monogame.net/blog/2026-07-15-3.8.5-release-2026/)

### Inferences
- Simitone's practical ceiling today is D3D11 hardware feature level 11_x with MGFX shaders up to SM 5.0 under HiDef, but only whatever the 3.8.4 runtime exposes (see section 2). The OpenGL path is capped at SM 3.0-class shaders, so any advanced technique must target the DX path and the GL path would need a fallback or be dropped.
- The "net9.0-windows" target in this checkout differs from the user's ".NET Framework 4.x" premise; the run folder DLL header says v4.0.30319, which is just the CLR metadata version used by all .NET assemblies. Whether the run folder build is the same net9 build was not checked.
- The 3.8.5 DX12/Vulkan targets are not usable by Simitone without swapping packages and likely the native-backend setup; I did not verify whether Simitone's FSO code compiles against them.

### Gaps
- Exact D3D feature level the NuGet 3.8.4 WindowsDX package requests (could not fetch GraphicsDevice.DirectX.cs at tag v3.8.4; 404). Only the vendored fork's list was read.
- Whether the run folder build was actually launched with DX or GL by default on the user's PC (not tested; config file not inspected).

## 2. Compute shaders, structured buffers, UAVs, render-target arrays, MRT, MSAA render targets, depth texture sampling

### Takeaway
Stock MonoGame 3.8.x has no compute shaders, structured buffers, UAVs, geometry/tessellation stages, or DXR. Compute exists only in a community fork/NuGet variant (cpt-max) and an unmerged upstream PR (#7533), now retargeted to milestones 4.0/5.x. MRT (up to 8 targets on DX) and MSAA render targets are supported in stock.

### Cited Findings
- Cited: PR #7533 "compute shader" by cpt-max is open and unmerged: opened 2021-07-15, based on tessellation/geometry PR #7352; milestones moved to 5.x/4.0 (Aug 2026 per page). Maintainer ThomasFOG says it is hard to upstream due to the number of platforms, breaking public API changes and parity needs; MojoShader/OpenGL expected to retire once Vulkan/DX12 stabilize; other shader stages to be considered after DesktopVK, GDK, NativeAOT. — [PR #7533](https://github.com/MonoGame/MonoGame/pull/7533)
- Cited: the fork adds compute (DX and OpenGL), StructuredBuffer/RWStructuredBuffer, append/counter buffers, RWTexture2D/3D, ByteAddressBuffer access to vertex/index buffers, indirect draw/dispatch, hull/domain/geometry shaders in MGFXC and WindowsDX, ShaderConductor replacing MojoShader. API: `pass.ApplyCompute()`, `GraphicsDevice.DispatchCompute()`, buffers created with `ShaderAccess.Read/ReadWrite`. Uses `cs_5_0`. Limits: no direct writes to cubemaps, texture arrays or specific mip levels (copy workaround); GetData on struct with `bool` throws; matrices are not auto-transposed; pixel-shader UAV writes DX only; on OpenGL structured-buffer reads from non-compute stages need SM5. — [Compute Shader Guide](https://raw.githubusercontent.com/cpt-max/Docs/master/MonoGame%20Compute%20Shader%20Guide.md), [Docs repo](https://github.com/cpt-max/Docs)
- Cited: NuGet packages named `MonoGame.Framework.Compute.WindowsDX` / `.DesktopGL` / `.Android` exist (versions up to 3.8.3 seen in search results; 3.8.2.3 for WindowsDX). — [NuGet](https://www.nuget.org/packages/MonoGame.Framework.Compute.WindowsDX)
- Cited: CHANGELOG has no entries for compute, structured buffers, UAVs, or DX12 in the stock framework (through 3.8.4). — [CHANGELOG](https://www.github.com/MonoGame/MonoGame/blob/develop/CHANGELOG.md)
- Cited: 3.8.1 "Enabled setting eight render targets on Windows" (#7549, "closest match to render target arrays"); added MSAA to SwapChainRenderTarget; fixed DesktopGL multisampling; shared MSAA render target. Earlier: MSAA render targets on OpenGL since 3.3, DX MSAA GetData/SetData fix in 3.8. — [CHANGELOG](https://www.github.com/MonoGame/MonoGame/blob/develop/CHANGELOG.md)
- Verified-local: the game already uses MRT-style multiple render targets (`SetRenderTargets`, `FSOEnvironment.UseMRT` in _2DWorldBatch.cs), pack depth into a Color target ("24-bit packed format"), uses `SurfaceFormat.HalfVector4` for a directional lightmap target, `DepthFormat.Depth24/Depth24Stencil8`, and `RenderTargetUsage.PreserveContents` (LMap\LMapBatch.cs). Real-time lighting is already a 2D lightmap pipeline in tso.world (LMap\*).
- Cited: the 3.8.5 post mentions "native texture sharing APIs" (no detail). — [3.8.5 release](https://monogame.net/blog/2026-07-15-3.8.5-release-2026/)

### Inferences
- Depth texture sampling: MonoGame has no `Depth` SurfaceFormat that can be sampled as a depth-comparison texture in 3.8.x; the standard workaround (also used by FSO: depth packed into a Color RT) is writing depth/position into a color target from a pixel shader. I did not find a source stating this explicitly; the local fork only mentions comparison samplers being ps_4_0+ in a test.
- Using the compute fork would mean replacing MonoGame.Framework.dll (both DX and GL variants) with the Compute-package build, which is a 3.8.2/3.8.3 base (not 3.8.4). Compatibility with the FSO code and the net9 build is untested. 3.8.3 is the latest Compute package version seen; I did not verify whether a 3.8.4/3.8.5-based one exists.
- Multisampled render targets would let MSAA happen in-engine, but FSO's lighting pipeline writes packed depth/IDs into color targets and relies on exact values, so MSAA resolve would corrupt them; AA would most plausibly be post-process (FXAA/SMAA/TAA) on the final composite. This is reasoning, not sourced.

### Gaps
- No source found that confirms render-target arrays or sampling a true depth buffer in stock 3.8.4.
- Not verified whether the Compute NuGet variants have a build later than 3.8.3.

## 3. DXR / hardware ray tracing reachability, and prior art

### Takeaway
No source shows DXR working from MonoGame; maintainers said it was unsupported in 2019 and D3D12 was far off, and 3.8.5 only now adds a separate DX12 target. DXR would need a separate D3D12 device (Vortice) sharing textures with MonoGame's D3D11 device, or software ray marching in pixel shaders; I found no MonoGame/XNA GPU ray tracing project.

### Cited Findings
- Cited: MonoGame forum, Aug 2019: DXR "is not supported right now" and "will probably take a long time for us to even support D3D12" (Jjagg). — [Forum thread](https://community.monogame.net/t/rtx-dxr-support/11804)
- Cited: MonoGame 3.8.5 adds a WindowsDX12 target (Direct3D 12 on SDL2) said to perform near D3D11 parity. No DXR/ray tracing is mentioned in that post. — [3.8.5 release](https://monogame.net/blog/2026-07-15-3.8.5-release-2026/)
- Cited: CSharpDirectXRaytracing (Jorgemagic) is a C# DXR tutorial port using the Vortice D3D12 binding; this shows DXR is reachable from C# independently of MonoGame. — [GitHub](https://github.com/Jorgemagic/CSharpDirectXRaytracing)
- Cited: NVIDIA DXR background: DXR pipelines use ray-generation, closest-hit, miss shaders and `DispatchRays`; it is a D3D12 feature. — [NVIDIA DXR tutorial](https://developer.nvidia.com/rtx/raytracing/dxr/dx12-raytracing-tutorial-part-2), [Microsoft DirectX blog](https://devblogs.microsoft.com/directx/announcing-microsoft-directx-raytracing/)
- Cited: web search for MonoGame/XNA GPU BVH ray tracers found only non-MonoGame projects (Unity compute, kayru/RayTracedShadows with CPU-built BVH and GPU traversal). The only MonoGame hit was compute sample code (MonoGame Shader Samples, requires the custom fork). — [kayru/RayTracedShadows](https://github.com/kayru/RayTracedShadows), [MonoGame Shader Samples](https://gitee.com/clrt6174/MonoGame-Shader-Samples)
- Verified-local: stock MonoGame DX exposes `GraphicsDevice.Handle` (the SharpDX D3D11 device) and `Texture.GetSharedHandle()` (DirectX), per the vendored fork source (GraphicsDevice.DirectX.cs:98, Texture.DirectX.cs:22). Assumed (not verified) to match 3.8.4 NuGet.

### Inferences
- Realistic DXR route: in-process hook that creates a second D3D12 device (Vortice) on the same adapter, builds BLAS/TLAS from Simitone's scene geometry, renders RT results into a shared texture (D3D11-D3D12 shared handle / keyed mutex via `GetSharedHandle`), and composites in MonoGame. That is a large engineering task and requires frame synchronization with fences; I found no example of it with MonoGame.
- Simitone's scene is 2D sprites with depth/ID buffers, not 3D triangles, so a ray-traced path would need depth-reconstructed or hand-built proxy geometry; a software screen-space approach (ray marching a depth/normal buffer in a pixel shader, SM 4/5, DX path) is plausible without leaving MonoGame. Not sourced.
- Because DX12 target in 3.8.5 is native-library based, DXR via it would still need raw access to the native device; not verified.

### Gaps
- No source on whether the 3.8.5 DX12 target exposes the device handle for interop, nor any DXR-with-MonoGame prior art.
- Did not verify shared-handle formats/limits for D3D11-D3D12 sharing with MonoGame's render targets.

## 4. Practical limits at 1920x1080 on an RTX-class GPU

### Takeaway
No source quantifies this for MonoGame; the hardware (RTX class) is not the constraint, the MonoGame 3.8.4 DX11 feature set and the engine's own pass structure are. Full-screen SM 5.0 pixel-shader work at 1080p is cheap on RTX hardware.

### Cited Findings
- Cited: DXR requires D3D12 and hardware ray tracing support (NVIDIA RTX class); see DXR sources above. — [NVIDIA DXR tutorial](https://developer.nvidia.com/rtx/raytracing/dxr/dx12-raytracing-tutorial-part-2)
- Verified-local: the user's PC is Windows 11 Pro 10.0.26200; the specific NVIDIA GPU model was not checked.

### Inferences
- Constraints likely to matter more than GPU speed: no compute/UAV in stock 3.8.4; MojoShader limits on the GL path; per-draw CPU cost from SpriteBatch-style world batching; and vsync/present path. All unsourced reasoning.
- Specific frame-time numbers were not found; any budget would need measurement on the user's GPU (consider timing a full-screen test shader through the existing SSAA effect, `Effects/SSAA`, which the game already loads).

### Gaps
- No GPU model, no benchmarks, and no source on MGFX performance ceilings at 1080p.

## 5. Replacing or adding effects at runtime without rebuilding the engine

### Takeaway
Yes. Effects are loaded as compiled data at runtime, so shaders can be swapped or added without recompiling the engine, as long as they are compiled with MGFXC for the same MonoGame version and profile.

### Cited Findings
- Verified-local: `public Effect(GraphicsDevice graphicsDevice, byte[] effectCode)` exists (vendored fork Effect.cs:65), so a compiled .mgfxo byte array can be loaded directly. The game's `Content.Load<Effect>("Effects/Name")` reads .xnb files from `Content\DX\Effects` or `Content\OGL\Effects`, so replacing/adding .xnb files with the same name replaces shaders on disk.
- Cited: docs say effects can be built via the content processor or processed with MGFXC and loaded at runtime. DX and GL builds need separate compiles (different defines/profile). MGFXC supports Wine for non-Windows and, as of 3.8.4, C header generation. — [Custom Effects docs](https://docs.monogame.net/articles/getting_started/content_pipeline/custom_effects.html), [CHANGELOG](https://www.github.com/MonoGame/MonoGame/blob/develop/CHANGELOG.md)
- Verified-local: the run folder includes MonoGame.Framework.Content.Pipeline.dll, which is the pipeline assembly used for building .xnb.
- Cited: 3.8.5 introduces a code-centric Content Builder that can run before the game. — [3.8.5 release](https://monogame.net/blog/2026-07-15-3.8.5-release-2026/)

### Inferences
- Adding a new shader pass (rather than replacing an existing one) still needs C# changes in Simitone/FSO (new `Effect` wrapper plus draw calls); runtime effect loading alone does not alter the render graph. Replacing the contents of an existing effect while keeping its parameter names and technique/pass names is the zero-code-change route. Parameter-name mismatches (the compiler strips unused parameters) would throw at the C# parameter lookups; not tested.
- Custom effect .xnb must be compiled against the same MonoGame version family (3.8.4 xnb format and MGFX version); mixing versions risks a load error. Not tested.

### Gaps
- Not tested: building/loading a test effect with MGFXC 3.8.4 against the run folder.
- Whether the Simitone build copies Content\DX at runtime from the repo or only from the run folder, and whether the game caches loaded effects, was not examined.
