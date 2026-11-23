# Simitone Conversations & Interactive Careers

**Vision:** Bring The Sims 1 (Simitone) Sims and careers to life with AI-driven autonomous social conversations, persistent per-Sim memory dossiers, and optional rank-specific mid-shift **Radio Dispatch** mini-games that reward real Simoleons, Job Performance, Skills, and neighborhood relationships.
**For:** Simitone players who want deeper social storytelling and bite-sized (15–45s) hands-on career moments without interrupting multi-Sim household management.
**Solves:** Vanilla Sims 1 careers are passive off-lot rabbit holes where Sims disappear for hours with zero player agency or rank identity.

## Goals

- Deliver an unobtrusive **Radio Dispatch** notification system that triggers once per Sim per work shift when an active-family Sim is off-lot at work, and cleanly degrades to normal shift resolution if ignored.
- Give every rank (Levels 1–10) of the **Law Enforcement** career a distinct, 15–45 second interactive mini-game plus milestone on-lot pie-menu perks tied to real VM state (Budget, JobPerformance, Skills, Motives, Relationships, Sim Memory, and Laya dialogue).
- Keep all mini-game rules in pure, fast, unit-tested C# under `Tools/Conversations/Logic/` so UI rendering stays thin and every rank's rules are verified automatically in `Tools/Conversations.Tests/`.

## Tech Stack

**Core:**

- Game Runtime: Simitone (FreeSO / MonoGame `.NET Framework 4.6.1`, `net461`)
- Mod Assembly: `Tools/Conversations/Conversations.csproj` (injected via `Tools/Patch` / `Install-Conversations.ps1`)
- Test Runner: `Tools/Conversations.Tests/Conversations.Tests.csproj` (`net9.0`, xUnit)

**Key dependencies:**

- `FSO.SimAntics` (VM clock, `VMAvatar`, `VMPersonDataVariable`, `VMMotive`, `CurrentFamily.Budget`, `LotTilePos.OUT_OF_WORLD`)
- `FSO.Client.UI.Framework` (`UIScreen`, `UIElement`, `UISpriteBatch`, `TextStyle.DefaultLabel`)
- `Laya` local LLM integration (`LayaClient`, `SimMemoryStore`) for social/dialogue-driven career ranks (Vice Squad, Police Chief) and Detective clues

## Scope

**v1 includes:**

- Core Radio Dispatch notification toast & pause-safe floating overlay framework
- Complete **Law Enforcement** career track (Levels 1–10) interactive mini-games
- Law Enforcement milestone on-lot perks (Lvl 3 `Neighborhood Watch`, Lvl 6 `Question Sim`, Lvl 10 `Heroic Pose / Inspire Room`)
- Instant debug/testing pipe commands (`convo dispatch ...`) for deterministic verification

**Explicitly out of scope (v1):**

- Full playable off-lot 3D workplace lots (all shift mini-games run as fast floating overlays over the live lot)
- Mandatory popups that force-pause multi-Sim households without the player clicking **ANSWER DISPATCH**
- Other Sims 1 career tracks (Criminal, Military, Science, Medicine, Politics, etc.) — deferred to v2 after Law Enforcement is complete

## Constraints

- **Technical:** UI styles in `FSO.Client.UI.Framework` MUST clone `TextStyle.DefaultLabel` (`VFont` is required by Simitone's vector font renderer).
- **Technical:** Keep pure game logic decoupled from `FSO.*` / `Microsoft.Xna.*` assemblies so `Tools/Conversations.Tests` compiles `..\Conversations\Logic\*.cs` directly on `net9.0`.
- **Testing:** Live visual verification runs against an isolated copy (`Simitone.DispatchPoc.exe`) and never modifies or kills a user's running Simitone instance.
