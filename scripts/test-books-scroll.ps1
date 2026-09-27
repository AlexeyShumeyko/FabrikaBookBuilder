# Asserts that the book list scrolls, with a virtualized panel in place.
#
# This is the test the virtualization change has to pass. A virtualized list that does not
# scroll is worse than a slow one: every book after the first screen would be unreachable,
# and a screenshot of the top of the list would look perfect.
#
# So it drives the real wheel over the book column, compares the pixels before and after,
# and leaves both screenshots behind - the bottom one has to show the last book, which is
# the owner's own 29 x 10 case.
#
# Usage: pwsh -File scripts\test-books-scroll.ps1 [-Books 29] [-Spreads 10]
#
# NOTE: this file contains Cyrillic literals and must stay UTF-8 **with BOM**.

param(
    [int]$Books = 29,
    [int]$Spreads = 10,
    [int]$TimeoutSeconds = 90
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
public class WinB {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, IntPtr e);
    private delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr h, uint cmd);
    public static IntPtr FindTopLevel(uint target) {
        var found = new List<IntPtr>();
        EnumWindows((h, l) => {
            uint pid; GetWindowThreadProcessId(h, out pid);
            if (pid != target) return true;
            if (GetWindow(h, 4) != IntPtr.Zero) return true;
            if (!IsWindowVisible(h)) return true;
            found.Add(h); return true;
        }, IntPtr.Zero);
        return found.Count > 0 ? found[0] : IntPtr.Zero;
    }
    public static void Wheel(int x, int y, int clicks) {
        SetCursorPos(x, y);
        System.Threading.Thread.Sleep(150);
        for (int i = 0; i < clicks; i++) {
            mouse_event(0x0800, 0, 0, unchecked((uint)-360), IntPtr.Zero);
            System.Threading.Thread.Sleep(60);
        }
    }
}
"@

function Save-Shot([IntPtr]$window, [string]$path) {
    $r = New-Object WinB+RECT
    [void][WinB]::GetWindowRect($window, [ref]$r)
    $bmp = New-Object System.Drawing.Bitmap ($r.Right - $r.Left), ($r.Bottom - $r.Top)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($r.Left, $r.Top, 0, 0, $bmp.Size)
    $g.Dispose()
    $dir = Split-Path -Parent $path
    if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}

$openLabel = '*' + [char]0x041E + [char]0x0442 + [char]0x043A + [char]0x0440 + [char]0x044B + [char]0x0442 + [char]0x044C + ' ' + [char]0x041F + [char]0x0440 + [char]0x043E + [char]0x0435 + [char]0x043A + [char]0x0442 + '*'

if (Get-Process -Name PhotoBookRenamer -ErrorAction SilentlyContinue) { throw 'close the app first' }

$proc = $null
try {
    $exe = Join-Path $root 'bin\Release\net8.0-windows\PhotoBookRenamer.exe'
    $proc = Start-Process -FilePath $exe -PassThru
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 400
        $proc.Refresh()
        if ($proc.HasExited) { throw "app exited $($proc.ExitCode)" }
        if ($proc.MainWindowHandle -ne 0) { break }
    }
    [void][WinB]::ShowWindow($proc.MainWindowHandle, 3)
    [void][WinB]::SetForegroundWindow($proc.MainWindowHandle)
    Start-Sleep -Seconds 3

    # Find the fixture by its shape, not by its position: opening a project bumps its
    # LastModified and moves it to the top of the list.
    $indexPath = Join-Path $env:LOCALAPPDATA 'PhotoBookRenamer\Projects\projects.json'
    $entries = @((Get-Content $indexPath -Raw -Encoding UTF8 | ConvertFrom-Json) |
        Sort-Object { [datetime]$_.lastModified } -Descending)
    $at = -1
    for ($i = 0; $i -lt $entries.Count; $i++) {
        if ($entries[$i].mode -eq 3 -and
            $entries[$i].bookCount -eq $Books -and
            $entries[$i].pageCount -eq $Spreads) { $at = $i; break }
    }
    if ($at -lt 0) { throw "no combined project with $Books books x $Spreads spreads" }
    Write-Host "opening '$($entries[$at].name)' ($Books books x $Spreads spreads)"

    $hwnd = [WinB]::FindTopLevel([uint32]$proc.Id)
    $el = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)
    $btnCond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::Button)
    $open = $null
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ($null -eq $open -and (Get-Date) -lt $deadline) {
        $cards = @($el.FindAll([System.Windows.Automation.TreeScope]::Descendants, $btnCond) |
            Where-Object { $_.Current.Name -like $openLabel })
        if ($cards.Count -gt $at) { $open = $cards[$at] }
        if ($null -eq $open) { Start-Sleep -Milliseconds 600 }
    }
    if ($null -eq $open) { throw 'no open button' }
    $open.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Seconds 8

    $hwnd = [WinB]::FindTopLevel([uint32]$proc.Id)
    $top = Join-Path $root 'doc\shots\books-scroll-top.png'
    $bottom = Join-Path $root 'doc\shots\books-scroll-bottom.png'
    Save-Shot $hwnd $top
    $before = [System.IO.File]::ReadAllBytes($top)

    $r = New-Object WinB+RECT
    [void][WinB]::GetWindowRect($hwnd, [ref]$r)
    # The book column: right of the 320px file list, below the project panel.
    $cx = [int](($r.Left + $r.Right) / 2)
    $cy = [int](($r.Top + $r.Bottom) / 2)
    Write-Host "wheeling at $cx,$cy"

    for ($i = 0; $i -lt 30; $i++) {
        [WinB]::Wheel($cx, $cy, 10)
        Start-Sleep -Milliseconds 120
    }
    Start-Sleep -Seconds 2

    $proc.Refresh()
    if ($proc.HasExited) { throw 'the app died while scrolling the book list' }

    Save-Shot $hwnd $bottom
    $after = [System.IO.File]::ReadAllBytes($bottom)

    if ([System.Linq.Enumerable]::SequenceEqual([byte[]]$before, [byte[]]$after)) {
        throw 'the wheel did not move the book list'
    }

    Write-Host 'PASS: the wheel scrolls the book list (the pixels changed)'
    Write-Host "      top:    $top"
    Write-Host "      bottom: $bottom   <- the last book must be visible in this one"
    exit 0
}
finally {
    if ($proc) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue }
    Pop-Location
}
