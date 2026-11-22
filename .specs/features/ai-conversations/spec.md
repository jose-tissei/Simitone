# AI-Driven Social Conversations Specification

**Status**: Draft (awaiting approval)
**Size**: Large (multi-component, new integrations). Gray areas are listed at the end and need your decision.

## Problem Statement

Social interactions in The Sims 1 are silent and one-sided. The player picks "Hug" or "Joke", the Sim plays an animation,
and a hidden score (target's relationship, mood and personality) decides whether it works. The player never chooses what
is said, never sees a reason for the result, and the outcome ignores most of the receiver's character (profession,
interests, current humor beyond mood).

We want conversations that feel alive: the player picks what to say from a few real lines that fit the two Sims, and the
result depends on the receiver's traits, while the game stays fast, offline-capable and balanced.

## Goals

- [ ] For the 18 social interactions that share the outcome step (see spike-results.md), the player can choose among 3 lines (tones) before the animation starts.
- [ ] The result (outcome 1, 2 or 3) is decided from both Sims' traits (personality, profession, interests, mood) and the chosen tone: Laya classifies the receiver's temperament and the tone, a deterministic compatibility table combines them, and the result may differ from the original script's outcome by at most one step (decided: policy B).
- [ ] Laya ships with the game: starting the game starts its own private Laya server, and closing the game stops it.
- [ ] Lines come from a fixed, pre-generated bank (made offline with local Ollama). No AI call blocks gameplay.
- [ ] The relationship numbers still come from the original tuning tables (same STR/LTR deltas per outcome), so game balance is unchanged.
- [ ] The whole feature can be turned off in the Options menu and degrades to today's behavior if Laya or the bank is missing.

## Out of Scope

| Feature | Reason |
|---|---|
| Free-text typing by the player | Needs live AI or heavy parsing; breaks the "fixed list" rule |
| Live Ollama generation during play | Latency; only background refill is allowed (P3) |
| Changing STR/LTR tuning values | Keeps balance and the documented rules in `docs/architecture/RELATIONSHIPS.md` |
| Voice / Simlish audio for lines | Text only |
| New interactions (not among the 23) | The list stays fixed; Talk subjects are P2 |
| Multiplayer / FreeSO online | Simitone is offline single-player |

---

## User Stories

### P1: Choose how to say it, and see why it worked ⭐ MVP

**User Story**: As a player, when I start a social (e.g. Compliment, Joke, Hug), I want to pick between 3 lines that fit my Sim and the other Sim, so that my choice matters and the exchange reads like a real conversation.

**Why P1**: This is the vertical slice: choice, a trait-based result, visible text, and unchanged relationship rules.

**Acceptance Criteria**:

1. WHEN a social from the supported list is about to play and AI conversations are on THEN the game SHALL show a 3-button choice, each button labelled with a line generated for that interaction, tone and the two Sims' trait profile.
2. WHEN the player picks a line THEN the game SHALL decide the outcome (1, 2 or 3) from the receiver's temperament (classified by Laya from their traits) and the line's tone, never more than one step away from the original script outcome, store it in the social object's attribute 9, and continue the original script so the animation and STR/LTR change use the original tables.
3. WHEN the outcome is decided THEN the game SHALL show the speaker's line and the receiver's reply (from the bank, matched to outcome) as speech text over the Sims.
4. WHEN Laya does not answer within its time limit THEN the game SHALL use the outcome the original script computed (no error, no delay beyond the limit).
5. WHEN AI conversations are off, or the bank has no entry for the combination THEN the game SHALL behave exactly as the unmodified game.
6. WHEN the Sim is not player-controlled (autonomous Sim to Sim) THEN the game SHALL skip the choice, pick the tone from the actor's personality, and still show the lines (P1 for visuals, choice skipped).

**Independent Test**: Enter the Newbie lot, start Joke on another Sim: 3 buttons appear, picking one shows lines and the STR bar changes by one of the Joke table values.

---

### P1: Laya starts and stops with the game

**User Story**: As a player, I want Laya to come with the game and start by itself, so that I never launch a second program.

**Acceptance Criteria**:

1. WHEN the game starts THEN the launcher SHALL start a Laya server bound to 127.0.0.1 on a free port with a random API key, warm it up with one request, and pass the port and key to the game.
2. WHEN the game exits or crashes THEN the Laya process SHALL stop (no orphan keeps using the GPU).
3. WHEN Laya cannot start (missing files, no port, no GPU and too slow) THEN the game SHALL start normally with the feature off and log one line.
4. WHEN a Laya server is already running on the configured port THEN the game SHALL not reuse an unauthenticated server it did not start.

**Independent Test**: Start the game, see a Laya process appear bound to 127.0.0.1 only; close the game, see it disappear.

---

### P2: Talk with subjects

**User Story**: As a player, I want to pick a conversation subject that fits what the other Sim likes, so that talking about the right thing builds the relationship.

**Why P2**: Talk is the most-used social and already scores by topic interest; it needs its own script work.

**Acceptance Criteria**:

1. WHEN the player starts Talk THEN the game SHALL offer up to 3 subjects drawn from the game's own topics (the topic table lists 12, the script `SHD do I like topic?` checks 15; each maps to one of the 8 interest values), weighted toward the speaker's interests.
2. WHEN the receiver's interest in the chosen subject is high THEN the outcome SHALL be positive (+3 STR per the existing rule), otherwise negative (-3).
3. WHEN the receiver's profession or personality strongly relates to a subject THEN the reply line SHALL reflect it.

**Independent Test**: Talk to a Sim whose interests are known; the offered subject they like raises STR, one they dislike lowers it.

---

### P2: Options menu toggle and strength

**User Story**: As a player, I want an Options menu entry to switch AI conversations on/off and choose between "always ask" and "only for Sims I control".

**Acceptance Criteria**:

1. WHEN the player toggles the entry THEN the active state SHALL show in green live, like the existing mode entries, and persist across restarts.
2. WHEN off THEN no choice dialog SHALL appear and no Laya call SHALL be made.

---

### P3: Background bank refill

**User Story**: As a player, I want the game to learn new lines over time without ever pausing.

**Acceptance Criteria**:

1. WHEN Ollama is reachable and a trait combination has no bank entry THEN the game SHALL queue it for background generation and use a fallback line now.
2. WHEN generated text fails validation (length, banned words, wrong format) THEN it SHALL be discarded.

---

## Edge Cases

- WHEN Ollama or Laya is not running THEN the game SHALL start and play normally (feature degrades silently, one log line).
- WHEN the receiver is a pet, child, NPC or ghost THEN the game SHALL use the original behavior (no choice, no lines) until separately specified.
- WHEN two socials overlap (group talk) THEN only the first SHALL get a choice; others use fallback.
- WHEN the save is loaded mid-interaction THEN the stored attribute 9 SHALL still hold a valid outcome (1-3).
- WHEN a generated line is longer than the balloon limit THEN it SHALL be truncated at a sentence boundary at build time, never at runtime.
- WHEN the Sim's name or trait data is missing THEN the profile SHALL use neutral defaults.

## Requirement Traceability

| ID | Story | Phase | Status |
|---|---|---|---|
| CONV-01 | P1 choice of 3 lines before playing | Design | Pending |
| CONV-02 | P1 outcome from traits via Laya, stored in attr 9 | Design | Pending |
| CONV-03 | P1 speaker line and receiver reply as text | Design | Pending |
| CONV-04 | P1 timeout and fallback to script outcome | Design | Pending |
| CONV-05 | P1 off switch and missing-data fallback | Design | Pending |
| CONV-06 | P1 autonomous Sims skip the choice | Design | Pending |
| CONV-07 | P1 relationship deltas unchanged | Design | Pending |
| CONV-08 | P2 Talk subjects | - | Pending |
| CONV-09 | P2 Options toggle, persisted | - | Pending |
| CONV-10 | P3 background refill and validation | - | Pending |
| CONV-11 | All: tests and CI | Design | Pending |
| CONV-12 | P1 Laya is installed with the game and started and stopped by it | Design | Pending |

**Coverage:** 12 total, 0 mapped to tasks yet.

## Success Criteria

- [ ] No game frame is delayed by more than 1 frame by this feature (no network call on the VM thread).
- [ ] 100% of the 23 interactions work with the feature on, and are bit-identical to today with it off (verified by the e2e comparison of STR after a scripted Hug with the feature off).
- [ ] With Laya stopped, every social still completes with the original outcome.
- [ ] Bank covers all (interaction x outcome x tone) cells, with at least 3 variants per trait bucket.

## Decisions made (2026-11-21)

1. **Outcome policy: B.** The result may differ from the script's outcome by at most one step.
2. **Text:** balloons for the lines, a dialog only for the 3-way choice.
3. **Ollama:** build time only; none during play (background refill stays P3).
4. **Laya is bundled:** installed with the game, started by the game (private address, random key), stopped on exit.

## Still open

- **How Laya is packaged.** PyTorch with CUDA is 4.4 GB and the weights 2.3 GB; an ONNX runtime path may be much smaller. Options: (a) require a system Python with `laya` and have the launcher start it, (b) ship an embedded Python plus ONNX Laya, (c) ship full PyTorch. Recommendation: spike (b) first, fall back to (a).
- **Laya on machines without a GPU.** Its CPU speed was not measured.
