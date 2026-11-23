# Career Radio Dispatch & Law Enforcement Mini-Games Specification

## Problem Statement

In The Sims 1 / Simitone, careers are passive off-lot rabbit holes: Sims leave in the carpool and return hours later with no interactive gameplay tied to their job rank. Players need optional, bite-sized (15–45 second) rank-specific mini-games and on-lot perks that make climbing the **Law Enforcement** career (Levels 1–10) feel distinct and rewarding without disrupting multi-Sim household play.

## Goals

- [x] Ship a verified PoC for the **Radio Dispatch** toast + **Level 1 (Security Guard): CCTV Night Watch** mini-game with real VM rewards (`d2b87f5`).
- [ ] Generalize the dispatch trigger and overlay shell to route all **10 Law Enforcement ranks** cleanly based on `(Career, PromotionLevel)`.
- [ ] Deliver unique, skill-responsive 15–45 second mini-games for **Levels 2–10** that tie into Simoleons, Job Performance, Skills, Motives, real neighborhood Sims, **Sim Memory**, and **Laya** social dialogue.
- [ ] Unlock 3 milestone **on-lot pie-menu perks** (Lvl 3, Lvl 6, Lvl 10) that reward career progression during regular home gameplay.

## Out of Scope

Explicitly excluded from this feature spec to prevent scope creep.

| Feature | Reason |
| --- | --- |
| Playable 3D off-lot workplace maps | Overlays keep shifts fast (15–45s) and avoid lot-switching reload overhead |
| Other Sims 1 career tracks (Criminal, Military, Science, etc.) | Deferred until Law Enforcement 1–10 proves the multi-rank framework |
| Mandatory shift popups that auto-pause the game | Violates multi-Sim household flow; players must always opt in via the toast or Career tab |

---

## Trigger Mechanisms

1. **Mid-Shift Radio Dispatch Toast (Implemented in PoC):** While an active-family Sim in Law Enforcement is off-lot at work during shift hours, a pulsing corner toast (*"RADIO DISPATCH — CH-04 LAW ENFORCEMENT"*) appears once per Sim per game day with a 30s countdown. Clicking **ANSWER DISPATCH** pauses `vm.SpeedMultiplier` and opens that rank's mini-game; clicking **IGNORE** or letting it expire resolves the shift normally.
2. **Optional Career Tab "Work Assignment" Button (Planned — P2):** A once-per-day action on the Career subpanel letting players trigger their rank's shift task before or after work (shares the same daily cooldown as Radio Dispatch so rewards cannot be double-farmed).

---

## User Stories

### P1: Core Radio Dispatch & Lvl 1 Security Guard — CCTV Night Watch (MVP — Shipped)

**User Story**: As a player with a Level 1 Security Guard Sim on night shift (12 AM – 6 AM), I want an optional Radio Dispatch call that opens a 4-camera CCTV Night Watch mini-game so that spotting real intruders vs. false alarms earns overtime pay and Job Performance.

**Why P1**: Proves the entire non-blocking toast → pause-safe overlay → real VM reward loop end-to-end.

**Acceptance Criteria**:

1. WHEN an active-family Security Guard Sim is `LotTilePos.OUT_OF_WORLD` during shift hours AND has not been dispatched today THEN system SHALL display a pulsing top-right Radio Dispatch toast for 30 seconds.
2. WHEN the player clicks **ANSWER DISPATCH** THEN system SHALL pause `vm.SpeedMultiplier` and open the 4-camera, 5-round CCTV Night Watch overlay with a `Stay Awake` meter scaled by the Sim's `Energy` motive and 3 `Sip Coffee` charges.
3. WHEN a real threat (`Burglar`, `Vandal`, `Prowler`) appears AND player triggers **Sound Alarm** (`A`), OR a false alarm (`Raccoon`, `StrayCat`, `TreeShadow`) appears AND player triggers **All Clear** (`D`) THEN system SHALL record a correct call and advance rounds.
4. WHEN all 5 rounds finish and player clicks **CLAIM REWARDS & RETURN** THEN system SHALL apply `S`/`A`/`B`/`C` rewards (`CurrentFamily.Budget`, `JobPerformance`, `Energy`, `Fun`) and restore `vm.SpeedMultiplier`.

**Independent Test**: Run `convo dispatch toast Bob` → click **ANSWER DISPATCH** (or `convo dispatch start Bob`), complete 5 rounds, verify `S` rank adds `+$90` and `+25` JobPerformance. *(Verified in commit `d2b87f5` + 12 unit tests in `SecurityWatchTests.cs`.)*

---

### P2: Multi-Rank Dispatch Router & Career Tab Trigger

**User Story**: As a player climbing Law Enforcement, I want the Radio Dispatch system to automatically launch the right mini-game for my Sim's current rank (and optionally let me start my daily assignment from the Career tab) so progression feels seamless.

**Why P2**: Required foundation before adding Levels 2–10 so each rank plugs cleanly into one shared driver and rewards pipeline.

**Acceptance Criteria**:

1. WHEN any Law Enforcement Sim (Levels 1–10) is off-lot during their rank's shift window THEN system SHALL look up their rank descriptor in a dispatch registry and display their rank title and dispatch teaser on the toast.
2. WHEN the player completes or dismisses a rank's mini-game THEN system SHALL mark that Sim's daily dispatch quota used for the current game day across both Radio Dispatch and the Career Tab **Work Assignment** button.
3. WHEN a mini-game completes THEN the shared reward pipeline SHALL support Simoleons, `JobPerformance`, Skill XP (`Body`, `Logic`, `Charisma`, `Mechanical`), Motives (`Energy`, `Fun`, `Social`), Neighborhood Relationship deltas, and **Sim Memory** fact entries.

**Independent Test**: Unit-test registry resolution for all 10 Law Enforcement promotion levels (0–9) and daily deduplication across toast + manual Career Tab trigger.

---

### P2: Lvl 2 Cadet — Academy Obstacle & Drill *(9 AM – 3 PM)*

**User Story**: As a Level 2 Cadet, I want a quick 3-round timing drill (`Sprint`, `Vault`, `Target Drill`) where my Sim's **Body** skill makes timing easier so I can earn Body XP and Job Performance.

**Why P2**: First new rank validating skill-scaled difficulty and Skill XP rewards.

**Acceptance Criteria**:

1. WHEN the Cadet dispatch opens THEN system SHALL present 3 sequential timing-bar rounds (`Sprint`, `Vault`, `Target Drill`) with a moving marker and green sweet-spot zone.
2. WHEN calculating the green hit zone width THEN system SHALL widen the zone proportionally to the Sim's current **Body** skill level (0–10).
3. WHEN the player locks in timing (`Space` / click) across all 3 rounds THEN system SHALL grade the drill and award `+Body XP` and `+Job Performance`.

**Independent Test**: Unit-test hit-zone width scaling at Body 0 vs. Body 10, hit/near/miss scoring, and `convo dispatch start` at promotion level 1.

---

### P2: Lvl 3 Patrol Officer — Beat Patrol: Spot the Violation *(5 PM – 1 AM)*

**User Story**: As a Level 3 Patrol Officer, I want to handle 3–4 timed street-patrol calls (`Jaywalker`, `Noise Complaint`, `Broken Streetlight`, `Suspicious Prowler`) by choosing **Warn**, **Ticket**, or **Investigate**, with real neighborhood Sims showing up in the log.

**Why P2**: Introduces neighbor-aware consequences (ticketing a real neighbor slightly dents Daily relationship) and unlocks the first on-lot perk.

**Acceptance Criteria**:

1. WHEN Beat Patrol starts THEN system SHALL generate 3–4 timed street-scene cards, binding minor infractions (`Jaywalker`, `Noise Complaint`) to real neighborhood Sims when available.
2. WHEN the player selects **Warn**, **Ticket**, or **Investigate** before the card timer expires THEN system SHALL evaluate proportionality against the incident type.
3. WHEN the player tickets a real neighborhood Sim THEN system SHALL apply a small negative Daily relationship delta with that neighbor alongside `+$50–$100` and `+Job Performance` on shift completion.

**Independent Test**: Complete a Beat Patrol shift via unit test and verify correct vs. overly harsh choices plus Daily relationship delta on a ticketed neighbor.

---

### P2: Lvl 4 Desk Sergeant — 911 Dispatch Triage *(9 AM – 3 PM)*

**User Story**: As a Level 4 Desk Sergeant, I want to route incoming 911 calls (`Cat in Tree`, `Burglar Alarm`, `Kitchen Fire`, `Prank Call`) to **Patrol**, **Fire**, or **Ignore** before the queue overflows so fast, accurate triage earns a zero-misroute bonus.

**Why P2**: Adds fast multi-item queue management distinct from single-round trivia/timing games.

**Acceptance Criteria**:

1. WHEN 911 Dispatch Triage runs THEN incoming calls SHALL enqueue over a 30–40 second shift, each displaying caller urgency and a short transcript snippet.
2. WHEN the player routes a call to **Patrol**, **Fire**, or **Ignore** THEN system SHALL score correct routing immediately and clear the slot; unrouted calls that expire count as misroutes.
3. WHEN the shift completes with zero misroutes THEN system SHALL grant a zero-misroute cash bonus, `+Job Performance`, and Mechanical/Logic skill XP.

**Independent Test**: Unit-test call spawning, queue overflow handling, routing accuracy, and zero-misroute bonus calculation.

---

### P2: Lvl 5 Vice Squad — Undercover Sting *(10 PM – 4 AM, Laya Social)*

**User Story**: As a Level 5 Vice Squad officer, I want a 3-turn undercover dialogue sting powered by **Laya + Charisma** where picking Gentle, Playful, or Bold cover lines keeps my **Suspicion Meter** low until the suspect slips up and confesses.

**Why P2**: Bridges the career mini-game system directly with the project's **Laya AI conversations** and **Sim Memory** system.

**Acceptance Criteria**:

1. WHEN Undercover Sting starts THEN system SHALL initialize a 3-turn suspect conversation with a `Suspicion Meter` (0–100%) and 3 cover-line options per turn (`Gentle`, `Playful`, `Bold`).
2. WHEN the player chooses a line THEN system SHALL evaluate suspect reaction using Laya (with deterministic offline fallback if Laya is unavailable), modified by the Sim's **Charisma** skill.
3. WHEN the suspect confesses before Suspicion reaches 100% THEN system SHALL award `+Charisma XP`, a bust cash bonus, `+Job Performance`, and record the bust fact in **Sim Memory**.

**Independent Test**: Test both Laya-backed and offline-fallback 3-turn flows, verifying Suspicion thresholds, confession outcome, and Sim Memory recording.

---

### P2: Lvl 6 Detective — Crime Scene Deduction *(9 AM – 3 PM)*

**User Story**: As a Level 6 Detective, I want a compact evidence board with 3 clues (`Footprint`, `Motive`, `Alibi`) and 3 real neighborhood Sims as suspects where my **Logic** skill helps spot contradictions so I can accuse the right culprit.

**Why P2**: Signature puzzle rank tying real neighborhood Sims, Logic skill, and the **Sim Memory** dossier together.

**Acceptance Criteria**:

1. WHEN Crime Scene Deduction opens THEN system SHALL generate a consistent case with 1 guilty culprit and 2 innocent suspects drawn from neighborhood Sims (or procedural townies if fewer than 3 exist), plus 3 clue cards (`Footprint`, `Motive`, `Alibi`).
2. WHEN the Detective's **Logic** skill is ≥ 5 THEN system SHALL visually highlight one contradictory statement on the evidence board.
3. WHEN the player accuses the true culprit THEN system SHALL award `+Logic XP`, a `+$150` case-closed bonus, and `+Job Performance`; a wrong accusation reduces Job Performance.

**Independent Test**: Unit-test puzzle generator guarantees exactly 1 uniquely deducible culprit across 100 random seeds and verify Logic contradiction hint threshold.

---

### P2: Lvl 7 Lieutenant — Precinct Squad Briefing *(9 AM – 3 PM)*

**User Story**: As a Level 7 Lieutenant, I want to assign 3 rookie officers with visible traits (*Fast*, *Observant*, *Charming*) to 3 matching city incidents (`Foot Chase`, `Stakeout`, `Community Mediation`) so smart delegation boosts precinct morale.

**Why P2**: Shifts gameplay from solo fieldwork to squad management matching the Lieutenant rank fantasy.

**Acceptance Criteria**:

1. WHEN Precinct Squad Briefing starts THEN system SHALL present 3 rookie officers (each with primary/secondary trait badges) and 3 city incidents requiring matching strengths.
2. WHEN the player pairs all 3 officers and clicks **DISPATCH SQUAD** THEN system SHALL score each assignment and reveal brief incident outcomes.
3. WHEN all 3 incidents are matched cleanly THEN system SHALL grant `+Charisma`/`+Logic` XP, `+Job Performance`, and a precinct morale bonus.

**Independent Test**: Unit-test optimal matching detection, partial-match grading, and reward calculation.

---

### P3: Lvl 8 SWAT Team Leader — Tactical Breach Plan *(9 AM – 3 PM)*

**User Story**: As a Level 8 SWAT Team Leader, I want a 4×4 room grid showing entry doors, suspects, and hostages where I pick a 3-step entry sequence (`Flashbang`, `Shield`, `Quiet Entry`) to clear the room with zero collateral damage.

**Why P3**: High-stakes tactical puzzle for upper-tier career play.

**Acceptance Criteria**:

1. WHEN Tactical Breach Plan opens THEN system SHALL display a 4×4 tactical floorplan with entry points, armed suspects, and hostage tiles.
2. WHEN the player queues a 3-step sequence from `Flashbang`, `Shield`, and `Quiet Entry` and executes the breach THEN system SHALL simulate step-by-step resolution showing suspect neutralization and hostage safety.
3. WHEN the room clears with zero hostage harm THEN system SHALL award `+$200–$300` hazard pay, large `+Job Performance`, and `+Body`/`+Logic` XP.

**Independent Test**: Unit-test 4×4 tactical resolver for flashbang-near-hostage penalty, shield-frontline protection, and clean-clear hazard pay.

---

### P3: Lvl 9 Police Chief — City Hall Press Conference & Budget *(9 AM – 5 PM)*

**User Story**: As a Level 9 Police Chief, I want a two-part shift where I allocate 5 budget points (`Patrols`, `Forensics`, `Community Outreach`) for today's crisis and answer 2 tough reporter questions judged by Laya so I can raise City Approval and townie friendships.

**Why P3**: Executive-level strategy + Laya social judgment suited for rank 9.

**Acceptance Criteria**:

1. WHEN Part 1 opens THEN system SHALL present today's city crisis brief and allow allocating exactly 5 points across `Patrols`, `Forensics`, and `Community Outreach`.
2. WHEN Part 2 opens THEN system SHALL pose 2 reporter questions evaluated by Laya tone judgment (with offline deterministic fallback).
3. WHEN both parts finish THEN system SHALL update City Approval (scaling daily bonus pay) and grant positive relationship bumps with neighborhood townies.

**Independent Test**: Unit-test budget allocation validation (must sum to 5), crisis alignment scoring, and offline fallback reporter scoring.

---

### P3: Lvl 10 Captain Hero — City Crisis Rescue *(10 AM – 5 PM)*

**User Story**: As a Level 10 Captain Hero, I want a stylized SimCity map with 2–3 simultaneous emergencies (`Runaway Trolley`, `Llama Heist`, `Giant Robot`) where I choose a rapid 3-beat heroic approach (`Strength`, `Wit`, `Inspiration`) to save the day.

**Why P3**: Celebratory capstone mini-game for reaching the top of Law Enforcement.

**Acceptance Criteria**:

1. WHEN City Crisis Rescue opens THEN system SHALL display a stylized SimCity map highlighting 2–3 simultaneous emergencies with distinct optimal heroic approaches (`Strength`, `Wit`, `Inspiration`).
2. WHEN the player resolves the 3-beat sequence before crisis timers expire THEN system SHALL generate a playful SimCity front-page fame headline (including occasional Sims 1 *TV Star* crossover nods).
3. WHEN the rescue succeeds THEN system SHALL award a top-tier cash reward, `+Fun`, `+Social`, and max-rank Job Performance maintenance.

**Independent Test**: Unit-test multi-crisis timer resolution, approach matching, headline generation, and reward scaling.

---

### P3: Milestone On-Lot Career Perks (Pie-Menu Interactions)

**User Story**: As a Law Enforcement Sim at home on my lot, I want rank-unlocked pie-menu interactions so my career rank gives tangible everyday benefits outside work hours.

**Why P3**: Connects career progression directly back to the live lot and Sim Memory system.

**Acceptance Criteria**:

1. **Lvl 3+ (`Neighborhood Watch`):** WHEN a Patrol Officer (or higher) clicks the front sidewalk/mailbox area THEN pie menu SHALL offer **Neighborhood Watch**, which boosts `JobPerformance` and deters burglars for the night.
2. **Lvl 6+ (`Question Sim`):** WHEN a Detective (or higher) clicks another Sim THEN pie menu SHALL offer **Question Sim**, which immediately reveals 2–3 unknown traits/interests into the active Sim's **Sim Memory** dossier.
3. **Lvl 10 (`Heroic Pose / Inspire Room`):** WHEN Captain Hero performs **Heroic Pose / Inspire Room** THEN all Sims in the same room SHALL receive a quick `Mood`/`Fun` lift.

**Independent Test**: Verify pie-menu filtering gates each interaction strictly by Law Enforcement career + minimum promotion level, and confirm `Question Sim` populates `SimMemoryStore`.

---

## Edge Cases

- WHEN a household has multiple Law Enforcement Sims at work simultaneously THEN system SHALL queue or show one toast at a time without overwriting another Sim's daily dispatch flag.
- WHEN Laya is offline or times out during Rank 5 (`Vice Squad`) or Rank 9 (`Police Chief`) THEN system SHALL fall back seamlessly to deterministic rule-based scoring without hanging the UI.
- WHEN a neighborhood has fewer than 3 townies for Rank 3 (`Beat Patrol`) or Rank 6 (`Detective`) THEN system SHALL pad the cast with procedural SimCity names so the mini-game never crashes or loops infinitely.
- WHEN the player closes the overlay early via `[X]` or `Esc` before completing the shift THEN system SHALL unpause `vm.SpeedMultiplier`, skip reward payout, and leave normal shift resolution intact.
- WHEN a Sim gets promoted mid-week THEN the next day's Radio Dispatch SHALL automatically route to the newly unlocked rank's mini-game.

---

## Requirement Traceability

| Requirement ID | Story | Phase | Status |
| --- | --- | --- | --- |
| DISP-01 | P1: Mid-Shift Radio Dispatch Toast & Daily Cooldown | Shipped (`d2b87f5`) | Verified |
| DISP-02 | P1: Pause-Safe Floating Overlay & VM Reward Application | Shipped (`d2b87f5`) | Verified |
| DISP-03 | P1: Lvl 1 Security Guard — CCTV Night Watch | Shipped (`d2b87f5`) | Verified |
| DISP-04 | P2: Multi-Rank Dispatch Registry & Career Tab Trigger | Milestone 2 | Pending |
| DISP-05 | P2: Lvl 2 Cadet — Academy Obstacle & Drill | Milestone 2 | Pending |
| DISP-06 | P2: Lvl 3 Patrol Officer — Beat Patrol | Milestone 2 | Pending |
| DISP-07 | P2: Lvl 4 Desk Sergeant — 911 Dispatch Triage | Milestone 2 | Pending |
| DISP-08 | P2: Lvl 5 Vice Squad — Undercover Sting (Laya) | Milestone 3 | Pending |
| DISP-09 | P2: Lvl 6 Detective — Crime Scene Deduction | Milestone 3 | Pending |
| DISP-10 | P2: Lvl 7 Lieutenant — Precinct Squad Briefing | Milestone 3 | Pending |
| DISP-11 | P3: Lvl 8 SWAT Team Leader — Tactical Breach Plan | Milestone 4 | Pending |
| DISP-12 | P3: Lvl 9 Police Chief — Press Conference & Budget (Laya) | Milestone 4 | Pending |
| DISP-13 | P3: Lvl 10 Captain Hero — City Crisis Rescue | Milestone 4 | Pending |
| DISP-14 | P3: On-Lot Perk — Neighborhood Watch (Lvl 3+) | Milestone 2 | Pending |
| DISP-15 | P3: On-Lot Perk — Question Sim → Sim Memory (Lvl 6+) | Milestone 3 | Pending |
| DISP-16 | P3: On-Lot Perk — Heroic Pose / Inspire Room (Lvl 10) | Milestone 4 | Pending |

**Coverage:** 16 total, 3 Verified (`DISP-01`–`DISP-03`), 13 Pending across Milestones 2–4.

---

## Success Criteria

- [x] Security Guard (Lvl 1) PoC playable end-to-end with 12 passing unit tests and verified live screenshots.
- [ ] Every Law Enforcement rank (1–10) completes in 15–45 seconds with both mouse and keyboard support.
- [ ] Zero game-thread hangs or crashes across all ranks, including when Laya is disabled or the neighborhood has few Sims.
- [ ] All pure mini-game state machines have deterministic seeded unit test coverage in `Tools/Conversations.Tests/`.
