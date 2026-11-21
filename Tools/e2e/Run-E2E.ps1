# End-to-end test: drives the real, patched Simitone with simtool (background input, window-only capture).
# Needs a Sims 1 install and the patched game, so it cannot run in CI; run it before pushing:
#   pwsh Tools/e2e/Run-E2E.ps1 [-App <patched Simitone dir>] [-SimTool <simtool.exe>]
# It saves and restores your mode choice and Ctrl+click overrides. Assumes a 1920x1080 window and the Newbie family
# lot ("7 Sim Lane", reached from the neighborhood map). The game must not already be mid-dialog.
param(
    [string]$App = (Join-Path $env:USERPROFILE '.t3\projects\the-sims-run\app'),
    [string]$SimTool = (Join-Path $PSScriptRoot '..\SimTool\out\simtool.exe'),
    [string]$Docs = (Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'Simitone'),
    [string]$Shots = (Join-Path ([IO.Path]::GetTempPath()) 'simitone-e2e')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
New-Item -ItemType Directory -Force $Shots | Out-Null
$profileFile = Join-Path $Docs 'spritecards.profile'
$userFile = Join-Path $Docs 'spritecards.user.txt'
$logFile = Join-Path $App 'Content\spritecards.log'
$failures = New-Object System.Collections.Generic.List[string]
$passed = 0

function Sim { & $SimTool @args | Out-Null }
function Pass([string]$name) { $script:passed++; Write-Host "  PASS  $name" -ForegroundColor Green }
function Fail([string]$name, [string]$why) { $script:failures.Add("$name - $why"); Write-Host "  FAIL  $name - $why" -ForegroundColor Red }
function Check([string]$name, [bool]$ok, [string]$why = '') { if ($ok) { Pass $name } else { Fail $name $why } }
# file-based checks poll briefly: the game writes them from its own thread
function WaitFor([scriptblock]$cond, [int]$seconds = 8) { $end = (Get-Date).AddSeconds($seconds); while ((Get-Date) -lt $end) { if (& $cond) { return $true }; Start-Sleep -Milliseconds 300 }; return [bool](& $cond) }
function Tail([string]$p) { if (Test-Path $p) { ((Get-Content $p | Select-Object -Last 4) -join ' | ') } else { "(no file $p)" } }
function Profile { if (Test-Path $profileFile) { (Get-Content $profileFile -Raw).Trim() } else { '' } }

# fraction of sampled pixels that differ noticeably between two screenshots
function PixelDiff([string]$a, [string]$b) {
    $x = [Drawing.Bitmap]::FromFile($a); $y = [Drawing.Bitmap]::FromFile($b)
    $n = 0; $d = 0
    for ($j = 40; $j -lt 900; $j += 20) { for ($i = 20; $i -lt $x.Width - 20; $i += 20) {
        $p = $x.GetPixel($i, $j); $q = $y.GetPixel($i, $j); $n++
        if ([math]::Abs($p.R - $q.R) + [math]::Abs($p.G - $q.G) + [math]::Abs($p.B - $q.B) -gt 40) { $d++ } } }
    $x.Dispose(); $y.Dispose(); return $d / $n
}
# count bright-green pixels around a menu button (the "selected" frame colour)
function GreenCount([string]$path, [int]$cx) {
    $b = [Drawing.Bitmap]::FromFile($path); $n = 0
    for ($y = 945; $y -lt 1030; $y += 2) { for ($x = $cx - 45; $x -lt $cx + 45; $x += 2) { $p = $b.GetPixel($x, $y); if ($p.G -gt 190 -and $p.R -lt 110 -and $p.B -gt 90 -and $p.B -lt 200) { $n++ } } }
    $b.Dispose(); return $n
}
function Shot([string]$name) { $p = Join-Path $Shots "$name.png"; & $SimTool shot $p | Out-Null; if ($LASTEXITCODE -ne 0) { throw "screenshot '$name' failed or was blank" }; return $p }

# Options-menu buttons (client pixels, 1920x1080) and dialog buttons
$menuButton = @{ '2d' = 884; 'list' = 1004; 'arch' = 1124; 'full3d' = 1244; 'reload' = 1364 }
function OpenMenu { Sim click 277 970; Sim wait 2 }
function ChooseMode([string]$id, [int]$wait = 9) { OpenMenu; Sim click $menuButton[$id] 985; Sim wait $wait }

# ---- setup: back up user state, start from a known state ---------------------------------------------------
$backup = @{}
foreach ($f in $profileFile, $userFile) { $backup[$f] = if (Test-Path $f) { Get-Content $f -Raw } else { $null } }
Set-Content $profileFile 'list' -Encoding ascii
Remove-Item $userFile, $logFile -ErrorAction SilentlyContinue

try {
    Write-Host "`n== launch and enter the Newbie lot"
    Sim launch
    $st = & $SimTool status
    Check 'game launches and responds' ($st -match 'responding=True') $st
    Check 'window is 1920x1080 (coordinates assume it)' ($st -match 'client=1920x1080') $st
    Sim wait 8; Sim click 1295 460; Sim wait 2; Sim click 360 812; Sim wait 28
    $base = Shot 'lot-list-mode'
    Check 'lot is displayed (mode: 3D with 2D list)' ((PixelDiff $base $base) -eq 0) 'capture failed'
    Check 'starts in the saved mode' ((Profile) -eq 'list') (Profile)

    Write-Host "`n== Ctrl+click chooser"
    Sim ctrlclick 833 715; Sim wait 2
    $dlg = Shot 'dialog-open'
    Check 'Ctrl+click opens the chooser (screen changed)' ((PixelDiff $base $dlg) -gt 0.05) ("changed " + (PixelDiff $base $dlg))
    Check 'hook logged the object' (WaitFor { (Test-Path $logFile) -and ((Get-Content $logFile -Raw) -match 'object trashoutside') }) ('no object trashoutside in log; tail: ' + (Tail $logFile))
    Sim click 572 620; Sim wait 1.5                                   # Use 2D model
    Check 'Use 2D model is saved' ((Test-Path $userFile) -and ((Get-Content $userFile -Raw) -match '(?m)^2d trashoutside\r?$')) 'spritecards.user.txt lacks "2d trashoutside"'
    $after2d = Shot 'after-use-2d'
    Check 'dialog closed after choosing' ((PixelDiff $dlg $after2d) -gt 0.05) 'screen unchanged'
    Sim ctrlclick 833 715; Sim wait 2; Sim click 1048 620; Sim wait 1.5   # Use 3D model (revert)
    Check 'Use 3D model reverts the override' ((Get-Content $userFile -Raw) -match '(?m)^3d trashoutside\r?$') 'spritecards.user.txt lacks "3d trashoutside"'
    Sim ctrlclick 833 715; Sim wait 2; Sim click 1436 620; Sim wait 1.5   # Cancel changes nothing
    Check 'Cancel changes nothing' ((Get-Content $userFile -Raw) -match '(?m)^3d trashoutside\r?$') 'override changed on Cancel'

    Write-Host "`n== modes"
    $shotsByMode = @{}
    foreach ($m in '2d', 'arch', 'full3d', 'list') {
        ChooseMode $m 10
        Check "menu switches to '$m' and remembers it" ((Profile) -eq $m) ("profile is '" + (Profile) + "'")
        $shotsByMode[$m] = Shot "mode-$m"
        $s = & $SimTool status
        Check "game still responding in '$m'" ($s -match 'responding=True') $s
    }
    Check 'Full 3D looks different from 3D with 2D list (free camera)' ((PixelDiff $shotsByMode['full3d'] $shotsByMode['list']) -gt 0.2) ("PixelDiff " + (PixelDiff $shotsByMode['full3d'] $shotsByMode['list']))
    Check '2D mode looks different from 3D arch + 2D objects' ((PixelDiff $shotsByMode['2d'] $shotsByMode['arch']) -gt 0.005) ("PixelDiff " + (PixelDiff $shotsByMode['2d'] $shotsByMode['arch']))

    Write-Host "`n== the active mode's button is highlighted green, live, after any route"
    foreach ($m in 'full3d', 'arch', '2d', 'list') {
        ChooseMode $m 10
        $p = Shot "highlight-$m"
        $counts = @{}; foreach ($k in $menuButton.Keys) { $counts[$k] = GreenCount $p $menuButton[$k] }
        $others = ($counts.Keys | Where-Object { $_ -ne $m } | ForEach-Object { $counts[$_] } | Measure-Object -Maximum).Maximum
        Check "'$m' is highlighted and the others are not (active $($counts[$m]) px, others <= $others)" (($counts[$m] -gt 150) -and ($others -lt 40)) ($counts.GetEnumerator() | ForEach-Object { "$($_.Key)=$($_.Value)" }) -join ' '
    }
    Write-Host "`n== reload"
    OpenMenu; Sim click $menuButton['reload'] 985; Sim wait 10
    Check 'Reload list keeps the game responding' ((& $SimTool status) -match 'responding=True') ''
    Check 'Reload list keeps the mode' ((Profile) -eq 'list') (Profile)
}
finally {
    & $SimTool disable | Out-Null
    foreach ($f in $backup.Keys) { if ($null -ne $backup[$f]) { Set-Content $f $backup[$f] -NoNewline -Encoding ascii } else { Remove-Item $f -ErrorAction SilentlyContinue } }
}

Write-Host ""
if ($failures.Count -gt 0) { Write-Host "$($failures.Count) failed, $passed passed. Screenshots: $Shots" -ForegroundColor Red; $failures | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }; exit 1 }
Write-Host "All $passed end-to-end checks passed. Screenshots: $Shots" -ForegroundColor Green
