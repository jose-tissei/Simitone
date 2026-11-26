# Opens and closes every redesigned page many times in the running game and fails if one stops coming back
# (the bug of 2026-11-26: the game's UIContainer.Remove leaves Parent set, so a page opened once and never again).
# Needs a running game with the test hook (Tools\Dev-Iterate.ps1 -House 5). Usage: pwsh Tools\Soak-Menus.ps1 [-Rounds 10]
param([int]$Rounds = 10)
$s = Join-Path $PSScriptRoot 'SimTool\out\simtool.exe'
$fail = 0
for ($i = 1; $i -le $Rounds; $i++) {
    $row = "round ${i}:"
    & $s convo memory-ui tab | Out-Null
    & $s until 4 'open=True' convo memory-ui status *> $null; if ($LASTEXITCODE) { $row += ' MEMORY-NOT-OPEN'; $fail++ } else { $row += ' memory-open' }
    & $s key esc | Out-Null
    & $s until 4 'open=False' convo memory-ui status *> $null; if ($LASTEXITCODE) { $row += ' MEMORY-STUCK'; $fail++ } else { $row += ' closed' }
    & $s click 277 971 | Out-Null   # the real Options button, no reset in between (a second click used to do nothing)
    & $s until 4 'inCurrentScreen=True' convo options diag *> $null; if ($LASTEXITCODE) { $row += ' OPTIONS-NOT-OPEN'; $fail++ } else { $row += ' options-open' }
    $d1 = (& $s convo options diag) -replace '.*draws=(\d+).*', '$1'; Start-Sleep -Milliseconds 400; $d2 = (& $s convo options diag) -replace '.*draws=(\d+).*', '$1'
    if ([int]$d2 -le [int]$d1) { $row += ' OPTIONS-NOT-DRAWN'; $fail++ }
    & $s key esc | Out-Null
    & $s until 4 'inCurrentScreen=False' convo options diag *> $null; if ($LASTEXITCODE) { $row += ' OPTIONS-STUCK'; $fail++ } else { $row += ' closed' }
    Write-Host $row
}
$pie = & $s convo pie
Write-Host "pie after the rounds: $pie"
if ($fail) { Write-Host "FAILED: $fail problem(s)"; exit 1 } else { Write-Host "OK: $Rounds rounds, nothing stuck" }
