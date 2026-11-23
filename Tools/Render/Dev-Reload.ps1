# Developer loop: stop the game, install the current build, start it, enter the Newbie lot, freeze the simulation at the given hour.
#   pwsh Tools/Render/Dev-Reload.ps1 [-Hour 22]
# The simulation is paused afterwards, so two screenshots of the same state are pixel-identical (noise floor 0).
param([int]$Hour = 22)
$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$st = Join-Path $root 'Tools\SimTool\out\simtool.exe'
Stop-Process -Name Simitone.Windows -Force -ErrorAction SilentlyContinue; Start-Sleep 2
& pwsh -File (Join-Path $root 'Tools\Install-Conversations.ps1') -Pristine (Join-Path $env:USERPROFILE '.t3\projects\the-sims-ci-release\app') -App (Join-Path $env:USERPROFILE '.t3\projects\the-sims-run\app') -WithSimTestHook | Select-Object -Last 1
& $st launch | Out-Null; & $st wait 8; & $st click 1295 460 | Out-Null; & $st wait 2; & $st click 360 812 | Out-Null; & $st wait 30
& $st time $Hour 0 | Out-Null; & $st speed 1 | Out-Null; & $st wait 6; & $st speed 0 | Out-Null; & $st wait 3
Write-Host "ready at hour $Hour"
