# Developer loop: stop the game, install the current build, start it, enter the Newbie lot, freeze the simulation at the given hour.
#   pwsh Tools/Render/Dev-Reload.ps1 [-Hour 22]
# The simulation is paused afterwards, so two screenshots of the same state are pixel-identical (noise floor 0).
# The game saves into the player's own save folder while it runs, so this script keeps a copy of that folder the first time it runs
# (Documents/Simitone/UserData-dev-backup) and puts it back with -RestoreSave.
param([int]$Hour = 22, [switch]$RestoreSave)
$ErrorActionPreference = 'Stop'
$docs = Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'Simitone'
$saveBackup = Join-Path $docs 'UserData-dev-backup'
if ($RestoreSave) {
    Stop-Process -Name Simitone.Windows -Force -ErrorAction SilentlyContinue; Start-Sleep 2
    if (-not (Test-Path $saveBackup)) { throw "no backup at $saveBackup" }
    Copy-Item (Join-Path $saveBackup '*') (Join-Path $docs 'UserData') -Recurse -Force
    Write-Host "saved game restored from $saveBackup"; return
}
if (-not (Test-Path $saveBackup) -and (Test-Path (Join-Path $docs 'UserData'))) { Copy-Item (Join-Path $docs 'UserData') $saveBackup -Recurse; Write-Host "saved game copied to $saveBackup" }
$root = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$st = Join-Path $root 'Tools\SimTool\out\simtool.exe'
Stop-Process -Name Simitone.Windows -Force -ErrorAction SilentlyContinue; Start-Sleep 2
& pwsh -File (Join-Path $root 'Tools\Install-Conversations.ps1') -Pristine (Join-Path $env:USERPROFILE '.t3\projects\the-sims-ci-release\app') -App (Join-Path $env:USERPROFILE '.t3\projects\the-sims-run\app') -WithSimTestHook | Select-Object -Last 1
& $st launch | Out-Null; & $st wait 8; & $st click 1295 460 | Out-Null; & $st wait 2; & $st click 360 812 | Out-Null; & $st wait 30
& $st time $Hour 0 | Out-Null; & $st speed 1 | Out-Null; & $st wait 6; & $st speed 0 | Out-Null; & $st wait 3
Write-Host "ready at hour $Hour"
