# SimTool

Small Windows CLI so an agent (or you) can drive a running Simitone: launch it, screenshot it,
click in it. It only ever touches the Simitone window.

    dotnet build -c Release -o out
    out\simtool.exe launch [--3d]     start Simitone (2D or 3D), wait for its window
    out\simtool.exe status            running? client size
    out\simtool.exe shot out.png      capture ONLY the Simitone window (Windows Graphics Capture)
    out\simtool.exe click X Y         left click at client pixel X,Y (rclick for right)
    out\simtool.exe move X Y          hover; stays active until `disable` (or 60 s idle)
    out\simtool.exe drag X1 Y1 X2 Y2
    out\simtool.exe wheel N
    out\simtool.exe key NAME [ms]     keypress (enter, esc, space, a-z, 0-9, f1-f12, arrows)
    out\simtool.exe ping | disable    hook status / hand the real mouse back to the game
    out\simtool.exe wait SECONDS
    out\simtool.exe kill

## Notes
- `shot` uses Windows Graphics Capture, so it works while other windows cover the game.
  (`PrintWindow` returns a blank frame for this game; `shot` reports `BLANK` and exits 3 if a
  capture is a single colour.)
- **Background input (default):** `Tools/SimTestHook` + `Tools/InputHookPatch` patch the game so it
  accepts synthetic mouse/keyboard over a per-user named pipe and treats the window as active while
  a test runs. Nothing is focused and your real mouse/keyboard are not used. The hook is inert
  until `enable`, and switches itself off after 60 s without commands.
  Install: build both, run `InputHookPatch <appDir> SimTestHook.dll <outDir>`, copy `<outDir>\*.dll`
  and `SimTestHook.dll` into the app dir (game closed). Back up the original DLLs first.
- While a hover (`move`) is active the game ignores your real mouse; run `disable` when done.
- `--real` uses the older focus-based input instead:
- Simitone ignores posted window messages; it reads the real mouse and only when focused.
  `click`/`move`/`key` therefore briefly focus the game, check it is in front and that the point
  is on its window (otherwise they send nothing), send real input, then restore your previous
  window and cursor position. Expect the pointer to jump for a moment.
- Verified: launch, status, shot, `--real` click (play button advanced the game clock), hook hover
  while the game was in the background (button highlighted, foreground window unchanged).
  Not yet verified: hook click/drag/key on a screen that is safe to change, `--real` keys.
- `AppDir` in `Program.cs` points at the local Simitone install; edit it for your machine.
