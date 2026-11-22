# Build status (2026-11-22)

| Requirement | Status | Where / how verified |
|---|---|---|
| CONV-01 three-line choice before the social | Built, verified in the game | `Game/ConvoPrimitive.cs`; e2e "a Hug opens the three-line choice dialog" |
| CONV-02 outcome from traits via Laya, max one step | Built, verified | Laya labels temperament, `OutcomeResolver` shifts; log showed script 2 -> final 3 for a Receptive Betty with Playful |
| CONV-03 speaker line + reply shown | Built, verified | `Game/ConvoOverlay.cs` (screen-space speech panel, not balloons over the Sims) |
| CONV-04 timeout and fallback | Built | Laya runs off-thread with cached results; unknown temperament = no shift; 135 unit tests |
| CONV-05 off switch, missing data | Built, verified | Options toggle (persisted) and `simtool convo off`; bank misses fall back to built-in lines |
| CONV-06 autonomous Sims skip the choice | Built | tone picked from personality; not exercised in the e2e |
| CONV-07 relationship deltas unchanged | Holds by construction | only the outcome number is touched; tables are the game's own |
| CONV-08 Talk subjects | Built, verified | hook on the topic routine; dialog showed three subjects; the choice reached the game |
| CONV-09 Options toggle | Built, verified | button "AI conversations", green when on, saved to `conversations.profile` |
| CONV-10 background refill | Partly | misses are written to `Content\conversations\missing.txt` for an offline `convobank` run; no live Ollama in the game (decided) |
| CONV-11 tests and CI | Built | 135 unit tests, 7 patch tests, e2e section, `ci.ps1` |
| CONV-12 Laya starts and stops with the game | Built, verified | `LayaHost`: 127.0.0.1 only, random key, job object; game log "laya: ready on port N" |

## Deviations from the plan
- Text is shown in a panel at the bottom of the screen, not balloons above the Sims (the client has no text-balloon facility).
- The hook point is not only script 397: in the expansion variants the outcome is computed after the call, so a second instruction is swapped; Talk needed its own hook (`VMThread.ExecuteSubRoutine`).
- Laya decides nothing about outcomes; it only classifies temperament (spike result).
- Hostile socials (Insult, Scare, Slap, Tease, Propose) get lines but no outcome shift.
- Laya is not bundled in the repository (about 6.7 GB); the launcher uses a bundled or system Python. Packaging remains open.
- Laya on CPU only was not measured.
