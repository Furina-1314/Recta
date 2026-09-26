param([string]$OutDir = "desktop/Recta.App/Assets")

Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = "Stop"
$OutDir = [System.IO.Path]::GetFullPath($OutDir)
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$blue = [System.Drawing.Color]::FromArgb(0x08, 0x78, 0xD1)
$white = [System.Drawing.Color]::White
$sizes = @(16, 32, 48, 256)
$pngFrames = [System.Collections.Generic.List[byte[]]]::new()

foreach ($size in $sizes) {
    # Render large first, then downsample for crisp small-size icons.
    $sourceSize = 1024
    $source = [System.Drawing.Bitmap]::new($sourceSize, $sourceSize)
    $graphics = [System.Drawing.Graphics]::FromImage($source)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $graphics.Clear($blue)
    $scale = $sourceSize / 256.0
    # Centered, square-shouldered R with a long outward leg at the same stroke width.
    $outer = @(
        @(48,32), @(192,32), @(192,128), @(128,128), @(208,224),
        @(166,224), @(103,148), @(80,148), @(80,224), @(48,224)
    )
    $inner = @(@(80,64), @(160,64), @(160,96), @(80,96))
    foreach ($shape in @(@{ Points = $outer; Color = $white }, @{ Points = $inner; Color = $blue })) {
        $points = [System.Drawing.PointF[]]::new($shape.Points.Count)
        for ($i = 0; $i -lt $shape.Points.Count; $i++) {
            $points[$i] = [System.Drawing.PointF]::new([float]($shape.Points[$i][0] * $scale), [float]($shape.Points[$i][1] * $scale))
        }
        $brush = [System.Drawing.SolidBrush]::new($shape.Color)
        $graphics.FillPolygon($brush, $points)
        $brush.Dispose()
    }
    $graphics.Dispose()

    $icon = [System.Drawing.Bitmap]::new($size, $size)
    $g = [System.Drawing.Graphics]::FromImage($icon)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $g.DrawImage($source, 0, 0, $size, $size)
    $g.Dispose()
    $source.Dispose()

    $path = Join-Path $OutDir "RectaLogo-$size.png"
    $icon.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $stream = [System.IO.MemoryStream]::new()
    $icon.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngFrames.Add($stream.ToArray())
    $stream.Dispose()
    $icon.Dispose()
}

# Store the same four PNG frames in a multi-resolution Windows ICO container.
$icoPath = Join-Path $OutDir "Recta.ico"
$writer = [System.IO.BinaryWriter]::new([System.IO.File]::Create($icoPath))
$writer.Write([UInt16]0)
$writer.Write([UInt16]1)
$writer.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $size = $sizes[$i]
    $frame = $pngFrames[$i]
    $writer.Write([byte]$(if ($size -eq 256) { 0 } else { $size }))
    $writer.Write([byte]$(if ($size -eq 256) { 0 } else { $size }))
    $writer.Write([byte]0)
    $writer.Write([byte]0)
    $writer.Write([UInt16]1)
    $writer.Write([UInt16]32)
    $writer.Write([UInt32]$frame.Length)
    $writer.Write([UInt32]$offset)
    $offset += $frame.Length
}
foreach ($frame in $pngFrames) { $writer.Write($frame) }
$writer.Dispose()
Write-Host "Generated PNG and ICO assets in $OutDir"
