# Installs the AI conversations feature (and the other Tools/ patches that live in the same patcher) into a game folder.
#   pwsh Tools/Install-Conversations.ps1 -Pristine <unmodified extracted v0.8.12> -App <game folder to update>
# The patchers must always start from the ORIGINAL DLLs (-Pristine); -App only receives the results.
# Files that change in -App are first copied to <Backup> (default: a "backup-conversations" folder next to -App).
param(
    [Parameter(Mandatory = $true)][string]$Pristine,
    [Parameter(Mandatory = $true)][string]$App,
    [string]$Backup = (Join-Path (Split-Path $App -Parent) 'backup-conversations'),
    [switch]$WithSimTestHook   # also (re)install the test hook used by simtool
)
$ErrorActionPreference = 'Stop'
$Pristine = (Resolve-Path $Pristine).Path
$App = (Resolve-Path $App).Path
$Tools = $PSScriptRoot
$Tmp = Join-Path ([IO.Path]::GetTempPath()) ("convo-install-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $Tmp, $Backup | Out-Null

function Build($proj, $out, $extra) { & dotnet build "$Tools\$proj" -c Release -o "$Tools\$proj\out" --nologo -v q @extra; if ($LASTEXITCODE -ne 0) { throw "build failed: $proj" } }
Build 'SpriteCards' $null @("-p:AppDir=$Pristine")
Build 'Conversations' $null @("-p:AppDir=$Pristine")
Build 'Render' $null @("-p:AppDir=$Pristine")
Build 'SpriteCardsPatch' $null @()
if ($WithSimTestHook) { Build 'SimTestHook' $null @("-p:AppDir=$Pristine") }

& dotnet "$Tools\SpriteCardsPatch\out\SpriteCardsPatch.dll" $Pristine "$Tools\SpriteCards\out\SpriteCards.dll" $Tmp "$Tools\Conversations\out\Conversations.dll" "$Tools\Render\out\Render.dll"
if ($LASTEXITCODE -ne 0) { throw 'patcher failed' }

$files = @('FSO.LotView.dll', 'FSO.SimAntics.dll', 'Simitone.Client.dll', 'Simitone.Windows.exe', 'Conversations.dll', 'FSO.Common.dll', 'FSO.Content.dll', 'Render.dll')
if ($WithSimTestHook) {
    # FSO.Common.dll carries both the render hook and the test input hook; the input patcher runs on top of the render-patched copy
    Build 'InputHookPatch' $null @()
    $stage = Join-Path $Tmp 'stage'
    New-Item -ItemType Directory -Force (Join-Path $stage 'Monogame\Windows') | Out-Null
    Copy-Item (Join-Path $Pristine 'Monogame\Windows\*.dll') (Join-Path $stage 'Monogame\Windows') -Force
    Get-ChildItem $Pristine -File | Where-Object { $_.Name -like 'FSO.*' -or $_.Name -like 'Simitone.*' } | Where-Object { $_.Extension -in '.dll', '.exe' } | Copy-Item -Destination $stage -Force
    Copy-Item (Join-Path $Tmp 'FSO.Common.dll') $stage -Force
    & dotnet "$Tools\InputHookPatch\out\InputHookPatch.dll" $stage "$Tools\SimTestHook\out\SimTestHook.dll" (Join-Path $Tmp 'hooked')
    if ($LASTEXITCODE -ne 0) { throw 'input hook patcher failed' }
    Copy-Item (Join-Path $Tmp 'hooked\FSO.Common.dll') $Tmp -Force   # FSO.UI and FSO.Client keep what the game folder has (input hook, button hover patches)
}
foreach ($f in $files) {
    $dst = Join-Path $App $f
    if ((Test-Path $dst) -and -not (Test-Path (Join-Path $Backup $f))) { Copy-Item $dst $Backup }
    Copy-Item (Join-Path $Tmp $f) $dst -Force
}
Copy-Item "$Tools\SpriteCards\out\SpriteCards.dll" $App -Force
if ($WithSimTestHook) { Copy-Item "$Tools\SimTestHook\out\SimTestHook.dll" $App -Force }
Copy-Item "$Tools\SpriteCards\icons\opt_convo.png", "$Tools\SpriteCards\icons\live_memory.png" (Join-Path $App 'Content\uigraphics\live\modes') -Force
Copy-Item "$Tools\SpriteCards\icons\opt_aa.png", "$Tools\SpriteCards\icons\opt_light.png", "$Tools\SpriteCards\icons\opt_pie.png" (Join-Path $App 'Content\uigraphics\live\modes') -Force
# Directional sprite shading is experimental and NOT installed: it needs Render/Shaders/World2D.xnb copied over Content/DX/Effects/2DWorldBatch.xnb
# (see Render/README.md). When measured it changed almost nothing visible, so the installer leaves the game's own effect file alone.
$bankSrc = Join-Path $Tools 'Conversations\bank\bank.json'
if (Test-Path $bankSrc) { New-Item -ItemType Directory -Force (Join-Path $App 'Content\conversations') | Out-Null; Copy-Item $bankSrc (Join-Path $App 'Content\conversations\bank.json') -Force }
$animSrc = Join-Path $Tools 'Conversations\Assets\Animations'
if (Test-Path $animSrc) {
    $animDst = Join-Path $App 'CustomAnims'
    New-Item -ItemType Directory -Force $animDst | Out-Null
    Copy-Item (Join-Path $animSrc '*.simanim.json') $animDst -Force
}
Remove-Item $Tmp -Recurse -Force
Write-Host "Installed. Originals saved in $Backup" -ForegroundColor Green
