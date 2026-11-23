# Plays a family's house, then goes through every magic-world lot (houses 90-99) with the same lot switch the Hole in the Ground uses,
# and back home each time, watching for a crash or a hung game. The player's save is copied first and restored at the end, and the
# script checks that every file in the save folder is byte-identical afterwards (the game writes the neighbourhood every time a lot loads).
#   pwsh Tools/e2e/Test-MagicLots.ps1 [-House 21] [-Lots 90..99]
param(
    [int]$House = 0,                     # 0 = the first house that has a family
    [int[]]$Lots = (90..99),
    [string]$SimTool = (Join-Path $PSScriptRoot '..\SimTool\out\simtool.exe'),
    [string]$Shots = (Join-Path ([IO.Path]::GetTempPath()) 'simitone-magic')
)
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force $Shots | Out-Null
$docs = Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'Simitone'
$files = @('UserData', 'config.ini', 'render.profile', 'spritecards.profile', 'conversations.profile')
$backup = Join-Path ([IO.Path]::GetTempPath()) ('simitone-magic-save-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $backup | Out-Null
foreach ($f in $files) { $src = Join-Path $docs $f; if (Test-Path $src) { Copy-Item $src (Join-Path $backup $f) -Recurse -Force } }
function Hashes($root) { $h = @{}; foreach ($f in $files) { $p = Join-Path $root $f; if (Test-Path $p) { Get-ChildItem $p -Recurse -File -Force -ErrorAction SilentlyContinue | ForEach-Object { $h[$_.FullName.Substring($root.Length)] = (Get-FileHash $_.FullName).Hash } } }; $h }
$before = Hashes $docs
Write-Host "save copied to $backup ($($before.Count) files)" -ForegroundColor DarkGray

$failures = New-Object System.Collections.Generic.List[string]
Add-Type -AssemblyName System.Drawing
# distinct colours (5 bits per channel) in the middle of the frame: a flat sky or a black screen has a few dozen, a lot with objects thousands
function ImageColors([string]$path) {
    $bmp = New-Object System.Drawing.Bitmap $path
    try { $set = New-Object 'System.Collections.Generic.HashSet[int]'
        for ($y = 150; $y -lt $bmp.Height - 150; $y += 6) { for ($x = 320; $x -lt $bmp.Width - 120; $x += 6) { $c = $bmp.GetPixel($x, $y); [void]$set.Add((($c.R -shr 3) -shl 10) -bor (($c.G -shr 3) -shl 5) -bor ($c.B -shr 3)) } }
        return $set.Count } finally { $bmp.Dispose() }
}
function Sim { (& $SimTool @args 2>&1) -join ' ' }
function Alive([string]$what) {
    # a fatal-error dialog stops the game thread, so a command that needs it times out
    $r = Sim time
    if ($r -match '^\d|ok \d') { return $true }
    $failures.Add("$what - game did not respond: $r"); Write-Host "  FAIL  $what - $r" -ForegroundColor Red; return $false
}
try {
    Get-Process Simitone.Windows -ErrorAction SilentlyContinue | Stop-Process -Force; Start-Sleep 2
    Sim launch | Out-Null; Sim wait 12 | Out-Null
    $fam = (Sim families)
    Write-Host "houses with a family: $fam"
    $list = ($fam -replace '^ok ', '').Split(' ', [StringSplitOptions]::RemoveEmptyEntries)
    if ($list.Count -eq 0) { throw 'no house has a family in this neighbourhood' }
    if ($House -le 0) { $House = [int]$list[0] }
    Write-Host "playing house $House"
    Sim playhouse $House | Out-Null; Sim wait 30 | Out-Null
    Sim speed 0 | Out-Null
    $sims = Sim sims
    $homeInfo = Sim lotinfo
    $simCount = ([regex]::Matches($sims, 'neighbor')).Count
    $familyNames = @([regex]::Matches($sims, '\d+:(\w+)\(neighbor') | ForEach-Object { $_.Groups[1].Value })
    if ($simCount -lt 1) { $failures.Add("no Sim in house $House ($sims)"); Write-Host "  FAIL  a Sim is in the lot - $sims" -ForegroundColor Red } else { Write-Host "  PASS  $simCount Sim(s) in house ${House}: $sims | $homeInfo" -ForegroundColor Green }
    & $SimTool shot (Join-Path $Shots "home.png") | Out-Null
    $warnings = New-Object System.Collections.Generic.List[string]
    foreach ($n in $Lots) {
        Sim dismiss | Out-Null
        $r = Sim gotolot $n
        Sim wait 15 | Out-Null
        if (-not (Alive "lot $n")) { break }
        # a paused game does not draw the freshly loaded lot (only sky), so let it run for a moment, close the welcome message, then look
        # (the hook's idle mouse sits at a screen edge, which scrolls the camera off the lot once the welcome message is closed, so it is parked mid-screen)
        Sim move 960 540 | Out-Null; Sim speed 1 | Out-Null; Sim wait 8 | Out-Null
        $info = Sim lotinfo
        $objs = if ($info -match 'objects=(\d+)') { [int]$Matches[1] } else { 0 }
        $al = Sim alerts
        $lotSims = Sim sims
        $famHere = @($familyNames | Where-Object { $lotSims -match [regex]::Escape($_) }).Count
        # lots 90-92 are vacant houses to move into (EverAfter Oaks, Alfred's B&B, Old Man Lum's): the game puts nobody on them and leaves the camera at the corner,
        # so there the camera is moved to the middle of the lot instead; every other lot must have the family's Sim on it
        if ($n -le 92) {
            if ($info -match 'size=(\d+)x(\d+)') { Sim camera ([int]([int]$Matches[1] / 2)) ([int]([int]$Matches[2] / 2)) | Out-Null; Sim wait 2 | Out-Null }
            Write-Host "  note  lot ${n} is a vacant house, so no Sim is expected" -ForegroundColor DarkGray
        }
        elseif ($famHere -lt 1) { $failures.Add("lot ${n}: none of the family's Sims is on the lot ($lotSims)"); Write-Host "  FAIL  lot $n has none of $($familyNames -join ', ') on it" -ForegroundColor Red }
        else { Write-Host "  PASS  lot ${n}: $famHere of the family's Sims are on it" -ForegroundColor Green }
        Sim dismiss | Out-Null; Sim wait 2 | Out-Null
        & $SimTool shot (Join-Path $Shots "lot$n.png") | Out-Null
        Sim speed 0 | Out-Null
        $colors = ImageColors (Join-Path $Shots "lot$n.png")
        if ($colors -lt 90) { $failures.Add("lot ${n}: the screenshot is flat ($colors distinct colours), the lot is not on screen"); Write-Host "  FAIL  lot $n is not visible on screen ($colors distinct colours)" -ForegroundColor Red }
        else { Write-Host "  PASS  lot $n is visible on screen ($colors distinct colours)" -ForegroundColor Green }
        if ($objs -lt 50 -or $info -notmatch 'downtown=True') { $failures.Add("lot ${n}: not loaded ($info)"); Write-Host "  FAIL  lot $n did not load - $info" -ForegroundColor Red }
        else { Write-Host "  PASS  lot $n loaded: $info" -ForegroundColor Green }
        # a SimAntics exception box is a failure: the lot's objects read the travel state the Hole in the Ground records (gotolot records it the same way), so none should appear
        if ($al -match 'SimAntics Exception') { $failures.Add("lot ${n}: SimAntics exception: $al"); Write-Host "  FAIL  lot $n showed a SimAntics exception box - $al" -ForegroundColor Red }
        Sim dismiss | Out-Null
        Sim gotolot 65535 | Out-Null     # (short)-1 = back to the family's own lot, as leaving through the hole does
        Sim wait 15 | Out-Null
        if (-not (Alive "back home from lot $n")) { break }
        $homeNow = Sim lotinfo
        if ((Sim alerts) -match 'Exception') { $failures.Add("exception box after returning home from lot $n"); Write-Host "  FAIL  exception box back home after lot $n" -ForegroundColor Red }
        $homeSims = if ($homeNow -match 'sims=(\d+)') { [int]$Matches[1] } else { 0 }
        $homeObjs = if ($homeNow -match 'objects=(\d+)') { [int]$Matches[1] } else { 0 }
        if ($homeSims -lt 1 -or $homeObjs -lt 50 -or $homeNow -notmatch 'downtown=False') { $failures.Add("home after lot ${n}: $homeNow"); Write-Host "  FAIL  home after lot $n - $homeNow" -ForegroundColor Red }
        else { Write-Host "  PASS  back home after lot ${n}: $homeNow" -ForegroundColor Green }
    }
    foreach ($w in $warnings) { Write-Host "  note: $w" -ForegroundColor Yellow }
    $log = Join-Path (Join-Path $env:USERPROFILE '.t3\projects\the-sims-run\app') 'Content\render.log'
    if (Test-Path $log) { $rep = (Get-Content $log | Where-Object { $_ -match 'placeholder' } | Select-Object -Last 2) -join ' | '; Write-Host "texture placeholders in render.log: $(if ($rep) { $rep } else { 'none' })" }
}
finally {
    Get-Process Simitone.Windows -ErrorAction SilentlyContinue | Stop-Process -Force; Start-Sleep 3
    # put the save back exactly as it was
    foreach ($f in $files) {
        $dst = Join-Path $docs $f; $src = Join-Path $backup $f
        if (Test-Path $src) { if ((Get-Item $src).PSIsContainer) { Remove-Item $dst -Recurse -Force -ErrorAction SilentlyContinue; Copy-Item $src $dst -Recurse -Force } else { Copy-Item $src $dst -Force } }
        elseif (Test-Path $dst) { Remove-Item $dst -Recurse -Force }
    }
    $after = Hashes $docs
    $diff = @($before.Keys | Where-Object { $after[$_] -ne $before[$_] }) + @($after.Keys | Where-Object { -not $before.ContainsKey($_) })
    if ($diff.Count -eq 0) { Write-Host "  PASS  the save folder is byte-identical to before the test ($($after.Count) files)" -ForegroundColor Green; Remove-Item $backup -Recurse -Force }
    else { $failures.Add("save differs: " + ($diff -join ', ')); Write-Host "  FAIL  the save differs after restoring: $($diff -join ', ') (copy kept in $backup)" -ForegroundColor Red }
}
Write-Host ""
if ($failures.Count -gt 0) { Write-Host "$($failures.Count) problem(s). Screenshots: $Shots" -ForegroundColor Red; $failures | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }; exit 1 }
Write-Host "All lots loaded and the save is intact. Screenshots: $Shots" -ForegroundColor Green
