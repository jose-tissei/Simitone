# AI Context: Simitone (fork: jose-tissei/Simitone)

> Machine-oriented project brief. Generated 2026-11-21 by the explain-architecture workflow.
> Diagram sources (readable as text): `docs/architecture/src/*.mmd`.
> Regenerate with the explain-architecture skill after major changes.

## Purpose
Windows frontend/re-implementation for The Sims 1 (Complete/Legacy Collection) built on FreeSO's engine; requires the user's own Sims 1 files. This fork adds display modes (2D / 3D with 2D list / 3D arch + 2D objects / Full 3D), an Options-menu mode switcher, a Ctrl+click per-object 2D/3D chooser, background-input test tooling, and CI.

## Stack
C#. Source on `master`: net9.0 (Simitone.Client net9.0, Simitone.Windows net9.0-windows), MonoGame, FreeSO libs (git submodule `FreeSO/`). **The running game used for all mod work is the prebuilt release v0.8.12 (Jan 2025, .NET Framework 4.x, MonoGame DirectX), which is binary-patched with Mono.Cecil; source and release differ.** Tools: .NET 9 SDK, Mono.Cecil 0.11.6, xunit 2.9, Vortice (Direct3D11) + Windows Graphics Capture (simtool). CI: GitHub Actions `windows-latest`. Docs/diagrams: Mermaid via mermaid-cli + Chrome.

## Commands
```bash
# game (release): extract SimitoneWindows.zip, run Simitone.Windows.exe   (patched build in C:\Users\<user>\.t3\projects\the-sims-run\app)
# launch flags (Program.cs): -ide -3d -aa -jit -dx|-dx11 -gl|-ogl -touch -nosound -lang<N> -hz<N> -path<dir>; patched build adds -2d (3D is on by default)
# source build:  dotnet build Client/Simitone/Simitone.sln     (not verified in this session)
# tools+tests:   pwsh Tools/ci.ps1 -App <pristine extracted release>     # builds every tool, 28 unit + 10 patch tests, smoke runs
# unit tests:    dotnet test Tools/SpriteCards.Tests
# e2e (local):   pwsh Tools/e2e/Run-E2E.ps1                              # 26 checks, drives real game via simtool, needs Sims files
# patch game:    dotnet Tools/SpriteCardsPatch/out/SpriteCardsPatch.dll <origAppDir> Tools/SpriteCards/out/SpriteCards.dll <outDir>   # ALWAYS from original DLLs
# hook (once):   git config core.hooksPath Tools/hooks                   # pre-commit runs unit tests
```

## Entry points
- `Client/Simitone/Simitone.Windows/Program.cs` — `Main`: locate Sims 1 (`GameLocator/WindowsLocator`: registry HKLM\SOFTWARE\Maxis\The Sims, else `C:\Program Files (x86)\Maxis\The Sims\`), parse flags, set `FSOEnvironment.UserDir = <Documents>/Simitone/`, `GameStartProxy.Start`.
- `Client/Simitone/Simitone.Client/SimitoneGame.cs`, `GameController.cs` (`EnterLoading`, `EnterGameMode(lotName)`, `EnterCAS`).
- Screens: `UI/Screens/{LoadingGameScreen,TS1GameScreen,TS1CASScreen}.cs`.

## Directory map
| Path | Role |
|---|---|
| `Client/Simitone/Simitone.Windows/` | launcher, game locator, start proxy |
| `Client/Simitone/Simitone.Client/UI/{Screens,Panels,Controls,Model}` | Simitone UI: lot control, options/main panel (`UIMainPanel`), button subpanels, catalog, CAS |
| `FreeSO/TSOClient/tso.simantics` (FSO.SimAntics) | VM: `VM`, `VMContext`, entities, primitives, JIT hooks |
| `FreeSO/TSOClient/tso.world` (FSO.LotView) | rendering: `World`, `WorldState`, `ObjectComponent`, `DGRPRenderer`, 2D/3D platforms, cameras |
| `FreeSO/TSOClient/tso.content` (FSO.Content) | asset providers; `TS1/` = TS1ObjectProvider, TS1NeighbourProvider, CustomUIProvider; root has `RCMeshProvider.cs` |
| `FreeSO/TSOClient/tso.files` (FSO.Files) | formats: IFF chunks (OBJD BHAV DGRP SPR2 FSOM FAMI NBRS HOUS ...), FAR1/FAR3, RC meshes (`DGRP3DMesh`) |
| `FreeSO/TSOClient/tso.common` (FSO.Common) | shared: `FSOEnvironment`, input framework (`GameScreen`, `UpdateState`), utils |
| `FreeSO/TSOClient/FSO.UI` | shared controls/dialogs, `GlobalSettings` (IniConfig -> `config.ini`), `UILayer` |
| `FreeSO/TSOClient/tso.vitaboy.*`, `tso.sound` (FSO.HIT) | avatars; audio |
| `FreeSO/TSOClient/FSO.IDE` | Volcanic object editor |
| `FreeSO/TSOClient/FSO.Server*`, `FSO.Common.*Service` | FreeSO online server stack; not referenced by Simitone projects |
| `Tools/SpriteCards` | `ModeLogic.cs` (pure mode table/lists, unit-tested) + `SpriteCards.cs` (game hook, net461) |
| `Tools/SpriteCardsPatch` | Cecil patcher: LotView draw entry points, `_2DWorldBatch.DrawImmediate`, Options menu, `UILotControl.ShowPieMenu` Ctrl+click, `Main` startup |
| `Tools/SimTestHook`, `Tools/InputHookPatch` | synthetic mouse/keyboard via named pipe `simitone-test`; rewrites `Mouse/Keyboard.GetState`, `Game.IsActive` |
| `Tools/SimTool` | CLI: launch/kill/status/shot (Windows Graphics Capture, window only)/click/ctrlclick/drag/key/wheel/ping/disable |
| `Tools/ButtonHoverPatch` | patches FSO.UI.dll default button hover colour |
| `Tools/SpriteCards.Tests`, `Tools/PatchTests`, `Tools/e2e`, `Tools/ci.ps1`, `Tools/hooks` | tests and automation |
| `.github/workflows/tools-ci.yml` | CI (release v0.8.12 pinned+cached, runs `Tools/ci.ps1`) |

## Data model
No database. Sims 1 files (inferred from `FSO.Files` chunk names; ERD: `src/04-erd.mmd`): `Neighborhood 1-* House 0..1 Family 1-* Sim`; `House 1-* PlacedObject *-1 ObjectKind(OBJD)`; `ObjectKind 1-1 Behavior(BHAV)`, `1-1 DrawGroup(DGRP) 1-* Sprite(SPR2)`, `DrawGroup 0..1 Mesh(FSOM)`. Mod data: `Documents/Simitone/{config.ini, spritecards.profile (2d|list|arch|full3d), spritecards.user.txt ("2d name"/"3d name"), UserData/, MeshCache/}`; game dir `Content/{spritecards.txt, MeshReplace/*.fsom, uigraphics/live/modes/opt_cam_*.png}`.

## API surface
No HTTP API. Local named-pipe protocol (`\\.\pipe\simitone-test`, per-user ACL, one text line per command, reply `ok ...` / `err ...`): `ping | enable | disable | mouse X Y | down|up l|r|m | wheel N | keydown|keyup <XnaKey>`. Hook auto-disables after 60 s idle. Game hook entry points called by patched code: `SpriteCards.{Apply, Label, Decorate, Menu2D, MenuList, MenuArch, MenuFull3D, MenuReload, CtrlClick, Startup}`.

## External services
| Service | Used for | Env vars (names only) |
|---|---|---|
| GitHub (releases, Actions) | download pinned release v0.8.12; CI | `GH_TOKEN` (CI, provided by Actions) |
| none at game runtime | Simitone is single-player/offline; FreeSO servers were shut down Dec 2024 and are unused | — |
| test-time paths | patch tests | `SIMITONE_APP`, `SPRITECARDS_DLL`, `SPRITECARDSPATCH` |

## Conventions observed
- FreeSO/Simitone namespaces `FSO.*` / `Simitone.Client.*`; disk folders `tso.*` build assemblies `FSO.*`.
- Settings via `GlobalSettings.Default` (IniConfig, `config.ini` in `FSOEnvironment.UserDir`).
- UI built from `UIContainer/UIElement`, dialogs via `UIScreen.GlobalShowDialog`; Simitone-styled dialog = `UIMobileAlert`.
- Mods never edit game source: Cecil patches of released DLLs, hook code in a separate assembly; pure logic kept in `ModeLogic.cs` (no game types) so it is unit-testable.
- Mode behaviour is defined in ONE table (`ModeLogic.Modes`, mirrored in `Tools/SpriteCards/README.md`); every object's render mode and `WorldState.ForceImmediate` are set explicitly every frame to prevent state leaking between modes.
- Build outputs (`bin/ obj/ out/`) are git-ignored per tool; commits end with `Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>`.
- Windows quirks that bit tests: PowerShell `diff` is an alias for `Compare-Object`; Windows PowerShell 5.1 misreads non-ASCII (e.g. "é" in `Zé`) in BOM-less scripts; use `\r?$` in regexes for CRLF files.

## Pre-existing agent instructions (digest)
None found in the repo (no CLAUDE.md / AGENTS.md / GEMINI.md / .cursorrules). Agent memory for this project lives outside the repo (`~/.claude/projects/...-the-sims/memory/simitone-setup.md`).

## Known sharp edges
- Source (net9, master) vs running release (Jan 2025): hooks target the release's method names; a new Simitone release likely needs patcher changes. `Tools/PatchTests` is the early warning.
- Patcher must run on ORIGINAL DLLs (second run on patched menu would duplicate entries; it is idempotent for most patches, but originals are the safe input). Backups: `the-sims-patch\backup\{pre-menu,pre-spritecards,pre-input-hook}`.
- `PrintWindow` returns blank frames for this game; use Windows Graphics Capture (`simtool shot`). Real mouse input is ignored unless focused: use the hook (`enable` keeps the game ignoring the real mouse until `disable`/60 s).
- First F12/menu action right after entering a lot can be ignored while the lot loads; queued in-game event dialogs (skunk, car pool) can block clicks.
- `Tools/e2e/Run-E2E.ps1` hard-codes 1920x1080 coordinates and the Newbie lot route (map click 1295,460 then Enter Lot 360,812).
- Not examined: `FreeSO/Other`, `Charvatia`, `Documentation`, `FSOInstaller`, server projects.
