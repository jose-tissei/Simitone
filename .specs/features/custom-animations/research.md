# Deep Research: Custom 3D Skeletal Animation Pipeline for Simitone (`adult.skel` / Vitaboy)

**Date:** 2026-11-23  
**Feature:** `custom-animations`  
**Verification Chain Followed:** Codebase (FreeSO source + Simitone mod) → Installed Game Assets (`Animation.far`) → Local Toolchain Audit → Web Research (tools, formats, mocap licenses, math)

---

## Executive Summary

Vanilla Sims 1 animations look stiff, mismatched, and comical in modern 3D minigame viewports for **five verified technical reasons**:

1. **Wrong semantic clips exist in 1999 TS1**: TS1 has no athletic track sprint (both `a2o-running-loop` and `a2o-walking-frantic-loop` are panic/fire runs with arms raised), no tactical hurdle vault (`a2o-pool-dive-jumpin` is a swimming-pool dive), and no precision range stance (`a2o-bball-shoot` is a basketball throw).
2. **Hard loop-wrap freeze in `FSO.Vitaboy.Animator.RenderFrame`**: At `frame == motion.FrameCount - 1`, `Animator.RenderFrame` clamps `next = curr` instead of wrapping to `0`, causing a visible hitch every loop cycle (`Animator.cs:111,126`).
3. **Zero crossfading / blending**: Switching stages or states (Idle → Sprint → Jump → Salute) snaps bones instantaneously in 1 frame.
4. **Root-motion vs. world-speed mismatch (foot skating)**: `a2o-walking-fast-loop` encodes `dist = 4.11` over `666ms` (`~3.70` world units/s at `SimScale = 0.60`), while the track moved Bob at `6.0` units/s — causing obvious moonwalking/sliding.
5. **Baked `ROOT` 90° Y orientation & crude whole-mesh pitch**: `adult.skel` rotates `ROOT` by `~90° Y` (`Q = (0, 0.707, 0, 0.707)`), and TS1's 20-frame compressed `.cfp` tracks have coarse quantization (`Delta` table) and rigid wrists.

**Key Good News (Verified in Source & Local Machine):**
- `FSO.Vitaboy` exposes **100% open C# bone transforms** (`Avatar.Skeleton.Bones[i].Translation` & `.Rotation` → `Avatar.ReloadSkeleton()` → `SkelBones` GPU skinning array). We do **not** need `.far`/`.bcf`/`.cfp` binary packing or any engine DLL patches to play custom 3D skeletal animations at 60 FPS!
- We can also construct runtime `FSO.Vitaboy.Animation` objects in memory (all fields are `public`) and optionally inject them into `TS1BCFProvider` so even **on-lot Sims via `VMAvatar`** can play custom animations later.
- **Blender 4.5** (`C:\Program Files\Blender Foundation\Blender 4.5`), **Python 3.11**, **Node.js**, and **ffmpeg** are already installed locally.

**Recommended Architecture:** A **3-tier, 100%-offline, license-clean hybrid pipeline**:
1. **Pure-C# Runtime Animation Engine** (`.simanim.json` clip player with wrap-safe SQUAD/SLERP interpolation, smooth crossfading, upper/lower-body bone masking at `SPINE`, root-motion speed coupling, hand gestures, timed events, and optional 2-bone analytical IK).
2. **Offline BVH Mocap Retargeter** targeting `adult.skel` using **CC-BY 4.0 / CC-BY 3.0** datasets (**100STYLE**, **ACCAD**, **CMU Mocap**) for fluid, natural human locomotion (true athletic sprint, hurdle vault, patrol walk, crouch, idle).
3. **Blender 4.5 Authoring + Headless `bpy` Exporter** (importing `adult.skel` once into an IK rig) for crisp, stylized Sims-flavored career actions & upper-body overlays (range aim/recoil, crisp salute, shoulder radio, clipboard ticket, flashlight sweep, SWAT breach, Captain Hero pose).

---

## 1. Verified FreeSO / Vitaboy Engine Internals

Inspected directly in `C:\Users\Zé\.t3\projects\the-sims\Simitone\FreeSO\TSOClient\`:

| Component | Source Path | Key Verified Facts |
|---|---|---|
| **Skeleton & FK** | `tso.vitaboy.model\Skeleton.cs` | `ComputeBonePositions(bone, world)` evaluates `myWorld = (Matrix.CreateFromQuaternion(bone.Rotation) * Matrix.CreateTranslation(bone.Translation)) * world` (MonoGame row-vector convention: local rotate, local translate, then parent world). |
| **Coordinate Mirroring** | `Skeleton.cs:147-160`, `Animation.cs:190-211`, `CFP.cs:67-76` | Vitaboy files are **left-handed**; FreeSO mirrors **X** on load into right-handed MonoGame space: `T = (-x, y, z)` and `Q = (x, -y, -z, -w)` (negating `-w` keeps the same 3D rotation as `(-x, y, z, w)`). |
| **GPU Skinning** | `tso.vitaboy.engine\Avatar.cs:248-256` | `Avatar.ReloadSkeleton()` runs `Skeleton.ComputeBonePositions(RootBone, Matrix.Identity)` and copies `Skeleton.Bones[i].AbsoluteMatrix` directly into `SkelBones[]`, uploaded to the shader as `SkelBindings`. |
| **Mesh Binding** | `tso.vitaboy.model\Mesh.cs:109-148` | Vertices are stored in **bone-local space** (primary bone in `Parameters.X`, optional blend bone in `Parameters.Y` with weight `Parameters.Z`). No inverse-bind-pose matrix is multiplied at runtime; bone transforms move vertices directly! |
| **Playback** | `tso.vitaboy.engine\Animator.cs:92-135` | `RenderFrame(avatar, anim, frame, fraction, weight)` overwrites or blends `bone.Translation` (`Vector3.Lerp`) and `bone.Rotation` (`Quaternion.Slerp`). **Loop bug confirmed**: `(frame + 1 >= motion.FrameCount) ? curr : next` freezes across the loop boundary instead of wrapping to `0`. |
| **Runtime `Animation` Class** | `tso.vitaboy.model\Animation.cs:25-60, 290-303` | `Animation` and `AnimationMotion` have `public` parameterless constructors and `public` fields (`Translations[]`, `Rotations[]`, `Motions[]`, `Duration`, `Distance`, `IsMoving`, `FramesPerSecond`). Can be instantiated purely in C# without disk `.bcf`/`.cfp` files. |
| **On-Lot VM Playback** | `tso.simantics\Entities\VMAvatar.cs` | `VMAvatar` drives `Animator.RenderFrame` with normalized weights (`state.Weight / totalWeight`), layers `CarryAnimationState` at `weight = 1f`, applies `ApplyHeadSeek`, reads `lefthand`/`righthand` `TimeProperties` to set `SimAvatar.LeftHandGesture`/`RightHandGesture`, and calls `ReloadSkeleton()`. |
| **TS1 Content Provider** | `tso.content\TS1\TS1BCFProvider.cs` | Resolves `*.anim` via `AnimHostBCF` dictionary (`BCF` header + `CFP.EnrichAnim`). Does **not** natively load loose `.anim` files in TS1 mode — custom clips are cleanest when loaded directly by our mod's `CustomAnimationLibrary` (with an optional bridge into `VMAvatar` when needed). |

---

## 2. Exact `adult.skel` Bone Anatomy (Dumped from `Animation.far`)

Extracted directly from `C:\Program Files (x86)\Maxis\The Sims\GameData\Animation\Animation.far` (`adult-skeleton.cmx.bcf`, 1,937 bytes) using FreeSO's exact `Skeleton.ReadBone` and `ComputeBonePositions` math:

- **Total Bones:** 29
- **Units & Scale:** Total rest height (`R_TOE0` at `Y = -0.005` to `HEAD` at `Y = 5.050` + head mesh crown `~5.5`) is ~`5.5` Vitaboy units (`~3.3` Cadet world units at `SimScale = 0.60`).
- **World Axes (MonoGame RH):** `+Y` = Up, `+Z` = Sim Forward (toes point `+Z`: `0.347 → 0.655`), `+X` = Sim's Left (`L_ARM` at `+0.130`, `R_ARM` at `-0.130`).
- **Rest Pose:** Natural **standing arms-down A-pose** (NOT T-pose!). Both `L_ARM1` and `R_ARM1` hang almost straight down (`Y: 4.566 → 3.690 → 2.875`). **Any BVH/Mixamo T-pose retargeter MUST compute an explicit T-pose reference quaternion $R^T_{\text{ref},i}$ for `adult.skel` before applying delta rotations.**
- **Bone Local Axis Convention:** Every limb and spine bone extends along its **local `-X` axis** (`SPINE localT = (-0.381, 0, 0)`, `R_LEG1 localT = (-1.447, 0, 0)`, `R_ARM2 localT = (-0.882, 0, 0)`).

| Idx | Bone | Parent | Rest World Pos `(X, Y, Z)` | Role / Mask Group |
|---:|---|---|---|---|
| 0 | `ROOT` | `NULL` | `( 0.000, 3.014,  0.008)` | Root translation + yaw (`T=true, R=true`) |
| 1 | `PELVIS` | `ROOT` | `( 0.000, 3.011, -0.028)` | Lower body root (`T=true, R=true`) |
| 2 | `SPINE` | `PELVIS` | `( 0.000, 3.391, -0.057)` | **Upper-body mask split point** |
| 3 | `SPINE1` | `SPINE` | `( 0.000, 3.812, -0.062)` | Mid torso |
| 4 | `SPINE2` | `SPINE1` | `( 0.000, 4.233, -0.067)` | Chest |
| 5 | `NECK` | `SPINE2` | `( 0.000, 4.654, -0.072)` | Neck & shoulder hub (parents both arms + head!) |
| 6 | `R_ARM` | `NECK` | `(-0.130, 4.654, -0.072)` | Right clavicle/shoulder |
| 7 | `R_ARM1` | `R_ARM` | `(-0.496, 4.566, -0.071)` | Right upper arm (length `0.882`) |
| 8 | `R_ARM2` | `R_ARM1` | `(-0.584, 3.690, -0.094)` | Right forearm (length `0.820`) |
| 9 | `R_HAND` | `R_ARM2` | `(-0.666, 2.875, -0.033)` | Right hand (also supports `SimHandGesture`) |
| 10 | `R_FINGER0` | `R_HAND` | `(-0.711, 2.578, -0.010)` | Right fingers |
| 11 | `L_ARM` | `NECK` | `( 0.130, 4.654, -0.072)` | Left clavicle/shoulder |
| 12 | `L_ARM1` | `L_ARM` | `( 0.496, 4.566, -0.071)` | Left upper arm (length `0.882`) |
| 13 | `L_ARM2` | `L_ARM1` | `( 0.584, 3.690, -0.094)` | Left forearm (length `0.820`) |
| 14 | `L_HAND` | `L_ARM2` | `( 0.666, 2.875, -0.034)` | Left hand (also supports `SimHandGesture`) |
| 15 | `L_FINGER0` | `L_HAND` | `( 0.728, 2.581, -0.012)` | Left fingers |
| 16 | `HEAD` | `NECK` | `( 0.000, 5.050, -0.029)` | Head (supports `CalculateHeadSeek`) |
| 17 | `R_LEG` | `PELVIS` | `(-0.241, 3.011, -0.028)` | Right thigh (length `1.447`) |
| 18 | `R_LEG1` | `R_LEG` | `(-0.241, 1.565,  0.031)` | Right shin (length `1.316`) |
| 19 | `R_FOOT` | `R_LEG1` | `(-0.241, 0.249,  0.007)` | Right ankle/foot |
| 20 | `R_TOE0` | `R_FOOT` | `(-0.241,-0.005,  0.347)` | Right toe base |
| 21 | `R_TOE01` | `R_TOE0` | `(-0.241,-0.005,  0.598)` | Right toe mid |
| 22 | `R_TOE02` | `R_TOE01` | `(-0.241,-0.005,  0.655)` | Right toe tip |
| 23 | `L_LEG` | `PELVIS` | `( 0.241, 3.011, -0.029)` | Left thigh (length `1.447`) |
| 24 | `L_LEG1` | `L_LEG` | `( 0.241, 1.565,  0.039)` | Left shin (length `1.316`) |
| 25 | `L_FOOT` | `L_LEG1` | `( 0.241, 0.250,  0.007)` | Left ankle/foot |
| 26 | `L_TOE0` | `L_FOOT` | `( 0.242,-0.005,  0.346)` | Left toe base |
| 27 | `L_TOE01` | `L_TOE0` | `( 0.242,-0.005,  0.597)` | Left toe mid |
| 28 | `L_TOE02` | `L_TOE01` | `( 0.242,-0.005,  0.654)` | Left toe tip |

---

## 3. Pipeline Options Compared

| Approach | Quality | Authoring Speed | Iteration Loop | License & Offline Safety | Engine Risk | Scales to 10+ Career Ranks? |
|---|---|---|---|---|---|---|
| **A. Classic Sims 1 Tools** (`SimPose`, Volcanic glTF, [`ts1-blender-io`](https://github.com/mixiate/ts1-blender-io)) | Medium–High (`ts1-blender-io` uses Blender IK; SimPose is legacy FK) | Slow–Moderate | Slow (binary `.bcf`/`.cfp` re-export) | `ts1-blender-io`: **MPL-2.0, offline**; SimPose: abandoned closed binary | Low (native format), no blending/layering | Weak alone (no upper/lower masking) |
| **B. Blender 4.5 (`bpy`) + Custom Clip Exporter** | **High** (full IK/FK, curves, pose library, Sims charm) | Moderate (fast once `.blend` IK rig exists) | **Fast** with hot-reload JSON | **100% Offline & Open** (Blender 4.5 already installed locally!) | Low (1:1 29-bone mapping) | **Excellent** for signature moves & upper-body overlays |
| **C. Free Mocap + Offline BVH Retargeter** ([100STYLE](https://www.ianxmason.com/100style/), [ACCAD](https://accad.osu.edu/research/motion-lab/mocap-system-and-data), [CMU](http://mocap.cs.cmu.edu/)) | **High** for athletics & locomotion | **Very Fast** (batch-convert dozens of clips in seconds) | Fast CLI batch convert | **100STYLE (CC BY 4.0)**, **ACCAD (CC BY 3.0)**, **CMU (free w/ notice)**; **AVOID Mixamo** (forbids repo redistribution), **Bandai/SFU** (NC) | Medium (T-pose vs A-pose & hip-height math — solved once!) | **Excellent** for shared locomotion & physical drills |
| **D. AI Motion Gen** ([HY-Motion 1.0](https://github.com/Tencent-Hunyuan/HY-Motion-1.0), [Rokoko Video](https://www.rokoko.com), [Plask](https://plask.ai), MediaPipe) | Low–Medium raw (foot skating, jitter) | Fast blocking, slow cleanup | Slow | **Poor**: paid SaaS (Rokoko/Plask), regional/MAU restrictions (HY-Motion), NC datasets (MDM/MotionDiffuse) | High (noisy root + skating) | Poor as primary pipeline |
| **E. Pure-C# Data-Driven Clip Engine + Blending & IK** | **High** when fed by B + C (and supports sparse authored keyframes too) | Fast once built (5–10× clip reuse via masking) | **Instant** (live hot-reload preview in Simitone!) | **100% Offline, MIT-safe, zero external deps** | Low–Medium (pure C# math, 100% unit-testable in `net9.0`) | **Essential backbone** for all 10 Law Enforcement ranks + future careers |

---

## 4. Verified Local Environment

Checked directly on this Windows machine:
- **Blender 4.5** installed at `C:\Program Files\Blender Foundation\Blender 4.5\blender.exe`
- **Python 3.11** at `C:\Program Files\Python311\python.exe`
- **Node.js** at `C:\nvm4w\nodejs\node.exe`
- **ffmpeg** available on PATH
- **dotnet SDK** available (`net461` mod build + `net9.0` xUnit test runner)
- **Original TS1 `Animation.far`** available at `C:\Program Files (x86)\Maxis\The Sims\GameData\Animation\Animation.far`

---

## 5. Recommendation

Build the **Hybrid Runtime + Offline Pipeline (`E + C + B`)**:
1. **Phase 1 (MVP — Immediate Visual Transformation):**
   - Implement the **Custom Skeletal Clip Runtime** (`Logic/CustomAnim*.cs` + `Game/VitaboyClipPlayer.cs`) with `.simanim.json` loading, wrap-safe SQUAD/SLERP interpolation, smooth crossfading, upper/lower-body masking, hand gestures, and root-motion velocity sync.
   - Build the **Python Offline Pipeline (`Tools/AnimPipeline/`)** containing:
     - `dump_adult_skel.py` (exports verified 29-bone rest pose + T-pose reference JSON directly from `Animation.far`),
     - `bvh_to_simanim.py` (global-space delta quaternion retargeter converting any CC-BY BVH from 100STYLE/ACCAD/CMU to `.simanim.json` with foot-floor grounding & stride-speed extraction),
     - `blender_rig_setup.py` + `blender_export_simanim.py` (headless Blender 4.5 scripts to generate a ready-to-animate `adult.skel` IK `.blend` rig and export Actions directly to `.simanim.json`).
   - Ship the **first custom Cadet Academy animation pack** (6 crisp clips: `cadet_idle_attention`, `cadet_sprint_loop`, `cadet_hurdle_vault`, `cadet_target_aim_loop`, `cadet_target_fire_recoil`, `cadet_victory_salute`) authored/retargeted through the pipeline, replacing all awkward vanilla TS1 fallback animations in `CadetAcademySceneRenderer`.
2. **Phase 2 (Polish & Scale to Ranks 3–10):**
   - Add **analytical 2-bone limb IK** (foot grounding + target aiming) and a **live Simitone hot-reload preview harness** (`convo anim preview <clip>`) with turntable MP4 capture.
   - Add upper-body career overlay packs (patrol radio, clipboard ticket, flashlight sweep, handcuffing, SWAT breach, Captain Hero hover/landing) layered over shared locomotion bases.
   - Optionally register clips into `VMAvatar` for on-lot milestone perks (Level 3 *Neighborhood Watch*, Level 6 *Question Sim*, Level 10 *Heroic Pose*).
