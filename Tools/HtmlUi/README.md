# HtmlUi: HTML + CSS windows for Simitone (proof of concept)

A page is an `.html` file with a `<style>` block. `HtmlView` turns it into game UI:

| Step | Library | Notes |
|---|---|---|
| Parse HTML, cascade CSS | AngleSharp 1.11 + AngleSharp.Css 1.6 (MIT) | `:hover` is rewritten to a class we toggle |
| Flexbox layout | Yoga.Net 3.2.3 (MIT), **vendored in `Yoga/`** and retargeted to net461 | `YogaCompat.cs` shims `HashCode`, `BitOperations`, `BitConverter` bits |
| Paint | `IPainter` | `Conversations/Game/HtmlPanel.cs` paints with the game's `UISpriteBatch`; `HtmlUi.Preview` paints with GDI+ |

`HtmlPanel` (in `Tools/Conversations`) is a full-screen overlay `UIElement`: it feeds mouse and Esc in, calls `HtmlView.Hover/Click`, and re-reads the file every 0.5 s when it changes (hot reload).

## Try it

```
dotnet build Tools/Conversations -c Release -o Tools/Conversations/out
pwsh Tools/HtmlUi/Deploy-HtmlUi.ps1            # copies DLLs, adds binding redirects, copies samples to Content/ui
simtool convo htmlui memory.html               # show; `convo htmlui close` / `status`
```

Design without the game: `Tools/HtmlUi.Preview/out/HtmlUi.Preview.exe samples/memory.html out.png 1100 700 [hoverX hoverY]`.

## What the page can use today

- Elements: any tag; text runs wrap; `display:none`.
- Layout: `flex-direction`, `flex-wrap`, `flex-grow/shrink/basis`, `justify-content`, `align-items/self`, `gap`, `width/height/min-*` (px, %), `padding/margin/border-width`, `position:absolute` with `top/left/...`.
- Paint: `background-color`, uniform `border` + `border-radius` (radius is only drawn by the preview tool), `color`, `font-size`, `font-weight` (bold only in the preview), `line-height`.
- Interaction: `data-action="name"` on any element raises `HtmlView.Action`; `:hover` styles; `SetText(id, text)`.

## Not done yet

Images and 9-slice skins, scrolling, per-side borders, text alignment, bold font, rounded corners in game, data binding beyond `SetText`, replacing the real Sim Memory window (the sample page shows static demo data), and moving the installer step into `Install-Conversations.ps1`.

## Notes found while building it

- The game runs .NET Framework 4.6.1 with no binding redirects, and ships older `System.Memory`/`Buffers`/`Unsafe`/`Numerics.Vectors`/`ValueTuple`/`Text.Encoding.CodePages`. AngleSharp needs newer ones, hence the redirects.
- Referencing `System.Memory` makes `array.Reverse()` resolve to a span method under C# 14; `Boot.cs` now uses `Enumerable.Reverse`.
- AngleSharp's `ComputeCurrentStyle` demands a render device it does not find here; `GetStyleCollection(new DefaultRenderDevice{...}).ComputeDeclarations(element)` works.
