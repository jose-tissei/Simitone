# Generates the four 65x65 white-on-transparent Options menu icons.
param([string]$OutDir = "$PSScriptRoot")
Add-Type -AssemblyName System.Drawing
function New-Icon($name, [scriptblock]$draw) {
    $bmp = New-Object System.Drawing.Bitmap 65, 65, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.Clear([System.Drawing.Color]::Transparent)
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), 4
    $pen.StartCap = 'Round'; $pen.EndCap = 'Round'; $pen.LineJoin = 'Round'
    & $draw $g $pen
    $bmp.Save((Join-Path $OutDir $name), [System.Drawing.Imaging.ImageFormat]::Png)
    $pen.Dispose(); $g.Dispose(); $bmp.Dispose()
}
function P($x, $y) { New-Object System.Drawing.PointF $x, $y }
# fixed camera: isometric cube
New-Icon 'opt_cam_fixed.png' { param($g, $pen)
    $g.DrawPolygon($pen, @((P 32 8), (P 55 20), (P 55 45), (P 32 57), (P 9 45), (P 9 20)))
    $g.DrawLine($pen, (P 9 20), (P 32 32)); $g.DrawLine($pen, (P 55 20), (P 32 32)); $g.DrawLine($pen, (P 32 32), (P 32 57)) }
# classic objects: house
New-Icon 'opt_cam_classic.png' { param($g, $pen)
    $g.DrawPolygon($pen, @((P 8 31), (P 32 9), (P 56 31)))
    $g.DrawPolygon($pen, @((P 15 29), (P 15 55), (P 49 55), (P 49 29)))
    $g.DrawRectangle($pen, 27, 38, 10, 17) }
# free camera: orbit
New-Icon 'opt_cam_free.png' { param($g, $pen)
    $g.DrawEllipse($pen, 7, 20, 51, 26)
    $g.FillEllipse([System.Drawing.Brushes]::White, 24, 24, 17, 17)
    $g.FillEllipse([System.Drawing.Brushes]::White, 49, 17, 9, 9) }
# reload list: circular arrow
New-Icon 'opt_cam_reload.png' { param($g, $pen)
    $g.DrawArc($pen, 11, 11, 43, 43, -60, 280)
    $g.FillPolygon([System.Drawing.Brushes]::White, @((P 44 6), (P 58 22), (P 36 24))) }

# plain 2D: flat square with diagonal slash (3D off)
New-Icon 'opt_cam_2d.png' { param($g, $pen)
    $g.DrawRectangle($pen, 12, 12, 41, 41)
    $g.DrawLine($pen, (P 8 57), (P 57 8)) }

# AI conversations: speech bubble with three dots
New-Icon 'opt_convo.png' { param($g, $pen)
    $g.DrawPolygon($pen, @((P 12 12), (P 53 12), (P 53 40), (P 33 40), (P 20 53), (P 21 40), (P 12 40)))
    foreach ($x in 24, 33, 42) { $g.FillEllipse([System.Drawing.Brushes]::White, ($x - 3), 23, 7, 7) } }
