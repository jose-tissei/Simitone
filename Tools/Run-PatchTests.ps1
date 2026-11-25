# Runs the patcher integration tests (Tools/PatchTests) on this machine; without SIMITONE_APP they are skipped, which is easy to miss.
#   pwsh Tools/Run-PatchTests.ps1 [-App <unmodified extracted release>]
# -App defaults to $env:SIMITONE_APP, then to ~/.t3/projects/the-sims-ci-release/app. Exits 0 with a notice when no release is found
# (so a pre-commit hook does not block machines without one); exits non-zero when a build or test fails.
param([string]$App = $env:SIMITONE_APP)
$ErrorActionPreference = 'Stop'
if (-not $App) { $App = Join-Path $env:USERPROFILE '.t3\projects\the-sims-ci-release\app' }
if (-not (Test-Path (Join-Path $App 'FSO.SimAntics.dll'))) { Write-Host "patch tests NOT run: no unmodified release at $App (set SIMITONE_APP)" -ForegroundColor Yellow; exit 0 }
$App = (Resolve-Path $App).Path
$Tools = $PSScriptRoot
function Build($proj, $extra) { & dotnet build "$Tools\$proj" -c Release -o "$Tools\$proj\out" --nologo -v q @extra; if ($LASTEXITCODE -ne 0) { throw "build failed: $proj" } }
Build 'SpriteCards' @("-p:AppDir=$App"); Build 'SpriteCardsPatch' @(); Build 'Conversations' @("-p:AppDir=$App"); Build 'Render' @("-p:AppDir=$App")
$env:SIMITONE_APP = $App
$env:SPRITECARDS_DLL = "$Tools\SpriteCards\out\SpriteCards.dll"
$env:SPRITECARDSPATCH = "$Tools\SpriteCardsPatch\out\SpriteCardsPatch.dll"
$env:CONVERSATIONS_DLL = "$Tools\Conversations\out\Conversations.dll"
$env:RENDER_DLL = "$Tools\Render\out\Render.dll"
dotnet test "$Tools\PatchTests" --nologo -v q
exit $LASTEXITCODE
