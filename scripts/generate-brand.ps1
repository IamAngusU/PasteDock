[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$root = Split-Path -Parent $PSScriptRoot
$assets = Join-Path $root 'src/PasteDock.Desktop/Assets'
$brand = Join-Path $root 'docs/assets'
New-Item -ItemType Directory -Force -Path $assets, $brand | Out-Null

function New-RoundedPath([single]$X, [single]$Y, [single]$W, [single]$H, [single]$R) {
    $path = [Drawing.Drawing2D.GraphicsPath]::new()
    $d = 2 * $R
    $path.AddArc($X, $Y, $d, $d, 180, 90)
    $path.AddArc($X + $W - $d, $Y, $d, $d, 270, 90)
    $path.AddArc($X + $W - $d, $Y + $H - $d, $d, $d, 0, 90)
    $path.AddArc($X, $Y + $H - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    return ,$path
}
$images = @()
foreach ($size in @(16, 24, 32, 48, 64, 128, 256)) {
    $bitmap = [Drawing.Bitmap]::new($size, $size)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = 'AntiAlias'
    $graphics.ScaleTransform($size / 256.0, $size / 256.0)
    $purple = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#5B48D6'))
    $pale = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#BEB5FF'))
    $white = [Drawing.SolidBrush]::new([Drawing.Color]::White)
    $background = New-RoundedPath 0 0 256 256 60
    $back = New-RoundedPath 99 46 101 134 18
    $front = New-RoundedPath 57 76 101 134 18
    $graphics.FillPath($purple, $background)
    $graphics.FillPath($pale, $back)
    $graphics.FillPath($white, $front)
    $stroke = [Drawing.Pen]::new([Drawing.ColorTranslator]::FromHtml('#5B48D6'), 9)
    $stroke.StartCap = $stroke.EndCap = 'Round'
    $graphics.DrawLine($stroke, 81, 110, 133, 110)
    $graphics.DrawLine($stroke, 81, 136, 123, 136)
    $graphics.DrawLine($stroke, 81, 162, 111, 162)
    $stream = [IO.MemoryStream]::new()
    $bitmap.Save($stream, [Drawing.Imaging.ImageFormat]::Png)
    $images += ,$stream.ToArray()
    if ($size -eq 256) {
        $bitmap.Save((Join-Path $brand 'pastedock-icon.png'), [Drawing.Imaging.ImageFormat]::Png)
        $bitmap.Save((Join-Path $assets 'PasteDock.png'), [Drawing.Imaging.ImageFormat]::Png)
    }
    $stream.Dispose(); $graphics.Dispose(); $bitmap.Dispose()
    $purple.Dispose(); $pale.Dispose(); $white.Dispose(); $stroke.Dispose()
    $background.Dispose(); $back.Dispose(); $front.Dispose()
}
$file = [IO.File]::Create((Join-Path $assets 'PasteDock.ico'))
$writer = [IO.BinaryWriter]::new($file)
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$images.Count)
    $offset = 6 + 16 * $images.Count
    $sizes = @(16, 24, 32, 48, 64, 128, 256)
    for ($i = 0; $i -lt $images.Count; $i++) {
        $dimension = if ($sizes[$i] -eq 256) { 0 } else { $sizes[$i] }
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
        $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32)
        $writer.Write([uint32]$images[$i].Length); $writer.Write([uint32]$offset)
        $offset += $images[$i].Length
    }
    foreach ($bytes in $images) { $writer.Write([byte[]]$bytes) }
} finally { $writer.Dispose(); $file.Dispose() }
