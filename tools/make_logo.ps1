param([string]$OutDir)
Add-Type -AssemblyName System.Drawing

# Recta logo - same construction as Equora.svg:
# 256x256 solid square (#0078D7) + white geometric letter block.
$white = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)
$rects = @(
    @(56, 48, 32, 160),
    @(88, 48, 84, 32),
    @(140, 48, 32, 96),
    @(88, 112, 84, 32)
)
$leg = @(120, 144, 152, 144, 200, 208, 168, 208)

foreach ($size in 16, 32, 48, 256) {
    $bmp = New-Object System.Drawing.Bitmap -ArgumentList $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::None
    $g.Clear([System.Drawing.Color]::FromArgb(0x00, 0x78, 0xD7))
    $s = $size / 256.0
    foreach ($r in $rects) {
        $g.FillRectangle($white, [float]($r[0] * $s), [float]($r[1] * $s), [float]($r[2] * $s), [float]($r[3] * $s))
    }
    $pts = New-Object System.Drawing.PointF[] 4
    for ($i = 0; $i -lt 4; $i++) {
        $pts[$i] = New-Object System.Drawing.PointF -ArgumentList ([float]($leg[$i * 2] * $s)), ([float]($leg[$i * 2 + 1] * $s))
    }
    $g.FillPolygon($white, $pts)
    $g.Dispose()
    $bmp.Save("$OutDir\RectaLogo-$size.png", [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}
Write-Host "pngs saved to $OutDir"
