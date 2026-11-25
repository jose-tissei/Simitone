# One command from "I changed the mod" to "the game is running a lot, ready to test".
#   pwsh Tools\Dev-Iterate.ps1 -House 5 -Speed 3
# - reinstalls the mod + test hook only when a source file is newer than the installed Conversations.dll (-Force to always)
# - boots through `simtool boot`, which waits on what the game reports (neighbourhood up, lot loaded, Sims present) instead of fixed sleeps,
#   answers the game's help/event dialogs by itself (a dialog the simulation waits on freezes everything) and keeps the speed
# Then use: simtool state | look [png] | log [regex] | logwait <sec> <regex> | until <sec> <regex> <command> | interact A B Talk [auto]
param([int]$House = 5, [int]$Speed = 1, [switch]$Force, [switch]$NoBoot)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$app = 'C:\Users\Zé\.t3\projects\the-sims-run\app'
$tool = Join-Path $repo 'Tools\SimTool\out\simtool.exe'

$dll = Join-Path $app 'Content\.dev-iterate-stamp'   # written after each install (the installer leaves identical files untouched, so their dates say nothing)
$newest = Get-ChildItem (Join-Path $repo 'Tools\Conversations'), (Join-Path $repo 'Tools\SimTestHook'), (Join-Path $repo 'Tools\HtmlUi') -Recurse -Include *.cs, *.html, *.css, *.json |
    Where-Object { $_.FullName -notmatch '\\(obj|bin|out)\\' } | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if ($Force -or -not (Test-Path $dll) -or $newest.LastWriteTime -gt (Get-Item $dll).LastWriteTime) {
    Write-Host "sources changed ($($newest.Name)): reinstalling"
    & $tool kill 2>$null
    & pwsh -NoProfile -File (Join-Path $repo 'Tools\Install-Conversations.ps1') -Pristine 'C:\Users\Zé\.t3\projects\the-sims-ci-release\app' -App $app -WithSimTestHook 2>&1 | Select-String 'error CS|Installed|rror\(s\)' | Select-Object -Last 3
    & pwsh -NoProfile -File (Join-Path $repo 'Tools\HtmlUi\Deploy-HtmlUi.ps1') 2>&1 | Select-Object -Last 1
    New-Item -ItemType File -Force $dll | Out-Null; (Get-Item $dll).LastWriteTime = Get-Date
    # the installer rebuilds this tracked binary; it is not part of the change
    git -C $repo checkout -- Tools/Render/out/Render.dll 2>$null
} else { Write-Host "installed build is current" }
if (-not $NoBoot) { & $tool boot $House $Speed }
