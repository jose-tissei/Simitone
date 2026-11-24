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

- Full-screen "Virtual Pocket Lot" 3D Academy Training Grounds (`CadetAcademyOverlay` + `CadetAcademySceneRenderer` + `VirtualLotTransition`) with smooth fade-from/to-black transitions, translucent arrival location banner, Career Uniform dressing, live Sideline Drill Instructor (`SimAvatar`), Precinct Building, bleachers, parked Patrol Cruisers with pulsing lightbars, waving flagpole, and minimal floating HUD (`ec04deb`, `04923b0`, `52037f3`)
- 3-round timing/sweet-spot rhythm bar (`Sprint`, `Vault`, `Target Drill`) with left-anchored Briefing & Scorecard cards
- Sim's **Body** skill dynamically widens the green hit zone; rewards `+Body XP` and `+Job Performance`

**Custom 3D Skeletal Animation Pipeline (`custom-animations`)** - COMPLETE

- Pure-C# `.simanim.json` v1 runtime evaluator with wrap-safe SQUAD/SLERP interpolation, smooth multi-clip crossfades, `SPINE`-split upper/lower masking, events, and analytical 2-bone IK (`Logic/CustomAnim*.cs`, `Logic/TwoBoneIkSolver.cs`, `Game/VitaboyClipPlayer.cs`)
- Offline hybrid authoring pipeline: exact `adult.skel` BCF dumper (`dump_adult_skel.py`), global-delta BVH mocap retargeter (`bvh_to_simanim.py`), local **Blender 4.5** `adult_rig.blend` rig & exporter (`Tools/AnimPipeline/`), and automated verification (`verify_pipeline.py`)
- 7 custom Blender-authored Cadet Academy clips (`cadet_idle_attention`, `cadet_sprint_loop`, `cadet_hurdle_hop`, `cadet_wall_vault`, `cadet_target_aim_loop`, `cadet_target_fire_recoil`, `cadet_victory_salute`) driving `CadetAcademySceneRenderer` with zero `simPitch` tilt hack and stride-matched ground speed

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
