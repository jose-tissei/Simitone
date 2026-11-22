# Builds every tool in Tools/ against a Simitone release and runs all automated tests that need no game files.
# Used by .github/workflows/tools-ci.yml and runnable locally:
#   pwsh Tools/ci.ps1 -App C:\path\to\extracted\SimitoneWindows
# -App must be an unmodified, extracted SimitoneWindows.zip (the patch tests refuse to run on a patched one).
param([Parameter(Mandatory = $true)][string]$App)

$ErrorActionPreference = 'Stop'
$App = (Resolve-Path $App).Path
$Tools = $PSScriptRoot
$Tmp = Join-Path ([IO.Path]::GetTempPath()) ("simitone-ci-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $Tmp | Out-Null

function Step([string]$Name, [scriptblock]$Body) {
    Write-Host "`n=== $Name" -ForegroundColor Cyan
    & $Body
    if ($LASTEXITCODE -ne 0) { throw "step failed: $Name (exit code $LASTEXITCODE)" }
}

$mono = Join-Path $App 'Monogame\Windows\MonoGame.Framework.dll'

# ---- build every tool -----------------------------------------------------------------------------------
Step 'build SpriteCards (hook)'        { dotnet build "$Tools\SpriteCards" -c Release -o "$Tools\SpriteCards\out" -p:AppDir="$App" --nologo -v q }
Step 'build SpriteCardsPatch'          { dotnet build "$Tools\SpriteCardsPatch" -c Release -o "$Tools\SpriteCardsPatch\out" --nologo -v q }
Step 'build SimTestHook'               { dotnet build "$Tools\SimTestHook" -c Release -o "$Tools\SimTestHook\out" -p:MonoGameDll="$mono" -p:AppDir="$App" --nologo -v q }
Step 'build InputHookPatch'            { dotnet build "$Tools\InputHookPatch" -c Release -o "$Tools\InputHookPatch\out" --nologo -v q }
Step 'build ButtonHoverPatch'          { dotnet build "$Tools\ButtonHoverPatch" -c Release -o "$Tools\ButtonHoverPatch\out" --nologo -v q }
Step 'build SimTool'                   { dotnet build "$Tools\SimTool" -c Release -o "$Tools\SimTool\out" --nologo -v q }

# ---- unit tests (pure logic) ----------------------------------------------------------------------------
Step 'unit tests: mode logic'          { dotnet test "$Tools\SpriteCards.Tests" --nologo -v q }

# ---- patch tests (real patcher against the real release) ------------------------------------------------
Step 'patch tests: SpriteCardsPatch'   {
    $env:SIMITONE_APP = $App
    $env:SPRITECARDS_DLL = "$Tools\SpriteCards\out\SpriteCards.dll"
    $env:SPRITECARDSPATCH = "$Tools\SpriteCardsPatch\out\SpriteCardsPatch.dll"
    dotnet test "$Tools\PatchTests" --nologo -v q
}

# ---- smoke tests: the older patchers still apply cleanly -------------------------------------------------
Step 'smoke: InputHookPatch' {
    $out = Join-Path $Tmp 'inputhook'
    dotnet "$Tools\InputHookPatch\out\InputHookPatch.dll" $App "$Tools\SimTestHook\out\SimTestHook.dll" $out
    if ($LASTEXITCODE -ne 0) { return }
    foreach ($f in 'FSO.Common.dll', 'FSO.UI.dll', 'FSO.Client.dll') { if (-not (Test-Path (Join-Path $out $f))) { throw "InputHookPatch did not write $f" } }
    $global:LASTEXITCODE = 0
}
Step 'smoke: ButtonHoverPatch' {
    $out = Join-Path $Tmp 'FSO.UI.dll'
    dotnet "$Tools\ButtonHoverPatch\out\ButtonHoverPatch.dll" (Join-Path $App 'FSO.UI.dll') $out $App
    if ($LASTEXITCODE -ne 0) { return }
    if (-not (Test-Path $out)) { throw 'ButtonHoverPatch did not write FSO.UI.dll' }
    $global:LASTEXITCODE = 0
}

Remove-Item $Tmp -Recurse -Force -ErrorAction SilentlyContinue
Write-Host "`nAll CI steps passed." -ForegroundColor Green
