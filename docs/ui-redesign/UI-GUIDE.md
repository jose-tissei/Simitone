# Sims remaster UI guide (for the dev and the AI agent)

Source of truth: the **Sims Remaster UI Kit** design canvas (11 boards), `design-tokens.json`, `sims-ui.css` (web mock-up styles) and `icons.svg`.
All sizes are px at 1920x1080 and are multiplied by `UIScale` (1.0 / 1.25 / 1.5).

## 1. Look in one paragraph
Simitone's mint-teal headers, white glossy pill buttons, glossy blue icon buttons and dark docks stay. Two themes share every token name: **Night** (default, Simitone dark) and **Sunny Lot** (classic daylight). The Sims quirk lives in shape and motion: gloss on pills, a hard 5 px "plastic" shadow, a bobbing plumbob, bounce easing, and friendly Simlish-flavoured microcopy. It never lives in the layout grid.

## 2. Non-negotiable rules
1. Hit targets are at least 48x48. Essential text is at least 16 px. Body is 18.
2. Never carry meaning in colour alone. Status = colour + icon or label or pattern.
3. Dialogs are centred cards (S 640, M 960, L 1360 wide, max height 800), never full-width bars.
4. One primary action per surface: mint pill, bottom right. Destructive is coral and always confirms.
5. Group, then label. No flat rows of unlabelled icons. Toggles say On/Off in words.
6. World-space elements (balloons, pie menu, tooltips) are dark glass and ignore the theme. Docks and panels follow the theme.
7. Keep 32 px safe margin. Only one modal at a time. Esc always closes the top modal.
8. Every animation has a reduce-motion fallback.

## 3. Screen map (what replaces what)
| Native / mod UI | New design |
|---|---|
| Bottom-left dial (Live/Buy/Build, zoom, speed, funds) and bottom bar | **Kept as-is.** UX layer only: mode labels (Live/Buy/Build), tooltips on every control, labelled and shorter tab rail with unread badge on Memory |
| Needs bars (original art) | Same bars + exact value at the bar end; low needs get a striped remainder, a "!" icon and a pulse; tooltip says what to do |
| Options: one row of 12 icons | Options panel with 4 groups (Game, View, Graphics, Sims AI), switches with On/Off, isolated Quit |
| Buy/Build tiles (original bar) | Same bar + selected-tile ring and an item flyout above it: cards with price + stat chips, selected-item detail, funds after, search/sort |
| Full-width native dialog | Contained dialog card with head band |
| Sim Memory dossier and News | Modal L: known-Sims list + dossier cards; News with certainty chips |
| Pie menu | 6-8 fixed slots on an ellipse, icon + label, tooltip explains, "..." marks submenus |
| Talk subject strip | Talk sheet: subjects with "why", free text, tone, memory hint |
| Neighborhood side buttons | Labelled side rails (240 wide) with the map untouched in the middle |

## 4. How to build a new screen (recipe)
1. Write the job in one sentence. If it needs "and", make two screens.
2. Pick the container: dock tab, modal panel, dialog, sheet, toast, pie, balloon, tooltip (see Playbook board).
3. Pick a width (S/M/L) and lay out on the 8 px scale. Padding 28-36.
4. Head band: icon, Fredoka title, optional subtitle chip, close top-right.
5. Compose only from kit components. A new component needs every state drawn: default, hover, pressed, focus, disabled.
6. Add a single quirk (gloss, plumbob, bounce, Simlish empty state).
7. Draw states: empty, loading, error, long text, 150% scale.
8. Review in both themes. Export layout JSON and skins.

### Layout JSON (read by the C# loader)
```json
{
  "screen": "sim_memory",
  "theme": "inherit",
  "root": { "type": "panel", "skin": "panel_l", "size": [1360, 800], "anchor": "center" },
  "children": [
    { "type": "head", "icon": "book", "text": "$mem.title", "actions": ["news", "close"] },
    { "type": "tabs", "bind": "subjects" },
    { "type": "split", "cols": [340, "*"], "gap": 24, "children": ["list_known", "dossier"] }
  ]
}
```
Element types: `panel, head, tabs, split, stack, grid, button, iconButton, chip, need, meter, row, text, input, switch, segment, tile, toast, balloon, pieSlot, tooltip`. Numbers are logical px; `"*"` means flex. Text is a key or a `$binding`.

## 5. Implementation path (fits the injected-mod constraint)
1. Keep drawing with `UISpriteBatch`. Add `Ui.T` (current theme struct loaded from `design-tokens.json`) and `Ui.Px(v)` for scale.
2. Export 9-slice skins (list below) from the design at 1x, 1.5x, 2x. Tint with theme colours at draw time where possible so one skin serves both themes (white-based pills, grey-based panels).
3. Write one `NineSlice.Draw(sb, skin, rect, state, tint)` and one `LayoutLoader` that builds `UIContainer` trees from layout JSON.
4. Replace hand-coded rectangles in `SimMemoryMenu.cs` with loader screens; keep the data binding in C#.
5. Fonts: Fredoka (titles, buttons) and Montserrat (everything else), both OFL. Bake SpriteFonts at 16/18/20/22/28/40 px for each UIScale step.
6. Options and Live-tab buttons injected with Cecil should use the same components and the "Mod" chip so players can tell native from mod.

### 9-slice skins to export
`panel_s/m/l` (inset 28), `card` (20), `head_band` (12, tile X), `btn_pill` x default/primary/danger/quiet x normal/hover/press/disabled (28), `ibtn` blue/mint (24), `tile_frame` (26), `chip` neutral/good/warn/bad/info (12), `input` (18), `switch` track on/off + knob, `seg`/`tab` (22), `track` + `fill` (9, tile), `balloon` + tail (18), `pie_item` normal/on (24), `toast`/`tooltip` (16), `icons.png` 8x8 sheet at 48 px.

## 6. Colour quick reference
See `design-tokens.json`. Fills (`good/warn/bad`) colour bars and chips; text on a surface uses the `*Ink` variants, which pass 4.5:1 in both themes. Balloons use `simIdentity[n]` for the 4 px top bar and speaker name, always paired with the name text.

## 7. Known mock-up caveats
- Backgrounds in the mock-ups are your screenshots; item previews and prices are placeholders.
- Quirky Simlish strings are examples. Replace with your localisation keys.
- Fredoka is a proposal for the "voice" font. If you want a pure Simitone look, set `display` to Montserrat 700 and nothing else changes.
