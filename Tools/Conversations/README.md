# AI conversations

Makes The Sims 1 social interactions livelier and autonomous:
- **Autonomous parallel room conversations:** awake adult Sims in the same room (or within 8 tiles outdoors) automatically talk to each other **in parallel with their ongoing activities** (without interrupting their interaction queue) whenever a Sim's Social motive is at or below **60%** (`<= +20` on `-100..+100`).
- **Laya-driven interaction & AI topic selection:** Laya (`/v1/systemone`) chooses both the verbal interaction (`talk`, `compliment`, `joke`, `brag`, `cheerup`, `apologize`, `flirt`, `tease`) and the AI talk subject, modulated by the speaker's **knowledge** of the listener (LTR/STR + observed history), both Sims' **skills** (Charisma reading/tact + Cooking/Mechanical/Creativity/Logic/Body topic resonance), and both Sims' **moods**.
- **Social memory & anti-negative-loop learning:** per-pair memory (`Documents\Simitone\conversations.memory.json`) records outcomes for both manual and autonomous exchanges, penalizes recently failed interactions/tones/topics, engages de-escalation at `NegativeStreak >= 2`, enforces cool-down backoff at `NegativeStreak >= 3`, and resets on positive outcomes.
- **Simplified manual pie menu (Talk & Romance only):** clicking another adult Sim shows only **Talk** and **Romance** (children, pets, self, ungreeted visitors, non-social NPCs, and feature-off keep the unmodified menu).
- **100% AI Talk subjects:** both manual Talk dialogs and autonomous parallel Talk draw exclusively from the AI Subject Bank (`bank.json` `subjects` + 48 curated built-in AI subjects) ranked by Laya—never raw hardcoded Sims 1 interest labels (`"Travel and Toys"`, `"Sixties and School"`, etc.).

## What the player sees
- **Autonomous parallel chatter:** when a Sim in a shared room hits Social `<= 60%`, speech balloons appear over both Sims' heads while they keep cooking, eating, reading, or watching TV. Both Sims' Social motives and STR/LTR update cleanly; concurrent exchanges in different rooms coexist (up to 4 active balloons).
- **Manual Sim pie menu:** only two top-level bubbles appear on adult human Sims: **Talk** and **Romance** (Flirt, Kiss, Romantic/Intimate/Leap-Into-Arms Hug, Back Rub, Slow/Romantic Dance, Propose).
- **Manual Talk:** a dialog `"Choose an AI conversation subject with Betty:"` with three AI-generated subjects ranked by Laya for the two Sims. Choosing one maps cleanly to its underlying Sims 1 interest slot (`0..7`), updates `SocialMemory`, and displays both spoken lines.
- **Manual Romance / scripted socials:** a dialog `"What do you say to Betty?"` with three lines (Gentle / Playful / Bold). After the choice, the game plays the social, `OutcomeResolver` shifts the outcome by at most one step using Laya's temperament classification, `SocialMemory` records the result, and balloons show both lines.
- **Options menu:** an "AI conversations" button (speech-bubble icon) switches the whole feature on and off; green = on. When off, parallel chatter stops and the full unmodified Sims 1 pie menu returns. Saved in `Documents\Simitone\conversations.profile`.
- Children, pets, self, ungreeted visitors, and anything the feature does not recognise keep the game's original behaviour.

## How autonomous & manual choices are decided
1. **Manual socials (policy B):** Laya reads a short description of the receiver and classifies temperament (`easily_offended`, `thick_skinned`, `cold`, `receptive`). `OutcomeResolver` shifts the game's outcome by `-1`, `0`, or `+1`. Unknown temperament or Laya down = no shift.
2. **Autonomous parallel room chatter (`ParallelSocialPlanner` + `SocialIntelligence`):**
   - Runs off the VM tick thread (`ThreadPool`) so gameplay never stalls.
   - Computes **Knowledge** (`0..1`) from LTR/STR + observed exchanges; low knowledge blurs/redacts the listener's temperament, interests, skills, and mood in the Laya prompt so strangers make realistic missteps while close friends read each other accurately.
   - **Charisma** improves accuracy and mood reading; shared/known **Cooking, Mechanical, Creativity, Body, and Logic** skills boost affinity for matching AI subjects.
   - **Mood** affects tact (low speaker mood raises error/impatience; perceived low listener mood favors `cheerup`/`talk`/`compliment`).
   - **SocialMemory** penalizes failed interactions/tones/subjects, forbids risky options and switches to de-escalation at 2 consecutive failures, backs off the pair at 3 failures, and resets on a great outcome.
   - If Laya is unavailable or times out, deterministic trait/skill/mood/memory scoring takes over seamlessly.

## Laya ships with the game
`Boot.Startup` (called from the game's Main) starts `python -m laya.serve` in the background: bound to `127.0.0.1` only, on a free
port, with a random API key, hidden, and tied to the game by a Windows job object so it stops when the game does (even on a crash).
It looks for a bundled `laya\python\python.exe` next to the game first, then a system Python (`py -3.11`, `python`).
Laya itself is not in this repository. `Tools/Package-Laya.ps1 -Game <game folder>` builds the bundle: embeddable Python 3.11,
CPU PyTorch, `laya[serve]` and only the English weights, 1.7 GB in total, then self-tests it offline. Nothing needs to be installed
on the PC and no GPU is needed. The bundled copy runs with `HF_HUB_OFFLINE=1`, its own `HF_HOME` and `PYTHONNOUSERSITE=1`.
Every request names the `english` checkpoint (newer Laya versions route to a multilingual one otherwise, which is not bundled).
Set `SIMITONE_LAYA_URL` (and `SIMITONE_LAYA_KEY`) to use an already running server instead (development).

## The lines and AI subjects (no live LLM at play time)
`Content\conversations\bank.json` holds both the dialogue lines (`speaker`, `reply`) and the AI Talk subjects (`subjects`).
It is built offline with your local Ollama by `Tools/ConvoBank`:
- `convobank --out Tools/Conversations/bank/bank.json --model gemma3:4b --subjects` (builds lines + AI subjects)
- `convobank --out Tools/Conversations/bank/bank.json --model gemma3:4b --subjects-only` (generates/tops up AI subjects only)
- `convobank --out Tools/Conversations/bank/bank.json --subjects-only --seed-subjects` (seeds the 48 built-in AI subjects offline)

Every line and subject passes validation (length, characters, blocked words, no raw TS1 interest labels). Missing bank entries fall back to built-in lines and built-in AI subjects, so there is never an empty line or raw TS1 topic.
Placeholders: `{me}` speaker, `{name}` receiver, `{job}` receiver's job title, `{topic}` AI Talk subject.

## How it plugs into the game (no game source is edited)
- `Tools/SpriteCardsPatch` (given `Conversations.dll` as 4th argument) patches `FSO.SimAntics.dll` with four idempotent Cecil hooks (plus the Options menu toggle in `Simitone.Client.dll`):
  1. `VMContext..ctor` -> `Boot.OnContextReady(this)`: installs conversation primitives (`ConvoPrimitive`, `ConvoApplyPrimitive`, `ConvoTalkPrimitive`) and swaps global script 397.
  2. `VMThread.ExecuteSubRoutine` -> `ConvoTalkPrimitive.OnSubRoutine(frame, opcode)`: intercepts the Talk topic routine (`select topic I like.` / `SHD select topic I like`) to present 3 AI subjects.
  3. `VM.InternalTick(uint)` -> `Boot.OnTick(this)`: drives `ParallelSocialDriver` on simulation ticks without touching `VMThread.Queue`.
  4. `VMEntity.GetPieMenu(VM, VMEntity, bool, bool)` -> `Boot.FilterPieMenu(list, this, vm, caller)`: restricts adult Sim manual pie menus to `Talk` and `Romance` (never touches SimAntics autonomy).

## Layout
| Path | What |
|---|---|
| `Logic/` | Pure `net461`/`net9.0` logic, zero game types, unit tested: `Traits`, `OutcomeResolver`, `DialogueBank`, `LayaClient`, `LayaHost`, `TopicLogic`, `AiSubjectBank`, `SocialMemory`, `SocialIntelligence`, `ParallelSocialPlanner`, `PieMenuLogic`, `BalloonLayout` |
| `Game/` | Game runtime: `Boot`, `ParallelSocialDriver`, `ConvoPieMenu`, `ConvoPrimitive`, `ConvoTalk`, `ConvoOverlay` |
| `bank/` | `bank.json` (built dialogue lines + 48 AI Talk subjects) and review sheets |
| `../Conversations.Tests` | 235 unit tests |
| `../Package-Laya.ps1` | Builds the bundled Laya |
| `../ConvoBank` | Offline dialogue & AI subject builder (Ollama) |
| `../Install-Conversations.ps1` | Patches a pristine release and installs into a game folder |
| `../PatchTests/ConversationsPatchTests.cs` | Cecil patcher integration & idempotency tests against the real release |

## Install
    pwsh Tools/Install-Conversations.ps1 -Pristine <unmodified v0.8.12> -App <game folder>
and make sure Python with `laya` is available (or a bundled `laya\python`).

## Try it without clicking (simtool)
    simtool convo status             enabled, Laya up, bank/subjects count, menu mode, parallel count, memory pairs, last decision
    simtool convo parallel           total parallel exchanges and last parallel decision
    simtool convo memory             per-pair social memory summary
    simtool convo trigger A B        immediately dispatch a parallel exchange from Sim A to Sim B
    simtool convo menu full|simple   temporarily show full legacy pie menu or Talk+Romance menu
    simtool convo auto on|off        treat every Sim as not player-controlled (tests the autonomous path)
    simtool convo tone 1             skip the dialog and use tone 1 (0 Gentle, 1 Playful, 2 Bold)
    simtool convo force 3            force the outcome (test aid), `force off` to stop
    simtool convo last | log         last decision / log tail
    simtool interact Bob Betty "Talk"

## Known limits
- Which script variant runs depends on the installed expansions; the hook finds the outcome step by its shape and logs
  `could not find the outcome store` if a variant does not match (the game then behaves as before).
- Parallel room chatter uses verbal interactions only (`talk`, `compliment`, `joke`, `brag`, `cheerup`, `apologize`, `flirt`, `tease`); physical socials remain in manual Romance or native scripts.
- Missing bank entries fall back to built-in lines and built-in AI subjects; expanding the bank is an offline run of `ConvoBank` (no live Ollama in the game).
