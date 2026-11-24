# Custom 3D Skeletal Animation System Specification

**Status:** Draft (Pending User Approval)  
**Feature:** `custom-animations`  
**Research:** `.specs/features/custom-animations/research.md`

---

## Problem Statement

Vanilla Sims 1 (`Animation.far`) clips were built in 1999 for isometric 2D gameplay and lack authentic athletic, tactical, and career-specific motions. Reusing fallback TS1 clips (like `a2o-walking-fast-loop`, `a2o-pool-dive-jumpin`, and `a2o-bball-shoot`) in close-up 3D career minigames produces stiff poses, raised/awkward arms, loop-wrap hitches, instant 1-frame pose snaps, and foot skating.

We need our own **custom 3D skeletal animation runtime and offline authoring/retargeting pipeline** targeting Simitone's exact 29-bone `adult.skel` Vitaboy rig so every career minigame (starting with Level 2 Cadet Academy and scaling cleanly to Ranks 3–10 and future careers) has fluid, expressive, zero-skating animations that we fully own and can author or retarget in minutes.

---

## Out of Scope

| Feature | Reason |
|---|---|
| Custom child / pet (`child.skel`, `dog`, `kat`) rigs | v1 targets the 29-bone `adult.skel` used by all working adult Sims; child/pet skeletons can reuse the same format later. |
| Custom 3D mesh/clothing authoring or skinning changes | Existing TS1/Simitone uniforms and heads (`SimAvatar`) work directly with `adult.skel`. |
| Redistributing Mixamo / Bandai-Namco / SFU motion assets | Licensing forbids repo redistribution or commercial use; only **100STYLE (CC BY 4.0)**, **ACCAD (CC BY 3.0)**, **CMU Mocap** (with notice), and **original Blender-authored clips** are permitted. |
| Cloud/paid AI mocap SaaS dependencies | The entire pipeline must run **100% offline** on this machine (Blender 4.5 + Python 3.11 + C#). |
| Modifying shipped FreeSO/Simitone engine DLLs (`FSO.Vitaboy.dll`) | Our animation runtime runs cleanly inside `Tools/Conversations` via public `Avatar.Skeleton.Bones` + `Avatar.ReloadSkeleton()`. |

---

## User Stories

### P1: Core Custom Animation Runtime & Smooth Blending — MVP

**User Story:** As a Simitone player, I want 3D minigame Sims to play fluid, seamlessly looping, smoothly crossfaded custom skeletal animations with grounded feet so they look natural and responsive instead of stiff or skating.

**Why P1:** Without a clean C# clip player that fixes Vitaboy's loop hitch, adds crossfading, and couples stride speed to motion, even great source animations will look jerky or slide across the floor.

**Acceptance Criteria:**

1. WHEN a `.simanim.json` clip is loaded THEN system SHALL parse bone tracks (local `Translation` and `Rotation` keyframes), metadata (`Duration`, `Fps`, `Loop`, `RootSpeedUnitsPerSec`), hand gesture cues, and timed events, validating all bone names against `adult.skel` without throwing on unknown optional bones.
2. WHEN a looping clip advances across the final frame (`t >= Duration`) THEN system SHALL smoothly wrap interpolation between the last keyframe and frame `0` (with quaternion hemisphere alignment `dot(q0, q1) >= 0`) with zero single-frame hitch or quaternion flip.
3. WHEN switching from clip A to clip B with crossfade duration `fadeSec > 0` THEN system SHALL blend every bone in local TRS space (`Vector3.Lerp` + shortest-arc `Quaternion.Slerp`) across `fadeSec` before committing to `Avatar.ReloadSkeleton()`.
4. WHEN a locomotion clip specifies `RootSpeedUnitsPerSec > 0` THEN system SHALL drive or scale world travel speed / playback cadence from that stride rate so boots do not visibly skate across the ground.
5. WHEN a clip is missing or malformed on disk THEN system SHALL log a single diagnostic warning and gracefully fall back to the verified vanilla TS1 clip without crashing the draw thread.

**Independent Test:** Pure `net9.0` unit tests in `Tools/Conversations.Tests/CustomAnimTests.cs` verifying JSON round-trip, wrap-around continuity (`dot > 0.99`), crossfade monotonicity, hemisphere sign correction, upper/lower bone mask isolation, and malformed-input fallback.

---

### P1: Offline BVH Retargeter + Blender 4.5 Authoring Pipeline — MVP

**User Story:** As a creator/developer, I want reproducible offline tools to both batch-retarget CC-licensed BVH mocap clips AND author/export custom keyframed clips in Blender 4.5 straight onto `adult.skel` `.simanim.json` files so we can produce crisp animations rapidly.

**Why P1:** Hand-writing raw quaternions in C# is slow and unnatural; having a verified BVH retargeter + ready-to-animate Blender 4.5 `adult.skel` IK rig unlocks real athletic motion and stylized career actions immediately.

**Acceptance Criteria:**

1. WHEN `dump_adult_skel.py` runs against installed TS1 `Animation.far` THEN it SHALL emit `adult_skel_ref.json` containing all 29 bones' exact hierarchy, rest-pose transforms, and computed T-pose reference quaternions (`R_ref`).
2. WHEN `bvh_to_simanim.py` converts a standard humanoid BVH (100STYLE / ACCAD / CMU) using a bone-mapping config THEN it SHALL apply global-space delta quaternion retargeting, scale root hip motion by hip-height ratio, enforce hemisphere continuity, ground the lowest toe/foot to `Y = 0`, extract `RootSpeedUnitsPerSec`, and output a valid `.simanim.json` clip.
3. WHEN `blender_rig_setup.py` runs via headless Blender 4.5 THEN it SHALL build a reusable `.blend` file with the exact 29-bone `adult.skel` armature, pole-target arm/leg IK controls, and reference proportions so animations can be authored or polished directly in Blender.
4. WHEN `blender_export_simanim.py` exports a named Blender Action THEN it SHALL convert Blender's right-handed Z-up armature poses into FreeSO/MonoGame `adult.skel` local bone transforms and write a valid `.simanim.json` clip with pose-marker events preserved.

**Independent Test:** Run the Python/Blender CLI scripts end-to-end on a test BVH and a test Blender Action, verify output `.simanim.json` passes the C# loader schema & bone-limit sanity tests, and visually inspect the rendered Sim in Simitone.

---

### P1: Cadet Academy Custom Animation Pack (6 Signature Clips) — MVP

**User Story:** As a player playing Level 2 Cadet Academy, I want my uniformed Sim to stand at sharp attention during Briefing, sprint athletically with pumping bent arms, vault cleanly with a tucked hurdle/wall leap, take a proper two-handed target stance with recoil, and snap a crisp victory salute at the finish arch.

**Why P1:** Directly resolves the user's complaint ("the animations look like shit") on our flagship 3D minigame and proves the entire pipeline end-to-end in live gameplay video.

**Acceptance Criteria:**

1. WHEN Cadet Academy opens on Briefing THEN the Cadet SHALL play `cadet_idle_attention` (alert standing posture with subtle breathing, arms naturally at sides/relaxed ready).
2. WHEN Stage 1 (Sprint) is active THEN the Cadet SHALL play `cadet_sprint_loop` (bent-elbow athletic arm pump, forward torso drive, full knee lift, stride-synced ground travel — zero hands-up panic pose and zero moonwalking) and smoothly crossfade into `cadet_hurdle_hop` over hurdles.
3. WHEN Stage 2 (Vault) resolves Perfect/Good THEN the Cadet SHALL crossfade from sprint approach into `cadet_wall_vault` (explosive takeoff, tucked knees/hands clearing the brick wall, cushioned landing roll/recover) instead of a swimming-pool dive.
4. WHEN Stage 3 (Target Drill) is active and fires THEN the Cadet SHALL hold `cadet_target_aim_loop` (authentic two-handed steady range stance tracking the aim sweep) and crossfade into `cadet_target_fire_recoil` on lock-in, followed by `cadet_victory_salute` on Perfect hit / Final Scorecard.
5. WHEN transitioning between any two stages or states THEN crossfade blending (`0.12s–0.22s`) SHALL eliminate 1-frame pose snaps.

**Independent Test:** Live isolated `Simitone.CadetAnim.exe` 20 FPS MP4 + stills capture verifying all 6 custom clips load (`source=custom`), animate smoothly with grounded boots and natural arm posture, and show crisp silhouettes in Briefing, Sprint, Vault, Target, and Victory.

---

### P2: Upper/Lower-Body Layered Masking & Live Preview Harness

**User Story:** As a creator scaling animations to Law Enforcement Ranks 3–10 (Patrol Officer, Desk Sergeant, Vice Squad, Detective, Lieutenant, SWAT, Chief, Captain Hero) and future careers, I want to layer reusable upper-body action clips over lower-body locomotion/idle clips and hot-reload clips in a live Simitone previewer.

**Why P2:** Cuts the number of clips needed across 10+ careers by 5×–10× and turns clip iteration into a 2-second visual feedback loop.

**Acceptance Criteria:**

1. WHEN a base layer (e.g., `patrol_walk_loop` or `cadet_idle_attention`) and an upper-body overlay (split at `SPINE`, e.g., `upper_radio_talk`, `upper_write_ticket`, `upper_flashlight_sweep`) play simultaneously THEN lower-body bones (`PELVIS`, legs, feet) SHALL come from the base layer while upper-body bones (`SPINE` and descendants) blend smoothly toward the overlay by `overlayWeight`.
2. WHEN `convo anim preview <clipName>` (and `convo anim reload`) is sent over the test pipe THEN Simitone SHALL display a clean 3D turntable preview of the uniformed Sim playing the hot-reloaded clip with frame/speed telemetry and support MP4 capture.

**Independent Test:** Unit tests verifying `PELVIS`/`L_LEG`/`R_LEG` remain untouched while `SPINE`/`R_ARM1`/`HEAD` take overlay rotations; live pipe preview test loading and hot-reloading a modified `.simanim.json` without restarting Simitone.

---

### P3: Analytical 2-Bone Limb IK & On-Lot `VMAvatar` Bridge

**User Story:** As a player, I want aimed arms to point precisely at dynamic 3D targets, feet to stay locked to uneven surfaces/mats, and custom career animations (like Level 10 Captain Hero Pose or Level 3 Neighborhood Watch) to work on actual Sims on the live neighborhood lot.

**Why P3:** Elevates quality from "great minigame animations" to "studio-grade procedural polish + full neighborhood lot integration" without blocking the MVP.

**Acceptance Criteria:**

1. WHEN 2-bone analytical IK is enabled on an arm or leg chain THEN the solver SHALL position the wrist/ankle at the target within `< 0.02` units using Law of Cosines + pole vector without flipping or stretching bone lengths.
2. WHEN a custom clip is converted to a runtime `FSO.Vitaboy.Animation` instance via `VitaboyAnimationAdapter.ToVitaboyAnimation(clip)` THEN it SHALL play cleanly via `Animator.RenderFrame` on any `VMAvatar` on the live lot.

**Independent Test:** Unit tests verifying exact 2-bone IK reachability, pole-vector knee/elbow bend direction, and bone-length preservation; adapter test converting `.simanim.json` → `FSO.Vitaboy.Animation` and verifying frame counts & transforms.

---

## Edge Cases

- WHEN a clip contains only a subset of the 29 `adult.skel` bones (e.g., upper-body overlay) THEN unspecified bones SHALL retain their base-layer or `BaseSkeleton` rest-pose transform instead of snapping to zero/identity.
- WHEN a quaternion in a `.simanim.json` file is slightly unnormalized (due to JSON rounding) THEN the loader SHALL normalize every quaternion on load (`q.Normalize()`).
- WHEN adjacent keyframes have opposite quaternion signs (`Dot(q0, q1) < 0`) THEN the loader SHALL flip `q1 = -q1` during preprocessing so SLERP/SQUAD always takes the short arc without 360° limb spin.
- WHEN `dt` spikes (e.g., window lag or pause resume) THEN the player SHALL clamp per-tick delta (`dt <= 0.05f`) so animations never skip through one-shot events or explode crossfade weights.
- WHEN Simitone runs with `Content/Animations/Custom/` missing or empty THEN `CadetAcademySceneRenderer` SHALL seamlessly fall back to the existing TS1 `Animation.far` clips with zero errors.

---

## Requirement Traceability

| Requirement ID | Story | Phase | Status |
|---|---|---|---|
| `ANIM-01` | P1: `.simanim.json` schema, loader & validation | Design | Pending |
| `ANIM-02` | P1: Wrap-safe SQUAD/SLERP interpolation & hemisphere alignment | Design | Pending |
| `ANIM-03` | P1: Local-TRS crossfade blending & root-motion speed coupling | Design | Pending |
| `ANIM-04` | P1: `adult.skel` extractor + Offline BVH retargeter (`bvh_to_simanim.py`) | Design | Pending |
| `ANIM-05` | P1: Blender 4.5 IK rig builder + headless `bpy` exporter | Design | Pending |
| `ANIM-06` | P1: Cadet Academy 6-clip custom pack & renderer integration | Design | Pending |
| `ANIM-07` | P2: Upper/Lower-body bone masking (split at `SPINE`) | Design | Pending |
| `ANIM-08` | P2: Live Simitone hot-reload preview harness (`convo anim preview`) | Design | Pending |
| `ANIM-09` | P3: Analytical 2-bone limb IK & `VMAvatar` runtime adapter | Design | Pending |

**Coverage:** 9 total, 9 mapped in `design.md` and `tasks.md`, 0 unmapped.

---

## Success Criteria

- [ ] `Conversations.Tests` passes all existing 285 tests + ≥25 new unit tests covering clip parsing, loop-wrap continuity, SQUAD/SLERP, crossfading, bone masking, and stride-speed sync.
- [ ] `bvh_to_simanim.py` and `blender_export_simanim.py` both run offline on this machine with exit code `0` and emit schema-valid `.simanim.json` files.
- [ ] Live 20 FPS Cadet Academy capture (`Simitone.CadetAnim.exe`) plays all 6 custom clips with natural bent-arm sprinting, real wall-vault tuck, two-handed target stance + recoil, crisp victory salute, smooth crossfades, and grounded non-skating boots.
- [ ] Zero proprietary/non-commercial motion assets committed to the repository (100% CC-BY / CMU-with-notice / original Blender clips).
