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
# fraction of pixels that differ between two screenshots, and the mean signed brightness change (b - a)
function ImageStats([string]$a, [string]$b) {
    $x = [Drawing.Bitmap]::FromFile($a); $y = [Drawing.Bitmap]::FromFile($b)
    $rect = New-Object Drawing.Rectangle 0, 0, $x.Width, $x.Height
    $fmt = [Drawing.Imaging.PixelFormat]::Format32bppArgb
    $dx = $x.LockBits($rect, 'ReadOnly', $fmt); $dy = $y.LockBits($rect, 'ReadOnly', $fmt)
    $n = $x.Width * $x.Height * 4; $bx = New-Object byte[] $n; $by = New-Object byte[] $n
    [Runtime.InteropServices.Marshal]::Copy($dx.Scan0, $bx, 0, $n); [Runtime.InteropServices.Marshal]::Copy($dy.Scan0, $by, 0, $n)
    $x.UnlockBits($dx); $y.UnlockBits($dy); $x.Dispose(); $y.Dispose()
    $changed = 0; $sum = 0L
    for ($i = 0; $i -lt $n; $i += 4) {
        $d = ($by[$i] - $bx[$i]) + ($by[$i + 1] - $bx[$i + 1]) + ($by[$i + 2] - $bx[$i + 2])
        if ($d -ne 0 -or $by[$i] -ne $bx[$i] -or $by[$i + 1] -ne $bx[$i + 1] -or $by[$i + 2] -ne $bx[$i + 2]) { $changed++ }
        $sum += $d
    }
    return [pscustomobject]@{ Changed = $changed / ($n / 4); MeanSigned = $sum / ($n / 4) / 3 }
}
function Shot([string]$name) { $p = Join-Path $Shots "$name.png"; & $SimTool shot $p | Out-Null; if ($LASTEXITCODE -ne 0) { throw "screenshot '$name' failed or was blank" }; return $p }

# Options-menu buttons (client pixels, 1920x1080) and dialog buttons
$menuButton = @{ '2d' = 884; 'list' = 1004; 'arch' = 1124; 'full3d' = 1244; 'reload' = 1364 }
function OpenMenu { Sim click 277 970; Sim wait 2 }
function ChooseMode([string]$id, [int]$wait = 9) { OpenMenu; Sim click $menuButton[$id] 985; Sim wait $wait }

# ---- setup: back up user state, start from a known state ---------------------------------------------------
$backup = @{}
$configFile = Join-Path $Docs 'config.ini'            # the game saves the graphics mode here when a mode is chosen in the menu
$renderFile = Join-Path $Docs 'render.profile'
foreach ($f in $profileFile, $userFile, $configFile, $renderFile) { $backup[$f] = if (Test-Path $f) { Get-Content $f -Raw } else { $null } }
Set-Content $profileFile 'list' -Encoding ascii
if (Test-Path $configFile) { (Get-Content $configFile -Raw) -replace 'GlobalGraphicsMode=\d', 'GlobalGraphicsMode=1' | Set-Content $configFile -NoNewline -Encoding ascii }
Set-Content $renderFile 'aa=off' -Encoding ascii
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

    # ---- AI conversations (only when Conversations.dll is installed and the lot has two Sims) ------------------------------
    $convoStatus = (& $SimTool convo status 2>&1) -join ' '
    if ($convoStatus -notmatch 'enabled=') { Write-Host "`n== AI conversations: not installed, skipped" -ForegroundColor Yellow }
    else {
        Write-Host "`n== AI conversations"
        function Convo { (& $SimTool convo @args 2>&1) -join ' ' }
        function WaitDialog([int]$seconds = 60) { WaitFor { (& $SimTool dialog 2>&1) -join ' ' -match 'waiting' } $seconds }
        function LastLine { $l = Convo 'last'; $l }
        # the Options panel pauses the game while it is open, so start the conversation checks from a fresh game
        Sim kill; Start-Sleep 3
        Sim launch; Sim wait 8; Sim click 1295 460; Sim wait 2; Sim click 360 812; Sim wait 28
        Sim speed 1
        Convo 'on' | Out-Null   # an earlier interrupted run may have left the feature switched off
        $convoStatus = Convo 'status'
        Check 'the conversation step is installed in the game scripts' ($convoStatus -match 'installed=[1-9]') $convoStatus
        Check "Laya was started by the game and answers (waited up to 90 s)" (WaitFor { (Convo 'status') -match 'laya=up' } 90) (Convo 'status')
        if (Test-Path (Join-Path $App 'laya\python\python.exe')) {
            $layaLog = (Get-Content (Join-Path $App 'Content\conversations.log') -Tail 40) -join ' '
            Check 'the game started the Laya bundled with it (no system Python needed)' ($layaLog -match 'laya: ready on port \d+ via .*laya\\python\\python\.exe') ($layaLog.Substring([Math]::Max(0, $layaLog.Length - 300)))
        }
        $sims = (& $SimTool sims) -join ' '
        Check 'the lot has Bob and Betty' (($sims -match 'Bob') -and ($sims -match 'Betty')) $sims
        Convo 'tone' 'off' | Out-Null; Convo 'force' 'off' | Out-Null

        # a Sim the player is not controlling: no dialog, the Sim picks a tone from their own personality, the step waits for
        # Laya (nothing has classified Betty yet) and the balloons still appear. The test aid makes every Sim count as not
        # player-controlled, because the game refuses to queue interactions for a Sim other than the active one.
        Convo 'auto' 'on' | Out-Null
        Sim interact Bob Betty 'Hug.../Friendly'
        Check 'an autonomous Hug is decided without asking the player' (WaitFor { (LastLine) -match '^hug \w+ .*\(Bob -> Betty\)' } 60) (LastLine)
        Check 'no dialog was opened for the autonomous Hug' ((& $SimTool dialog) -join ' ' -match 'dialog=none') ((& $SimTool dialog) -join ' ')
        $line = LastLine
        Check 'the autonomous Hug already knew the receiver''s temperament (the step waited for Laya)' (($line -notmatch 'temperament=Unknown') -and ($line -match 'cached=True')) $line
        Check 'the autonomous balloons are placed over the Sims' ((Convo 'status') -match 'balloons=over-heads') (Convo 'status')
        Sim wait 40
        Sim interact Bob Betty 'Talk.../About Interests'
        Check 'an autonomous Talk picks its own subject and is decided without a dialog' (WaitFor { (LastLine) -match '^talk about ' } 90) (LastLine)
        Check 'no dialog was opened for the autonomous Talk' ((& $SimTool dialog) -join ' ' -match 'dialog=none') ((& $SimTool dialog) -join ' ')
        Convo 'auto' 'off' | Out-Null
        Sim wait 40

        # a hug: the three-line dialog appears, the chosen tone is applied and the lines are shown
        Sim interact Bob Betty 'Hug.../Friendly'
        Check 'a Hug opens the three-line choice dialog' (WaitDialog 70) ((& $SimTool dialog) -join ' ')
        $dlg = Shot 'convo-dialog'
        Sim click 960 664                                   # Playful
        Sim wait 4
        $line = LastLine
        Check 'the Playful tone was applied to the Hug' ($line -match '^hug Playful') $line
        Check 'the result is at most one step from the game''s own outcome' (($line -match 'script=(\d) final=(\d)') -and ([math]::Abs([int]$Matches[1] - [int]$Matches[2]) -le 1)) $line
        $overlay = Shot 'convo-overlay'
        Check 'the speech balloons are on screen' ((PixelDiff $dlg $overlay) -gt 0.05) ('changed ' + (PixelDiff $dlg $overlay))
        Check 'the balloons are placed over the Sims, not in the fallback strip' ((Convo 'status') -match 'balloons=over-heads') (Convo 'status')
        Check 'the game continues after the choice' ((& $SimTool dialog) -join ' ' -match 'dialog=none') ((& $SimTool dialog) -join ' ')

        # Talk: subject choice
        Sim wait 40
        Sim interact Bob Betty 'Talk.../About Interests'
        Check 'Talk opens the subject choice dialog' (WaitDialog 90) ((& $SimTool dialog) -join ' ')
        Sim click 380 605                                   # first subject
        Sim wait 4
        $line = LastLine
        Check 'the chosen subject was used for the Talk' ($line -match '^talk about ') $line

        # off switch: no dialog while off, and the choice returns when on again
        Sim wait 40
        # Bob has free will: he may have started a conversation of his own meanwhile and be waiting for the player's choice
        for ($i = 0; $i -lt 4 -and ((& $SimTool dialog) -join ' ') -match 'waiting'; $i++) { & $SimTool answer 0 | Out-Null; Sim wait 6 }
        Check 'AI conversations can be turned off' ((Convo 'off') -match 'enabled=false') ''
        Sim interact Bob Betty 'Hug.../Friendly'
        Sim wait 30
        Check 'with the feature off a Hug asks nothing' ((& $SimTool dialog) -join ' ' -match 'dialog=none') ((& $SimTool dialog) -join ' ')
        Convo 'on' | Out-Null
        Check 'AI conversations can be turned on again' ((Convo 'status') -match 'enabled=True') (Convo 'status')
    }

    # ---- anti-aliasing (only when Render.dll is installed) ------------------------------------------------------------------
    $rs = (& $SimTool render status 2>&1) -join ' '
    if ($rs -notmatch 'aa=') { Write-Host "`n== anti-aliasing: not installed, skipped" -ForegroundColor Yellow }
    else {
        Write-Host "`n== anti-aliasing (FXAA)"
        $aaBefore = if ($rs -match 'aa=(\w+)') { $Matches[1] } else { 'off' }
        Sim speed 0; Sim wait 2                              # freeze the game so the screenshots show the same frame
        & $SimTool render aa off | Out-Null; Sim wait 1; $off = Shot 'aa-off'
        & $SimTool render aa fxaa | Out-Null; Sim wait 1; $on = Shot 'aa-fxaa'
        $st = ImageStats $off $on
        Check 'FXAA draws frames' ((& $SimTool render status) -join ' ' -match 'draws=[1-9]\d*') ((& $SimTool render status) -join ' ')
        Check 'FXAA changes edge pixels only (1% to 40% of the picture)' (($st.Changed -gt 0.01) -and ($st.Changed -lt 0.40)) ('changed ' + [math]::Round($st.Changed * 100, 2) + '%')
        Check 'FXAA does not brighten or darken the picture' ([math]::Abs($st.MeanSigned) -lt 1.0) ('mean change ' + [math]::Round($st.MeanSigned, 3))
        & $SimTool render aa off | Out-Null; Sim wait 1; $off2 = Shot 'aa-off-again'
        $st2 = ImageStats $off $off2
        Check 'turning FXAA off restores the original frame' ($st2.Changed -lt 0.001) ('changed ' + [math]::Round($st2.Changed * 100, 3) + '%')
        & $SimTool render aa $aaBefore | Out-Null
        Sim speed 1
    }
}
finally {
    & $SimTool disable | Out-Null
    foreach ($f in $backup.Keys) { if ($null -ne $backup[$f]) { Set-Content $f $backup[$f] -NoNewline -Encoding ascii } else { Remove-Item $f -ErrorAction SilentlyContinue } }
}

Write-Host ""
if ($failures.Count -gt 0) { Write-Host "$($failures.Count) failed, $passed passed. Screenshots: $Shots" -ForegroundColor Red; $failures | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }; exit 1 }
Write-Host "All $passed end-to-end checks passed. Screenshots: $Shots" -ForegroundColor Green
