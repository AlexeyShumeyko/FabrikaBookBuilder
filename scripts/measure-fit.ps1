# Measures the adaptive card sizing instead of trusting it.
#
# The owner reported the row filling wrongly in both directions: four cards per row with
# dead space on the right, and - on a smaller monitor - two cards per row with a fifth of
# the width empty. Neither is visible in a single screenshot, and neither can be checked by
# reading the code, because the answer depends on a measured width and on rounding.
#
# So this walks the window through a range of widths, photographs the live app at each
# one, and measures the pixels:
#
#   cards/row  - the slot card borders found along one horizontal line through the first
#                row of cards. The book card's own border is the outermost pair, so
#                (borders / 2) - 1 is the number of cards.
#   right gap  - the distance from the last card's border to the book card's own right
#                border. THIS is the number the owner is complaining about: a hole in the
#                row. A correct fit leaves less than a fifth of one card's step.
#
# The book card's right border is found by walking left from the window edge: the first run
# of pure white is the card (the screen background is #F8FAFC, the card is #FFFFFF). The
# last card is then the first non-white pixel to the left of it.
#
# Usage: pwsh -File scripts\measure-fit.ps1 [-OpenIndex 0] [-Widths 1000,1150,1300,1616]
#
# NOTE: this file contains Cyrillic literals and must stay UTF-8 **with BOM**.

param(
    [int]$OpenIndex = -1,
    [string]$Widths = '1100,1200,1280,1366,1440,1500,1616',
    [int]$Height = 800
)

[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @"
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public class Win2 {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr h, int x, int y, int w, int ht, bool repaint);

    private delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr h, uint cmd);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();

    public static IntPtr FindTopLevel(uint target) {
        var found = new List<IntPtr>();
        EnumWindows((h, l) => {
            uint pid; GetWindowThreadProcessId(h, out pid);
            if (pid != target) return true;
            if (GetWindow(h, 4) != IntPtr.Zero) return true;
            if (!IsWindowVisible(h)) return true;
            found.Add(h);
            return true;
        }, IntPtr.Zero);
        if (found.Count == 0) return IntPtr.Zero;
        IntPtr fg = GetForegroundWindow();
        foreach (var h in found) if (h == fg) return h;
        return found[0];
    }
    public static void Resize(IntPtr h, int w, int ht) {
        ShowWindow(h, 1);
        MoveWindow(h, 20, 20, w, ht, true);
    }
}
"@

# Slate200, the card border, and Brand500, the cover's 2px border. Both are vertical lines
# through the whole first row of cards, which is what makes them countable.
$CardBorder = @(226, 232, 240)
$CoverBorder = @(99, 102, 241)

function Get-ByName($rootEl, [string]$label, [string]$typeName) {
    $ct = [System.Windows.Automation.ControlType]::$typeName
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty, $ct)
    foreach ($b in $rootEl.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)) {
        if ($b.Current.Name -like "*$label*") { return $b }
    }
    return $null
}

# ALL matches, not the first one: the project list is picked by position, and a helper that
# returns a single element leaves the caller with one card and no way to reach the second.
function Get-AllByName($rootEl, [string]$label, [string]$typeName) {
    $ct = [System.Windows.Automation.ControlType]::$typeName
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty, $ct)
    return @($rootEl.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond) |
        Where-Object { $_.Current.Name -like "*$label*" })
}

function Is-White($c) { $c.R -ge 253 -and $c.G -ge 253 -and $c.B -ge 253 }
function Is-Border($c) {
    ($c.R -eq $CardBorder[0] -and $c.G -eq $CardBorder[1] -and $c.B -eq $CardBorder[2]) -or
    ($c.R -eq $CoverBorder[0] -and $c.G -eq $CoverBorder[1] -and $c.B -eq $CoverBorder[2])
}

# A real card border is a VERTICAL line: it shows up in every row of cards, because the
# cards of a book are stacked and their edges line up. A stray pixel of the same colour
# inside a photograph does not, which is what made the first version of this count 93
# "cards" in a row. So: sample the column all the way down the book area and keep the
# columns that are border-coloured over and over.
function Find-VerticalLines($bmp, [int]$yFrom, [int]$yTo, [int]$step, [int]$minHits) {
    $raw = @()
    for ($x = 0; $x -lt $bmp.Width; $x++) {
        $hits = 0
        for ($y = $yFrom; $y -le $yTo; $y += $step) {
            if (Is-Border $bmp.GetPixel($x, $y)) { $hits++ }
        }
        if ($hits -ge $minHits) { $raw += $x }
    }

    # The cover draws a 2px border, so one line arrives as two adjacent columns. Merge
    # them, or the "every third line is a double" pattern breaks the spacing below.
    $lines = @()
    foreach ($x in $raw) {
        if ($lines.Count -gt 0 -and $x - $lines[-1] -le 4) { continue }
        $lines += $x
    }

    # Everything left of the book area belongs to the file list: its card, its search box
    # and its rows all have borders, and its rows are evenly spaced too. The file list
    # column is a fixed 320px wide in the layout, so the book area always starts just past
    # x=380 whatever the window width is - the app's own MinWidth is 1100, so that column
    # is never squeezed.
    return @($lines | Where-Object { $_ -ge 380 })
}

# The book card's own right border: walk left from the window edge to the first column that
# is white over and over (the card's padding). The screen background is #F8FAFC, not white.
function Find-BookRight($bmp, [int]$yFrom, [int]$yTo, [int]$step) {
    for ($x = $bmp.Width - 1; $x -ge 0; $x--) {
        $hits = 0; $n = 0
        for ($y = $yFrom; $y -le $yTo; $y += $step) { $n++; if (Is-White $bmp.GetPixel($x, $y)) { $hits++ } }
        if ($n -gt 0 -and $hits -ge ($n * 0.8)) { return $x }
    }
    return -1
}

# One row of cards is a run of PAIRS of lines: a card's left border, then its right border
# one card-width away, then 16px of margin, then the next card's left border. Walking the
# lines as pairs gives the card count AND the right edge of the last card in one go, which
# is all this script needs. A card is 100..500px wide at any sane size, while the gaps
# between pairs are 16-20px and the book card's own borders are further apart than any card.
function Find-Row([int[]]$lines) {
    $cards = 0
    $last = -1
    $widths = @()
    $i = 0
    while ($i -lt $lines.Count - 1) {
        $w = $lines[$i + 1] - $lines[$i]
        if ($w -ge 100 -and $w -le 500) {
            $cards++
            $widths += $w
            $last = $lines[$i + 1]
            $i += 2
        }
        else { $i++ }
    }

    $step = 0
    if ($widths.Count -gt 0) {
        $step = [int][Math]::Round((($widths | Measure-Object -Average).Average) + 17)
    }

    return [pscustomobject]@{ Cards = $cards; LastCard = $last; Step = $step }
}

# The last card's right border is simply the last vertical line before the book card's own
# border. Testing for "a non-white column" instead was fooled by the inter-row margins: a
# card border is only present where cards are, so it covers about a third of the sampled
# height, not the 60% the test demanded, and the walk sailed past every real border.
function Find-LastCardRight([int[]]$lines, [int]$bookRight) {
    for ($i = $lines.Count - 1; $i -ge 0; $i--) {
        if ($lines[$i] -lt $bookRight) { return $lines[$i] }
    }
    return -1
}



$app = Get-Process -Name PhotoBookRenamer -ErrorAction SilentlyContinue
if ($app) { throw 'close the app first' }

# The project to measure has to be a COMBINED one - the other mode's tab is disabled while
# a combined project is open, and vice versa. The index on disk is read to find the newest
# combined project in the order the app shows it (LastModified descending), because a
# hardcoded index silently stops being one after any project is opened: opening rewrites
# LastModified and moves that card to the top.
if ($OpenIndex -lt 0) {
    $indexPath = Join-Path $env:LOCALAPPDATA 'PhotoBookRenamer\Projects\projects.json'
    # ConvertFrom-Json hands back a single array object for an array root, so the items are
    # unrolled with ForEach-Object before sorting - otherwise the sort key is an array.
    $parsed = Get-Content $indexPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $all = @($parsed | ForEach-Object { $_ } | Sort-Object { [datetime]$_.lastModified } -Descending)
    $combined = @($all | Where-Object { $_.mode -eq 3 })
    if ($combined.Count -eq 0) { throw 'no combined project in the index' }
    $OpenIndex = [array]::IndexOf($all, $combined[0])
    Write-Host "measuring '$($combined[0].name)' (index $OpenIndex)"
}

$proc = $null
try {
    $exe = Join-Path $root 'src\PhotoBook.Desktop.Wpf\bin\Release\net8.0-windows\PhotoBookRenamer.exe'
    $proc = Start-Process -FilePath $exe -PassThru
    $deadline = (Get-Date).AddSeconds(60)
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 500
        $proc.Refresh()
        if ($proc.HasExited) { throw "app exited $($proc.ExitCode)" }
        if ($proc.MainWindowHandle -ne 0) { break }
    }
    if ($proc.MainWindowHandle -eq 0) { throw 'no window' }
    [void][Win2]::ShowWindow($proc.MainWindowHandle, 3)
    Start-Sleep -Seconds 2

    $hwnd = $proc.MainWindowHandle
    $el = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)
    $openLabel = '*' + [char]0x041E + [char]0x0442 + [char]0x043A + [char]0x0440 + [char]0x044B + [char]0x0442 + [char]0x044C + ' ' + [char]0x041F + [char]0x0440 + [char]0x043E + [char]0x0435 + [char]0x043A + [char]0x0442 + '*'
    $target = $null
    $loadDeadline = (Get-Date).AddSeconds(60)
    while ($null -eq $target -and (Get-Date) -lt $loadDeadline) {
        Start-Sleep -Milliseconds 700
        $buttons = @(Get-AllByName $el $openLabel 'Button')
        if ($buttons.Count -gt $OpenIndex) { $target = $buttons[$OpenIndex] }
    }
    if ($null -eq $target) { throw 'no project cards appeared' }
    $target.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Seconds 7

    $hwnd = [Win2]::FindTopLevel([uint32]$proc.Id)
    $el = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)
    $tab = Get-ByName $el 'Комбинированный' 'RadioButton'
    if ($null -eq $tab) { throw 'the combined tab is not there' }
    $tab.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Seconds 2

    $list = @($Widths -split ',' | ForEach-Object { [int]$_.Trim() })
    $shots = Join-Path $root 'doc\shots'
    if (-not (Test-Path $shots)) { New-Item -ItemType Directory -Force -Path $shots | Out-Null }

    Write-Host ("{0,6}  {1,10}  {2,8}  {3,10}  {4}" -f 'width', 'cards/row', 'step', 'right gap', 'verdict')
    Write-Host ('-' * 58)

    $bad = 0
    foreach ($w in $list) {
        [Win2]::Resize($hwnd, $w, $Height)
        [void][Win2]::SetForegroundWindow($hwnd)
        Start-Sleep -Milliseconds 1200

        $r = New-Object Win2+RECT
        [void][Win2]::GetWindowRect($hwnd, [ref]$r)
        $ww = $r.Right - $r.Left; $hh = $r.Bottom - $r.Top
        $bmp = New-Object System.Drawing.Bitmap $ww, $hh
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.CopyFromScreen($r.Left, $r.Top, 0, 0, (New-Object System.Drawing.Size($ww, $hh)))
        $g.Dispose()

        # The book area, sampled top to bottom: card borders are vertical, so one pass over
        # the column finds every card of every row at once.
        $yFrom = [int]($hh * 0.22)
        $yTo = [int]($hh * 0.92)
        $lines = @(Find-VerticalLines $bmp $yFrom $yTo 6 6)
        $bookRight = Find-BookRight $bmp $yFrom $yTo 6

        $best = $null
        $row = Find-Row $lines
        if ($bookRight -gt 0 -and $row.LastCard -gt 0) {
            $best = [pscustomobject]@{
                BookRight = $bookRight
                LastCard  = $row.LastCard
                Cards     = $row.Cards
                Step      = $row.Step
                Gap       = $bookRight - $row.LastCard
            }
        }

        $file = Join-Path $shots ("fit-{0}.png" -f $w)
        $bmp.Save($file, [System.Drawing.Imaging.ImageFormat]::Png)
        $bmp.Dispose()

        if ($null -eq $best) {
            Write-Host ("{0,6}  {1}" -f $w, 'no card row found')
            $bad++
            continue
        }

        # A correct fit leaves no more than the gaps that are always there: the last card's
        # right margin (16), the book card's padding (20), its 1px border, and the 12px the
        # sizing rule is allowed to leave. Anything beyond that is a hole in the row - the
        # thing the owner reported as "a lot of free space".
        $ok = $best.Gap -le 52
        if (-not $ok) { $bad++ }
        Write-Host ("{0,6}  {1,10}  {2,8}  {3,10}  {4}" -f $w, $best.Cards, $best.Step, $best.Gap,
            $(if ($ok) { 'ok' } else { 'HOLE' }))
    }

    Write-Host ''
    if ($bad -gt 0) { Write-Host "FAIL: $bad of $($list.Count) widths leave a hole in the row"; exit 1 }
    Write-Host "PASS: every width fills the row"
    exit 0
}
finally {
    if ($proc) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue }
    Pop-Location
}
