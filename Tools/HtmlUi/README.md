# HtmlUi: HTML + CSS windows for Simitone

A window is an `.html` template plus the shared `sims-ui.css`, drawn in the game from the design in `docs/ui-redesign`.

| Step | Library | Notes |
|---|---|---|
| Fill the template with data | `Template.cs` (ours, ~140 lines) | `{{name}}`, `<!--#each list-->`, `<!--#if x--><!--#else-->`, `{{.}}`, `{{#}}` |
| Design tokens | `Conversations/Game/ThemeTokens.cs` | `design-tokens.json` (theme night or day) becomes `var(--card)`, `var(--good-ink)`, `var(--r-card)` ... replaced in the text before parsing |
| Parse HTML, cascade CSS | AngleSharp 1.11 + AngleSharp.Css 1.6 (MIT) | `:hover` is rewritten to a class we toggle |
| Flexbox layout | Yoga.Net 3.2.3 (MIT), **vendored in `Yoga/`**, retargeted to net461 | `YogaCompat.cs` shims `HashCode`, `BitOperations`, `BitConverter` bits |
| Paint | `IPainter` | `Conversations/Game/HtmlSurface.cs` paints with the game's `UISpriteBatch` (rounded corners, gradients, borders, icons from `icons.png`); `HtmlUi.Preview` paints with GDI+ |

`HtmlSurface` shows one page on a `UIElement`: it rebuilds when the data changes, re-reads the files when they change on disk (hot reload, about 0.5 s), and reports `data-action` clicks. `SimMemoryMenu` uses it with data from `SimMemoryPageData`; `HtmlPanel` shows any page full screen (`simtool convo htmlui <page.html>`).

## Folders

- `ui/` the pages: `sims-ui.css`, `sim_memory.html`, `sim_memory.sample.json` (data for the preview and the linter).
- `docs/ui-redesign/` the guide, tokens and icon sheet (`skins/icons.png`).

## Try it

```
dotnet build Tools/Conversations -c Release -o Tools/Conversations/out
pwsh Tools/HtmlUi/Deploy-HtmlUi.ps1         # DLLs + binding redirects, pages, css, tokens, icons into the game folder
simtool convo memory-ui open                # the redesigned Sim Memory window; edit Content/ui/sim_memory.html while it is open
```

Design without the game:
```
dotnet build Tools/HtmlUi.Preview -c Release -o Tools/HtmlUi.Preview/out
Tools/HtmlUi.Preview/out/HtmlUi.Preview.exe Tools/HtmlUi/ui/sim_memory.html out.png --data Tools/HtmlUi/ui/sim_memory.sample.json --theme day --bg screenshot.png
```
Check a page against the redesign rules: `node .claude/skills/ui-redesign-check/check-ui.mjs Tools/HtmlUi/ui`.

## What a page can use

- Layout: flex only (`flex-direction`, `flex-wrap`, `flex-grow/shrink/basis`, `justify-content`, `align-items/self`, `gap`, `width/height/min-*` in px or %, `padding/margin/border-width`, `position:absolute` with `top/left/...`). Containers do not shrink by default; text does.
- Paint: `background-color`, two-colour `linear-gradient` (vertical, or 90deg horizontal), uniform `border` with `border-radius`, `color`, `font-size`, `line-height`, `text-transform:uppercase`, icons (`<span class="ic s24 i-book"></span>`, names from the tokens' icon order).
- Interaction: `data-action="name"` raises an action; `:hover` styles.
- Not drawn: grid, shadows, transitions, animation, bold, per-side borders, scrolling, images other than icons.

## Notes found while building it

- The game runs .NET Framework 4.6.1 with no binding redirects and ships older `System.Memory`, `Buffers`, `Unsafe`, `Numerics.Vectors`, `ValueTuple`, `Text.Encoding.CodePages`. AngleSharp needs newer ones, hence `Deploy-HtmlUi.ps1` swaps them in and adds redirects (originals are backed up).
- Referencing `System.Memory` makes `array.Reverse()` resolve to a span method under C# 14; `Boot.cs` uses `Enumerable.Reverse`.
- AngleSharp's `ComputeCurrentStyle` demands a render device it does not find here; `GetStyleCollection(new DefaultRenderDevice{...}).ComputeDeclarations(element)` works.
- The game blends with premultiplied alpha: colours and the icon sheet are converted before drawing.
- Yoga needs `flex-shrink: 0` on containers or wrapped chip rows get squeezed under their neighbours.
