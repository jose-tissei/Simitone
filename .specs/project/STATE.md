# State

**Last Updated:** 2026-11-23T13:45:00-03:00
**Current Work:** career-radio-dispatch - Specification captured for Ranks 2–10 & on-lot perks

---

## Recent Decisions (Last 60 days)

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

---

## Quick Tasks Completed

| #   | Description                                                        | Date       | Commit    | Status |
| --- | ------------------------------------------------------------------ | ---------- | --------- | ------ |
| 001 | Radio Dispatch toast + Security Guard (Lvl 1) CCTV Night Watch PoC | 2026-11-23 | `d2b87f5` | Done   |

---

## Deferred Ideas

- [ ] Career Panel **"Work Assignment"** button (once per day) so players can also launch a shift task before/after work without waiting for the mid-shift radio call — Captured during: `career-radio-dispatch` brainstorm
- [ ] Extend level-by-level interactive mini-games to additional Sims 1 careers (Criminal, Military, Science, Medicine, Politics, Business, Entertainment, etc.) — Captured during: `career-radio-dispatch` brainstorm
- [ ] Add optional career-specific radio squelch / alarm audio stings — Captured during: `career-radio-dispatch` PoC

---

## Todos

- [ ] Review Security Guard (Lvl 1) PoC feel & pacing with user before starting Milestone 2 design
- [ ] Refactor `CareerDispatchUI.cs` into a rank-routed dispatch registry when beginning Milestone 2 (Levels 2–4)

---

## Preferences

**Model Guidance Shown:** never
