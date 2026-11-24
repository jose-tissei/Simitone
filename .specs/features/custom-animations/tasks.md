# Custom 3D Skeletal Animation System — Implementation Tasks

**Spec:** `.specs/features/custom-animations/spec.md`  
**Design:** `.specs/features/custom-animations/design.md`  
**Status:** Ready for Approval

---

## Gate Check Commands

- **Quick gate (Logic unit tests):** `dotnet test Tools/Conversations.Tests/Conversations.Tests.csproj -c Release --nologo`
- **Full gate (Both test suites):** `dotnet test Tools/Conversations.Tests/Conversations.Tests.csproj -c Release --nologo && dotnet test Tools/SpriteCards.Tests/SpriteCards.Tests.csproj -c Release --nologo`
- **Python pipeline gate:** `python Tools/AnimPipeline/verify_pipeline.py`
- **Live visual E2E gate:** Isolated `Simitone.CadetAnim.exe` 20 FPS MP4 + stills capture + silhouette/foot-grounding verification

---

## Phase 1 — Pure C# Animation Engine (MVP Core)

### T1: Pure-C# Math Primitives, Clip Data Models & `.simanim.json` Loader (`ANIM-01`)

**What:** Create `CustomAnimModel.cs` (`AnimVec3`, `AnimQuat` with `Normalize`/`Dot`/`Slerp`/`Squad`/`Multiply`/`Conjugate`, `CustomAnimClip`, `CustomBoneTrack`, `CustomAnimEvent`, `CustomHandCue`, `AnimBoneMask`) and `CustomAnimLoader.cs` (zero-dependency JSON parser/writer with quaternion normalization and hemisphere alignment on load), plus unit tests.  
**Where:**
- `Tools/Conversations/Logic/CustomAnimModel.cs`
- `Tools/Conversations/Logic/CustomAnimLoader.cs`
- `Tools/Conversations.Tests/CustomAnimTests.cs`
**Depends on:** None  
**Reuses:** Pure `Logic/` zero-Xna pattern (`SimUiLayout.cs`, `CadetAcademyGame.cs`)

**Done when:**
- [ ] `CustomAnimLoader.ParseJson` parses valid v1 `.simanim.json` clips, normalizes quaternions, aligns adjacent hemispheres (`dot >= 0`), and handles malformed JSON gracefully.
- [ ] Round-trip `ParseJson(ToJson(clip))` preserves tracks, events, gestures, and `RootSpeedUnitsPerSec`.
- [ ] Quick gate passes (`285 + >=12` new tests = `>=297` passing).

**Tests:** unit  
**Gate:** quick  
**Commit:** `feat(custom-anim): add .simanim.json data models, quaternion math, and loader`

---

### T2: Wrap-Safe Evaluator, Crossfade Blender & Upper/Lower Bone Masking (`ANIM-02`, `ANIM-03`, `ANIM-07`)

**What:** Create `CustomAnimBlender.cs` implementing wrap-safe looping evaluation (SQUAD / shortest-arc SLERP across `frameCount - 1 -> 0`), smooth multi-clip crossfading, `SPINE`-split upper/lower-body masked layering, timed event dispatching, and root-motion forward displacement calculation.  
**Where:**
- `Tools/Conversations/Logic/CustomAnimBlender.cs`
- `Tools/Conversations.Tests/CustomAnimTests.cs` (extend)
**Depends on:** T1  
**Reuses:** `AnimQuat` / `CustomAnimClip` from T1

**Done when:**
- [ ] Loop wrap `t = Duration - 0.001 -> t = 0.001` produces continuous rotations (`dot > 0.99`) with zero frame-end freeze.
- [ ] Crossfade `Play(clipB, fadeSec: 0.2f)` monotonically blends poses from clip A to clip B without overshoot.
- [ ] Upper-body overlay mask (`SPINE` + descendants) leaves `PELVIS`/leg bones 100% driven by the base layer.
- [ ] `RootSpeedUnitsPerSec` outputs exact per-tick forward travel synced to playback speed.
- [ ] Quick gate passes (`>=310` total tests passing).

**Tests:** unit  
**Gate:** quick  
**Commit:** `feat(custom-anim): add wrap-safe SQUAD evaluator, crossfade blender, and upper/lower masking`

---

## Phase 2 — Offline Python & Blender 4.5 Authoring / Retargeting Pipeline

### T3: `adult.skel` Extractor & Offline BVH Retargeter (`ANIM-04`) [P]

**What:** Create `Tools/AnimPipeline/dump_adult_skel.py` (extracts exact 29-bone rest pose + visual T-pose reference `adult_skel_ref.json` from `Animation.far`) and `Tools/AnimPipeline/bvh_to_simanim.py` (parses standard RH Y-up BVH, applies Global-Space Delta Quaternion retargeting, scales hip height, grounds feet to `Y = 0`, extracts `rootSpeedUnitsPerSec`, and writes `.simanim.json`), plus `verify_pipeline.py` self-test.  
**Where:**
- `Tools/AnimPipeline/dump_adult_skel.py`
- `Tools/AnimPipeline/adult_skel_ref.json`
- `Tools/AnimPipeline/bvh_to_simanim.py`
- `Tools/AnimPipeline/bone_maps/standard_bvh.json`
- `Tools/AnimPipeline/verify_pipeline.py`
**Depends on:** T1 (schema finalized)  
**Reuses:** Verified `Animation.far` BCF reader math from `research.md`

**Done when:**
- [ ] `dump_adult_skel.py` generates `adult_skel_ref.json` with all 29 bones matching FreeSO `Skeleton.ComputeBonePositions`.
- [ ] `bvh_to_simanim.py` converts a synthetic/CC-BY BVH walk/run cycle with FK verification error `< 1e-4`, grounded feet (`minY >= -0.02`), and non-zero `rootSpeedUnitsPerSec`.
- [ ] Output `.simanim.json` loads cleanly in C# `CustomAnimLoader` (verified by unit test).

**Tests:** unit + python CLI verification  
**Gate:** quick  
**Commit:** `feat(anim-pipeline): add adult.skel extractor and global-delta BVH retargeter`

---

### T4: Blender 4.5 IK Rig Generator & Headless `bpy` Exporter (`ANIM-05`) [P]

**What:** Create `Tools/AnimPipeline/blender_rig_setup.py` (builds `adult_rig.blend` with the 29-bone `adult.skel` armature, pole-target leg/arm IK, and visual guide mesh) and `Tools/AnimPipeline/blender_export_simanim.py` (headless Blender 4.5 exporter sampling any Action + pose-marker events into exact MonoGame/FreeSO local bone transforms `.simanim.json`).  
**Where:**
- `Tools/AnimPipeline/blender_rig_setup.py`
- `Tools/AnimPipeline/blender_export_simanim.py`
- `Tools/AnimPipeline/README.md` (how to open `adult_rig.blend`, animate, and export in 1 command)
**Depends on:** T3 (`adult_skel_ref.json`)  
**Reuses:** Basis transform $B = R_X(+90^\circ)$ from `design.md`

**Done when:**
- [ ] Running `"C:\Program Files\Blender Foundation\Blender 4.5\blender.exe" --background --python Tools/AnimPipeline/blender_rig_setup.py` exits `0` and produces `adult_rig.blend`.
- [ ] Exporting a rest-pose Action via `blender_export_simanim.py` reproduces `adult_skel_ref.json` local bone quaternions within `< 1e-3` error.

**Tests:** python + Blender CLI verification  
**Gate:** build  
**Commit:** `feat(anim-pipeline): add Blender 4.5 adult.skel IK rig builder and headless exporter`

---

## Phase 3 — Vitaboy Bridge, Cadet Academy 6-Clip Pack & Live Verification

### T5: `VitaboyClipPlayer`, Hot-Reload Library & Deployment Hook (`ANIM-01`, `ANIM-03`)

**What:** Create `Tools/Conversations/Game/VitaboyClipPlayer.cs` (loads/caches `.simanim.json` clips from `<App>/CustomAnims/`, applies `CustomAnimBlender` poses onto `SimAvatar.Skeleton.Bones` + hand gestures + `ReloadSkeleton()`) and update `Tools/Install-Conversations.ps1` to copy `Tools/Conversations/Assets/Animations/*.simanim.json` to `<App>/CustomAnims/`.  
**Where:**
- `Tools/Conversations/Game/VitaboyClipPlayer.cs`
- `Tools/Install-Conversations.ps1` (modify)
**Depends on:** T2  
**Reuses:** `SimAvatar` + `ResetSkeletonToBase` pattern from `CadetAcademySceneRenderer.cs`

**Done when:**
- [ ] `Conversations.csproj` builds cleanly for `net461` with zero errors.
- [ ] Missing or corrupt clip falls back safely without throwing.
- [ ] `Install-Conversations.ps1` deploys `CustomAnims/*.simanim.json` alongside `Conversations.dll`.
- [ ] Full gate passes.

**Tests:** unit + build  
**Gate:** full  
**Commit:** `feat(custom-anim): add VitaboyClipPlayer bridge and CustomAnims deployment`

---

### T6: Author/Retarget Cadet Academy 6-Clip Custom Pack & Wire `CadetAcademySceneRenderer` (`ANIM-06`)

**What:** Produce 6 high-quality `.simanim.json` clips via our Blender 4.5 / BVH pipeline (`cadet_idle_attention`, `cadet_sprint_loop`, `cadet_hurdle_hop`, `cadet_wall_vault`, `cadet_target_aim_loop`, `cadet_target_fire_recoil`, `cadet_victory_salute`), wire them into `CadetAcademySceneRenderer.cs` with smooth crossfading and stride-locked ground speed, and capture a live 20 FPS MP4 + stills to visually verify every stage.  
**Where:**
- `Tools/AnimPipeline/generate_cadet_pack.py`
- `Tools/Conversations/Assets/Animations/cadet_*.simanim.json`
- `Tools/Conversations/Game/CadetAcademySceneRenderer.cs` (modify)
**Depends on:** T4, T5  
**Reuses:** `VitaboyClipPlayer`, `CadetAcademySceneRenderer` 3D yard & camera rig

**Done when:**
- [ ] All 6 custom clips load (`source=custom`) in live Simitone with zero fallback warnings.
- [ ] Sprint shows natural bent-elbow athletic pumping arms, knee drive, forward spine lean (no `simPitch` tilt hack), and stride-matched non-skating track speed.
- [ ] Vault shows an authentic tucked hurdle/wall vault over the brick wall (not a pool dive).
- [ ] Target Drill shows a steady two-handed range stance + recoil kick, and Final Scorecard shows a sharp victory salute.
- [ ] Live isolated capture MP4 + stills visually verified and full test gate passes.

**Tests:** unit + live visual E2E capture  
**Gate:** full  
**Commit:** `feat(cadet-academy): replace vanilla TS1 anims with custom 6-clip skeletal animation pack`

---

## Phase 4 — Scaling & Polish (P2 / P3)

### T7: Live Hot-Reload Animation Previewer (`convo anim preview` / `reload`) (`ANIM-08`) [P]

**What:** Add a minimal debug preview overlay (`AnimPreviewOverlay.cs`) triggered via `convo anim preview <clip> [overlay] [speed]` and `convo anim reload` that renders any `.simanim.json` clip on Bob/Betty in uniform with turntable camera, floor grid, and frame telemetry.  
**Where:**
- `Tools/Conversations/Game/AnimPreviewOverlay.cs`
- `Tools/Conversations/Game/Boot.cs` (modify pipe command router)
**Depends on:** T5  
**Reuses:** `VitaboyClipPlayer`, `SimUiElement`

**Done when:**
- [ ] `convo anim preview cadet_sprint_loop` opens live 3D turntable preview.
- [ ] Editing a `.simanim.json` file on disk and calling `convo anim reload` updates the live Sim pose without restarting Simitone.

**Tests:** live pipe integration  
**Gate:** full  
**Commit:** `feat(custom-anim): add live hot-reload 3D animation previewer`

---

### T8: Analytical 2-Bone Limb IK Solver & `VMAvatar` Runtime Adapter (`ANIM-09`) [P]

**What:** Add `TwoBoneIkSolver.cs` (Law of Cosines + pole vector for foot pinning & dynamic target aiming) in `Logic/` and `VitaboyAnimationAdapter.cs` (`CustomAnimClip -> FSO.Vitaboy.Animation`) in `Game/` so future on-lot perks (Ranks 3, 6, 10) can play custom clips directly on neighborhood Sims.  
**Where:**
- `Tools/Conversations/Logic/TwoBoneIkSolver.cs`
- `Tools/Conversations/Game/VitaboyAnimationAdapter.cs`
- `Tools/Conversations.Tests/TwoBoneIkTests.cs`
**Depends on:** T2, T5  
**Reuses:** `AnimVec3` / `AnimQuat` from T1

**Done when:**
- [ ] 2-bone IK unit tests verify end-effector reaches reachable targets within `< 0.01`, preserves bone lengths, and bends knees/elbows strictly toward the pole vector.
- [ ] `VitaboyAnimationAdapter.ToVitaboyAnimation(clip)` constructs a valid `FSO.Vitaboy.Animation` usable by `Animator.RenderFrame`.
- [ ] Full gate passes.

**Tests:** unit  
**Gate:** full  
**Commit:** `feat(custom-anim): add analytical 2-bone limb IK and VMAvatar animation adapter`

---

## Parallel Execution Map

```text
Phase 1 (Sequential):
  T1 (Models + JSON Loader) --> T2 (Evaluator + Crossfade + Masking)

Phase 2 (Parallel after T1):
  T1 complete --> T3 [P] (adult.skel Extractor + BVH Retargeter)
              --> T4 (Blender 4.5 IK Rig + Headless Exporter, after T3)

Phase 3 (Integration):
  T2 complete --> T5 (VitaboyClipPlayer + Installer Deploy)
  T4 + T5 complete --> T6 (Cadet Academy 6-Clip Pack + Live Capture)

Phase 4 (Parallel Polish after T5):
  T5 complete --> T7 [P] (Live Hot-Reload Previewer)
              --> T8 [P] (2-Bone IK + VMAvatar Adapter)
```

---

## Diagram–Definition Cross-Check

| Task | Depends On (task body) | Diagram Shows | Status |
|---|---|---|---|
| T1 | None | Start | Match |
| T2 | T1 | T1 -> T2 | Match |
| T3 | T1 | T1 -> T3 | Match |
| T4 | T3 | T3 -> T4 | Match |
| T5 | T2 | T2 -> T5 | Match |
| T6 | T4, T5 | T4 + T5 -> T6 | Match |
| T7 | T5 | T5 -> T7 | Match |
| T8 | T2, T5 (T5 implies T2) | T5 -> T8 | Match |

## Test Co-location Validation

| Task | Code Layer | Required Tests | Task Tests Field | Status |
|---|---|---|---|---|
| T1 | `Logic/` | unit (`Conversations.Tests`) | unit | OK |
| T2 | `Logic/` | unit (`Conversations.Tests`) | unit | OK |
| T3 | `Tools/AnimPipeline/` | CLI + C# loader unit | unit + python CLI | OK |
| T4 | `Tools/AnimPipeline/` | Blender CLI round-trip | python + Blender CLI | OK |
| T5 | `Game/` + installer | build + unit | unit + build | OK |
| T6 | `Assets/` + `Game/` | live visual E2E + full | unit + live visual E2E | OK |
| T7 | `Game/` | pipe integration | live pipe integration | OK |
| T8 | `Logic/` + `Game/` | unit (`TwoBoneIkTests`) | unit | OK |
