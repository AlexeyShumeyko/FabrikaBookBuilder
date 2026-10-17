<#
.SYNOPSIS
    Photographs the projects list in a narrow window.

.DESCRIPTION
    A long project name fits on a wide monitor and does not fit on a narrow one, so the
    fault in the card's heading only shows on the second kind of screen. This resizes the
    window to a given width and photographs it there, which is the only way to see it
    without asking somebody to move their displays.

    The window is resized with MoveWindow rather than by changing the program's own size:
    this is a question about how the layout behaves at a width, not a request for a
    different default.
#>
[CmdletBinding()]
param(
    [int]$Width = 1100,
    [int]$Height = 800,
    [string]$Out = 'doc\shots\narrow.png'
)

$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\common.ps1"

Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class NarrowShot {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr h, int x, int y, int w, int ht, bool repaint);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int n);
}
"@

$exe = @(Get-AppExe)[-1]
Get-Process -Name 'PhotoBook.Desktop.Avalonia' -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 1

$proc = Start-Process -FilePath $exe -PassThru
$deadline = (Get-Date).AddSeconds(45)
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Milliseconds 500
    $proc.Refresh()
    if ($proc.MainWindowHandle -ne 0) { break }
}
if ($proc.MainWindowHandle -eq 0) { throw 'no window appeared' }

Start-Sleep -Seconds 8
Set-WindowInFront $proc.MainWindowHandle $proc.Id | Out-Null

$before = New-Object NarrowShot+RECT
[void][NarrowShot]::GetWindowRect($proc.MainWindowHandle, [ref]$before)
[void][NarrowShot]::ShowWindow($proc.MainWindowHandle, 9)
[void][NarrowShot]::MoveWindow($proc.MainWindowHandle, $before.Left, $before.Top, $Width, $Height, $true)

# The cards are laid out when the window changes size, not when it opens, so the settle
# here is about the layout pass rather than about anything being read from disk.
Start-Sleep -Seconds 3
Set-WindowInFront $proc.MainWindowHandle $proc.Id | Out-Null
Start-Sleep -Milliseconds 800

$r = New-Object NarrowShot+RECT
[void][NarrowShot]::GetWindowRect($proc.MainWindowHandle, [ref]$r)
$w = $r.Right - $r.Left
$h = $r.Bottom - $r.Top

$bmp = New-Object System.Drawing.Bitmap $w, $h
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($r.Left, $r.Top, 0, 0, (New-Object System.Drawing.Size $w, $h))
$g.Dispose()

New-Item -ItemType Directory -Force -Path (Split-Path $Out) | Out-Null
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()

Write-Host "saved $Out ($w x $h)"
Get-Process -Name 'PhotoBook.Desktop.Avalonia' -ErrorAction SilentlyContinue | Stop-Process -Force