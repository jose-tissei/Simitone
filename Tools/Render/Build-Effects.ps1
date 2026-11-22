# Compiles an HLSL effect with the game's own effect compiler (FSOMonoGame 2MGFX, writes MGFX version 8) and wraps it
# in an .xnb the game can load, using an existing game effect as the template for the container.
#
#   pwsh Tools/Render/Build-Effects.ps1 -Fx MyEffect.fx -Template <game>\Content\DX\Effects\SSAA.xnb -Out MyEffect.xnb
#
# The compiler (Tools/Render/Mgfx) is built on first use. The .xnb container is: header, type reader table, then the effect
# as a length-prefixed byte array, so only the length prefix and the total file size change.
param(
    [Parameter(Mandatory)][string]$Fx,
    [Parameter(Mandatory)][string]$Template,
    [Parameter(Mandatory)][string]$Out,
    [string]$Profile = 'DirectX_11'
)
$ErrorActionPreference = 'Stop'
$mgfx = Join-Path $PSScriptRoot 'Mgfx'
$exe = Join-Path $mgfx 'out\2mgfx.exe'
if (-not (Test-Path $exe)) {
    python -X utf8 (Join-Path $mgfx 'gen_csproj.py')
    dotnet build (Join-Path $mgfx 'Mgfx.csproj') -c Release -o (Join-Path $mgfx 'out') -v q | Out-Null
    if (-not (Test-Path $exe)) { throw 'could not build the effect compiler' }
}
$obj = [IO.Path]::ChangeExtension([IO.Path]::GetFullPath($Out), '.mgfxo')
$Out = [IO.Path]::GetFullPath($Out)
& $exe (Resolve-Path $Fx) $obj "/Profile:$Profile"
if ($LASTEXITCODE -ne 0) { throw "effect compile failed ($LASTEXITCODE)" }

$tpl = [IO.File]::ReadAllBytes((Resolve-Path $Template))
$new = [IO.File]::ReadAllBytes($obj)
# find the payload: the template's "MGFX" magic, preceded by its 4-byte length
$idx = -1
for ($i = 8; $i -lt $tpl.Length - 4; $i++) { if ($tpl[$i] -eq 0x4D -and $tpl[$i+1] -eq 0x47 -and $tpl[$i+2] -eq 0x46 -and $tpl[$i+3] -eq 0x58) { $idx = $i; break } }
if ($idx -lt 4) { throw 'the template is not a compiled effect (no MGFX payload)' }
$oldLen = [BitConverter]::ToInt32($tpl, $idx - 4)
$tail = $tpl.Length - ($idx + $oldLen)
if ($tail -lt 0) { throw 'unexpected template layout' }
$ms = New-Object IO.MemoryStream
$ms.Write($tpl, 0, $idx - 4)
$ms.Write([BitConverter]::GetBytes([int]$new.Length), 0, 4)
$ms.Write($new, 0, $new.Length)
if ($tail -gt 0) { $ms.Write($tpl, $idx + $oldLen, $tail) }
$bytes = $ms.ToArray()
[BitConverter]::GetBytes([int]$bytes.Length).CopyTo($bytes, 6)   # total file size
[IO.File]::WriteAllBytes($Out, $bytes)
Remove-Item $obj -ErrorAction SilentlyContinue
Write-Host "wrote $Out ($($bytes.Length) bytes, effect $($new.Length) bytes, MGFX v$($new[4]))"
