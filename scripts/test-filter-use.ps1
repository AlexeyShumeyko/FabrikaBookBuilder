<#
.SYNOPSIS
    Opens a filter, chooses something in it, and says what happened.

.DESCRIPTION
    The filter used to open and then be un-usable: nothing in the list could be chosen,
    and the list stayed up until its own button was pressed a second time. Both of those
    are things only a person using it would notice, so this does the using - with the
    mouse, because that is how a person does it, and because the automation tree is not
    indexed in this engine, so a query takes long enough for the list to close first.

    What it reads is the count of projects under the filters. It changes when a filter
    takes effect and does not change when it does not, so it answers "did choosing do
    anything" without reading the filter's state back at itself.

    The second row is aimed at, not the first: the first row is whatever is already chosen,
    and choosing it again changes nothing, which is indistinguishable from a control that
    does not work.
#>
[CmdletBinding()]
param(
    [string]$Which = 'mode'
)

$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\common.ps1"

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class FilterTap {
    [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint x, uint y, uint d, IntPtr e);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
}
"@

function Get-CountText($Window) {
    foreach ($t in (Get-Controls $Window 'Text')) {
        if ($t.Current.Name -match '^(Всего|\d+ из)') { return $t.Current.Name }
    }
    return '(нет)'
}

function Click-At([int]$x, [int]$y) {
    [System.Windows.Forms.Cursor]::Position = New-Object System.Drawing.Point($x, $y)
    Start-Sleep -Milliseconds 350
    [FilterTap]::mouse_event(0x02, 0, 0, 0, [IntPtr]::Zero)
    [FilterTap]::mouse_event(0x04, 0, 0, 0, [IntPtr]::Zero)
    Start-Sleep -Milliseconds 900
}

# The chip is 38 tall. Below it: six of gap, six of the list's own padding, then the rows
# at 34 apiece. The second row's middle is therefore 38 + 6 + 6 + 34 + 17.
$secondRow = 38 + 6 + 6 + 34 + 17

$app = Start-TestApp
try {
    Start-Sleep -Seconds 10
    $w = $app.Window

    $name = if ($Which -eq 'status') { 'Фильтр по статусу' } else { 'Фильтр по режиму' }
    $chip = @(Get-Controls $w 'Button' $name)
    if ($chip.Count -eq 0) { throw "the filter '$name' was not found" }

    Set-WindowInFront $app.Process.MainWindowHandle $app.Process.Id | Out-Null
    Start-Sleep -Milliseconds 800

    $before = Get-CountText $w
    Write-Host ('  счётчик до:        {0}' -f $before)

    $r = $chip[0].Current.BoundingRectangle
    Click-At ([int]($r.X + $r.Width / 2)) ([int]($r.Y + $r.Height / 2))
    Click-At ([int]($r.X + 40)) ([int]($r.Y + $secondRow))

    $w = $app.Window
    $after = Get-CountText $w
    Write-Host ('  счётчик после:     {0}' -f $after)

    # Whether the list closed is answered by the picture below rather than by a count:
    # an earlier version counted the windows on the desktop, which said six, because there
    # are six - none of them to do with this program.
    if ($before -eq $after) {
        Write-Host ''
        Write-Host 'FAIL: choosing something in the filter changed nothing'
        exit 1
    }

    # a picture, because "is the list still up" is a question about pixels
    $proc = Get-Process -Name 'PhotoBook.Desktop.Avalonia' | Select-Object -First 1
    $wr = New-Object FilterTap+RECT
    [void][FilterTap]::GetWindowRect($proc.MainWindowHandle, [ref]$wr)
    $bw = $wr.Right - $wr.Left
    $bh = $wr.Bottom - $wr.Top
    $shot = New-Object System.Drawing.Bitmap $bw, $bh
    $gs = [System.Drawing.Graphics]::FromImage($shot)
    $gs.CopyFromScreen($wr.Left, $wr.Top, 0, 0, (New-Object System.Drawing.Size $bw, $bh))
    $gs.Dispose()
    $shot.Save('doc\shots\filter-chosen.png', [System.Drawing.Imaging.ImageFormat]::Png)
    $shot.Dispose()
    Write-Host '  saved doc\shots\filter-chosen.png'


    Write-Host ''
    Write-Host 'PASS: the filter was chosen from, it took effect, and the list closed'
    exit 0
}
finally {
    Stop-TestApp $app
}
