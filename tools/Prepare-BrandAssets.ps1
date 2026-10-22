param(
    [string]$Source = (Join-Path $PSScriptRoot '..\src\Snap\Assets\Snap-logo.png')
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$assetDirectory = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\src\Snap\Assets'))
$wizardDirectory = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\installer\assets'))
New-Item -ItemType Directory -Path $assetDirectory, $wizardDirectory -Force | Out-Null
$logo = [System.Drawing.Image]::FromFile([System.IO.Path]::GetFullPath($Source))
try {
    if ($logo.Width -ne $logo.Height) { throw 'The app icon source must be square.' }
    $frames = @()
    foreach ($size in @(16, 20, 24, 32, 40, 48, 64, 128, 256)) {
        $bitmap = [System.Drawing.Bitmap]::new($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        try {
            $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
            $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $graphics.Clear([System.Drawing.Color]::Transparent)
            $graphics.DrawImage($logo, [System.Drawing.Rectangle]::new(0, 0, $size, $size))
            $stream = [System.IO.MemoryStream]::new()
            try {
                $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
                $frames += ,@($size, $stream.ToArray())
            } finally { $stream.Dispose() }
        } finally { $graphics.Dispose(); $bitmap.Dispose() }
    }
    $writer = [System.IO.BinaryWriter]::new([System.IO.File]::Create((Join-Path $assetDirectory 'Snap-app.ico')))
    try {
        $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$frames.Count)
        $offset = 6 + 16 * $frames.Count
        foreach ($frame in $frames) {
            $dimension = if ($frame[0] -eq 256) { 0 } else { $frame[0] }
            $writer.Write([byte]$dimension); $writer.Write([byte]$dimension); $writer.Write([byte]0); $writer.Write([byte]0)
            $writer.Write([uint16]1); $writer.Write([uint16]32)
            $writer.Write([uint32]$frame[1].Length); $writer.Write([uint32]$offset)
            $offset += $frame[1].Length
        }
        foreach ($frame in $frames) { $writer.Write([byte[]]$frame[1]) }
    } finally { $writer.Dispose() }

    # Layout the unchanged generated mark on a native setup-wizard canvas.
    $canvas = [System.Drawing.Bitmap]::new(328, 628)
    $graphics = [System.Drawing.Graphics]::FromImage($canvas)
    $nameFont = [System.Drawing.Font]::new('Segoe UI', 34, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
    $captionFont = [System.Drawing.Font]::new('Segoe UI', 17, [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Pixel)
    $format = [System.Drawing.StringFormat]::new()
    $muted = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(174, 178, 188))
    try {
        $graphics.Clear([System.Drawing.Color]::FromArgb(24, 27, 34))
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
        $graphics.DrawImage($logo, [System.Drawing.Rectangle]::new(44, 104, 240, 240))
        $format.Alignment = [System.Drawing.StringAlignment]::Center
        $graphics.DrawString('Snap', $nameFont, [System.Drawing.Brushes]::White, [System.Drawing.RectangleF]::new(0, 374, 328, 55), $format)
        $graphics.DrawString('Capture. Simply.', $captionFont, $muted, [System.Drawing.RectangleF]::new(0, 434, 328, 40), $format)
        $canvas.Save((Join-Path $wizardDirectory 'wizard-large.bmp'), [System.Drawing.Imaging.ImageFormat]::Bmp)
    } finally { $graphics.Dispose(); $canvas.Dispose(); $nameFont.Dispose(); $captionFont.Dispose(); $format.Dispose(); $muted.Dispose() }

    $small = [System.Drawing.Bitmap]::new(147, 147, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($small)
    try {
        $graphics.Clear([System.Drawing.Color]::Transparent)
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.DrawImage($logo, [System.Drawing.Rectangle]::new(0, 0, 147, 147))
        $small.Save((Join-Path $wizardDirectory 'wizard-small.png'), [System.Drawing.Imaging.ImageFormat]::Png)
    } finally { $graphics.Dispose(); $small.Dispose() }
} finally { $logo.Dispose() }
Write-Output 'Prepared transparent Windows icons (16–256 px) and setup branding.'
