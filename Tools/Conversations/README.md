# AI conversations

Makes The Sims 1 social interactions livelier: before a social plays, the player picks one of three lines to say; the result
depends on the other Sim's character; both lines are shown as speech balloons over the Sims. Talk offers three subjects. Nothing here changes the
game's relationship numbers: the game's own scripts still apply their own tables to whatever outcome is chosen.

## What the player sees
- **Hug, Kiss, Flirt, Joke, Compliment, ... (18 socials):** a dialog "What do you say to Betty?" with three lines
  (Gentle / Playful / Bold) and three buttons. After the choice the game plays the social and a balloon over each Sim shows the
  speaker's line, then (1.6 s later) the other Sim's answer. The balloons follow the Sims and keep inside the screen; if a Sim's
  position is unknown the line goes to a strip at the bottom instead.
- **Talk:** a dialog "What do you want to talk to Betty about?" with three subjects (from the eight Sim interests).
  The game then scores the subject by how much the listener likes it, as before.
- **Options menu:** an "AI conversations" button (speech-bubble icon) switches the whole feature on and off; green = on. The choice
  is saved in `Documents\Simitone\conversations.profile`.
- Sims the player does not control (autonomous socials) are not asked; they pick a tone from their personality, wait up to about 3 s of game time for Laya to classify the receiver (so the first conversation
  already has a temperament), and still show the balloons. Autonomous Talk picks the Sim's favourite subject.
- Children, pets and anything the feature does not recognise keep the game's original behaviour.

## How the result is decided (policy B)
1. The game's own script produces an outcome (1, 2 or 3).
2. **Laya** (a small local classifier) reads a short description of the receiver (personality, mood, job) and labels their
   temperament: `easily_offended`, `thick_skinned`, `cold` or `receptive`. This runs in the background while the player is
   choosing, and is cached.
3. A small table (`OutcomeResolver`) turns (temperament, chosen tone) into a shift of -1, 0 or +1 on the game's outcome.
   The result never moves more than one step. Unknown temperament or Laya down = no shift = the original game.
4. Hostile and special socials (Insult, Scare, Slap, Tease, Propose) keep the game's outcome; only the lines are added.

Measured in the spike: Laya answers in about 44 ms but cannot judge "how will they react" (about 0.33 per option); it does classify
text well. So it is used only for classification, and the rules table decides.

## Laya ships with the game
`Boot.Startup` (called from the game's Main) starts `python -m laya.serve` in the background: bound to `127.0.0.1` only, on a free
port, with a random API key, hidden, and tied to the game by a Windows job object so it stops when the game does (even on a crash).
It looks for a bundled `laya\python\python.exe` next to the game first, then a system Python (`py -3.11`, `python`).
Laya itself is not in this repository. `Tools/Package-Laya.ps1 -Game <game folder>` builds the bundle: embeddable Python 3.11,
CPU PyTorch, `laya[serve]` and only the English weights, 1.7 GB in total, then self-tests it offline. Nothing needs to be installed
on the PC and no GPU is needed. The bundled copy runs with `HF_HUB_OFFLINE=1`, its own `HF_HOME` and `PYTHONNOUSERSITE=1`.
Every request names the `english` checkpoint (newer Laya versions route to a multilingual one otherwise, which is not bundled).
Measured on CPU only (GPU hidden): ready in about 6 s, 190 ms per classification (GPU: 30 ms). That is fine because it runs
in the background. If Laya cannot start, the feature falls back to the game's own outcomes and logs one line to `Content\conversations.log`.
Set `SIMITONE_LAYA_URL` (and `SIMITONE_LAYA_KEY`) to use an already running server instead (development).

## The lines (no AI at play time)
`Content\conversations\bank.json` holds the fixed list of lines. It is built offline with your local Ollama by `Tools/ConvoBank`
(for example `convobank --out Tools/Conversations/bank/bank.json --model gemma3:4b`). Every line passes the same filter the game uses
(length, characters, blocked words, placeholders); a review sheet of kept and dropped lines is written next to it.
Where the bank has nothing for a combination, built-in lines are used, so there is never an empty line.
Placeholders: `{me}` speaker, `{name}` receiver, `{job}` receiver's job title, `{topic}` Talk subject.

## How it plugs into the game (no game source is edited)
- `Tools/SpriteCardsPatch` (given `Conversations.dll` as 4th argument) adds a call at the end of the `VMContext` constructor
  (`Boot.OnContextReady`) and one at the start of `VMThread.ExecuteSubRoutine` (`ConvoTalkPrimitive.OnSubRoutine`), and the Options menu toggle.
- `OnContextReady` registers two primitives and rewrites the one instruction of the game's global script 397
  ("enable social outcome choice?", called by the 18 social scripts) to call ours. Ours shows the dialog and returns FALSE,
  so the social script continues. Where the script computes its outcome later, the instruction after the game's own decision
  is swapped for a second primitive that adjusts the outcome right before it is stored.
- `OnSubRoutine` finds the Talk subject routine (`select topic I like.` / `SHD select topic I like`) the first time it is called
  and replaces its first instruction.
- The dialog uses the engine's own blocking-dialog path, so the game clock stops while the player chooses.

## Layout
| Path | What |
|---|---|
| `Logic/` | Pure code, no game types, unit tested: traits, outcome rules, dialogue bank, Laya client and host, Talk subjects |
| `Game/` | Game side: `Boot` (entry points), `ConvoPrimitive`, `ConvoTalk`, `ConvoOverlay` |
| `bank/` | `bank.json` (built lines) and review sheets |
| `../Conversations.Tests` | 148 unit tests |
| `Logic/BalloonLayout.cs` | Balloon wrapping and placement (tested) |
| `../Package-Laya.ps1` | Builds the bundled Laya |
| `../ConvoBank` | Offline bank builder (Ollama) |
| `../Install-Conversations.ps1` | Patches a pristine release and installs into a game folder |
| `../PatchTests/ConversationsPatchTests.cs` | Patcher tests against the real release |

## Install
    pwsh Tools/Install-Conversations.ps1 -Pristine <unmodified v0.8.12> -App <game folder>
and make sure Python with `laya` is available (or a bundled `laya\python`).

## Try it without clicking (simtool)
    simtool convo status             enabled, Laya up, bank loaded, last decision
    simtool convo auto on|off        treat every Sim as not player-controlled (tests the autonomous path)
    simtool convo tone 1             skip the dialog and use tone 1 (0 Gentle, 1 Playful, 2 Bold)
    simtool convo force 3            force the outcome (test aid), `force off` to stop
    simtool convo last | log         last decision / log tail
    simtool interact Bob Betty "Hug.../Friendly"

## Known limits
- Which script variant runs depends on the installed expansions; the hook finds the outcome step by its shape and logs
  `could not find the outcome store` if a variant does not match (the game then behaves as before).
- Autonomous Talk: the Sim talks about the subject they like most and the balloons are shown.
- Missing bank entries fall back to built-in lines; refilling the bank is an offline run of `ConvoBank` (no live Ollama in the game).
