$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$frames = @()
foreach ($size in @(16, 32, 48, 256)) {
    $bitmap = [System.Drawing.Bitmap]::new($size, $size)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.ScaleTransform($size / 64.0, $size / 64.0)
    $shape = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $shape.AddArc(2, 2, 24, 24, 180, 90)
    $shape.AddArc(38, 2, 24, 24, 270, 90)
    $shape.AddArc(38, 38, 24, 24, 0, 90)
    $shape.AddArc(2, 38, 24, 24, 90, 90)
    $shape.CloseFigure()
    $orange = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(233,84,32))
    $graphics.FillPath($orange, $shape)
    $pen = [System.Drawing.Pen]::new([System.Drawing.Color]::White, 3.5)
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    foreach ($points in @(@(16,25,16,16),@(16,16,25,16),@(39,16,48,16),@(48,16,48,25),@(48,39,48,48),@(48,48,39,48),@(25,48,16,48),@(16,48,16,39))) {
        $graphics.DrawLine($pen, $points[0], $points[1], $points[2], $points[3])
    }
    $graphics.FillEllipse([System.Drawing.Brushes]::White, 27, 27, 10, 10)
    $stream = [System.IO.MemoryStream]::new()
    $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $frames += ,@($size, $stream.ToArray())
    $stream.Dispose(); $pen.Dispose(); $orange.Dispose(); $shape.Dispose(); $graphics.Dispose(); $bitmap.Dispose()
}
$outputDir = Join-Path $PSScriptRoot '..\src\Snap\Assets'
New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
$stream = [System.IO.File]::Create((Join-Path $outputDir 'Snap.ico'))
$writer = [System.IO.BinaryWriter]::new($stream)
$writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$frames.Count)
$offset = 6 + 16 * $frames.Count
foreach ($frame in $frames) {
    $dimension = if ($frame[0] -eq 256) { 0 } else { $frame[0] }
    $writer.Write([byte]$dimension); $writer.Write([byte]$dimension); $writer.Write([byte]0); $writer.Write([byte]0)
    $writer.Write([uint16]1); $writer.Write([uint16]32); $writer.Write([uint32]$frame[1].Length); $writer.Write([uint32]$offset)
    $offset += $frame[1].Length
}
foreach ($frame in $frames) { $writer.Write([byte[]]$frame[1]) }
$writer.Dispose()
