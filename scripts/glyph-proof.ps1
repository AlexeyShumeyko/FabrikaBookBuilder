# Renders candidate Segoe MDL2 Assets glyph codes to a PNG so they can be eyeballed.
# Guessing MDL2 codepoints is unreliable; this makes the choice verifiable.
#
# Usage:  pwsh -File scripts/glyph-proof.ps1 -Codes E8B7,E74E -Out doc\shots\glyphs.png

param(
    [string]$Codes = 'E700,E713,E715,E721,E722,E734,E8B7,E8C8,E8DA,E8E5,E71C,E71D,E74E,E777,E82D,E898,E8AB,E8BD,E7A7,E72A,E945,E8F1,E8F0,E946',
    [string]$Out = 'doc\shots\glyphs.png',
    [int]$Size = 28
)

# The harness console runs on code page 866, which turns every Cyrillic string this
# script prints into "?" and floods the agent context with mojibake. Force UTF-8 on
# both channels; a child powershell.exe resets these on its own, so it has to be set
# inside each script rather than once in the caller.
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$font = New-Object System.Drawing.Font('Segoe MDL2 Assets', $Size)
$small = New-Object System.Drawing.Font('Segoe UI', 8)
$list = $Codes.Split(',') | ForEach-Object { $_.Trim() } | Where-Object { $_ }

$cols = 6
$rows = [Math]::Ceiling($list.Count / $cols)
$cellW = 110; $cellH = 70
$w = $cols * $cellW; $h = $rows * $cellH

$bmp = New-Object System.Drawing.Bitmap $w, $h
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.Clear([System.Drawing.Color]::White)
$g.TextRenderingHint = 'AntiAliasGridFit'

$brush = [System.Drawing.Brushes]::Black
$grey = [System.Drawing.Brushes]::DimGray

for ($i = 0; $i -lt $list.Count; $i++) {
    $code = $list[$i]
    $x = ($i % $cols) * $cellW
    $y = [Math]::Floor($i / $cols) * $cellH

    $ch = [char][Convert]::ToInt32($code, 16)
    $sf = [System.Drawing.StringFormat]::new()
    $sf.Alignment = 'Center'
    $sf.LineAlignment = 'Center'
    $g.DrawString([string]$ch, $font, $brush, (New-Object System.Drawing.RectangleF($x, $y, $cellW, 42)), $sf)
    $sf.Dispose()
    $g.DrawString("U+$code", $small, $grey, (New-Object System.Drawing.RectangleF($x, ($y + 42), $cellW, 20)), $sf)
}

$full = Join-Path (Split-Path -Parent $PSScriptRoot) $Out
$dir = Split-Path -Parent $full
if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
$bmp.Save($full, [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose(); $font.Dispose(); $small.Dispose()
Write-Host "saved $full"
