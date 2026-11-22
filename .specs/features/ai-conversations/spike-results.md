# Spike results (T1 and T2), 2026-11-21

## T1: hook point

| Question | Result | How it was checked |
|---|---|---|
| Which scripts call `397 enable social outcome choice?` | **18 scripts** (23 call sites), all in `SocialInteractions.iff`: Apologize, Back Rub, Brag, Cheer Up, Compliment, Dance With, Entertain, Flirt, Give Gift, Hug, Insult, Joke, Kiss, Propose, Scare, Slap, Tease, Tickle | Decoded dump of every `... - Person A` script |
| Social scripts that do NOT call it | Talk, Say Goodbye, Call Over, Move In, Stereo Dance, Arrest, Scare - Ghost, the sofa Cuddle set, a shack cut-scene | Same dump |
| What is the stack object inside 397 | The social object. The caller saves the stack object at entry, switches to the listener only to read their stats, then restores it before the outcome is written | Static reading of `Hug - Person A` and `Kiss - Person A` |
| Is attr 9 (outcome) writable there | Yes: it is a plain object attribute the script itself sets to 1, 2 or 3 just before calling 397 | Same |
| What gates the native 3-button dialog | `IsFlagSet(Global[25], 2)` tests value 2 (`1 << (2-1)`). The VM initialises `Global[25] = 4`, so the dialog is normally off | `VMExpression.cs`, `VM.Init` |
| In-game proof that forcing the outcome changes animation and STR | **Not done.** A one-instruction patch (`Global[25] = 6`) was built and applied to a copy of `FSO.SimAntics.dll`, the game launched with it, and then restored from backup (verified byte-identical). The Newbie lot has a single Sim (Bob), so there was no one to socialise with; reaching a two-Sim household through the map UI was not completed | `Tools` simtool session |

Consequence: the plan's seam is supported by the script logic but is still unproven in the running game. First task of the build phase: repeat with a two-Sim household using the `Global[25] = 6` patch, which needs no new code.

Correction to the spec: it said "23 interactions". It is **18 scripts**; Talk is separate (already P2), and Move In, Say Goodbye and the sofa socials are not covered.

## T2: Laya and Ollama

Laya 0.3.21 is installed (Python 3.11, user site), CUDA, models `english`, `multilingual`, `typed-decisions`. It was not running; starting `python -m laya.serve` works and `/health` reports CUDA with no CPU fallbacks.

**Speed** (60 calls, short Sims-style prompts): first call 1.1 s (warm-up), then median **44 ms**, p95 **76 ms**, max 84 ms. A 150 ms cap is safe; the first-call warm-up must be done at game start.

**Quality** (the important result):

| Task given to Laya | Result |
|---|---|
| "How does the receiver react? 1/2/3" from a trait description | **Unusable.** Probabilities about 0.33 each. An obvious insult to an angry stranger was classified "warm" |
| Same with word labels, a score question, a yes/no question | Same: the yes/no answered 0.5 for everything |
| "What is the tone of this sentence?" (hostile, neutral, kind, romantic, playful) | **Good**: 4 of 5 right, hostile 0.85, kind 0.59, romantic 0.48; the joke was read as neutral (0.42 vs playful 0.32) |
| "Which describes how this person takes jokes?" from a trait description | **Good**: sensitive Sim -> easily_offended 0.52, grumpy -> cold 0.72, warm Sim -> receptive 0.48; the tough, playful Sim was a near tie (receptive 0.39 vs thick_skinned 0.37) |

Conclusion: Laya classifies text into categories well and cannot reason about a reaction. So Laya is used for **classification** (receiver temperament from their description, tone of a line), and a small **deterministic compatibility table** turns (temperament, tone, script score) into the outcome, under policy B.

Other facts for bundling:
- Laya is configured only by environment variables: `LAYA_HOST` (default **0.0.0.0**, so it listens on every network interface unless set), `LAYA_PORT`, `LAYA_MODELS`, `LAYA_API_KEY`, `LAYA_DEVICE`, `LAYA_MAX_CONCURRENT`.
- Footprint: the `laya` package is 1.4 MB, but PyTorch with CUDA is **4.4 GB** and the model weights are **2.3 GB**. An ONNX path exists (`onnx_agent.py`, `onnxruntime` installed) and may allow a much smaller bundle; not measured.
- Running Laya's `--help` starts the server instead of printing help (it ignores arguments).

Ollama is running with `gemma3:4b`, `qwen3:8b`, `qwen2.5:1.5b`, `llama3.2:1b`, deepseek variants and others. **Not yet done:** the sample-line comparison between gemma3:4b and qwen3:8b; it was dropped to keep this round focused on Laya and the hook.

## T1b attempt (2026-11-21, second session): not completed

**Correction:** the Newbie lot is **not** a single-Sim household. The family is Bob and Betty (shown on the lot card). Earlier I only saw Bob on screen and wrongly concluded there was one Sim.

What was learned:
- For Sims 1 lots, `Global[25]` is set to 4 in **two** places: `VM.Init` and `VMTS1Activator.LoadFromIff`. The first test patched only `VM.Init` and so had no effect. A patch of both (`Global[25] = 6`) is built: `the-sims-spike/FSO.SimAntics.flag6.dll` (made from the original DLL by `the-sims-spike/flag`).
- With the full patch the game ran; the pie menu on the other Sim offers Dance, Entertain, Flirt, Hug (Friendly, Leap Into Arms, Intimate, Romantic), Kiss, Talk, Tickle (Ribs, Extreme), Ask.
- Walls-down view (HUD icon at 145,920) shows the whole house, and zooming out (minus button at 115,996 twice) shows the whole lot. Pause is the button at (273,1040), normal speed at (183,1040). Game time passes about 3 minutes per real second, so Sims move away between clicks.
- I chose Hug > Friendly and Tickle > Ribs. No outcome dialog appeared and I could not confirm that either action was queued or ran (the queue panel was not visible; the active-Sim portrait kept switching to a flamingo placeholder). The original DLL was restored and verified identical afterwards.

Conclusion: driving this through clicks and screenshots is too fragile to be the proof. Better route for T1b: add a pipe command to `SimTestHook` that queues a named interaction between two named Sims directly (`interact Bob Betty hug-friendly`), plus a log line when script 397 runs and what attr 9 holds. That also gives the e2e suite a reliable way to test conversations later.
