#  Puts the reference screenshot and the build side by side, pixel for pixel, and
#  reports where they differ.
#
#    pwsh -File scripts\compare-to-golden.ps1 -Build doc\shots\av-list.png `
#                                              -Golden doc\shots\gate-Мои_проекты.png
#
#  Why this exists
#
#  Comparing two screens by looking at them answers "does this look right", which is a
#  question about the whole picture. The defects that were found this way were the ones
#  that are obvious - a missing card, a button in the wrong place. The ones that are not
#  obvious are exactly the ones worth finding: a padding that is one pixel out, a colour
#  that is close but not equal, a card that is eight pixels narrower than the one beside
#  it. Those were found by arithmetic and by reading the other shell's markup, one number
#  at a time, and each one cost a whole round of changes.
#
#  A diff finds them all at once, and it says where rather than that.
#
#  What it does and does not promise
#
#  It compares two images of the same screen taken at the same size. It does not know what
#  any pixel should be, so a difference is a question, not a verdict: photographs differ
#  between runs, and a screen with real data will differ from one with test data in every
#  place the data is drawn. What it reports honestly is the SIZE and the PLACE of each
#  difference, which is enough to know whether a screen is merely different or is wrong in
#  a specific spot.
#
#  A band that is a few pixels of colour along one edge is a shadow or a hairline. A band
#  that is thirty pixels tall across the full width is a layout that is wrong.
#
param(
    [Parameter(Mandatory)][string]$Build,
    [Parameter(Mandatory)][string]$Golden,

    # Where to write the result. Defaults to the build's own name with -diff.
    [string]$Out = '',

    # Differences below this are noise: one channel's worth, out of 255.
    [int]$Tolerance = 24,

    # How tall a band of consecutive differing rows is before it is worth naming.
    [int]$MinimumBand = 6
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8
. "$PSScriptRoot\common.ps1"

Add-Type -AssemblyName System.Drawing

$buildPath = if ([System.IO.Path]::IsPathRooted($Build)) { $Build } else { Join-Path (Get-RepoRoot) $Build }
$goldenPath = if ([System.IO.Path]::IsPathRooted($Golden)) { $Golden } else { Join-Path (Get-RepoRoot) $Golden }
$outPath = if ($Out) {
    if ([System.IO.Path]::IsPathRooted($Out)) { $Out } else { Join-Path (Get-RepoRoot) $Out }
}
else {
    [System.IO.Path]::ChangeExtension($buildPath, '.diff.png')
}

foreach ($p in @($buildPath, $goldenPath)) {
    if (-not (Test-Path $p)) { throw "no image at $p" }
}

$a = [System.Drawing.Bitmap]::FromFile($goldenPath)
$b = [System.Drawing.Bitmap]::FromFile($buildPath)
try {
    Write-Host ("golden {0} {1}x{2}" -f (Split-Path $goldenPath -Leaf), $a.Width, $a.Height)
    Write-Host ("build  {0} {1}x{2}" -f (Split-Path $buildPath -Leaf), $b.Width, $b.Height)

    if ($a.Width -ne $b.Width -or $a.Height -ne $b.Height) {
        throw ("the two images are not the same size ({0}x{1} and {2}x{3}). " -f $a.Width, $a.Height, $b.Width, $b.Height) +
              "A diff of two different sizes is a picture of the resize, not of the difference."
    }

    $w = $a.Width
    $h = $a.Height

    # Row by row: how much of the row differs at all, and where.
    $rowDiff = New-Object 'int[]' $h
    $rowFirstX = New-Object 'int[]' $h
    $rowLastX = New-Object 'int[]' $h
    $mask = New-Object System.Drawing.Bitmap $w, $h
    $diffPixels = 0

    for ($y = 0; $y -lt $h; $y++) {
        $first = -1
        $last = -1

        for ($x = 0; $x -lt $w; $x++) {
            $ca = $a.GetPixel($x, $y)
            $cb = $b.GetPixel($x, $y)

            $d = [Math]::Abs($ca.R - $cb.R)
            if ([Math]::Abs($ca.G - $cb.G) -gt $d) { $d = [Math]::Abs($ca.G - $cb.G) }
            if ([Math]::Abs($ca.B - $cb.B) -gt $d) { $d = [Math]::Abs($ca.B - $cb.B) }

            if ($d -le $Tolerance) {
                # Unchanged: the reference's own colours, dimmed so the differences read.
                $grey = [int](0.25 * $ca.R + 0.75 * 255)
                $mask.SetPixel($x, $y, [System.Drawing.Color]::FromArgb($grey, $grey, $grey))
                continue
            }

            $diffPixels++
            if ($first -lt 0) { $first = $x }
            $last = $x

            # How different, as brightness: a changed layout is white, a changed shade is red.
            $strength = [Math]::Min(255, 128 + $d)
            $mask.SetPixel($x, $y, [System.Drawing.Color]::FromArgb(255, $strength, 40))
        }

        if ($last -lt 0) { $rowDiff[$y] = 0 } else { $rowDiff[$y] = $last - $first + 1 }
        $rowFirstX[$y] = $first
        $rowLastX[$y] = $last
    }

    # Bands: consecutive rows that differ, taller than MinimumBand. A band's extent says
    # what kind of thing it is - full width is a layout, a column is a single element.
    Write-Host ''
    Write-Host ("differing pixels : {0:N0} of {1:N0} ({2:P2})" -f $diffPixels, ($w * $h), ($diffPixels / ($w * $h)))

    $bands = @()
    $start = -1
    for ($y = 0; $y -le $h; $y++) {
        $differs = $y -lt $h -and $rowDiff[$y] -gt 0

        if ($differs -and $start -lt 0) { $start = $y }
        if (-not $differs -and $start -ge 0) {
            $end = $y - 1
            $x0 = [int]::MaxValue
            $x1 = -1
            for ($k = $start; $k -le $end; $k++) {
                if ($rowFirstX[$k] -ge 0 -and $rowFirstX[$k] -lt $x0) { $x0 = $rowFirstX[$k] }
                if ($rowLastX[$k] -gt $x1) { $x1 = $rowLastX[$k] }
            }
            if ($x0 -gt $x1) { $x0 = 0; $x1 = 0 }

            $bands += [pscustomobject]@{
                Y = $start
                Height = $end - $start + 1
                X = $x0
                Width = $x1 - $x0 + 1
                Span = ($x1 - $x0 + 1) / $w
            }
            $start = -1
        }
    }

    $named = @($bands | Where-Object { $_.Height -ge $MinimumBand })

    Write-Host ("bands {0:N0} px tall or more : {1}" -f $MinimumBand, $named.Count)
    Write-Host ''

    foreach ($band in $named) {
        $kind =
            if ($band.Span -gt 0.85) { 'across the whole width - a layout' }
            elseif ($band.Span -gt 0.4) { 'over most of the width - a row or a section' }
            elseif ($band.Width -lt 120) { 'a narrow column - one element' }
            else { 'a block - one element or a group' }

        Write-Host ("  y {0,4}..{1,-4} ({2,3} tall)  x {3,4}..{4,-4} ({5,4} wide)  {6}" -f `
            $band.Y, ($band.Y + $band.Height - 1), $band.Height, $band.X, ($band.X + $band.Width - 1), $band.Width, $kind)
    }

    # The result: the reference on the left, the build on the right, differences in the
    # middle. One image, because looking at three is how a difference goes unnoticed in one
    # of them.
    $canvas = New-Object System.Drawing.Bitmap ($w * 3), $h
    $g = [System.Drawing.Graphics]::FromImage($canvas)
    try {
        $g.Clear([System.Drawing.Color]::FromArgb(24, 24, 27))
        $g.DrawImage($a, 0, 0)
        $g.DrawImage($b, $w * 2, 0)
        $g.DrawImage($mask, $w, 0)

        $font = New-Object System.Drawing.Font('Consolas', 13)
        $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White, 2)
        $brush = [System.Drawing.Brushes]::White
        $g.DrawString('reference', $font, $brush, 8, 8)
        $g.DrawString('differences', $font, $brush, $w + 8, 8)
        $g.DrawString('build', $font, $brush, ($w * 2) + 8, 8)
        $pen.Dispose()
        $font.Dispose()
    }
    finally { $g.Dispose() }

    $canvas.Save($outPath, [System.Drawing.Imaging.ImageFormat]::Png)
    $canvas.Dispose()

    Write-Host ''
    Write-Host "written $outPath"
    Write-Host ''
    Write-Host 'Read the middle strip first. Red is where to look, white is where to look hardest.'
}
finally {
    $a.Dispose()
    $b.Dispose()
    $mask.Dispose()
}