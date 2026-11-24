# State

**Last Updated:** 2026-11-23T23:36:00-03:00
**Current Work:** custom-animations - Deep research, spec, design, and task breakdown ready for user approval

---

## Recent Decisions (Last 60 days)

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

---

## Quick Tasks Completed

| #   | Description                                                        | Date       | Commit    | Status |
| --- | ------------------------------------------------------------------ | ---------- | --------- | ------ |
| 001 | Radio Dispatch toast + Security Guard (Lvl 1) CCTV Night Watch PoC | 2026-11-23 | `d2b87f5` | Done   |
| 002 | Simitone UI design tokens + responsive zero-overlap layout engine  | 2026-11-23 | `fc06dab` | Done   |
| 003 | Multi-Rank Dispatch Router + Level 2 Cadet Academy 3D Obstacle Course | 2026-11-23 | `ec04deb` | Done   |
| 004 | Cadet Academy briefing wrap, sprint arm posture & victory camera polish | 2026-11-23 | `04923b0` | Done   |

---

## Deferred Ideas

- [ ] **Option 2 Real-Time Multi-Camera `FSO.LotView.World` CCTV** — upgrade Security Guard (Lvl 1) CCTV monitors from captured lot backdrops + 3D actors to 4 live `RenderTarget2D` cameras (~15 FPS round-robin) rendering actual `FSO.LotView.World` or a dedicated mini Precinct `SubWorldComponent` (requires isolating `PPXDepthEngine` / `WorldContent` shared static render targets) — Captured during: Level 1 CCTV scale review
- [ ] Career Panel **"Work Assignment"** button (once per day) so players can also launch a shift task before/after work without waiting for the mid-shift radio call — Captured during: `career-radio-dispatch` brainstorm
- [ ] Extend level-by-level interactive mini-games to additional Sims 1 careers (Criminal, Military, Science, Medicine, Politics, Business, Entertainment, etc.) — Captured during: `career-radio-dispatch` brainstorm
- [ ] Add optional career-specific radio squelch / alarm audio stings — Captured during: `career-radio-dispatch` PoC

---

## Todos

- [ ] Get user sign-off on `custom-animations` (`research.md`, `spec.md`, `design.md`, `tasks.md`) before executing Phase 1 (`T1`–`T6`)
- [ ] After `custom-animations` ships, continue Milestone 2 with Level 3 Patrol Officer (**Beat Patrol**) & Level 4 Desk Sergeant (**911 Dispatch Triage**)

---

## Preferences

**Model Guidance Shown:** never
