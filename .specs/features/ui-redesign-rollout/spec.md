# UI redesign rollout: AI components

Scope: Large (several screens, engine gaps). Specify phase only: this maps what has to change. Design and Tasks come after the order below is agreed.

Sources: `docs/ui-redesign/UI-GUIDE.md` (section 3 screen map), `Tools/HtmlUi/README.md`, the code under `Tools/Conversations`. Done already: **Sim Memory** (Dossier + News), commit `a42af63`.

## Inventory of AI-owned UI

| ID | Component | Where it lives today | How it is drawn | Guide screen | Status |
|---|---|---|---|---|---|
| UI-01 | Sim Memory dossier + News | `Game/SimMemoryMenu.cs`, `Logic/SimMemoryPageData.cs`, `ui/sim_memory.html` | HtmlSurface | Sim Memory modal L | **Done** |
| UI-02 | Tone dialog (3 lines to say) | `Game/TalkSheetMenu.cs` (lines variant), `ConvoPrimitive.ShowSheet` | HtmlSurface sheet; answers the VM's dialog with `VMNetDialogResponseCmd` | Talk sheet (tone + lines) | **Done** (2026-11-26), native dialog is the fallback |
| UI-03 | Talk subject dialog (3 AI subjects) | `Game/TalkSheetMenu.cs`, `Logic/TalkSheetPageData.cs`, `ui/talk_sheet.html` | HtmlSurface sheet: subjects with "why", free text, tone with advice, memory hint, keys 1-3 / Enter / Esc | Talk sheet | **Done** (2026-11-26); typed words become the line, Laya picks the row they are about |
| UI-04 | Speech balloons (two lines over the Sims) | `Game/ConvoOverlay.cs` | HtmlSurface painter, tokens, tail | Not in the guide's map; needs the tokens, 16 px text, tail | **Done** (96a7faf) |
| UI-05 | Pie menu "Talk" / "Romance" bubbles | `Game/ConvoPieMenu.cs` (filter only), `Logic/PieMenuLogic.cs` | The game's native pie menu; we only change which entries show | Pie menu (6-8 slots on an ellipse, icon + label, "..." for submenus) | **Done** as an overlay (orbit, border/lip/shadow, hover, tooltip for ~13 verbs, submenu, back) |
| UI-06 | Memory tab in the Live panel and Sim switcher | `Boot.OpenLiveMemoryTab` (reflection into `MainPanel.Switcher`, tab 5) | Native tab rail | Labelled tab rail with unread badge on Memory | **Badge done** (2026-11-26); rail labels still native |
| UI-07 | Parallel-chat feedback (autonomous Sims talking) | `Game/ParallelSocialDriver.cs` | Reuses the balloons (UI-04) | Same as UI-04 | **Done** with UI-04 |
| UI-08 | Gossip news (what a Sim just heard) | `Game/GossipDirector.cs`, `Logic/Gossip/*` | No UI of its own: shows in News (UI-01) and balloons | Optional toast | Decision needed |
| UI-09 | AI settings (Laya on/off, tone/auto/menu mode, gossip, render options) | Only console commands in `Boot.cs` (`convo on|off|menu full|simple|parallel`) and `Render/Logic/RenderSettings.cs` | No in-game UI found | Options panel, group "Sims AI" (switches with On/Off) | **Done**: full page (Game, View tiles, Graphics, Sims AI), opened by the Options button (0342d18) |

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

## Spike results (2026-11-24, from the decompiled client: `the-sims-spike/decomp`)

**Dialogs (gap 1) are replaceable, no new IL patch needed.** `UILotControl.vm_OnDialog` turns our `VMDialogInfo` (from `ConvoPrimitive.ShowChoice`) into a native `UIAlert` and answers with `DialogResponse(code)`, which only does this: `vm.SendCommand(new VMNetDialogResponseCmd { ActorUID = ActiveEntity.PersistID, ResponseCode = code, ResponseText = ... })`, then restores the speed from 0 to 1. So the Talk sheet can be our own `UIElement` (HtmlSurface) that sends that same command itself, exactly as the client's own `HouseSelected` does for the neighbourhood picker. Plan: `ShowChoice` stops calling `SignalDialog` for Talk, opens the sheet, keeps the thread's `VMDialogResult` blocking state, and the sheet sends `VMNetDialogResponseCmd` (0, 1, 2 for the three tones/subjects). Free text: the sheet captures the keyboard, the text goes to Laya on a worker thread (never blocks a frame), Laya classifies it into the nearest subject or tone, and the sheet answers with that code; the typed words themselves are kept in a static for the balloon line. To verify in the game: the speed handling (`SpeedMultiplier` 0 / -2 for TS1 blocking) when we skip the native alert.

**Pie menu (UI-05) is replaceable as an overlay, again with no IL patch.** The native `UIPieMenu` is built in `UILotControl` (line ~623) from the `VMPieMenuInteraction` list. Choosing an item only does `VMNetInteractionCmd { Interaction = item.ID, ActorUID, CalleeID, Param0, Global }` (or `VMNetGotoCmd` when the target is the goto object), plays a sound and calls `ClosePie()`. Plan: our overlay notices that `UILotControl.PieMenu` appeared (reflection), hides it, draws the redesigned pie from its `m_CurrentItem.Children` (icon + label, 6-8 slots on an ellipse, "..." for categories) and on a click calls the native `PieButtonClick` with the matching button, so categories, goto, sounds and closing stay native. Risk: reflection into a private field and the head-scene render; both are inside the mod, so a failure falls back to the native pie.

**Remaining first steps:** (a) Talk sheet page and sheet class, (b) Laya question set for free text, (c) pie overlay, (d) Options button entry (patch reinstall).

## Open questions (answered above, kept for history)

- Is the Talk sheet's free-text field wanted? It is a new feature (needs a keyboard capture path), not only a restyle.
- Should gossip get a toast (UI-08), or is News enough?
- Pie menu: attempt the replacement, or leave native?
- Which first: the visible quick win (balloons, Options) or the high-value risky one (Talk sheet)?

## Not verified

Gap 1 and gap 3 are read from our own code only; I have not read how the game client builds the dialog window or the pie menu. UI-09: I found no in-game settings UI in `Conversations` or `Render`, only commands; if one exists elsewhere in the native game it is not mapped here.

## Status (2026-11-25)

**Done and verified in the running game (screenshots in `docs/ui-redesign/screenshots/`):** UI-01 Sim Memory (now household-only "Memory of", Known Sims search, News filter and "Show more"), UI-04 balloons, UI-07, UI-09 full Options page reached from the game's Options button, UI-05 pie menu, theme setting (night/day), engine-wide smoothing (anti-aliased rounded corners, rings, tails, round dots, area-averaged icons, 8x multisampled Sim head, rounded gradient headers) with a unit test that new UI never draws shapes by hand. `ui-redesign-check`: 2 pages, 0 errors.

**Not done:**
- ~~UI-02 and UI-03 Talk sheet~~: done 2026-11-26 (see Status below).
- UI-06 tab rail badge/labels: native, untouched.
- UI-08 gossip toast: decision pending; News covers it for now.
- Guide screens never started: Buy/Build flyout, Hood side rails, HUD overlays (needs value readout, tooltips), contained dialog card for native dialogs.
- Gap 5 fonts/bold: no Fredoka/Montserrat, regular weight; no shadows/gloss on cards, buttons, chips (only the pie has depth). This is why the in-game pages look flatter than the mock-ups.
- Gap 7 installer does not run Deploy-HtmlUi (done by hand or by `Tools/Dev-Iterate.ps1`).

**Not verified:** day theme in the game after the smoothing work (preview only); the new Options buttons Save / Neighborhood / Quit / anti-aliasing / lighting were not clicked in the game; page is 960x800 while the design differs; tooltips cover ~13 pie verbs only; round avatar disc in the pie is missing; UIR-02 screenshots exist for Memory, Options, balloons and pie only.

**Merge state:** all of the above is on `fork/master` (1f93abb). The Laya branch (`laya-autonomous-sim-social`) does not contain it yet; the modded install (`the-sims-play`) runs an older merged build.
## Talk sheet (2026-11-26)

Built and checked in the running game (house 5, `simtool interact` + `convo talksheet`): `Game/TalkSheetMenu.cs`, `Logic/TalkSheetPageData.cs` (16 unit tests), `ui/talk_sheet.html`, screenshots `talk-sheet-night.png` and `talk-sheet-lines-night.png`.
- The simulation stays blocked on the thread's `VMDialogResult` exactly as with the native dialog; the sheet answers with the same `VMNetDialogResponseCmd` (code = row 0..2, `ResponseText` = `<tone>|<words>`). `ConvoPrimitive.ShowSheet` falls back to the native `ShowChoice` when the page is missing (UIR-03). `autodialog` in the test hook leaves the sheet alone.
- Talk: three subject rows with a reason chip (what the speaker knows of the listener's taste, or "New topic"), a free-text field, tone (Gentle/Playful/Bold) with "works well" advice from SocialMemory, a memory hint with "Open memory", keys 1-3 / Enter / Esc. Verified: pick row 2 + tone Bold + own words -> `decision: talk about <row 2> ... "<own words>"`; Esc ("Never mind") closes it, the clock resumes, no conversation is recorded and the Sim is idle.
- Three lines (Hug etc.): same sheet without tone, text or memory; picking a row answers at once. Verified: picking line 2 -> `decision: hug Playful ... "<line 2>"`.
- Free text: Laya (`ScoreChoices`) reads which offered topic the words are about (kept row if no clear match, 0.5); the words are what the Sim says. Not verified in the game: Laya reassigning the row (offline classification only exercised through the unit-level logic), keyboard typing with a real keyboard (tested through the hook's text path), day theme of the sheet beyond one screenshot, and the sheet under the pie / other modals.
## Tab rail badge (2026-11-26)

`Game/TabBadge.cs` (+ `Logic/TabBadgeLogic.cs`, 7 tests): an overlay kept on top of the screen finds the Memory button through the game's `UICategorySwitcher` (CatSwitchButtons / MainButton, by reflection) and draws the count of News stories of the Sim being played that the News tab has not shown yet: on the Memory button in the open rail, on the rail's main button otherwise. Checked in the game: "8" over the Memory tab; after the News tab showed the stories, `convo badge` -> "no unread". `simtool convo badge` reports what it is doing. Not done: the rail's text labels (native control); "seen" counts live in memory only, so every session starts with all stories unread.
## Pie and badge follow-ups (2026-11-26)

- Pie: tooltips now cover about 40 verbs (Sim-to-Sim actions and common object verbs), unit-tested; the head at the center has the design's white + mint ring (the head itself is the game's render, so the pink letter disc of the mock-up is not reproduced). Checked in the game (screenshot `pie-menu-night.png`).
- Badge: hidden when the Live panel is closed or a modal is open (it used to float alone over the empty rail position).
## Hood rails, HUD, Buy/Build: spike findings (2026-11-26, not built)

Checked against the design boards (`Hood`, `Hud`, `Buy`) and the running game; nothing here is implemented.
- **Hood side rails (labels, lot card):** the native rails pack icons 110 px apart (95 px icons), so a 16 px label under each does not fit without moving native controls. Options: labels shown on hover beside the icon (cheap, overlay, needs the mouse position and the fixed button rects), or replace the rails with our own buttons that call the same native handlers (bigger, riskier). The lot card (name, Sims, funds, Play) maps to the native house-selection popup, which would need the same replace-and-forward approach as the pie.
- **HUD (value readout on needs bars, tooltips on every control):** the bars are native art; a value readout is an overlay reading `VMAvatar.GetMotiveData` and the bar rectangles. The rectangles are not exposed in a stable way (fixed art positions per resolution); needs a look at the needs panel controls before estimating.
- **Buy/Build flyout (cards with price + stat chips, funds after, search/sort):** a new window fed from the object catalog, with the native bar kept. Largest of the three: needs the catalog data path, item selection hand-off to the native placement code, and search/sort. Spike first.
- **Contained dialog card for the native dialogs:** only the mod's own dialogs are converted. The rest (`UIAlert`: help hints, event pop-ups, save prompts) is the game's and would be a skin of that one control.

## Merge state (2026-11-26)

- `fork/master` has everything above (Talk sheet, tab badge, depth/bold, pie ring and tooltips, day-theme fix, test tooling, Save-Guard).
- The Laya branch's own worktree was not touched. The merged build (Laya branch + master) is commit `021274a`, pushed as branch `merged-laya-and-ui` on the fork, and installed in `the-sims-play` ("Simitone (Modded)"): 459 unit tests pass, the new Options page opened from the real Options button there. The Talk sheet, badge and day theme were verified in `the-sims-run` (master), not in the modded install (it has no test hook, and Bob is away at work at night in that save, so there is no second Sim to talk to).
- Conflicts resolved by hand: `Boot.cs` (the law-enforcement drivers' tick list plus `TabBadge.Ensure()`), `ConvoPieMenu.cs` (the career pie menus plus `MarkAsked`).