# Autonomous Parallel Room Social & AI-Only Talk/Romance Specification

## Problem Statement

The existing AI conversations mod (`Tools/Conversations`) enriches social interactions with Laya-classified outcomes, 3-line choices, and speech balloons, but every social still has to be triggered manually or through the game's heavyweight single-threaded social queue (which interrupts whatever activity a Sim is doing). Meanwhile, the manual pie menu still exposes dozens of legacy Sims 1 social verbs and hardcoded base-game Talk interest names (`"Travel and Toys"`, `"Sixties and School"`, etc.).

We need Sims in the same room to carry on natural, autonomous Laya-driven conversations **in parallel with their ongoing activities** whenever their Social need is at or below 60%, choosing interactions and AI-generated topics based on their knowledge of the other Sim, skills, mood, and learned memory of what went badly—while simplifying manual Sim-to-Sim pie menus to only **Talk** and **Romance** with 100% AI-sourced talk subjects.

## Goals

- [x] Sims in the same room autonomously initiate and sustain parallel verbal conversations whenever a Sim's Social motive is at or below 60% (`<= +20` on the `-100..+100` scale), without interrupting or cancelling either Sim's active VM interaction queue.
- [x] Laya selects both the parallel social interaction and the AI talk subject, modulated by the speaker's knowledge of the listener, both Sims' skills (especially Charisma and shared/known skills), and both Sims' moods.
- [x] Per-pair social memory records bad outcomes and prevents all-negative interaction loops via failed-option penalties, de-escalation shifts, and cool-down backoff.
- [x] Manual Sim-to-Sim pie menus expose only two top-level social bubbles—**Talk** and **Romance**—when AI conversations are enabled, and 100% of Talk subjects (manual and autonomous) come from the AI subject catalog rather than hardcoded Sims 1 interest labels.
- [x] Zero game-thread stalls (all Laya calls run off the VM thread) and graceful degradation when Laya is unavailable or the feature is toggled off.

## Out of Scope

| Feature | Reason |
| --- | --- |
| Live Ollama generation blocking gameplay | Gameplay remains zero-latency and offline-safe; AI subjects are pre-generated in `bank.json` (with `ConvoBank --subjects` offline builder) and selected/ranked by Laya at runtime. |
| Physical-contact parallel socials (Hug, Kiss, Slap, Dance, Back Rub) | Physical interactions require proximity/animations and stay in the manual Romance menu or native scripts; parallel room chatter uses verbal interactions (`talk`, `compliment`, `joke`, `brag`, `cheerup`, `apologize`, `flirt`, `tease`). |
| Children, pets, and non-social NPCs (Maid, Grim Reaper, etc.) | Keeps existing mod boundary; children, pets, and non-social NPCs retain unmodified Sims 1 behavior. |
| Voice / Simlish audio synthesis | Text balloons and motive/relationship simulation only. |

---

## User Stories

### P1: Autonomous Parallel Room Conversations (Social <= 60%) — MVP

**User Story**: As a player, I want Sims who share a room to talk naturally with each other while continuing their current activities whenever a Sim's Social need is at or below 60%, so the household feels alive without breaking task queues.

**Why P1**: Core behavioral enrichment: turns AI conversations from a manual-only trigger into a living, autonomous background system.

**Acceptance Criteria**:

1. WHEN AI conversations are enabled AND an awake adult human Sim's Social motive is `<= 60%` (`<= 20` on `-100..+100`) AND another awake adult human Sim is in the same indoor room (or outdoors within 8 tiles) THEN the system SHALL autonomously initiate a parallel social exchange after cooldown without modifying either Sim's `VMThread.Queue`.
2. WHEN a parallel exchange runs THEN both Sims SHALL continue their current activities uninterrupted (cooking, eating, watching TV, reading, skill-building, standing, etc.).
3. WHEN a parallel exchange resolves THEN the system SHALL display the speaker's line and listener's reply in `ConvoOverlay` speech balloons over their heads, update both Sims' Social motives (clamped `-100..+100`), and update both directions' STR and LTR relationships (clamped `-100..+100`).
4. WHEN a Sim is sleeping, dead, off-lot, a child, a pet, currently in a blocking dialog, or already participating in an active scripted or parallel social THEN the system SHALL NOT trigger a parallel exchange for that Sim.
5. WHEN AI conversations are turned off THEN NO parallel exchanges SHALL trigger.

**Independent Test**: Place two awake adult Sims in the same room with one Sim's Social at `<= 20` while both have active non-social tasks; verify a parallel exchange fires, balloons appear, Social and STR update, and neither Sim's interaction queue is cleared.

---

### P1: Laya Interaction & AI Topic Selection Guided by Knowledge, Skills, and Mood — MVP

**User Story**: As a player, I want a Sim's autonomous choice of interaction and AI talk subject to be driven by Laya and noticeably shaped by how well they know the other Sim, their skills (especially Charisma), and their moods, so socially skilled friends converse smoothly while grumpy strangers make awkward missteps.

**Why P1**: Defines the intelligence of autonomous choices so conversations feel grounded in Sims 1 mechanics rather than random chatter.

**Acceptance Criteria**:

1. WHEN the parallel social planner selects an interaction and (for `talk`) a topic THEN it SHALL query Laya asynchronously on a worker thread using a natural-language context built from the speaker's personality, skills, mood, and what the speaker currently knows about the listener.
2. WHEN the speaker has low **knowledge** of the listener (low LTR/STR and few prior exchanges) THEN the speaker's view of the listener's temperament, interests, skills, and mood SHALL be partial/noisy, increasing the probability of choosing a suboptimal interaction or topic.
3. WHEN the speaker has high **knowledge** of the listener (high LTR/STR and/or prior observed exchanges) THEN the speaker SHALL accurately perceive the listener's temperament, interests, and skills, significantly raising choice quality.
4. WHEN the speaker has higher **Charisma** skill (0..1000) THEN social judgment accuracy SHALL increase and misjudgment noise SHALL decrease; WHEN both Sims share or the speaker knows high non-Charisma **skills** (Cooking, Mechanical, Creativity, Logic, Body) THEN AI subjects aligned with those skills SHALL receive affinity priority and resonance bonuses.
5. WHEN the speaker is in a **bad mood** (`Mood < -30`) THEN selection SHALL suffer higher error/impatience bias; WHEN the listener is in a **bad mood** and the speaker has sufficient Knowledge/Charisma to read it THEN the speaker SHALL prefer gentler/supportive interactions (`talk`, `cheerup`, `compliment`).
6. WHEN Laya is unavailable, paused, or times out THEN the planner SHALL fall back cleanly to deterministic trait/skill/mood/memory scoring with zero VM-thread blocking or exceptions.

**Independent Test**: Unit-test `SocialIntelligence` and `ParallelSocialPlanner` across (low vs high knowledge), (low vs high Charisma/skills), and (bad vs good mood) with a fake `ILayaTransport`, asserting prompt context redaction, probability modulation, and deterministic accuracy trends.

---

### P1: Learning from Bad Interactions & Anti-Negative-Loop Protection — MVP

**User Story**: As a player, I want Sims to remember interactions and topics that went badly with a specific Sim and steer away from them, so two Sims never get trapped in an endless negative relationship spiral.

**Why P1**: Autonomous social chatter without failure memory quickly causes runaway STR drops when two mismatched Sims keep picking bad interactions.

**Acceptance Criteria**:

1. WHEN any social interaction or Talk subject (parallel OR manual/scripted) ends with a negative outcome (`quality == 1`) between speaker A and listener B THEN `SocialMemory` SHALL record the failed `(interaction, tone)` and/or `subjectId` for pair `(A, B)`, increment `NegativeStreak`, and apply a decaying score penalty to the failed option.
2. WHEN selecting a subsequent interaction or topic for pair `(A, B)` THEN options with recent negative history SHALL be penalized/avoided in favor of untested or previously successful options.
3. WHEN `NegativeStreak >= 2` for pair `(A, B)` THEN the planner SHALL engage anti-negative-loop de-escalation: forbidding hostile/risky interactions (`tease`, `brag`, `flirt`) and `Bold` tone, and strongly boosting safe/repair options (`apologize`, gentle `talk` on a safe/liked AI subject, `cheerup`, `compliment`).
4. WHEN `NegativeStreak >= 3` for pair `(A, B)` THEN the planner SHALL enforce an extended pair cool-down backoff so the pair pauses autonomous chatter rather than looping negatively.
5. WHEN a positive outcome (`quality == 3`) occurs for pair `(A, B)` THEN `SocialMemory` SHALL reset `NegativeStreak` to 0, record the successful interaction/topic as a known preference, and increase the speaker's knowledge of the listener.
6. WHEN the game exits or saves memory THEN `SocialMemory` SHALL persist cleanly to `Documents/Simitone/conversations.memory.json` and reload on next startup without ever throwing on missing/corrupt files.

**Independent Test**: Simulate repeated negative outcomes for `(Bob, Betty)` on `joke` and a disliked AI subject; verify `joke` and that subject are avoided next, verify de-escalation kicks in at streak 2, verify backoff cooldown triggers at streak 3, and verify a positive outcome resets the streak and persists through JSON round-trip.

---

### P1: Simplified Manual Pie Menu (Talk & Romance Only) and 100% AI Talk Subjects — MVP

**User Story**: As a player, when I manually click another Sim I want only two bubbles—**Talk** and **Romance**—and when I pick **Talk**, I want all conversation subjects to be rich AI-generated subjects rather than hardcoded Sims 1 interest names.

**Why P1**: Explicitly requested UI simplification and completion of the AI-driven social model.

**Acceptance Criteria**:

1. WHEN AI conversations are enabled AND the player inspects the pie menu of another adult human Sim (with at least one Talk or Romance interaction available) THEN `GetPieMenu` SHALL expose ONLY two top-level bubbles:
   - **`Talk`** (single direct top-level entry bound to the best available Talk interaction, preferring `Talk.../About Interests` or `Talk`)
   - **`Romance`** (category bubble `"Romance/<Action>"` containing only the available romantic interactions: Flirt, Kiss, Romantic/Intimate/Leap-Into-Arms Hug, Back Rub, Slow/Romantic Dance, Propose).
2. WHEN AI conversations are OFF THEN `GetPieMenu` SHALL return the exact unmodified Sims 1 pie menu.
3. WHEN the target Sim is self, a pet, a child, an ungreeted visitor with only `Greet`, or a non-social NPC with neither Talk nor Romance available THEN `GetPieMenu` SHALL leave the original menu intact so no gameplay flow is softlocked.
4. WHEN the player clicks **Talk** manually THEN the 3 offered subjects in the choice dialog SHALL come exclusively from the **AI Subject Bank** (`AiSubjectBank`, ranked by Laya/context for the two Sims)—never the raw hardcoded Sims 1 labels (`"Travel and Toys"`, `"Violence and Aliens"`, `"Politics and Pets"`, `"Sixties and School"`, `"Weather"`, `"Sports"`, `"Music"`, `"Outdoors"`).
5. WHEN an AI subject is chosen in manual or autonomous Talk THEN it SHALL map cleanly to its underlying Sims 1 interest (`0..7`) and optional skill affinity so the game's relationship scoring and balloon lines reflect both the AI subject title and the listener's genuine likes/skills.

**Independent Test**: Run `PieMenuLogic` unit tests on realistic Sims 1 Complete Collection menu lists (adult with full socials, low-rel adult without romance, ungreeted visitor, child, pet, feature off); run `AiSubjectBank` and `ConvoTalk` tests verifying 3 distinct AI subjects are offered and none match raw TS1 interest strings.

---

### P2: Diagnostics, ConvoBank Subject Generation, and Patch Integrity

**User Story**: As a developer/tester, I want `simtool convo` commands to inspect parallel status, social memory, and trigger/test parallel chatter deterministically, plus `ConvoBank --subjects` to regenerate AI subjects with Ollama, and automated patch tests confirming all Cecil hooks are idempotent and valid.

**Acceptance Criteria**:

1. WHEN `simtool convo status` runs THEN it SHALL report parallel engine state, AI subject count, memory pair count, and last parallel/manual decision.
2. WHEN `simtool convo memory` or `simtool convo menu full|simple` or `simtool convo trigger <speaker> <listener>` runs THEN it SHALL inspect memory, toggle menu filtering for legacy test scripts, or trigger an immediate parallel exchange on demand.
3. WHEN `Tools/ci.ps1` runs against a pristine Simitone v0.8.12 install THEN all unit tests, patch tests (including `VM.InternalTick` and `VMEntity.GetPieMenu` hooks and idempotency), and smoke tests SHALL pass with exit code 0.

---

## Edge Cases

- WHEN Laya is down or returns malformed JSON or near-tie probabilities THEN parallel planning and Talk subject ranking SHALL seamlessly fall back to deterministic scoring using traits, knowledge, skills, mood, and memory.
- WHEN two Sims are outdoors (`VMRoom.IsOutside == true` or room 0) THEN they SHALL only count as "same room" if within 8 tiles of each other; indoors they count whenever `roomA == roomB`.
- WHEN a Sim's Social motive is exactly 60% (`+20`) THEN it SHALL satisfy the trigger threshold (`<= 20`); when `> 20` (e.g., `+21` / 60.5%) THEN it SHALL NOT autonomously trigger.
- WHEN `bank.json` is missing or lacks a `"subjects"` section (e.g. an older `bank.json`) THEN `AiSubjectBank` SHALL load its rich built-in catalog of AI-authored subjects so Talk still never falls back to raw TS1 interest labels.
- WHEN `conversations.memory.json` is missing, empty, or corrupted THEN `SocialMemory` SHALL start fresh without throwing.
- WHEN multiple rooms have Sims talking at the same time THEN `ConvoOverlay` SHALL preserve active balloons from separate exchanges (up to a bounded cap) instead of erasing one pair mid-sentence.
- WHEN `SpriteCardsPatch` runs twice on already-patched DLLs THEN all new hooks (`VM.InternalTick`, `VMEntity.GetPieMenu`) SHALL be idempotent and rewrite zero files.

---

## Requirement Traceability

| ID | Story | Component(s) | Status |
| --- | --- | --- | --- |
| APS-01 | P1 Parallel room trigger when Social <= 60% without interrupting queue | `ParallelSocialPlanner`, `ParallelSocialDriver`, `SpriteCardsPatch` | Done |
| APS-02 | P1 Same-room detection (indoor room equality, outdoor proximity) & Sim eligibility (awake, adult, human, not busy in social/dialog) | `ParallelSocialDriver`, `ParallelSocialPlanner` | Done |
| APS-03 | P1 Parallel resolution: speech balloons, Social motive delta, STR/LTR updates | `ParallelSocialDriver`, `ConvoOverlay` | Done |
| APS-04 | P1 Laya async scoring for interaction + AI topic selection | `LayaClient`, `ParallelSocialPlanner` | Done |
| APS-05 | P1 Knowledge of other Sim (LTR/STR + observed history) impacts visibility & accuracy | `SocialIntelligence`, `SocialMemory` | Done |
| APS-06 | P1 Skills (Charisma accuracy + Cooking/Mechanical/Creativity/Logic/Body affinity) impact choice quality & resonance | `SocialIntelligence`, `Traits`, `AiSubjectBank` | Done |
| APS-07 | P1 Mood (speaker mood tact/error + perceived listener mood) impacts choice quality | `SocialIntelligence` | Done |
| APS-08 | P1 Learning from bad interactions (per-pair penalty for failed interaction/tone/topic) | `SocialMemory`, `ParallelSocialPlanner`, `ConvoPrimitive`, `ConvoTalkPrimitive` | Done |
| APS-09 | P1 Anti-negative-loop guard (streak >= 2 de-escalation, streak >= 3 pair backoff, positive reset) | `SocialMemory`, `ParallelSocialPlanner` | Done |
| APS-10 | P1 SocialMemory JSON persistence | `SocialMemory`, `Boot` | Done |
| APS-11 | P1 Manual pie menu filtered to only `Talk` and `Romance` (with safe edge fallbacks & off switch) | `PieMenuLogic`, `ConvoPieMenu`, `SpriteCardsPatch` | Done |
| APS-12 | P1 100% AI Talk subjects (manual & autonomous) with `bank.json` catalog + `ConvoBank --subjects` | `AiSubjectBank`, `ConvoTalkPrimitive`, `bank.json`, `ConvoBank` | Done |
| APS-13 | P2 `simtool convo` diagnostics & test triggers | `Boot`, `ParallelSocialDriver` | Done |
| APS-14 | P2 Full unit tests, Cecil patch tests, and `Tools/ci.ps1` green gate | `Conversations.Tests`, `PatchTests`, `ci.ps1` | Done |

**Coverage:** 14 total requirements mapped to design and tasks.
