$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\common.ps1"
. "$PSScriptRoot\test-helpers.ps1"

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class LongestShot {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint x, uint y, uint d, IntPtr e);
}
"@

function Click-At([int]$x, [int]$y) {
    [System.Windows.Forms.Cursor]::Position = New-Object System.Drawing.Point($x, $y)
    Start-Sleep -Milliseconds 300
    [LongestShot]::mouse_event(0x02, 0, 0, 0, [IntPtr]::Zero)
    [LongestShot]::mouse_event(0x04, 0, 0, 0, [IntPtr]::Zero)
    Start-Sleep -Milliseconds 800
}

# chip 38 tall, then 6 of gap, 6 of the list's padding, rows of 34
function Row-Y([int]$chipTop, [int]$index) { return $chipTop + 38 + 6 + 6 + ($index * 34) + 17 }

$app = Start-TestApp
try {
    Start-Sleep -Seconds 10
    $w = $app.Window
    Set-WindowInFront $app.Process.MainWindowHandle $app.Process.Id | Out-Null
    Start-Sleep -Milliseconds 800

    # the two longest names the program has to fit
    $mode = @(Get-Controls $w 'Button' 'Фильтр по режиму')[0].Current.BoundingRectangle
    Click-At ([int]($mode.X + 40)) (Row-Y ([int]$mode.Y) 2)

    $w = $app.Window
    $status = @(Get-Controls $w 'Button' 'Фильтр по статусу')[0].Current.BoundingRectangle
    Click-At ([int]($status.X + 40)) (Row-Y ([int]$status.Y) 2)

    $proc = Get-Process -Name 'PhotoBook.Desktop.Avalonia' | Select-Object -First 1
    $wr = New-Object LongestShot+RECT
    [void][LongestShot]::GetWindowRect($proc.MainWindowHandle, [ref]$wr)
    $bw = $wr.Right - $wr.Left
    $bh = $wr.Bottom - $wr.Top

    $bmp = New-Object System.Drawing.Bitmap $bw, $bh
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($wr.Left, $wr.Top, 0, 0, (New-Object System.Drawing.Size $bw, $bh))
    $g.Dispose()
    $bmp.Save('doc\shots\measure-filters-longest.png', [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()


    Write-Host "saved doc\shots\measure-filters-longest.png ($bw x $bh)"
    Write-Host ('  рамка режима:  x {0}..{1}  ширина {2}' -f [int]$mode.X, [int]($mode.X + $mode.Width), [int]$mode.Width)
    Write-Host ('  рамка статуса: x {0}..{1}  ширина {2}' -f [int]$status.X, [int]($status.X + $status.Width), [int]$status.Width)
}
finally {
    Stop-TestApp $app
}
