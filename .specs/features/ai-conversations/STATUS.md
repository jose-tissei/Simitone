# Build status (2026-11-22)

| Requirement | Status | Where / how verified |
|---|---|---|
| CONV-01 three-line choice before the social | Built, verified in the game | `Game/ConvoPrimitive.cs`; e2e "a Hug opens the three-line choice dialog" |
| CONV-02 outcome from traits via Laya, max one step | Built, verified | Laya labels temperament, `OutcomeResolver` shifts; log showed script 2 -> final 3 for a Receptive Betty with Playful |
| CONV-03 speaker line + reply shown | Built, verified | `Game/ConvoOverlay.cs`: balloons over the Sims' heads (the game's own chat-balloon screen mapping), answer 1.6 s later, bottom strip only as a fallback; layout in `Logic/BalloonLayout.cs` (tested); e2e checks "placed over the Sims" |
| CONV-04 timeout and fallback | Built | Laya runs off-thread with cached results; unknown temperament = no shift; 148 unit tests |
| CONV-05 off switch, missing data | Built, verified | Options toggle (persisted) and `simtool convo off`; bank misses fall back to built-in lines |
| CONV-06 autonomous Sims skip the choice | Built, verified | tone from personality; the step waits up to 90 ticks for Laya so the temperament is known; e2e via `simtool convo auto on` (the game refuses to queue for a non-active Sim) |
| CONV-07 relationship deltas unchanged | Holds by construction | only the outcome number is touched; tables are the game's own |
| CONV-08 Talk subjects | Built, verified | hook on the topic routine; dialog showed three subjects; the choice reached the game |
| CONV-09 Options toggle | Built, verified | button "AI conversations", green when on, saved to `conversations.profile` |
| CONV-10 background refill | Offline only (decided) | misses are written to `Content\conversations\missing.txt` for a `convobank` run; thin groups (kiss Gentle, compliment, tease) were refilled |
| CONV-11 tests and CI | Built | 148 unit tests, 17 patch tests, 47 e2e checks, `ci.ps1` |
| CONV-12 Laya starts and stops with the game | Built, verified | `LayaHost`: 127.0.0.1 only, random key, job object; `Tools/Package-Laya.ps1` builds the bundled CPU copy (1.7 GB); e2e confirms the game started `laya\python\python.exe` |

## Deviations from the plan
- The hook point is not only script 397: in the expansion variants the outcome is computed after the call, so a second instruction is swapped; Talk needed its own hook (`VMThread.ExecuteSubRoutine`).
- Laya decides nothing about outcomes; it only classifies temperament (spike result).
- Hostile socials (Insult, Scare, Slap, Tease, Propose) get lines but no outcome shift.
- Laya is not in the repository: `Package-Laya.ps1` builds it next to the game (embeddable Python, CPU PyTorch, English weights only). CPU-only measured: ready in about 6 s, 190 ms per classification (GPU 30 ms).
- Requests pin the `english` checkpoint; newer Laya versions otherwise route short text to the multilingual one.

## Still open
Nothing known. (Autonomous Talk now picks the Sim's favourite subject and shows balloons; Compliment Bold and Tease Gentle lines were written by hand.)
