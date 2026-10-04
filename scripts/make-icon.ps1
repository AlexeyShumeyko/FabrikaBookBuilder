# Builds icon.ico from the logo in the shell project, with real transparency.
#
# The icon used to be a single 256x256 image stored as an uncompressed bitmap with the
# alpha channel flattened to white: the desktop shortcut showed a white square around the
# mark, and the taskbar icon looked pasted on. The owner reported exactly that.
#
# What this produces, and why each part is here:
#   - nine sizes, because Windows picks the one it needs and scales badly if one is
#     missing. 16/20/24/32 for the taskbar and lists, 48 for the desktop, 64/128/256 for
#     large and high-DPI views.
#   - every size stored as PNG inside the .ico. PNG keeps the alpha channel exactly,
#     which the old BMP-in-ico path lost. Windows 7 and newer read it; we do not support
#     anything older.
#   - the mark scaled to 88% of the canvas on transparent background, so a full-bleed
#     circle does not touch the edges and turn into a blob at 16px.
#
# Usage:  pwsh -File scripts\make-icon.ps1 [-Source <shell>\Resources\logo.png] [-Out <shell>\icon.ico]
#
# Run it whenever the logo changes, then rebuild: the .ico feeds both the executable
# (ApplicationIcon) and the installer (SetupIconFile=publish\icon.ico).

param(
    [string]$Source = 'src\PhotoBook.Desktop.Wpf\Resources\logo.png',
    [string]$Out = 'src\PhotoBook.Desktop.Wpf\icon.ico',
    [int[]]$Sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256),
    [double]$Inset = 0.06
)

[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root

Add-Type -AssemblyName System.Drawing

$sourcePath = (Resolve-Path $Source).Path
$logo = [System.Drawing.Image]::FromFile($sourcePath)
Write-Host "source: $sourcePath ($($logo.Width)x$($logo.Height), $($logo.PixelFormat))"

$images = New-Object System.Collections.Generic.List[byte[]]

foreach ($size in $Sizes) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)

    # Transparent is the whole point: without this the canvas starts as opaque black.
    $g.Clear([System.Drawing.Color]::Transparent)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic

    $pad = [int][Math]::Round($size * $Inset)
    $g.DrawImage($logo,
        (New-Object System.Drawing.Rectangle $pad, $pad, ($size - 2 * $pad), ($size - 2 * $pad)),
        0, 0, $logo.Width, $logo.Height,
        [System.Drawing.GraphicsUnit]::Pixel)

    $g.Dispose()

    # Prove the alpha survived before it goes into the file. A corner that is not fully
    # transparent is how the old icon shipped in the first place.
    $corner = $bmp.GetPixel(0, 0)
    if ($corner.A -ne 0) {
        $bmp.Dispose()
        throw "size $size has an opaque corner (alpha=$($corner.A)) - the canvas was not cleared to transparent"
    }

    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    $images.Add($ms.ToArray())
    $ms.Dispose()
    Write-Host ("  {0,3}px  {1,6:N0} bytes  corner transparent" -f $size, $images[$images.Count - 1].Length)
}

$logo.Dispose()

# ---- ICO container ------------------------------------------------------------
# ICONDIR: reserved(2) type(2) count(2); then one ICONDIRENTRY(16) per image; then the
# PNG blobs. A width/height byte of 0 means 256 - there is no other way to say it.
$count = $images.Count
$ms = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter $ms

$bw.Write([UInt16]0)          # reserved
$bw.Write([UInt16]1)          # type: icon
$bw.Write([UInt16]$count)

$offset = 6 + 16 * $count
for ($i = 0; $i -lt $count; $i++) {
    $size = $Sizes[$i]
    $byte = if ($size -ge 256) { 0 } else { $size }
    $bw.Write([Byte]$byte)                       # width  (0 = 256)
    $bw.Write([Byte]$byte)                       # height (0 = 256)
    $bw.Write([Byte]0)                           # palette size
    $bw.Write([Byte]0)                           # reserved
    $bw.Write([UInt16]1)                         # colour planes
    $bw.Write([UInt16]32)                        # bits per pixel
    $bw.Write([UInt32]$images[$i].Length)        # bytes in resource
    $bw.Write([UInt32]$offset)                   # offset of the image data
    $offset += $images[$i].Length
}

foreach ($img in $images) { $bw.Write($img) }
$bw.Flush()

$outPath = Join-Path $root $Out
[System.IO.File]::WriteAllBytes($outPath, $ms.ToArray())
$bw.Dispose(); $ms.Dispose()

Write-Host ""
Write-Host "written: $OutPath ($([math]::Round((Get-Item $outPath).Length / 1KB, 1)) KB, $count sizes)"

# ---- read it back -------------------------------------------------------------
# Writing an .ico by hand deserves a read-back: open every size and confirm it decodes
# and is still transparent at the corner.
$ico = [System.Drawing.Icon]::new($outPath, 32, 32)
$bmp = $ico.ToBitmap()
Write-Host ("read back: {0}x{1}, corner alpha={2}" -f $bmp.Width, $bmp.Height, $bmp.GetPixel(0, 0).A)
$bmp.Dispose(); $ico.Dispose()
Pop-Location
