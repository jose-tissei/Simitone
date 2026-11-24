# Roadmap

**Current Milestone:** Milestone 2 — Law Enforcement Ranks 2–4 (Field & Precinct)
**Status:** In Progress

---

## Milestone 1: Radio Dispatch Foundation & Security Guard (Lvl 1) PoC

**Goal:** Prove the non-blocking mid-shift Radio Dispatch flow end-to-end with a complete, playable, unit-tested Level 1 mini-game that applies real VM rewards.
**Target:** Shipped on `laya-autonomous-sim-social` (`d2b87f5`)

### Features

**Radio Dispatch Toast & Overlay Shell** - COMPLETE

- Automatic detection of off-lot active-family Security Guard Sims during shift hours (once per Sim per day)
- Pulsing top-right `CH-04 LAW ENFORCEMENT` toast with 30s timeout, **ANSWER DISPATCH** and **IGNORE**
- Pause-safe floating monitor overlay + `convo dispatch ...` instant test commands

**Law Enforcement Lvl 1 — Security Guard: CCTV Night Watch** - COMPLETE

- 4-camera night-vision CRT feed with 5 rounds of threats (`Burglar`, `Vandal`, `Prowler`) vs. false alarms (`Raccoon`, `StrayCat`, `TreeShadow`)
- `Stay Awake` alertness meter scaled by Sim `Energy` + 3 `Sip Coffee` charges
- `S`/`A`/`B`/`C` Shift Report applying real `CurrentFamily.Budget`, `JobPerformance`, `Energy`, and `Fun`

---

## Milestone 2: Multi-Rank Dispatch Registry, Custom 3D Animations & Law Enforcement Ranks 2–4

**Goal:** Generalize `CareerDispatchDriver` into a rank-dispatched framework, upgrade 3D mini-game visuals with a custom skeletal animation pipeline, and ship Levels 2–4 plus the first on-lot perk.

### Features

**Career Dispatch Framework Refactor (`DISP-04`)** - COMPLETE

- Rank router mapping `(Career, PromotionLevel)` to `ICareerMiniGame` + overlay renderer (`ec04deb`)
- Shared Shift Report & Rewards card (supporting Simoleons, JobPerformance, Skill XP, Motives, and Relationship deltas)
- Optional Career tab **"Work Assignment"** button (once per day) — deferred to follow-up polish

**Lvl 2 — Cadet: Academy Obstacle & Drill (`DISP-05`)** - COMPLETE

- Full 3D live-rendered Academy obstacle course (`CadetAcademyOverlay` + `CadetAcademy3DView`) with Career Uniform dressing and pre-game Briefing screen (Start / Skip) (`ec04deb`, `04923b0`)
- 3-round timing/sweet-spot rhythm bar (`Sprint`, `Vault`, `Target Drill`)
- Sim's **Body** skill dynamically widens the green hit zone; rewards `+Body XP` and `+Job Performance`

**Custom 3D Skeletal Animation Pipeline (`custom-animations`)** - SPEC / DESIGN READY

- Pure-C# `.simanim.json` v1 runtime evaluator with quaternion SLERP, seamless loop-wrap interpolation, smooth crossfades, and root-motion-matched playback (`ANIM-01`–`ANIM-09`)
- Offline hybrid authoring pipeline: CC-BY/public-domain BVH mocap retargeter + local **Blender 4.5** `adult.skel` IK rig & exporter (`Tools/AnimPipeline/`)
- Initial 6 custom Cadet Academy clips (`sprint`, `vault`, `aim`, `shoot`, `idle_ready`, `victory_salute`) replacing stiff 1999 TS1 animations

**Lvl 3 — Patrol Officer: Beat Patrol — Spot the Violation** - PLANNED

- 3–4 street-scene dispatch cards (`Jaywalker`, `Noise Complaint`, `Broken Streetlight`, `Suspicious Prowler`)
- Timed **Warn / Ticket / Investigate** choices; ticketing a real neighborhood Sim slightly dents Daily relationship
- Unlocks on-lot **Neighborhood Watch** front-sidewalk interaction

**Lvl 4 — Desk Sergeant: 911 Dispatch Triage** - PLANNED

- Multi-call incoming queue (`Cat in Tree`, `Burglar Alarm`, `Kitchen Fire`, `Prank Call`)
- Route calls to **Patrol / Fire / Ignore** before queue overflow; zero-misroute cash bonus + Mechanical/Logic feel

---

## Milestone 3: Law Enforcement Ranks 5–7 (Investigations & Command)

**Goal:** Integrate Laya conversational AI, real neighborhood Sims, and the Sim Memory dossier into mid-to-senior Law Enforcement gameplay.

### Features

**Lvl 5 — Vice Squad: Undercover Sting (Laya Social)** - PLANNED

- 3-turn undercover conversation with a shady suspect powered by **Laya + Charisma**
- Pick Gentle / Playful / Bold cover lines to keep **Suspicion Meter** low until confession; records facts in **Sim Memory**

**Lvl 6 — Detective: Crime Scene Deduction** - PLANNED

- Compact evidence board with 3 clues (`Footprint`, `Motive`, `Alibi`) and 3 real neighborhood Sims as suspects
- Higher **Logic** highlights a clue contradiction; rewards `+Logic XP`, `+$150` case-closed bonus, and `+Job Performance`
- Unlocks on-lot **Question Sim** pie-menu interaction (reveals 2–3 unknown traits/interests into **Sim Memory**)

**Lvl 7 — Lieutenant: Precinct Squad Briefing** - PLANNED

- Match 3 rookie officers (traits: *Fast*, *Observant*, *Charming*) to 3 city incidents (`Foot Chase`, `Stakeout`, `Community Mediation`)
- Clean 3/3 match awards Charisma/Logic boost, Job Performance, and precinct morale bonus

---

## Milestone 4: Law Enforcement Ranks 8–10 (Tactical & Heroic Endgame)

**Goal:** Deliver high-stakes tactical, civic leadership, and superhero endgame shifts.

### Features

**Lvl 8 — SWAT Team Leader: Tactical Breach Plan** - PLANNED

- 4×4 room grid showing entry doors, suspects, and hostages
- Choose a 3-step entry sequence (`Flashbang`, `Shield`, `Quiet Entry`) for zero collateral damage; hazard pay (`+$200–$300`) + Body/Logic XP

**Lvl 9 — Police Chief: City Hall Press Conference & Budget** - PLANNED

- Part 1: Allocate 5 budget points across `Patrols`, `Forensics`, and `Community Outreach` for today's crisis
- Part 2: Answer 2 tough reporter questions (**Laya-judged tone**); boosts City Approval (daily pay scaling) + townie relationships

**Lvl 10 — Captain Hero: City Crisis Rescue** - PLANNED

- Stylized SimCity map with 2–3 simultaneous emergencies (`Runaway Trolley`, `Llama Heist`, `Giant Robot`)
- Rapid 3-beat heroic approach picker (`Strength`, `Wit`, `Inspiration`); huge cash reward, fame headlines, `+Fun`/`+Social`, TV Star crossover nod
- Unlocks on-lot **Heroic Pose / Inspire Room** interaction

---

## Future Considerations

- **Real-Time Multi-Camera `FSO.LotView.World` CCTV (Option 2)** — upgrade Security Guard (Lvl 1) CCTV Night Watch to render 4 live `RenderTarget2D` camera angles (round-robin 1 camera/frame at ~15 FPS) from either the player's lot or a dedicated mini Precinct `SubWorldComponent`, isolating `PPXDepthEngine`/`WorldContent` static render state.
- Expand the same 10-rank Radio Dispatch / Work Assignment format to other Sims 1 careers: **Criminal**, **Military**, **Science**, **Medicine**, **Politics**, **Business**, **Entertainment**, **Pro Athlete**, **X-Treme**, and **Life of Crime / Hacker**.
- Optional career-specific sound stings (radio squelch, alarm chime, crowd cheer) via Simitone audio hooks.
