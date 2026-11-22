# Real-time lighting, shadows, GI and ray tracing for an isometric sprite world (Simitone / FreeSO-style) with optional 3D mesh mode

Method note: about 14 tool calls. Search results were mostly snippets or summaries; only the arXiv abstract, the FreeSO blog post and the jason.today post were fetched. Anything without a link is my inference or background knowledge and is labelled as such. Ms figures for options other than HRC and XeGTAO are NOT sourced.

## 1. Which 2D / 2.5D GI and lighting techniques exist, and which fit a sprite engine with colour + depth (+ optional normal)?

### Takeaway
Best fit by cost/benefit: (1) keep and extend FreeSO-style per-light light-maps with wall/object shadow casters, (2) add depth-derived normals so sprites shade correctly, (3) add Radiance Cascades (2D, screen/world-grid) fed by an occluder/emitter map built from the room layout for true indirect light. Screen-space AO/GTAO is a cheap add-on. LPV, voxel cone tracing and RTXGI are poor fits for a flat sprite world and are better only for the 3D mesh mode.

### Cited Findings
- FreeSO (the codebase Simitone derives from) already ships a "completely custom lighting engine, built from the ground up for TSO/TS1" using "GPU accelerated light-maps with soft shadowing". Wall shadows are projected from each wall line in the light's room, object shadows are estimated from collision bounds, and the lighting can be turned off for performance. It targets DX9.3 rather than 9.1. This is the in-engine prior art and the baseline. — [FreeSO blog, May 15th update](http://www.freeso.org/may-15th-update-brighten-your-monday/)
- A search for a Simitone-specific lighting fork returned nothing. FreeSO mentions "custom dynamic lighting" among its changes, and an AlternativeTo listing shows 131 forks of FreeSO, but I could not tell whether any is Simitone-specific. — [search result: SegerEnd/FreeSO](https://github.com/SegerEnd/FreeSO), [AlternativeTo](https://alternativeto.net/software/freeso/about)
- Radiance Cascades (RC): invented by Alexander Sannikov (Grinding Gear Games), presented at ExileCon 2023, and used for GI in Path of Exile 2. The radiance field is decomposed into ranges stored separately and rebuilt every frame with no temporal reuse. — [80.lv summary](https://80.lv/articles/radiance-cascades-new-approach-to-calculating-global-illumination). Secondary source; a Habr claim of roughly 3 ms/frame in PoE2 is unverified.
- Mechanism of 2D RC, as explained by Jason McGhee. Scene painted into a texture, then seed pass, jump flood and SDF. Each pixel's rays are split across cascades. Lower cascades have short intervals, high spatial resolution and few rays. Upper cascades have long intervals, low spatial resolution and many rays. Merging means a lower cascade reads the upper one only where its own rays hit nothing. The naive version (512 rays per pixel at 1024x1024) is about 1e9 texture lookups per frame, about 100x too slow for 60 fps. RC shares rays to cut this. The demo is WebGL/three.js and the page shows no licence. — [jason.today post / gist](https://gist.github.com/jasonjmcghee/0c72d185830685f67d3b8d9c7f330c3a)
- Cascade layout in a simple 2D implementation. The first cascade has 4 rays per probe, and each further cascade has 4x the rays, probes spaced twice as far apart (4x fewer) and rays travelling twice as far. Memory per cascade is constant. Merge is by interpolating the 4 nearest upper probes. — [Otavio Peixoto write-up](https://otaviopeixoto1.github.io/portfolio/2DRadianceCascades)
- Holographic Radiance Cascades (HRC), arXiv 2505.02041 (Freeman, Sannikov, Margel, May 2025). It is single-shot, scene-agnostic, and runs at constant cost for a given scene size. Authors report 1.85 ms at 512x512 and 7.67 ms at 1024x1024 on an RTX 3080 Laptop GPU, and claim output is visually indistinguishable from a 2D reference. Self-reported. — [arXiv](https://arxiv.org/abs/2505.02041)
- A 3D/screen-space RC line exists but is less mature. Sannikov's own 3D implementation is a video, not a repo. A Chalmers thesis on screen-space RC in 3D games lists limits: off-screen content cannot contribute or occlude, the background is over-brightened, and low settings give upscaling artifacts. Sannikov's "Bilinear 3D" depth-aware upscaler is covered in another Chalmers thesis (Screen-Space Probes with World-Space Intervals). A "Split Radiance Cascades" item (world-space 3D) was listed on GameDev.net with a July 2026 date that I could not confirm. — [search results: Chalmers thesis](https://odr.chalmers.se/items/9e42007a-bb60-440f-ba28-04410822758f/full), [Chalmers SPWI thesis](https://odr.chalmers.se/items/3ee9fb4e-1880-46c2-802a-a660e38dc9ee/full), [GameDev.net](https://gamedev.net/news/4628-split-radiance-cascades-real-time-global-illumination-via-sparse-radiance-probes/), [UE forum thread](https://forums.unrealengine.com/t/radiance-cascades-in-unreal/1939802)
- Unity URP 2D lights support per-sprite normal maps (secondary texture, "Fast" or "Accurate"). Adding normal maps triggers an extra depth pre-pass per layer batch, and the docs warn that using normal maps to simulate depth is "very expensive". Sprite tangents are wrong with flip X/Y in one shader collection. This is the standard deferred-2D-lights-with-normals model. — [Unity 2D lighting docs](https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@14.0/manual/Lights-2D-intro.html), [UnitySpriteShaders](https://github.com/pharan/UnitySpriteShaders)
- Godot's SDFGI is 3D-only (Forward+ renderer) and does not apply to 2D. Godot 2D has no built-in GI. — [Godot SDFGI docs](https://docs.godotengine.org/en/stable/tutorials/3d/global_illumination/using_sdfgi.html)
- Normal-map generators for artist-authored sprites: Laigter is free and open source, and its author says the licence covers the tool's code, not generated maps. SpriteIlluminator (CodeAndWeb) is paid, with normal export only in paid tiers. Sprite Lamp is paid on Steam. These are only useful if normals must be authored. A game with depth sprites can derive normals instead. — [Laigter itch comments](https://itch.io/post/9123701), [SpriteIlluminator licences](https://www.codeandweb.com/spriteilluminator/licenses-comparison), [Sprite Lamp Steam](https://store.steampowered.com/app/318820/)

### Inferences (not sourced; background knowledge, verify before relying)
- Required data for a sprite world: (a) per-pixel world position, which you already have from the depth sprite plus iso projection; (b) normals, reconstructed from the depth image (finite differences on world position, with edge-aware handling at silhouettes), unless the sprite pack has a normal channel (The Sims 1 sprites ship z-buffer sprites but I did not verify a normal channel exists); (c) an occluder/emitter map, which for The Sims is easy to build from the wall grid, floor tiles, room ids, and object footprints/heights, giving a top-down 2D grid (cell-level, perhaps 1/4 tile resolution) that RC or a shadow pass can use as its "scene".
- A top-down occluder grid makes RC practically a "flatland" 2D problem, which is exactly its sweet spot. Lighting is computed in world-plan space (xz) and sampled by each sprite using its world position. Height is faked using a few height layers or a per-cell height value. True 3D occlusion (an object shading a wall) would need a coarse 3D voxel grid (for example 1 cell per half-tile, a few floors) or screen-space tracing against the depth buffer.
- Screen-space methods (SSAO/GTAO/SSGI/SSR) work directly on a depth buffer composed from sprite depth, and sprite pixels written into a depth buffer are a natural fit. They suffer from the usual limits: no off-screen or hidden data, which matters for walls cutaway.
- Light Propagation Volumes and voxel cone tracing would need a voxelised scene; both are heavy for the benefit in a small-lot sprite game, and LPV has light-leaking problems. Better reserved for 3D mesh mode if at all.

### Gaps
- No verification of which Simitone/FreeSO source files implement lighting (not examined here); no check whether TS1 sprite data contains normals.
- No authoritative performance numbers for PoE2's RC, for SSGI, LPV or VCT.
- No licence verified for the RC repos beyond those listed in section 4.

## 2. Hardware ray tracing (DXR / Vulkan RT / RTX): what to render, minimum worthwhile, and software alternatives

### Takeaway
On a sprite world, hardware RT is only worth it for 3D mesh mode, or for a hybrid where sprites are reconstructed as depth-displaced geometry. Minimum visible benefit is ray-traced shadows (one ray per pixel to the light, optionally AO), which does not need a denoiser-heavy GI pipeline. For sprite mode, software ray marching on a 2D/low-res 3D grid (RC, SDF) delivers GI with no RT hardware.

### Cited Findings
- Hybrid ray-traced shadows start from a G-buffer depth/position, build a ray from the pixel's reconstructed world position to the light, and trace it. Reconstructing position from depth is imprecise, so a bias along the normal and view direction is needed. Soft penumbrae come from cone-distributed rays (PICA PICA) or hybrid blending with shadow maps (NVIDIA hybrid frustum traced shadows). One ray is traced per non-sky pixel. — [NVIDIA GDC15 hybrid RT shadows](https://developer.download.nvidia.com/assets/events/GDC15/hybrid_ray_traced_GDC_2015.pdf), [Ray Tracing Gems ch. 25 (SEED)](https://media.contentapi.ea.com/content/dam/ea/seed/presentations/2019-ray-tracing-gems-chapter-25-barre-brisebois-et-al.pdf)
- NVIDIA RTX SDKs (RTXGI, RTXDI, NRD, DLSS) share one licence that restricts modification and redistribution (the repo license is shown as "other"). RTXGI DDGI is a probe-grid GI that needs a scene BVH. The GitHub repo is NVIDIAGameWorks/RTXGI-DDGI. I did not find licence terms for RTX Remix or NRD. — [NVIDIA RTX SDKs licence](https://developer.download.nvidia.com/gameworks/NVIDIA-RTX-SDKs-License-23Jan2023.pdf), [RTX Kit](https://developer.nvidia.com/rtxgi)
- Intel's DXR guide says the minimum per-ray memory footprint is 256 bytes (no payload, no recursion). Not directly relevant to cost on this workload. — [Intel RT guide](https://www.intel.com/content/www/us/en/developer/articles/guide/real-time-ray-tracing-in-games.html)
- Software alternative with no RT hardware: 2D RC and HRC (section 1), jump-flood SDF ray marching (jason.today post).

### Inferences
- A practical DXR path for sprites: build one BVH of simple proxy geometry (wall boxes, floor, object bounding boxes or extruded depth heightfields) and use inline RayQuery from the lighting pass. The sprites are still composited normally; RT only supplies a visibility term (shadow/AO) per pixel. This is cheap to integrate if the renderer is already on D3D11/12 or Vulkan, but a DX9-era FNA/MonoGame renderer (Simitone is built on FreeSO's MonoGame) would need a new backend. I did not verify Simitone's graphics API version.
- Reflections on a sprite world have little visual payoff (floors in The Sims are mostly matte), and sprite pixel data cannot supply the reflected view-dependent result without mesh. GI via RT (DDGI/ReSTIR) needs a scene representation sprites don't have; proxy geometry would be required.
- Software RC or SDF marching delivers most of the visible benefit (soft shadows, colour bleed from emissive walls and lamps) with fewer integration risks and works on any GPU, so it is the ranked-first recommendation for sprite mode.

### Gaps
- No source for the minimum code needed for a DXR shadow pass; no sprite-specific RT prior art found (the search returned none).
- Vulkan RT / DXR driver and hardware prevalence figures not looked up.

## 3. Performance budget at 1920x1080 on RTX-class GPU, and denoising

### Takeaway
Only two sourced anchors exist: GTAO about 0.5 ms at 1080p on a mid-range card, and HRC 7.67 ms at 1024x1024 on a laptop 3080 (constant cost with scene size, not screen size). Everything else below is an estimate.

### Cited Findings
- XeGTAO (Intel, MIT licence, DX12/HLSL SM 6.3). Reported at 1080p on an RTX 2060: XeGTAO High about 0.56 ms vs ASSAO Medium about 0.72 ms. It includes a 5x5 depth-aware denoiser and relies on the host's TAA for temporal filtering. Author-reported. — [XeGTAO repo](https://github.com/GameTechDev/XeGTAO)
- HRC: 1.85 ms at 512x512, 7.67 ms at 1024x1024 on an RTX 3080 Laptop (about 4x per doubling of side, i.e. roughly linear in cell count), constant cost irrespective of scene complexity. — [arXiv 2505.02041](https://arxiv.org/abs/2505.02041)
- Naive per-pixel ray marching GI is about 100x too slow for 60 fps at 1024^2 and 512 rays. — [jason.today post](https://gist.github.com/jasonjmcghee/0c72d185830685f67d3b8d9c7f330c3a)
- RC does not use temporal accumulation (rebuilt each frame), so it needs no denoiser. — [80.lv](https://80.lv/articles/radiance-cascades-new-approach-to-calculating-global-illumination)

### Inferences: ranked comparison table (costs are my estimates unless marked sourced)
| Rank | Approach | Est. cost at 1080p, RTX-class | Quality | Complexity | Sprite fit |
|---|---|---|---|---|---|
| 1 | Per-light light-map with wall/object shadow casters (FreeSO baseline, already exists) + depth-derived normals for sprite shading | well under 1 ms | direct light + soft shadows only | low, already in codebase | excellent |
| 2 | 2D Radiance Cascades / HRC on a plan-view occluder+emitter grid (e.g. 512-1024^2 cells) | sourced HRC: 1.85-7.7 ms on a laptop 3080; a desktop RTX likely lower (estimate) | soft shadows, colour bleeding, multi-bounce possible, noiseless | medium (cascade passes, merge, grid build, sampling by sprites) | very good, because the room layout gives the occluders for free |
| 3 | GTAO / SSAO on composed depth | sourced about 0.5 ms (RTX 2060) | contact darkening only; no colour | low, MIT code | good, depth buffer exists |
| 4 | Screen-space RC / SSGI | estimate 1-4 ms | dynamic indirect for visible pixels only | high; off-screen limits (sourced) | fair |
| 5 | Hybrid DXR shadows/AO on proxy geometry | estimate 0.5-2 ms plus BVH build | accurate soft shadows | high: new API backend, proxy geometry, denoiser (SVGF/NRD) | fair in sprite mode, good in 3D mode |
| 6 | Voxel cone tracing / LPV / DDGI probes | estimate 2-6 ms plus voxelisation | full 3D GI | high | poor for sprites; only worth it in mesh mode |

Denoising need: RC/HRC none (sourced); GTAO built-in 5x5 spatial filter plus TAA (sourced); RT shadows/AO at 1 spp need a denoiser (NRD is licensed under NVIDIA's restricted RTX SDK terms; open alternatives include SVGF-style filters, not verified here); screen-space GI usually needs temporal plus spatial filtering (inference).

### Gaps
- No sourced numbers for DXR shadows, SSGI, VCT or LPV at 1080p. All rows marked "estimate" should be benchmarked.
- Cost of building the plan-view grid per frame, and of sampling it per sprite, not measured.

## 4. Open-source repos and tooling worth reusing (with licences)

### Takeaway
There are permissively licensed 2D RC code bases for study. Check each repo's LICENSE file before copying; several have no verified licence.

### Cited Findings
- sparse_rc_2d (IceKontroI): 2D sparse RC proof of concept; dual MIT/Apache-2.0. — [awesome.ecosyste.ms entry](https://awesome.ecosyste.ms/projects/github.com%2Ficekontroi%2Fsparse_rc_2d)
- solis_2d (Rust/Bevy crate): 2D GI via radiance cascades; MIT or Apache-2.0. — [docs.rs solis_2d](https://docs.rs/crate/solis_2d/0.1.1)
- bevy-magic-light-2d (zaycev): Apache-2.0 notice seen; not confirmed to use RC (it is an earlier SDF/probe-style 2D GI, per my background knowledge, not verified). — [repo](https://github.com/zaycev/bevy-magic-light-2d)
- radiance_cascades_in_2d (mpppv, C++/SDL3, "extremely naive"); licence not found. — [entry](https://awesome.ecosyste.ms/projects/github.com%2Fmpppv%2Fradiance_cascades_in_2d)
- bevy_radiance_cascades (nixon-voxell); licence not found. — [entry](https://awesome.ecosyste.ms/projects/github.com%2Fnixon-voxell%2Fbevy_radiance_cascades)
- three-rc (CodyJasonBennett): 3D RC with screen-space probes and a BVH; licence not found. — [listing](https://scour.ing/@hello/p/https://github.com/CodyJasonBennett/three-rc)
- Jason McGhee's RC article/gist (three.js demo); no licence stated on the page. — [gist](https://gist.github.com/jasonjmcghee/0c72d185830685f67d3b8d9c7f330c3a)
- Other RC write-ups: Chalmers theses ([1](https://odr.chalmers.se/items/9e42007a-bb60-440f-ba28-04410822758f/full), [2](https://odr.chalmers.se/items/3ee9fb4e-1880-46c2-802a-a660e38dc9ee/full)), [tlegoc blog](https://tlegoc.github.io/blogs/radiance-cascade), [TypeGPU RC](https://docs.swmansion.com/TypeGPU/ecosystem/typegpu-radiance-cascades/) (licence not checked).
- XeGTAO: MIT. — [GameTechDev/XeGTAO](https://github.com/GameTechDev/XeGTAO)
- GI-1.0 (AMD GPUOpen, two-level radiance cache, 3D): reference reading. — [paper](https://ar5iv.labs.arxiv.org/html/2310.19855), [GPUOpen PDF](https://gpuopen.com/download/GPUOpen2022_GI1_0.pdf)
- Laigter (open-source normal map generator; tool licence, outputs are the user's). — [itch.io](https://itch.io/post/9123701)
- NVIDIA RTXGI-DDGI / NRD / RTX SDKs: restrictive "other" licence, not copyleft or MIT. — [licence PDF](https://developer.download.nvidia.com/gameworks/NVIDIA-RTX-SDKs-License-23Jan2023.pdf)

### Inferences
- Because Simitone is GPL-/MIT-adjacent (not verified here; FreeSO's own licence is MPL-2.0 per my memory, unverified), only MIT/Apache code is safely embeddable; the NVIDIA SDK licence would be a distribution complication for an open-source game.
- Suggested incremental plan: (1) depth-to-normal pass and lit sprite shader with N·L; (2) plan-view occluder/emitter grid from walls, floors and objects; (3) port a simple 2D RC (sparse_rc_2d / solis_2d as reference) driven by that grid, with the lot ambient colour as sky/boundary radiance; (4) GTAO as optional polish; (5) DXR shadows only for 3D mesh mode after a backend upgrade.

### Gaps
- Not verified: Simitone's renderer API and shader model, which determines whether compute-shader RC is feasible without a backend change (RC can be done in pixel shaders, per the WebGL demos).
- Sprite-specific RT or relighting-from-depth prior art found no results in the searches run; no sources for Sprite Dino.
- Search tool is US-only and snippet based; several claims should be re-checked against the primary papers and repo LICENSE files.
