---
name: ui-redesign-check
description: Check whether a Simitone UI component, page or window follows the Sims remaster UI redesign (docs/ui-redesign). Use when asked to review, audit, build or "make compliant" any in-game UI (HTML pages in Tools/HtmlUi/ui, windows, dialogs, panels, chips, buttons), or before committing UI changes. Runs an automatic linter against design-tokens.json and the guide's non-negotiable rules, renders the page in both themes, and walks the judgement checklist the linter cannot decide.
---

# UI redesign check

The redesign lives in `docs/ui-redesign/`: `UI-GUIDE.md` (rules, screen map, recipe), `design-tokens.json` (the only source of colours, sizes, radii, type), `kit/` (the design canvas boards: `Memory.dc.html`, `Components.dc.html`, ...), `skins/icons.png` (8x8 icon sheet).
In-game pages are HTML + CSS in `Tools/HtmlUi/ui/` (`sims-ui.css` + one `.html` template per window + `.sample.json` data), drawn by `HtmlSurface`.

## 1. Run the linter (always)

```
node .claude/skills/ui-redesign-check/check-ui.mjs Tools/HtmlUi/ui            # every page in the folder
node .claude/skills/ui-redesign-check/check-ui.mjs Tools/HtmlUi/ui/sim_memory.html --json
```

Run from the repo root (`Simitone/`). It reads the page, `sims-ui.css` next to it, `design-tokens.json` and `<page>.sample.json`.

| Level | Meaning | Rules it covers |
|---|---|---|
| ERROR | breaks a non-negotiable rule or the token file; fix before committing | `unknown-colour` (hex not in tokens), `unknown-token` (`var(--x)` that does not exist), `min-font` (< 16 px outside captions/overlines/badges/keycaps), `hit-target` (< 44 px; 44-47 is a WARN), `icon-label` (icon-only control without `aria-label`), `colour-only` (status chip with neither icon nor label), `dialog-size` (width not 640/960/1360, height > 800), `close-action` (modal without `data-action="close"`), `one-primary` (more than one `.btn.primary`), `font` (not Fredoka/Montserrat) |
| WARN | probably wrong | `token-colour` (a hex that equals a token: use `var(--...)` so both themes work), `radius` (not in the radius scale), `esc-hint` (modal without the Esc hint), `colour-only` (chip has a label but no icon), `template-field` (`{{field}}` missing from the sample data), `hit-target` (no fixed size to check) |
| INFO | not a rule | `engine-support`: grid, box-shadow, transitions, animations, color-mix, clip-path, backdrop-filter are fine in web mock-ups but are NOT drawn by the in-game engine (use flex, the `-a16`/`-a8` tint tokens, and gradients of two colours) |

Fix every ERROR. Fix WARNs or say why they stay. The linter works on text, it does not see the rendered result.

## 2. Render and look (always for a new or changed page)

```
Tools/HtmlUi.Preview/out/HtmlUi.Preview.exe Tools/HtmlUi/ui/<page>.html out-night.png --data Tools/HtmlUi/ui/<page>.sample.json --theme night --bg <game screenshot>
Tools/HtmlUi.Preview/out/HtmlUi.Preview.exe Tools/HtmlUi/ui/<page>.html out-day.png   --data ... --theme day --bg ...
```

(Build once with `dotnet build Tools/HtmlUi.Preview -c Release -o Tools/HtmlUi.Preview/out`.) Read both images. Test the long-text and many-items cases too: put long names and extra items in a copy of the sample data. For the real window, also deploy (`pwsh Tools/HtmlUi/Deploy-HtmlUi.ps1`) and take a `simtool shot` in the game: the game font is wider than the preview's.

## 3. Judgement checklist (the linter cannot decide these)

Answer each as pass / fail / n.a. with one line of evidence.

1. One job, one sentence. If it needs "and", it is two screens.
2. Container fits the job: dock tab, modal panel (S/M/L), dialog card, sheet, toast, pie, balloon, tooltip. Dialogs are centred cards, never full-width bars.
3. Head band has icon + Fredoka title + close top-right; at most one primary action (mint pill, bottom right); destructive actions are coral and confirm.
4. Meaning is never colour alone: status = colour + icon or label (see the certainty chips on the News page).
5. Grouped and labelled: no flat rows of unlabelled icons; toggles say On/Off in words.
6. Everything comes from kit components and tokens. A new component has all states drawn: default, hover, pressed, focus, disabled.
7. States exist for: empty, loading, error, very long text, 150 % UIScale. (Empty text should be friendly, Simlish flavour is optional.)
8. Both themes (night default, day "Sunny Lot") look right; world-space things (balloons, pie, tooltips) ignore the theme.
9. 32 px safe margin; one modal at a time; Esc closes the top modal; keyboard path exists for every action.
10. Motion: any animation has a reduce-motion fallback (not yet drawn in-game).
11. Mod UI is marked so players can tell native from mod (the "Mod" chip), when it is injected into native panels.
12. Copy is in plain words, no jargon; numbers have units or labels.

## 4. Report

Give: linter result (counts, every ERROR/WARN with the fix), the two renders reviewed, the checklist with evidence, and the verdict (compliant / compliant with listed exceptions / not compliant). If you change the design rules or tokens, update `docs/ui-redesign/UI-GUIDE.md` and this skill together.

## Notes for building pages

- Template syntax (`Tools/HtmlUi/Template.cs`): `{{name}}`, `<!--#each list-->...<!--/each-->`, `<!--#if x-->...<!--#else-->...<!--/if-->`, `{{.}}` and `{{#}}` inside `each`.
- Actions: `data-action="name"` on any element; the window maps it to behaviour (`close`, `switch`, `speaker-N`, `pick-N` in `SimMemoryMenu`).
- Engine limits: flex only (no grid), no shadows, transitions or animation, uniform border per box, one font (the game's), bold is not drawn. Wrapped chip rows must sit in a column parent, not in a row (Yoga mis-measures them).
- Hot reload: with the game open, edit `Content/ui/*.html|css` in the game folder and the window redraws within half a second.
