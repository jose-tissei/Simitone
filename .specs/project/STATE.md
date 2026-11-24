# State

**Last Updated:** 2026-11-24T09:56:00-03:00
**Current Work:** Full-Screen Virtual Pocket Lot (`CadetAcademyOverlay` + `VirtualLotTransition` + Sideline Drill Sergeant w/ authentic `c_police.apr`/`c_police` head + 18-cue Police Academy Procedural SFX Engine) — Implemented, tested (303+28), and verified in 16:9 HD video with synced AAC audio; ready for Milestone 2 Rank 3 (Patrol Officer)

---

## Recent Decisions (Last 60 days)

### AD-005: Full-Screen "Virtual Pocket Lot" Pattern for Work Minigames (`VirtualLotTransition`) (2026-11-24)

**Decision:** Render 3D career work venues (starting with Level 2 Cadet: *SimCity Police Academy — Training Grounds*) edge-to-edge full-screen (`1600×900` dynamic aspect-matched `RenderTarget2D` covering 100% of the viewport) wrapped in a reusable pure-C# `VirtualLotTransition` (smooth fade-from-black entry, translucent arrival location ribbon, and fade-to-black return home) while keeping the player's real home lot `VM` safely paused in memory (`vm.SpeedMultiplier = 0`) instead of loading a separate TS1 `.iff` lot. Overlay only minimal translucent floating HUD chrome (top strip, left-anchored Briefing/Scorecard cards, bottom floating timing dock) so 3D Sims and sideline NPCs remain unobstructed.
**Reason:** Feels like teleporting to a dedicated Police Academy lot without any of TS1's lot-loading pause, save/reload risk, or neighborhood slot consumption, and showcases full-screen 3D animation, custom scenery (Precinct Building, bleachers, patrol cruisers with pulsing lightbars, waving flagpole, trees, skyline), and live NPC sideline actors (Senior Law Enforcement Drill Instructor).
**Trade-off:** Pure custom 3D staging rather than TS1 `.iff` build/buy objects, though fully compatible with authentic TS1 `SimAvatar` actors, uniforms, and custom `.simanim.json` clips.
**Impact:** Establishes the standard full-screen Virtual Pocket Lot template (`VirtualLotTransition` + edge-to-edge 3D + minimal floating HUD) for upcoming Ranks 3–10 career shifts.

### AD-004: Pure-C# Custom Skeletal Animation Runtime (`.simanim.json`) + Offline Hybrid Authoring Pipeline (2026-11-23)

**Decision:** Build a custom keyframe animation format (`.simanim.json` v1) targeting `adult.skel` (29 Vitaboy bones), played at runtime via a pure-C# quaternion-SLERP evaluator with smooth crossfades, seamless loop-wrap interpolation, and root-motion-synced playback (`Logic/CustomAnim*.cs` + `Game/CustomAnimPlayer.cs`) that drives `Avatar.Skeleton` + `Avatar.ReloadSkeleton()` without patching FreeSO DLLs. Author clips offline using a 3-tier hybrid toolchain: **Tier 1** CC-BY/public-domain BVH mocap retargeting (`Tools/AnimPipeline/bvh_to_simanim.py`), **Tier 2** local **Blender 4.5** `adult.skel` IK rig + direct `.simanim.json` exporter (`Tools/AnimPipeline/blender_adult_rig.py` + `export_simanim.py`), and **Tier 3** thin procedural overlays (head look-at, breathing, recoil).
**Reason:** Vanilla 1999 TS1 animations lack modern action semantics (e.g., `a2o-running-loop` is a fire-panic arms-up run), suffer from a FreeSO `Animator.RenderFrame` loop-wrap freeze bug (`Animator.cs:111,126`), have zero crossfade blending, and mismatch root-motion vs. world velocity. `Avatar.ReloadSkeleton()` already recomputes `AbsoluteMatrix` and updates `SkelBones[]` directly, making a clean custom runtime zero-risk to FreeSO binaries.
**Trade-off:** Requires an offline Python/Blender authoring step for new clips, and custom clips must be authored against `adult.skel`'s standing arms-down A-pose (`+Y` up, `+Z` forward, bones along local `-X`).
**Impact:** Replaces stiff/mismatched TS1 animations in Cadet Academy 3D (`sprint`, `vault`, `aim`, `shoot`, `idle_ready`, `victory_salute`) and unlocks clean custom animations for all upcoming career mini-games (Levels 3–10) and optional on-lot `VMAvatar` playback in P3.

### AD-003: Shared Simitone Design Tokens & Pure Responsive Layout Engine (2026-11-23)

**Decision:** Centralize all mod overlay styling in `Tools/Conversations/Game/SimUiTheme.cs` (`SimUiTokens`, `SimUiAssets`, `SimUiElement`) using Simitone's native `UIStyle.DARK` palette and `Content/uigraphics/` textures (`dialog_title_grad.png`, `button.png`, `greenbutton.png`, `motive_bg.png`, `diag.png`), and compute all non-overlapping responsive frames in pure C# (`Tools/Conversations/Logic/SimUiLayout.cs`).
**Reason:** Previous overlays used bespoke dark-slate colors and fixed pixel offsets that collided on compact viewports and visually clashed with Simitone's ocean-teal/mint UI chrome. Extracting layout math to `Logic/` allows automated overlap/bounds unit tests across `800×600`–`2560×1440` (`SimUiLayoutTests.cs`).
**Trade-off:** Colored cards (e.g. Shift Report grade badge) cannot tint `circle10px.png` directly because that texture is baked black `(0,0,0,191)`; `DrawGlassCard` uses `FillRect` + crisp border for tinted cards while reserving `circle10px.png` for neutral dark glass.
**Impact:** `SimMemoryMenu`, `CareerDispatchToast`, `SecurityWatchOverlay`, `ConvoOverlay`, and all upcoming Rank 2–10 overlays share one visual language and zero-overlap responsive rules (`CompactDeck`, `StackedDossierHeader`, auto-ellipsizing, clamped wrapping).

### AD-001: Non-Blocking Mid-Shift Radio Dispatch Toast (2026-11-23)

**Decision:** Trigger career mini-games via a pulsing top-right Radio Dispatch toast (30s timeout, clickable **ANSWER DISPATCH** / **IGNORE**) instead of auto-opening a modal dialog when a Sim goes to work.
**Reason:** In multi-Sim households, interrupting the player while they are managing other Sims on the lot breaks flow; an optional toast lets players opt in when they want and gracefully lets the shift resolve normally if ignored.
**Trade-off:** Players can miss a call if they don't notice the corner toast within 30s (mitigated by planned Career Panel "Work Assignment" trigger in Milestone 2).
**Impact:** `CareerDispatchToast` runs non-blocking on `UIScreen.Current`; `vm.SpeedMultiplier` only pauses after the player clicks **ANSWER DISPATCH**.

### AD-002: Pure Logic / Thin MonoGame UI Split (2026-11-23)

**Decision:** Keep all career mini-game rules, round generators, scoring, and dispatch eligibility in `Tools/Conversations/Logic/` with zero `FSO.*` / `Microsoft.Xna.*` dependencies, and keep rendering/VM glue in `Tools/Conversations/Game/`.
**Reason:** `Tools/Conversations.Tests` targets `net9.0` and compiles `..\Conversations\Logic\*.cs` directly without needing Simitone runtime DLLs, giving sub-second unit test feedback.
**Trade-off:** Thin mapping layer needed between `VMAvatar` data and pure logic structs.
**Impact:** All future ranks (Levels 2–10) should follow the same `Logic/<RankGame>.cs` + `Game/CareerDispatchUI.cs` (or dedicated view) split.

---

## Active Blockers

*None.*

---

## Lessons Learned

### L-001: Always Clone `TextStyle.DefaultLabel` in Simitone UI (2026-11-23)

**Context:** Rendering `"ALL QUIET"` inside `SecurityWatchOverlay.DrawCameraFeed`.
**Problem:** Constructing `new TextStyle { Font = camStyle.Font, Size = 12, ... }` left `VFont` uninitialized, hanging/crashing the game draw thread as soon as the overlay opened.
**Solution:** Always create `TextStyle` instances via `TextStyle.DefaultLabel.Clone()` (e.g., `MakeStyle(size, color)`) and cache them on the overlay instance.
**Prevents:** Draw-thread crashes and per-frame allocations across all upcoming Rank 2–10 mini-game overlays.

### L-002: `UIContainer.Remove` Does Not Clear `child.Parent` (2026-11-23)

**Context:** Re-opening `SimMemoryMenu` and `SecurityWatchOverlay` after closing them via `instance.Parent.Remove(instance)`.
**Problem:** `FSO.Client.UI.Framework.UIContainer.Remove(child)` removes `child` from `Children` and calls `child.Removed()`, but leaves `child.Parent` pointing to the old screen. Guarding `screen.Add(instance)` behind `if (instance.Parent != screen)` caused the overlay to silently fail to render on the second open.
**Solution:** Always call `screen.Add(instance)` unconditionally on open (it is idempotent and brings the element to the front of Z-order), only calling `instance.Parent.Remove(instance)` first if `instance.Parent != null && instance.Parent != screen`.
**Prevents:** Invisible overlays on reopen across all reusable singleton UI elements.

### L-003: Always Advance Crossfade Weights with Real `dt` When Scrubbing One-Shot Clips (2026-11-24)

**Context:** Driving timeline-locked one-shots (`cadet_wall_vault`, `cadet_target_fire_recoil`) via `seekTime` inside `CadetAcademySceneRenderer`.
**Problem:** Passing `dt = 0f` whenever `customSeekTime.HasValue` prevented `CustomAnimBlender` from advancing `Weight` from `0 -> 1`, leaving the previous looping clip frozen at 100% weight across the entire vault/shot. Similarly, relying solely on `game.ElapsedSeconds` froze the Victory Salute animation once `game.IsFinished` stopped the game timer.
**Solution:** Always tick `blender.Tick(dt)` first so crossfade weights converge smoothly, then apply `blender.SetTime(seekTime)` right before bone evaluation (`VitaboyClipPlayer.UpdateAndApply(..., seekTime)`). Use a live `Stopwatch` delta to keep post-completion Victory showcase animations running smoothly.
**Prevents:** Frozen crossfade deadlocks and static completion screens in all upcoming 3D mini-games.

### L-004: Validate Both `AvatarAppearances` AND `AvatarTextures` for Custom TS1 NPC Heads (2026-11-24)

**Context:** Binding the Sideline Drill Sergeant's head in `CadetAcademySceneRenderer.TryPickInstructorHead`.
**Problem:** Checking only `content.AvatarAppearances.Get("c_police.apr") != null` succeeded while `TS1TextureID = "policeface"` was missing from `Textures.far` (the real texture is `C_Police.bmp` → `"c_police"`), causing FSO's Vitaboy renderer to draw the entire head solid black.
**Solution:** Always validate BOTH `content.AvatarAppearances?.Get(apr) != null` AND `content.AvatarTextures?.Get(tex + ".bmp") != null` (or `Get(tex) != null`) before returning any `Outfit`, and surface the resolved head tag in diagnostics (`instrHead=c_police.apr/c_police`).
**Prevents:** Untextured solid-black NPC heads or bodies across upcoming career shifts (Patrol Officer beat NPCs, SWAT, Detective, etc.).

---

## Quick Tasks Completed

| #   | Description                                                        | Date       | Commit    | Status |
| --- | ------------------------------------------------------------------ | ---------- | --------- | ------ |
| 001 | Radio Dispatch toast + Security Guard (Lvl 1) CCTV Night Watch PoC | 2026-11-23 | `d2b87f5` | Done   |
| 002 | Simitone UI design tokens + responsive zero-overlap layout engine  | 2026-11-23 | `fc06dab` | Done   |
| 003 | Multi-Rank Dispatch Router + Level 2 Cadet Academy 3D Obstacle Course | 2026-11-23 | `ec04deb` | Done   |
| 004 | Cadet Academy briefing wrap, sprint arm posture & victory camera polish | 2026-11-23 | `04923b0` | Done   |
| 005 | Custom 3D Skeletal Animation Engine + Blender 4.5 Pipeline + 7 Cadet Clips | 2026-11-24 | `52037f3` | Done   |
| 006 | Full-Screen Virtual Pocket Lot HUD + Sideline Drill Instructor & Scenery | 2026-11-24 | pending   | Done   |

---

## Deferred Ideas

- [ ] **Option 2 Real-Time Multi-Camera `FSO.LotView.World` CCTV** — upgrade Security Guard (Lvl 1) CCTV monitors from captured lot backdrops + 3D actors to 4 live `RenderTarget2D` cameras (~15 FPS round-robin) rendering actual `FSO.LotView.World` or a dedicated mini Precinct `SubWorldComponent` (requires isolating `PPXDepthEngine` / `WorldContent` shared static render targets) — Captured during: Level 1 CCTV scale review
- [ ] Career Panel **"Work Assignment"** button (once per day) so players can also launch a shift task before/after work without waiting for the mid-shift radio call — Captured during: `career-radio-dispatch` brainstorm
- [ ] Extend level-by-level interactive mini-games to additional Sims 1 careers (Criminal, Military, Science, Medicine, Politics, Business, Entertainment, etc.) — Captured during: `career-radio-dispatch` brainstorm
- [ ] Add optional career-specific radio squelch / alarm audio stings — Captured during: `career-radio-dispatch` PoC

---

## Todos

- [x] Execute `custom-animations` (`T1`–`T8`) and verify live Cadet Academy 3D gameplay video with all 7 custom clips
- [ ] Continue Milestone 2 with Level 3 Patrol Officer (**Beat Patrol**) & Level 4 Desk Sergeant (**911 Dispatch Triage**)

---

## Preferences

**Model Guidance Shown:** never
