# AI-Driven Social Conversations Tasks

**Design**: `.specs/features/ai-conversations/design.md`
**Status**: Draft

No `.specs/codebase/TESTING.md` exists. The repo's real gates are used:
- **quick**: `dotnet test Tools/Conversations.Tests` (unit, pure logic)
- **full**: `pwsh Tools/ci.ps1 -App <pristine v0.8.12>` (builds every tool, unit + patch tests, smoke)
- **e2e**: `pwsh Tools/e2e/Run-E2E.ps1` (drives the real game; local only, needs the Sims files; not parallel-safe)

## Execution Plan

```
Phase 0 (spikes, sequential)   T1 -> T2
Phase 1 (pure logic, parallel) T2 -> T3, T4, T5
Phase 2 (tools, parallel)      T4,T5 -> T6 ; T3 -> T7
Phase 3 (game hook)            T2,T3,T5,T7 -> T8 -> T9 -> T10
Phase 4 (UI + menu)            T10 -> T11 -> T12
Phase 5 (verify)               T6,T12 -> T13 -> T14
```

```
T1 -> T2 -+-> T3 -> T7 ----------+
          +-> T4 -> T6 ----------+-> T8 -> T9 -> T10 -> T11 -> T12 -> T13 -> T14
          +-> T5 -> T6          (T3,T5,T7 also feed T8)
```

## Tasks

**Spikes T1 and T2 were run on 2026-11-21: see `spike-results.md`.** T1 is complete except the in-game proof, which moves to T1b below. T2 is complete except the Ollama model comparison, which moves into T6. Task changes caused by the findings: T5 and T7 now use classification plus a table (not a Laya-decided outcome); new tasks T1b and T7b are added; the diagram below is unchanged except that T7b feeds T8.

### T1b: In-game proof of the hook seam (replaces the unfinished part of T1)
**What**: With the `Global[25] = 6` patch (already built in `the-sims-spike/flag`), start a two-Sim household, do Hug and Kiss, pick each of the 3 outcomes in the native dialog, record animation and STR change against the `Tuning Hug`/`Tuning Kiss` tables.
**Where**: `.specs/features/ai-conversations/spike-results.md` (append), `Tools/e2e` (route to a two-Sim household)
**Depends on**: None   **Requirement**: CONV-02, CONV-07
**Done when**:
- [ ] Each outcome produces the expected STR delta (Hug: A -10/+2/+7, B -10/+4/+8)
- [ ] The button-to-outcome mapping of the native dialog is recorded
- [ ] The e2e route to a two-Sim household is saved for later tasks
**Tests**: e2e   **Gate**: e2e

### T7b: `LayaHost` - start, warm up and stop the bundled Laya (CONV-12) [P]
**What**: Launcher-side process manager: free port, random key, `LAYA_HOST=127.0.0.1`, hidden process, `/health` poll, warm-up request, kill on exit via a job object, refuse foreign servers.
**Where**: `Tools/Conversations/LayaHost.cs`, `Tools/Conversations.Tests/LayaHostTests.cs`
**Depends on**: T2   **Reuses**: `SimTool` launch pattern
**Done when**:
- [ ] Tests with a fake server process: starts, becomes healthy, is killed when the parent dies
- [ ] Bound address is 127.0.0.1 only (asserted)
- [ ] Missing Laya yields "feature off", not an exception
- [ ] quick gate passes
**Tests**: unit + integration   **Gate**: full

### T7c: Laya packaging spike
**What**: Measure an ONNX/embedded-Python Laya: size, start time, speed on CPU and GPU; choose bundled vs system Python.
**Where**: `.specs/features/ai-conversations/spike-packaging.md`
**Depends on**: T2   **Requirement**: CONV-12
**Done when**:
- [ ] Size and median latency recorded for each option, decision written
**Tests**: none (spike)   **Gate**: none

### T1: Spike - verify the hook point in the real scripts and game
**What**: Confirm BHAV 397 has exactly 23 callers, what the stack object and attr 9 are inside it, and that overwriting attr 9 changes the animation and STR in the real game.
**Where**: `.specs/features/ai-conversations/spike-hook.md` (notes), `Tools/ScriptDump` (reuse)
**Depends on**: None   **Reuses**: ScriptDump, SimTool
**Requirement**: CONV-02, CONV-07
**Done when**: 
- [ ] 23 callers listed with file and BHAV id
- [ ] A throwaway patch forcing attr 9 = 3 on Hug shows the best animation and the Hug outcome-3 STR (+7/+8) in game
- [ ] Open questions 1, 2 and 4 of design.md answered in writing
**Tests**: none (spike)   **Gate**: none

### T2: Spike - Laya contract and speed; Ollama bank sample
**What**: Start Laya, send a 3-way classification of a Hug scenario, measure latency over 50 calls; generate 10 sample lines with gemma3:4b and qwen3:8b and compare.
**Where**: `.specs/features/ai-conversations/spike-services.md`
**Depends on**: T1   **Reuses**: `C:\Users\Zé\.claude\laya_route.py`
**Requirement**: CONV-02, CONV-04, CONV-10
**Done when**:
- [ ] Median and p95 latency written down; 150 ms cap confirmed or changed
- [ ] Request/response JSON shape for the outcome question recorded
- [ ] Model choice for the bank recorded with 10 sample lines each
**Tests**: none (spike)   **Gate**: none

### T3: `TraitProfile` + bucketing (pure) [P]
**What**: `RawSim` -> bucketed `TraitProfile` (personality L/M/H, job group, mood level, top interest, key and summary text).
**Where**: `Tools/Conversations/TraitProfile.cs`, `Tools/Conversations.Tests/TraitProfileTests.cs`
**Depends on**: T2   **Reuses**: `ModeLogic.cs` structure
**Requirement**: CONV-01, CONV-02, CONV-05
**Done when**:
- [ ] Boundary values (0, 333, 334, 666, 667, 1000) bucket as specified
- [ ] Missing data yields neutral defaults, no exception
- [ ] quick gate passes; test count recorded
**Tests**: unit   **Gate**: quick

### T4: `DialogueBank` loader + lookup + fallbacks (pure) [P]
**What**: Load `bank.json`, look up by interaction, tone, outcome and bucket with fallback chain and seeded variant choice.
**Where**: `Tools/Conversations/DialogueBank.cs`, `Tools/Conversations.Tests/DialogueBankTests.cs`
**Depends on**: T2   **Reuses**: ModeLogic parsing style
**Requirement**: CONV-01, CONV-03, CONV-05
**Done when**:
- [ ] Specific -> generic bucket -> built-in template fallback proven by tests
- [ ] Invalid or empty file returns "disabled", not an exception
- [ ] quick gate passes
**Tests**: unit   **Gate**: quick

### T5: `OutcomeResolver` (pure) [P]
**What**: Table-driven shift in {-1, 0, +1} from (interaction family, tone, temperament), applied to the script outcome and clamped to 1-3 (policy B). No temperament means no shift.
**Where**: `Tools/Conversations/OutcomeResolver.cs`, `Tools/Conversations.Tests/OutcomeResolverTests.cs`
**Depends on**: T2   **Reuses**: none
**Requirement**: CONV-02, CONV-04
**Done when**:
- [ ] Table-driven tests for all (script, laya, policy) combinations including null and out-of-range
- [ ] quick gate passes
**Tests**: unit   **Gate**: quick

### T6: `ConvoBank` offline builder
**What**: CLI that calls Ollama per (interaction x outcome x tone x bucket), validates (length, language, banned words, format), writes `bank.json` and a review sheet; resumable.
**Where**: `Tools/ConvoBank/`, `Tools/Conversations.Tests/ValidationTests.cs`
**Depends on**: T4, T5 (schema)   **Reuses**: DialogueBank schema
**Requirement**: CONV-10, CONV-03
**Done when**:
- [ ] Runs against a fake Ollama in tests (validation rejects bad lines)
- [ ] A real run for `hug,kiss,joke` produces valid entries; review sheet reviewed by you
- [ ] full gate passes
**Tests**: unit   **Gate**: full

### T7: `LayaClient` (async, timeout, cache) [P]
**What**: HTTP client for the two Laya classification questions (receiver temperament, line tone) with 150 ms cap, a per-Sim/mood cache, a probability floor and one-line failure logging.
**Where**: `Tools/Conversations/LayaClient.cs`, `Tools/Conversations.Tests/LayaClientTests.cs`
**Depends on**: T3   **Reuses**: none
**Requirement**: CONV-02, CONV-04
**Done when**:
- [ ] Tests with a local fake server: success, slow (timeout), 500, bad JSON, out-of-range answer
- [ ] No call is made when the feature is off
- [ ] quick gate passes
**Tests**: unit   **Gate**: quick

### T8: `ConversationHook` (game side, net461)
**What**: Reads both Sims into `RawSim`, runs profile -> Laya -> resolver, writes attr 9, queues the balloon lines; skips pets, children, NPCs, ghosts.
**Where**: `Tools/Conversations/ConversationHook.cs`
**Depends on**: T3, T5, T7, T4   **Reuses**: `SpriteCards.cs` game-hook style
**Requirement**: CONV-02, CONV-03, CONV-05, CONV-06
**Done when**:
- [ ] Builds against the pristine release
- [ ] Autonomous Sim-to-Sim socials pick the tone from personality
- [ ] full gate passes
**Tests**: integration (patch test below covers wiring)   **Gate**: full

### T9: `ConversationsPatch` - register primitive and redirect BHAV 397
**What**: Cecil patch that registers the new opcode at startup and rewrites BHAV 397 instruction 0 at IFF load to call it; idempotent; runs on original DLLs.
**Where**: `Tools/ConversationsPatch/Program.cs`, `Tools/PatchTests/ConversationsPatchTests.cs`
**Depends on**: T8   **Reuses**: `SpriteCardsPatch` idempotency and backup rules
**Requirement**: CONV-02, CONV-05
**Done when**:
- [ ] Patch tests: hook present, second run changes nothing, original behavior kept when flag off
- [ ] `397` has exactly 23 callers asserted in a test fixture
- [ ] full gate passes
**Tests**: integration   **Gate**: full

### T10: Async 3-choice dialog through the VM wait path
**What**: Show 3 labelled buttons (`VMDialogInfo` YesNoCancel with caller-supplied text) and resume the script with the chosen tone; Escape = no choice.
**Where**: `Tools/Conversations/ConversationHook.cs` (dialog part), `Client` UI via existing dialog route
**Depends on**: T9   **Reuses**: `VMDialog` primitive and `UIMobileAlert`
**Requirement**: CONV-01
**Done when**:
- [ ] Choosing each button yields tone 1, 2, 3; Escape yields neutral
- [ ] No Sim freezes when the dialog is ignored for 60 s (timeout path)
- [ ] e2e step passes
**Tests**: e2e   **Gate**: e2e

### T11: Speaker line and reply as balloon text
**What**: Show the speaker line then the receiver reply using headline/balloon text with length limits.
**Where**: `Tools/Conversations/ConversationHook.cs` (output part)
**Depends on**: T10   **Reuses**: `set_balloon_headline` / `VMEntity.Headline`
**Requirement**: CONV-03
**Done when**:
- [ ] Both lines visible in a screenshot (`simtool shot`) during a Joke
- [ ] No text longer than the limit
**Tests**: e2e   **Gate**: e2e

### T12: Options menu toggle (persisted, green when on)
**What**: New entry in the Options list with live highlight; setting saved next to `spritecards.profile`.
**Where**: `Tools/SpriteCards` menu patch (new entries), `Tools/SpriteCards.Tests`
**Depends on**: T11   **Reuses**: `SpriteCards.Decorate`, `UIMainPanel.Switcher_OnCategorySelect` patch
**Requirement**: CONV-09
**Done when**:
- [ ] Toggle changes behavior live; off = no dialog and no Laya request
- [ ] Persists after restart
- [ ] full gate passes
**Tests**: unit + e2e   **Gate**: full

### T13: Talk subjects (P2)
**What**: Offer up to 3 subjects mapped to the 8 interest values and reflect the receiver's interest in outcome and reply.
**Where**: `Tools/Conversations/TopicLogic.cs`, tests
**Depends on**: T12   **Reuses**: script `SHD do I like topic?` mapping from `Tools/ScriptDump` output
**Requirement**: CONV-08
**Done when**:
- [ ] Topic-to-interest mapping asserted against the decoded script
- [ ] Liked topic gives +3 STR, disliked -3 (existing rule), verified in game
- [ ] full gate passes
**Tests**: unit + e2e   **Gate**: full

### T14: CI, docs, regression
**What**: Add tools to `ci.ps1` and the workflow; extend `Run-E2E.ps1` (feature off equals original STR after a scripted Hug); update `README`, `ARCHITECTURE.md`, `AI-CONTEXT.md`.
**Where**: `Tools/ci.ps1`, `.github/workflows/tools-ci.yml`, `Tools/e2e/Run-E2E.ps1`, docs
**Depends on**: T13, T6   **Reuses**: existing CI
**Requirement**: CONV-11, CONV-07
**Done when**:
- [ ] CI green on GitHub
- [ ] e2e: with the feature off the scripted Hug STR delta matches the original table
**Tests**: integration + e2e   **Gate**: full

## Pre-approval checks

**Granularity**: T8 is the widest (hook + profile read + autonomy rule); acceptable as one cohesive file, split if it grows.

**Diagram vs definitions**

| Task | Depends on (body) | Diagram | Status |
|---|---|---|---|
| T1 | - | start | OK |
| T2 | T1 | T1 -> T2 | OK |
| T3, T4, T5 | T2 | from T2 | OK |
| T6 | T4, T5 | from T4, T5 | OK |
| T7 | T3 | T3 -> T7 | OK |
| T8 | T3, T4, T5, T7 | from T3,T4,T5,T7 | OK |
| T9 -> T10 -> T11 -> T12 -> T13 | previous | chain | OK |
| T14 | T13, T6 | from T13, T6 | OK |

**Test co-location**: every code task carries its tests; T1 and T2 are spikes with no shipped code. T8 has no standalone unit test because it needs the game; it is covered by T9 patch tests and T10/T11 e2e (flagged for your review).

**Parallel**: T3, T4, T5 are pure and parallel-safe. Everything that runs the game (T10-T14 e2e) is sequential.

## Tools to use

- MCP: filesystem only. Laya is reachable as a plain HTTP service; its MCP server was not connected this session.
- Skills: none needed. `mermaid-studio` is not installed; diagrams are inline Mermaid.
