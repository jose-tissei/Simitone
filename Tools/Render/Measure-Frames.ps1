# Measures the cost of the anti-aliasing passes in the running game (it must be on a lot, with Render.dll installed).
# The game's frame loop is limited to about 1000 frames per second even with vsync off, and the passes cost far less than a
# millisecond, so each pass is run -Repeat times per frame (the picture is identical) and the difference is divided by it.
#   pwsh Tools/Render/Measure-Frames.ps1 [-Seconds 6] [-Repeat 40] [-SimTool <simtool.exe>]
param(
    [int]$Seconds = 6,
    [int]$Repeat = 40,
    [string]$SimTool = (Join-Path $PSScriptRoot '..\SimTool\out\simtool.exe')
)
$ErrorActionPreference = 'Stop'
function Frames { $t = (& $SimTool render frames) -join ' '; if ($t -match 'calls=(\d+) ms=(\d+)') { return @([long]$Matches[1], [long]$Matches[2]) } else { throw "unexpected: $t" } }
& $SimTool speed 0 | Out-Null                 # a paused game still draws every frame, so the numbers do not depend on what the Sims do
& $SimTool render vsync off | Out-Null
Start-Sleep 2
$rows = foreach ($mode in 'off', 'fxaa', 'smaa') {
    & $SimTool render aa $mode | Out-Null
    & $SimTool render repeat ($(if ($mode -eq 'off') { 1 } else { $Repeat })) | Out-Null
    Start-Sleep 2                                # warm-up: effects load on first use
    $a = Frames; Start-Sleep $Seconds; $b = Frames
    $frames = $b[0] - $a[0]; $ms = $b[1] - $a[1]
    [pscustomobject]@{ Mode = $mode; Fps = [math]::Round($frames * 1000.0 / $ms, 1); MsPerFrame = [math]::Round($ms / [math]::Max($frames, 1), 3) }
}
& $SimTool render repeat 1 | Out-Null
& $SimTool render vsync on | Out-Null
& $SimTool speed 1 | Out-Null
$base = ($rows | Where-Object Mode -eq 'off').MsPerFrame
$rows | ForEach-Object { $_ | Add-Member CostMs -NotePropertyValue $(if ($_.Mode -eq 'off') { 0 } else { [math]::Round(($_.MsPerFrame - $base) / $Repeat, 3) }) -PassThru } | Format-Table Mode, Fps, MsPerFrame, CostMs -AutoSize
