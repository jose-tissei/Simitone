# Sims remaster UI redesign

Made with Claude Design, imported 2026-11-24.

| File | What it is |
|---|---|
| `UI-GUIDE.md` | The rules (hit targets, text sizes, dialogs, motion), the screen map (what replaces what) and the recipe for a new screen. Read this first. |
| `design-tokens.json` | The only source of colours (themes `night` and `day`), type, spacing, radii, sizes, motion, icon order. |
| `kit/` | The design canvas export: 11 boards as `*.dc.html` (`Memory`, `MemoryNews`, `Components`, `Options`, `Buy`, `Conversation`, `TalkSheet`, `Hood`, `Hud`, `Playbook`, `Main`), `assets/` (the web stylesheet `b685...css`, `icons.svg`, mock-up screenshots) and `canvas.json`. Open a board through a local web server: `python -m http.server` inside `kit/`. The React runtime was left out; boards need it, re-export from the design to view them. |
| `skins/icons.png`, `icons.svg` | The icon sheet rendered white on transparent, 8 x 8 cells of 96 px (so it can be tinted). Order = `iconSheet.order` in the tokens. |
| `screenshots/` | The Sim Memory window built from this design, in the running game (night) and in the preview tool (day). |

## How it is built here

The guide proposes a JSON layout read by a C# loader. We use the HTML + CSS engine from `Tools/HtmlUi` instead (see its README): pages are `Tools/HtmlUi/ui/<screen>.html` with `sims-ui.css`, colours come from the tokens as `var(--name)`, data is filled in with a small template (`Template.cs`), and the C# window only supplies data and handles `data-action` clicks. Done so far: **Sim Memory** (Dossier and News), `Tools/Conversations/Game/SimMemoryMenu.cs` + `Logic/SimMemoryPageData.cs`. Everything else in the screen map is still to do.

## Checking a component

Use the project skill `ui-redesign-check` (`.claude/skills/ui-redesign-check`): `node .claude/skills/ui-redesign-check/check-ui.mjs Tools/HtmlUi/ui`, then render night and day with `Tools/HtmlUi.Preview`, then the judgement checklist in the skill.

## Differences from the mock-up (Sim Memory)

- The in-game font is the game's own (no Fredoka/Montserrat SpriteFonts yet), regular weight only; titles are not bold. Sizes are scaled by `HtmlSurface.FontScale` (0.70) because the game font is about 1.4 times larger than the same CSS px size.
- No gloss, hard "plastic" shadow, bobbing plumbob or bounce: the engine has no box-shadow, animation or transitions. Rounded corners, gradients, borders and icons are drawn.
- The day theme works in the preview tool; the game uses night (no setting yet).
- Lists are capped so the window keeps the 800 px height rule: 2 likes/dislikes, 6 "works well", 4 "avoid", 2 topics, 5 news stories, each followed by "+N more". The News filter (All / Saw it / Heard / Mismatch) and the "Tab switches Sim" hint were left out because they are not implemented.
