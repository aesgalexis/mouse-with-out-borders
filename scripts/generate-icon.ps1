param(
    [Parameter(Mandatory=$true)][string]$OutputPath,
    [string]$PreviewDirectory
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

# The checked-in vector is the single source for the app, tray and Setup icons.
[xml]$source = Get-Content -LiteralPath (Join-Path $PSScriptRoot '../MouseWithOutBorders/assets/app.svg') -Raw
$shapes = $source.svg.g
$folder = Split-Path -Parent ([IO.Path]::GetFullPath($OutputPath))
New-Item -ItemType Directory -Path $folder -Force | Out-Null
if ($PreviewDirectory) { New-Item -ItemType Directory -Path $PreviewDirectory -Force | Out-Null }
$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$images = [System.Collections.Generic.List[byte[]]]::new()
$blue = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml($shapes.fill))
$line = [System.Drawing.Pen]::new($blue.Color, [float]$shapes.'stroke-width')
$line.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
$line.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
try {
    foreach ($size in $sizes) {
        # Render from the vector at four times the target resolution.
        $canvas = [System.Drawing.Bitmap]::new($size * 4, $size * 4)
        $graphics = [System.Drawing.Graphics]::FromImage($canvas)
        $bitmap = [System.Drawing.Bitmap]::new($size, $size)
        $scaled = [System.Drawing.Graphics]::FromImage($bitmap)
        $stream = [IO.MemoryStream]::new()
        try {
            $graphics.Clear([System.Drawing.Color]::Transparent)
            $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
            $graphics.ScaleTransform($size / 16.0, $size / 16.0)
            foreach ($segment in $shapes.line) {
                $graphics.DrawLine($line, [float]$segment.x1, [float]$segment.y1,
                    [float]$segment.x2, [float]$segment.y2)
            }
            [System.Drawing.PointF[]]$points = @($shapes.polygon.points.Split(' ') | ForEach-Object {
                $xy = $_.Split(',')
                [System.Drawing.PointF]::new([float]$xy[0], [float]$xy[1])
            })
            $graphics.FillPolygon($blue, $points)
            $scaled.Clear([System.Drawing.Color]::Transparent)
            $scaled.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
            $scaled.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $scaled.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $scaled.DrawImage($canvas, 0, 0, $size, $size)
            $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
            $images.Add($stream.ToArray())
            if ($PreviewDirectory) {
                $bitmap.Save((Join-Path $PreviewDirectory "icon-$size.png"), [System.Drawing.Imaging.ImageFormat]::Png)
            }
        } finally {
            $stream.Dispose()
            $scaled.Dispose()
            $bitmap.Dispose()
            $graphics.Dispose()
            $canvas.Dispose()
        }
    }
    $output = [IO.File]::Create([IO.Path]::GetFullPath($OutputPath))
    $writer = [IO.BinaryWriter]::new($output)
    try {
        $writer.Write([uint16]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]$sizes.Count)
        $offset = 6 + 16 * $sizes.Count
        for ($i = 0; $i -lt $sizes.Count; $i++) {
            $dimension = if ($sizes[$i] -eq 256) { 0 } else { $sizes[$i] }
            $writer.Write([byte]$dimension)
            $writer.Write([byte]$dimension)
            $writer.Write([byte]0)
            $writer.Write([byte]0)
            $writer.Write([uint16]1)
            $writer.Write([uint16]32)
            $writer.Write([uint32]$images[$i].Length)
            $writer.Write([uint32]$offset)
            $offset += $images[$i].Length
        }
        foreach ($bytes in $images) { $writer.Write($bytes) }
    } finally { $writer.Dispose() }
} finally {
    $line.Dispose()
    $blue.Dispose()
}
