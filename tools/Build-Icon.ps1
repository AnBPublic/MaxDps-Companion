$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$output = Join-Path $root 'app/MaxDpsCompanion/assets'
Add-Type -AssemblyName System.Drawing
$source = [Drawing.Bitmap]::new((Join-Path $root 'assets/companion-master.png'))
$frames = @()
try {
    foreach ($size in @(16, 20, 24, 32, 40, 48, 64, 128, 256)) {
        $bitmap = [Drawing.Bitmap]::new($size, $size, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $graphics = [Drawing.Graphics]::FromImage($bitmap)
        $stream = [IO.MemoryStream]::new()
        try {
            $graphics.CompositingMode = [Drawing.Drawing2D.CompositingMode]::SourceCopy
            $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $graphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $graphics.DrawImage($source, 0, 0, $size, $size)
            $bitmap.Save($stream, [Drawing.Imaging.ImageFormat]::Png)
            $frames += ,@{ Size = $size; Bytes = $stream.ToArray() }
            if ($size -eq 256) { [IO.File]::WriteAllBytes((Join-Path $output 'icon-256.png'), $stream.ToArray()) }
        } finally { $stream.Dispose(); $graphics.Dispose(); $bitmap.Dispose() }
    }
    $writer = [IO.BinaryWriter]::new([IO.File]::Create((Join-Path $output 'companion.ico')))
    try {
        $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$frames.Count)
        $offset = 6 + 16 * $frames.Count
        foreach ($frame in $frames) {
            $dimension = if ($frame.Size -eq 256) { 0 } else { $frame.Size }
            $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
            $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]32)
            $writer.Write([uint32]$frame.Bytes.Length); $writer.Write([uint32]$offset)
            $offset += $frame.Bytes.Length
        }
        foreach ($frame in $frames) { $writer.Write([byte[]]$frame.Bytes) }
    } finally { $writer.Dispose() }
} finally { $source.Dispose() }
Write-Output 'Generated icon-256.png and companion.ico (16 through 256 px, nine sizes).'
$macIcon = Join-Path $root 'app/MaxDpsCompanion.Mac/assets/AppIcon.icns'
Copy-Item -LiteralPath (Join-Path $root 'assets/companion-master.png') -Destination (Join-Path $root 'app/MaxDpsCompanion.Mac/assets/icon-source-1024.png')
function Write-BigEndian($writer, [int]$value) {
    $bytes = [BitConverter]::GetBytes($value)
    [Array]::Reverse($bytes)
    $writer.Write($bytes)
}
$small = [IO.File]::ReadAllBytes((Join-Path $output 'icon-256.png'))
$largeBitmap = [Drawing.Bitmap]::new((Join-Path $root 'assets/companion-master.png'))
$largeStream = [IO.MemoryStream]::new()
try {
    $largeBitmap.Save($largeStream, [Drawing.Imaging.ImageFormat]::Png)
    $large = $largeStream.ToArray()
} finally { $largeStream.Dispose(); $largeBitmap.Dispose() }
$writer = [IO.BinaryWriter]::new([IO.File]::Create($macIcon))
try {
    $writer.Write([Text.Encoding]::ASCII.GetBytes('icns'))
    Write-BigEndian $writer (24 + $small.Length + $large.Length)
    $writer.Write([Text.Encoding]::ASCII.GetBytes('ic08'))
    Write-BigEndian $writer (8 + $small.Length)
    $writer.Write($small)
    $writer.Write([Text.Encoding]::ASCII.GetBytes('ic10'))
    Write-BigEndian $writer (8 + $large.Length)
    $writer.Write($large)
} finally { $writer.Dispose() }
Write-Output 'Generated macOS ICNS (256 and 1024 px).'
