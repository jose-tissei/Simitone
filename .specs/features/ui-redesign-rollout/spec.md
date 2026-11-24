# UI redesign rollout: AI components

Scope: Large (several screens, engine gaps). Specify phase only: this maps what has to change. Design and Tasks come after the order below is agreed.

Sources: `docs/ui-redesign/UI-GUIDE.md` (section 3 screen map), `Tools/HtmlUi/README.md`, the code under `Tools/Conversations`. Done already: **Sim Memory** (Dossier + News), commit `a42af63`.

## Inventory of AI-owned UI

| ID | Component | Where it lives today | How it is drawn | Guide screen | Status |
|---|---|---|---|---|---|
| UI-01 | Sim Memory dossier + News | `Game/SimMemoryMenu.cs`, `Logic/SimMemoryPageData.cs`, `ui/sim_memory.html` | HtmlSurface | Sim Memory modal L | **Done** |
| UI-02 | Tone dialog (3 lines to say) | `Game/ConvoPrimitive.cs` `ShowDialog` / `ShowChoice` | The engine's native Yes/No/Cancel `VMDialogInfo` (title, message text, 3 buttons) | Talk sheet (tone + lines) | To do |
| UI-03 | Talk subject dialog (3 AI subjects) | `Game/ConvoTalk.cs` line 170, same `ShowChoice` | Native dialog, subjects as button text | Talk sheet (subjects with "why", free text, tone, memory hint) | To do |
| UI-04 | Speech balloons (two lines over the Sims) | `Game/ConvoOverlay.cs` | Hand-drawn rectangles and text, follows the Sim each frame | Not in the guide's map; needs the tokens, 16 px text, tail | To do |
| UI-05 | Pie menu "Talk" / "Romance" bubbles | `Game/ConvoPieMenu.cs` (filter only), `Logic/PieMenuLogic.cs` | The game's native pie menu; we only change which entries show | Pie menu (6-8 slots on an ellipse, icon + label, "..." for submenus) | To do, blocked by engine (see gaps) |
| UI-06 | Memory tab in the Live panel and Sim switcher | `Boot.OpenLiveMemoryTab` (reflection into `MainPanel.Switcher`, tab 5) | Native tab rail | Labelled tab rail with unread badge on Memory | To do, native |
| UI-07 | Parallel-chat feedback (autonomous Sims talking) | `Game/ParallelSocialDriver.cs` | Reuses the balloons (UI-04) | Same as UI-04 | Follows UI-04 |
| UI-08 | Gossip news (what a Sim just heard) | `Game/GossipDirector.cs`, `Logic/Gossip/*` | No UI of its own: shows in News (UI-01) and balloons | Optional toast | Decision needed |
| UI-09 | AI settings (Laya on/off, tone/auto/menu mode, gossip, render options) | Only console commands in `Boot.cs` (`convo on|off|menu full|simple|parallel`) and `Render/Logic/RenderSettings.cs` | No in-game UI found | Options panel, group "Sims AI" (switches with On/Off) | To do, new screen |

Not AI-owned, and the guide says keep as-is: bottom-left dial and bar, needs bars (only a value readout is asked for), Buy/Build bar, neighbourhood side buttons. Out of this feature.

## Requirements

- **UIR-01** Every item above uses tokens from `design-tokens.json` only, passes `ui-redesign-check`, and renders in both themes in the preview tool.
- **UIR-02** Each converted screen has a real-game screenshot in `docs/ui-redesign/screenshots/` taken before it is called done.
- **UIR-03** A screen falls back to today's drawing if its page is missing or fails to parse (same pattern as `SimMemoryMenu.UseHtml`).
- **UIR-04** UI-02 and UI-03 keep the engine's blocking behaviour: the clock stops while the choice is open and the answer reaches `VMDialogResult` as 0, 1 or 2.
- **UIR-05** Hit targets are at least 48 px, essential text at least 16 px, one primary action, Esc closes the top modal.

## Engine gaps that decide the order

1. **Blocking dialogs (UI-02, UI-03).** Today the choice is the engine's `VMDialogInfo`. The native dialog window would have to be replaced or hidden and our page shown instead, while still writing the result into `VMDialogResult`. Needs a look at how the client turns `SignalDialog` into a window (not yet read). Free text and the "why" line from the guide are new features, not restyling.
2. **Balloons (UI-04).** HtmlSurface rebuilds on data or size change; a balloon moves every frame. Either place the surface at a new position without rebuilding (cheap, needs a position-only path) or paint the balloon directly through `IPainter` (rounded rect + tail + text). No shadow in the engine, so the tail is a plain shape.
3. **Pie menu (UI-05).** The pie menu is native and we only filter entries. A redesign means replacing the pie UI, which is outside the injected-mod seam so far. Cheapest honest step: restyle nothing, keep the filter, and add tooltips if the guide allows. Decide whether it is worth it.
4. **Native tab rail (UI-06).** Reached by reflection. Labels and a badge mean patching the native control; the Memory badge could instead be an HtmlSurface overlay near the rail.
5. **Font and gloss.** Still the game font, regular weight, no shadows (limits from the Sim Memory work). Affects every item. Fredoka/Montserrat SpriteFonts and bold would lift all of them at once.
6. **Theme setting.** The game always uses night. UI-09 is the natural place for the switch.
7. **Installer.** `Install-Conversations.ps1` does not run `Deploy-HtmlUi.ps1`, so a player installing the mod would not get any page.

## Suggested order (cheapest and most visible first)

1. Engine groundwork: installer step, theme setting plumbing, fonts and bold if feasible (gaps 5-7).
2. UI-04 balloons (also gives UI-07), small and always visible.
3. UI-09 Options "Sims AI" panel (new page, no engine risk, gives the theme switch).
4. UI-02 + UI-03 Talk sheet (highest value, riskiest: gap 1). Start with a spike on replacing the native dialog.
5. UI-06 tab rail badge.
6. UI-05 pie menu, only if gap 3 has a workable answer.
7. UI-08 toast, only if wanted.

## Decisions (2026-11-24)

- Talk sheet gets a **free-text field**. What the player types is sent to Laya to classify (which of the Talk intents or tones it is, and how it lands with the listener), and the reply comes from that classification. Needs a keyboard-capture path in the game and a Laya question set (UIR-06, to specify).
- **Pie menu: attempt the replacement** (UI-05). Spike first.
- Order: quick wins first. Done: UI-04 balloons, UI-09 Options "Sims AI" page (opened with `simtool convo options`; the Options button entry still needs the patch reinstall), theme setting.

## Open questions (answered above, kept for history)

- Is the Talk sheet's free-text field wanted? It is a new feature (needs a keyboard capture path), not only a restyle.
- Should gossip get a toast (UI-08), or is News enough?
- Pie menu: attempt the replacement, or leave native?
- Which first: the visible quick win (balloons, Options) or the high-value risky one (Talk sheet)?

## Not verified

Gap 1 and gap 3 are read from our own code only; I have not read how the game client builds the dialog window or the pie menu. UI-09: I found no in-game settings UI in `Conversations` or `Render`, only commands; if one exists elsewhere in the native game it is not mapped here.
