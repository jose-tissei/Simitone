# Menu flakiness: new menus stop working after a while

Size: Medium (one bug class, several files). Specify -> inline design -> implement + soak-verify.

## Report (user, 2026-11-26)
All the new menus are flaky: they work for a while, then disappear. The Options page opens some times, then stops.
The pie menu reverts to the original. The Memory window also stops opening.

## Requirements
- MENU-01: Options page opens every time its button is clicked, for the whole session.
- MENU-02: Memory window opens every time (Live tab, Options entry), for the whole session.
- MENU-03: The redesigned pie menu is shown for every pie, for the whole session.
- MENU-04: Talk sheet and tab badge keep working too (same mechanism).
- MENU-05: A soak test (repeated open/close, lot reload, speed and modal changes) proves it and is re-runnable.

## Suspects (to confirm or reject with evidence)
S1. `IsOpen` uses `instance.Parent != null`; if the game replaces `UIScreen.Current` while a page is open, the old parent stays set, IsOpen stays true, and Open is skipped forever.
S2. PieOverlay is not kept last among the screen's children; the game adds panels later and the overlay ends up drawn under the native pie.
S3. `Ensure()` runs only from the VM tick (paused / no lot = never re-attached).
S4. ConvoLoader.Look sets `looked = true` before loading; one failed load leaves `boot == null` for the whole session, and every call swallows the error.
S5. Exceptions swallowed in Draw/Update leave a menu half-open.

## Findings / status
(see below, updated as work proceeds)

## Root cause (confirmed in the running game, 2026-11-26)
FSO's `UIContainer.Remove` does not clear the child's `Parent` (the line is commented out in FreeSO/TSOClient/FSO.UI/Framework/UIContainer.cs).
Every page used `Parent != null` as "is open" and `Parent != screen` as "must be added", so after ONE close:
- Options: `IsOpen` stayed true, the Options button skipped `Open()`, and `Open()` skipped the re-add. Reproduced: `convo options diag` showed draws frozen while "open=True".
- Memory / Talk sheet: `Open()` skipped the re-add, so the page was "open" but never on screen.
- Pie menu and tab badge: both hide while `SimsOptionsMenu.IsOpen`, which was now stuck true, so the pie fell back to the native one and the badge vanished.
S1 = root cause. S2/S3/S5 rejected as the cause (pie and badge recovered with S1 fixed alone). S4 (ConvoLoader never retrying a failed load) was a real latent bug, fixed too.

## Fix
- Game/ModalHost.cs: `IsOn/Show/IsShown/Detach` ask the screen's own child list; used by Options, Memory, Talk sheet, HtmlPanel and the conversation balloons.
- ConvoLoader retries a failed load (up to 5 times) instead of caching the failure. (SpriteCards: takes effect with the next full build.)
- `convo options diag` (inCurrentScreen, visible, draws, updates) and a log line on every Memory open request.

## Verification
- Real clicks on the Options button, 5 cycles: shown, drawn (draws counter rising), Esc closes, reopens.
- `Tools\Soak-Menus.ps1`: 4 rounds of Memory tab (real Switcher path) + Options open/close, then the pie still drawn by the overlay ("new slots=1") and the badge back. Re-run it after any UI change.
- Not verified: a screen swap (lot -> neighbourhood -> lot) with a page open; the Memory tab via a real mouse click (the rail icon positions need a hit-test; the Select(5) path was exercised through the hook).
