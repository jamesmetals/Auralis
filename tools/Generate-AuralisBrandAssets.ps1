param(
    [string]$AssetsDirectory = "$PSScriptRoot\..\src\MelhorWindows.Desktop\Assets"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

function New-RoundedRectanglePath {
    param(
        [System.Drawing.RectangleF]$Rectangle,
        [float]$Radius
    )

    $diameter = $Radius * 2
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $arc = New-Object System.Drawing.RectangleF($Rectangle.X, $Rectangle.Y, $diameter, $diameter)

    $path.AddArc($arc, 180, 90)
    $arc.X = $Rectangle.Right - $diameter
    $path.AddArc($arc, 270, 90)
    $arc.Y = $Rectangle.Bottom - $diameter
    $path.AddArc($arc, 0, 90)
    $arc.X = $Rectangle.X
    $path.AddArc($arc, 90, 90)
    $path.CloseFigure()
    return $path
}

function New-MultiSizeIco {
    param(
        [Parameter(Mandatory = $true)]
        [string]$OutputPath,
        [Parameter(Mandatory = $true)]
        [hashtable]$PngFilesBySize
    )

    $sizes = $PngFilesBySize.Keys | Sort-Object
    $fileStream = [System.IO.File]::Create($OutputPath)
    $writer = New-Object System.IO.BinaryWriter($fileStream)

    try {
        $writer.Write([UInt16]0)
        $writer.Write([UInt16]1)
        $writer.Write([UInt16]$sizes.Count)

        $imageData = @()
        $offset = 6 + (16 * $sizes.Count)

        foreach ($size in $sizes) {
            $bytes = [System.IO.File]::ReadAllBytes($PngFilesBySize[$size])
            $imageData += [pscustomobject]@{
                Size = [int]$size
                Bytes = $bytes
                Offset = $offset
            }
            $offset += $bytes.Length
        }

        foreach ($entry in $imageData) {
            $writer.Write([byte]([Math]::Min($entry.Size, 256) % 256))
            $writer.Write([byte]([Math]::Min($entry.Size, 256) % 256))
            $writer.Write([byte]0)
            $writer.Write([byte]0)
            $writer.Write([UInt16]1)
            $writer.Write([UInt16]32)
            $writer.Write([UInt32]$entry.Bytes.Length)
            $writer.Write([UInt32]$entry.Offset)
        }

        foreach ($entry in $imageData) {
            $writer.Write($entry.Bytes)
        }
    }
    finally {
        $writer.Dispose()
        $fileStream.Dispose()
    }
}

$assetsDirectory = [System.IO.Path]::GetFullPath($AssetsDirectory)
$temporaryAssetsDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ("Auralis.IconBuild." + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $assetsDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $temporaryAssetsDirectory -Force | Out-Null

$masterSize = 1024
$masterBitmap = New-Object System.Drawing.Bitmap $masterSize, $masterSize
$graphics = [System.Drawing.Graphics]::FromImage($masterBitmap)

try {
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $graphics.Clear([System.Drawing.Color]::Transparent)

    $shadowPath = New-RoundedRectanglePath ([System.Drawing.RectangleF]::new(154, 162, 716, 716)) 112
    $shellPath = New-RoundedRectanglePath ([System.Drawing.RectangleF]::new(144, 144, 716, 716)) 112

    $shadowBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(42, 3, 6, 18))
    $graphics.FillPath($shadowBrush, $shadowPath)

    $backgroundBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        [System.Drawing.PointF]::new(140, 140),
        [System.Drawing.PointF]::new(860, 860),
        [System.Drawing.Color]::FromArgb(255, 7, 20, 45),
        [System.Drawing.Color]::FromArgb(255, 10, 34, 63))
    $backgroundBrush.SetBlendTriangularShape(0.58, 0.95)
    $graphics.FillPath($backgroundBrush, $shellPath)

    $highlightPen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(80, 124, 178, 255), 5)
    $graphics.DrawPath($highlightPen, $shellPath)

    $glowBrush = New-Object System.Drawing.Drawing2D.PathGradientBrush($shellPath)
    $glowBrush.CenterColor = [System.Drawing.Color]::FromArgb(34, 34, 152, 255)
    $glowBrush.SurroundColors = @([System.Drawing.Color]::FromArgb(0, 0, 0, 0))
    $graphics.FillEllipse($glowBrush, 255, 210, 520, 520)

    $outerPoints = @(
        [System.Drawing.PointF]::new(334, 748),
        [System.Drawing.PointF]::new(514, 266),
        [System.Drawing.PointF]::new(698, 748)
    )

    $innerPoints = @(
        [System.Drawing.PointF]::new(476, 640),
        [System.Drawing.PointF]::new(565, 402),
        [System.Drawing.PointF]::new(650, 640)
    )

    $rightLegPoints = @(
        [System.Drawing.PointF]::new(676, 618),
        [System.Drawing.PointF]::new(756, 748)
    )

    $outerShadowPen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(78, 4, 8, 20), 86)
    $outerShadowPen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $outerShadowPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $outerShadowPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $graphics.TranslateTransform(12, 14)
    $graphics.DrawLines($outerShadowPen, $outerPoints)
    $graphics.DrawLines($outerShadowPen, $innerPoints)
    $graphics.DrawLines($outerShadowPen, $rightLegPoints)
    $graphics.ResetTransform()

    $gradientRectangle = [System.Drawing.RectangleF]::new(296, 250, 500, 520)
    $symbolBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        $gradientRectangle,
        [System.Drawing.Color]::FromArgb(255, 56, 103, 255),
        [System.Drawing.Color]::FromArgb(255, 72, 238, 212),
        315)

    $outerPen = New-Object System.Drawing.Pen($symbolBrush, 76)
    $outerPen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $outerPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $outerPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $graphics.DrawLines($outerPen, $outerPoints)

    $innerPen = New-Object System.Drawing.Pen($symbolBrush, 52)
    $innerPen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $innerPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $innerPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $graphics.DrawLines($innerPen, $innerPoints)
    $graphics.DrawLines($innerPen, $rightLegPoints)

    $centerGlowPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(120, 152, 246, 255), 10)
    $centerGlowPen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $centerGlowPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $centerGlowPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $graphics.DrawLines($centerGlowPen, @(
        [System.Drawing.PointF]::new(520, 286),
        [System.Drawing.PointF]::new(642, 618)))

    $masterPngPath = Join-Path $assetsDirectory "AppIcon.png"
    $masterBitmap.Save($masterPngPath, [System.Drawing.Imaging.ImageFormat]::Png)
}
finally {
    $graphics.Dispose()
    $masterBitmap.Dispose()
}

$iconSizes = @(16, 32, 48, 64, 128, 256)
$scaledPngs = @{}

foreach ($iconSize in $iconSizes) {
    $outputPath = Join-Path $temporaryAssetsDirectory ("AppIcon.{0}.png" -f $iconSize)
    $bitmap = New-Object System.Drawing.Bitmap $iconSize, $iconSize
    $g = [System.Drawing.Graphics]::FromImage($bitmap)
    $source = [System.Drawing.Image]::FromFile((Join-Path $assetsDirectory "AppIcon.png"))

    try {
        $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $g.Clear([System.Drawing.Color]::Transparent)
        $g.DrawImage($source, 0, 0, $iconSize, $iconSize)
        $bitmap.Save($outputPath, [System.Drawing.Imaging.ImageFormat]::Png)
        $scaledPngs[$iconSize] = $outputPath
    }
    finally {
        $source.Dispose()
        $g.Dispose()
        $bitmap.Dispose()
    }
}

New-MultiSizeIco -OutputPath (Join-Path $assetsDirectory "AppIcon.ico") -PngFilesBySize $scaledPngs

Remove-Item -Path $temporaryAssetsDirectory -Recurse -Force -ErrorAction SilentlyContinue

Write-Output (Join-Path $assetsDirectory "AppIcon.png")
Write-Output (Join-Path $assetsDirectory "AppIcon.ico")
