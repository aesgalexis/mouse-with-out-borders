param(
    [Parameter(Mandatory=$true)][string]$OutputPath
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$folder = Split-Path -Parent $OutputPath
New-Item -ItemType Directory -Path $folder -Force | Out-Null

$bitmap = [System.Drawing.Bitmap]::new(64, 64)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$graphics.Clear([System.Drawing.Color]::Transparent)
$blue = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(32, 113, 226))
$white = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::White)
$line = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(32, 113, 226), 3)
try {
    $graphics.FillEllipse($blue, 2, 10, 60, 44)
    $graphics.FillEllipse($white, 21, 6, 22, 50)
    $graphics.DrawLine($line, 32, 12, 32, 26)
    $graphics.DrawLine([System.Drawing.Pens]::White, 5, 32, 15, 32)
    $graphics.DrawLine([System.Drawing.Pens]::White, 49, 32, 59, 32)
    $graphics.Flush()

    $pngStream = [System.IO.MemoryStream]::new()
    try {
        $bitmap.Save($pngStream, [System.Drawing.Imaging.ImageFormat]::Png)
        $bytes = $pngStream.ToArray()
        $output = [System.IO.File]::Create($OutputPath)
        try {
            $writer = [System.IO.BinaryWriter]::new($output)
            $writer.Write([uint16]0)
            $writer.Write([uint16]1)
            $writer.Write([uint16]1)
            $writer.Write([byte]64)
            $writer.Write([byte]64)
            $writer.Write([byte]0)
            $writer.Write([byte]0)
            $writer.Write([uint16]1)
            $writer.Write([uint16]32)
            $writer.Write([uint32]$bytes.Length)
            $writer.Write([uint32]22)
            $writer.Write($bytes)
            $writer.Flush()
        } finally { $output.Dispose() }
    } finally { $pngStream.Dispose() }
} finally {
    $graphics.Dispose()
    $bitmap.Dispose()
    $blue.Dispose()
    $white.Dispose()
    $line.Dispose()
}
