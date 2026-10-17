<#
.SYNOPSIS
    Where the control panel sits on a screen, read from a photograph of that screen.

.DESCRIPTION
    The three control panels are supposed to be one structure, and whether they are is a
    question about what is on screen. It cannot be answered from the markup: all three
    Borders carry Padding="20", and a panel is also as tall as whatever is inside it, so
    two panels with identical padding can still come out a different height.

    How it finds the panel
    ----------------------
    The window background and the card background are the same colour - both Surface, both
    #FFFFFF. So the panel has no fill of its own to look for. What it does have is a
    1-pixel border in Slate200At70 (#B3CBD5E1 at seventy per cent), which lands on screen as
    about #D8E0E9 - which is not what the token computes to, because the blend is done in a different space than arithmetic suggests. That was found by reading the pixels off a real screen, not by reasoning about the colour: the number above is measured. A row that is that colour across most of the window's width is a panel
    edge; the rows between two such lines are the panel.

    Cards, banners and separators are ruled out by width, not by colour: a card is a third
    of the window wide and a separator is short, so neither reaches the share of the row
    that the panel does.

    Why this rather than the automation tree
    ---------------------------------------
    A Border in this engine puts no node in the automation tree at all - no AutomationId,
    no Name, nothing - so there is nothing to ask. Photographs are also the instrument the
    list-timing work already settled on, and the reason is the same here: the tree is not
    indexed, and one walk of it makes the program answer questions instead of drawing.
#>
[CmdletBinding()]
param(
    # One or more photographs to measure.
    [Parameter(Mandatory = $true)]
    [string[]]$Png,

    # What each photograph shows, in the order given.
    [string[]]$Label = @(),

    # Fail when the panels are not all the same height. Off by default so the script can
    # be used just to see the numbers.
    [switch]$RequireEqual
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

# Slate200At70 (#B3CBD5E1) at seventy per cent laid over white.
$borderR = 216
$borderG = 224
$borderB = 233
$tolerance = 10

function Get-PanelBand {
    param([string]$File)

    $bmp = New-Object System.Drawing.Bitmap $File
    try {
        $w = $bmp.Width
        $h = $bmp.Height

        # The window has a margin, and the header sits at the top. Rows are counted from
        # below the header so the header's own bottom line is not mistaken for a panel.
        $from = 90
        $x0 = 40
        $x1 = $w - 40
        $span = $x1 - $x0
        $needed = [int]($span * 0.55)

        $edges = New-Object System.Collections.Generic.List[object]

        for ($y = $from; $y -lt $h - 2; $y++) {
            $hits = 0
            for ($x = $x0; $x -le $x1; $x++) {
                $c = $bmp.GetPixel($x, $y)
                if ([math]::Abs($c.R - $borderR) -le $tolerance -and
                    [math]::Abs($c.G - $borderG) -le $tolerance -and
                    [math]::Abs($c.B - $borderB) -le $tolerance) {
                    $hits++
                }
            }

            if ($hits -ge $needed) {
                # A one-pixel border shows on one row. Two in a row means the line is
                # thicker than the design says, and the second row is the same edge.
                if ($edges.Count -eq 0 -or ($y - $edges[$edges.Count - 1].Y) -gt 1) {
                    $edges.Add([pscustomobject]@{ Y = $y })
                }
            }
        }

        # The header has a rule along its own bottom, so the first two lines found are the
        # header and the top of the panel - only twenty-odd pixels apart. A panel carries a
        # full 40 pixels of padding above whatever is in it, so no band shorter than that
        # can be one, and the first pair with room in it is the panel.
        for ($i = 0; $i -lt $edges.Count - 1; $i++) {
            $gap = $edges[$i + 1].Y - $edges[$i].Y
            if ($gap -ge 60) {
                return [pscustomobject]@{
                    Top = $edges[$i].Y
                    Bottom = $edges[$i + 1].Y
                    Height = $gap
                    All = $edges
                }
            }
        }

        return $null
    }
    finally {
        $bmp.Dispose()
    }
}

$results = @()
for ($i = 0; $i -lt $Png.Count; $i++) {
    $file = $Png[$i]
    $name = if ($i -lt $Label.Count) { $Label[$i] } else { Split-Path $file -Leaf }

    if (-not (Test-Path $file)) {
        Write-Host ("  {0}: no such file" -f $name)
        $results += [pscustomobject]@{ Name = $name; Height = $null }
        continue
    }

    $band = Get-PanelBand $file
    if ($null -eq $band) {
        Write-Host ("  {0}: no panel found" -f $name)
        $results += [pscustomobject]@{ Name = $name; Height = $null }
        continue
    }

    Write-Host ("  {0,-14} panel top {1,4}  bottom {2,4}  height {3}" -f $name, $band.Top, $band.Bottom, $band.Height)
    $results += [pscustomobject]@{ Name = $name; Height = $band.Height }
}

$heights = @($results | Where-Object { $_.Height } | Select-Object -ExpandProperty Height | Sort-Object -Unique)
Write-Host ''
Write-Host ("  heights: {0}" -f ($heights -join ', '))

if ($heights.Count -gt 1) {
    Write-Host ''
    Write-Host ("  РАЗНЫЕ: the three panels are not one height")
    if ($RequireEqual) { exit 1 }
}
elseif ($heights.Count -eq 1) {
    Write-Host ''
    Write-Host ("  РАВНЫЕ: every panel is {0} px tall" -f $heights[0])
}
else {
    Write-Host ''
    Write-Host '  НИЧЕГО НЕ ИЗМЕРЕНО'
    if ($RequireEqual) { exit 1 }
}