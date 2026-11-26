# Protects the player's data in Documentos\Simitone while the game is driven by tests.
#   pwsh Tools\Save-Guard.ps1 -Snapshot   # copies UserData, the *.profile files and conversations.*.json to the-sims-run\save-snapshot (only if there is none yet; -Force replaces it)
#   pwsh Tools\Save-Guard.ps1 -Restore    # puts them back (the game must not be running), and reports what the test run had changed
# Why: the Options "Save" button (and a "save first?" answered Yes) writes the real UserData\Houses and Neighborhood.iff.
param([switch]$Snapshot, [switch]$Restore, [switch]$Force)
$docs = 'C:\Users\Zé\OneDrive\Documentos\Simitone'
$snap = 'C:\Users\Zé\.t3\projects\the-sims-run\save-snapshot'
$items = @('UserData\Neighborhood.iff', 'UserData\Houses', 'UserData\Characters', 'render.profile', 'spritecards.profile', 'spritecards.user.txt', 'conversations.menu', 'conversations.gossip.json', 'conversations.memory.json', 'config.ini')
if ($Snapshot) {
    if ((Test-Path $snap) -and -not $Force) { Write-Host "snapshot already exists ($snap); -Force to replace"; return }
    if (Test-Path $snap) { Remove-Item $snap -Recurse -Force }
    foreach ($i in $items) { $src = Join-Path $docs $i; if (Test-Path $src) { $dst = Join-Path $snap $i; New-Item -ItemType Directory -Force (Split-Path $dst) | Out-Null; Copy-Item $src $dst -Recurse -Force } }
    Write-Host "snapshot written to $snap"
}
elseif ($Restore) {
    if (-not (Test-Path $snap)) { throw 'no snapshot' }
    if (Get-Process Simitone.Windows -ErrorAction SilentlyContinue) { throw 'stop the game first' }
    foreach ($i in $items) {
        $src = Join-Path $snap $i; if (-not (Test-Path $src)) { continue }
        $dst = Join-Path $docs $i
        $changed = Get-ChildItem $src -Recurse -File -ErrorAction SilentlyContinue | Where-Object { $d = Join-Path $dst $_.FullName.Substring($src.Length).TrimStart('\'); (-not (Test-Path $d)) -or (Get-FileHash $d).Hash -ne (Get-FileHash $_.FullName).Hash }
        if (-not (Get-Item $src).PSIsContainer) { $changed = if ((-not (Test-Path $dst)) -or (Get-FileHash $dst).Hash -ne (Get-FileHash $src).Hash) { ,(Get-Item $src) } else { @() } }
        Copy-Item $src $dst -Recurse -Force
        foreach ($c in $changed) { Write-Host "restored $i ($($c.Name))" }
    }
    Write-Host 'restored'
}
else { Write-Host 'use -Snapshot or -Restore' }
