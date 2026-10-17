<#
.SYNOPSIS
    Photographs a filter with its list open.

.DESCRIPTION
    The part of a filter that gets styled is the part you can see when it is closed. The
    list that comes down only exists while it is down, so a photograph of the closed
    control says nothing about whether the work was done - this opens it and photographs
    what a person actually looks at.

    Why the mouse and not the automation tree
    ------------------------------------------
    Expanding through the tree opens the list, and the tree query that follows takes long
    enough - this engine's FindAll is not indexed - for the list to close again before the
    photograph is taken. The result is a picture of a control that looks like it never
    opened, which is indistinguishable from a control whose list was never styled. A
    person opens a list by clicking it, and so does this.

    Why the whole window rather than a crop
    ---------------------------------------
    The list is a separate top-level window that the operating system places itself. A
    crop taken at the control's own coordinates photographs empty screen when the window
    manager decides to open it somewhere else, which looks the same as not opening at all.
#>
[CmdletBinding()]
param(
    [string]$Out = 'doc\shots\filter-open.png',
    [string]$Which = 'mode'
)

$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\common.ps1"

$name = if ($Which -eq 'status') { 'Фильтр по статусу' } else { 'Фильтр по режиму' }

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class FilterShot {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint x, uint y, uint d, IntPtr e);
}
"@

$app = Start-TestApp
try {
    Start-Sleep -Seconds 10
    $w = $app.Window

    $combo = @(Get-Controls $w 'ComboBox' $name)
    if ($combo.Count -eq 0) { throw "the filter '$name' was not found" }

    $r = $combo[0].Current.BoundingRectangle

    # The list opens on the click and the window has to be the one being clicked, or the
    # click lands on whatever is in front of it.
    Set-WindowInFront $app.Process.MainWindowHandle $app.Process.Id | Out-Null
    Start-Sleep -Milliseconds 800

    [System.Windows.Forms.Cursor]::Position = New-Object System.Drawing.Point(
        [int]($r.X + $r.Width / 2), [int]($r.Y + $r.Height / 2))
    Start-Sleep -Milliseconds 400

    [FilterShot]::mouse_event(0x02, 0, 0, 0, [IntPtr]::Zero)
    [FilterShot]::mouse_event(0x04, 0, 0, 0, [IntPtr]::Zero)
    Start-Sleep -Seconds 2

    $proc = Get-Process -Name 'PhotoBook.Desktop.Avalonia' | Select-Object -First 1
    $wr = New-Object FilterShot+RECT
    [void][FilterShot]::GetWindowRect($proc.MainWindowHandle, [ref]$wr)

    $w2 = $wr.Right - $wr.Left
    $h2 = $wr.Bottom - $wr.Top

    $bmp = New-Object System.Drawing.Bitmap $w2, $h2
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($wr.Left, $wr.Top, 0, 0, (New-Object System.Drawing.Size($w2, $h2)))
    $g.Dispose()

    New-Item -ItemType Directory -Force -Path (Split-Path $Out) | Out-Null
    $bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()

    Write-Host "saved $Out ($w2 x $h2)"
}
finally {
    Stop-TestApp $app
}