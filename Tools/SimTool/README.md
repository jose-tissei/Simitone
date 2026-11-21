# SimTool

Small Windows CLI so an agent (or you) can drive a running Simitone: launch it, screenshot it,
click in it. It only ever touches the Simitone window.

    dotnet build -c Release -o out
    out\simtool.exe launch [--3d]     start Simitone (2D or 3D), wait for its window
    out\simtool.exe status            running? client size
    out\simtool.exe shot out.png      capture ONLY the Simitone window (Windows Graphics Capture)
    out\simtool.exe click X Y         left click at client pixel X,Y (rclick for right)
    out\simtool.exe move X Y          move the pointer (hover)
    out\simtool.exe key NAME [ms]     keypress (enter, esc, space, a-z, 0-9, f1-f12, arrows)
    out\simtool.exe wait SECONDS
    out\simtool.exe kill

## Notes
- `shot` uses Windows Graphics Capture, so it works while other windows cover the game.
  (`PrintWindow` returns a blank frame for this game; `shot` reports `BLANK` and exits 3 if a
  capture is a single colour.)
- Simitone ignores posted window messages; it reads the real mouse and only when focused.
  `click`/`move`/`key` therefore briefly focus the game, check it is in front and that the point
  is on its window (otherwise they send nothing), send real input, then restore your previous
  window and cursor position. Expect the pointer to jump for a moment.
- Verified: launch, status, shot, click (play button advanced the game clock). Not yet verified:
  `key` (arrow keys did not visibly scroll the camera).
- `AppDir` in `Program.cs` points at the local Simitone install; edit it for your machine.
