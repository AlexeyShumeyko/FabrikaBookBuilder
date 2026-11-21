# Verifies the welcome page scrolls with the mouse wheel.
#
# It did not. The empty project list was a transparent scroll view lying over the welcome page,
# so every wheel notch landed on a list with nothing in it: the scrollbar moved the page and the
# keyboard moved the page, but the wheel did nothing, and the "create your first project" button
# sat below the edge of the window where nobody could reach it.
#
# A build cannot see this, and neither can the screenshot that showed the button missing: the
# only honest check is to send a real wheel notch and ask the scroll view whether it moved.
#
# Usage:  pwsh -File scripts\verify-welcome-scroll.ps1 [-TimeoutSeconds 40]
#
# NOTE: this file contains Cyrillic literals and must stay UTF-8 **with BOM**.

param(
    [int]$TimeoutSeconds = 40
)

[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8

$ErrorActionPreference = 'Stop'

. "$PSScriptRoot\common.ps1"
Push-Location (Split-Path -Parent $PSScriptRoot)

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class WheelInput {
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint f, int dx, int dy, int d, UIntPtr e);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
    public static void Spin(int delta) { mouse_event(0x0800, 0, 0, delta, UIntPtr.Zero); }
}
"@

$exe = Get-AppExe
if (-not (Test-Path $exe)) { throw "no build at $exe" }

$proc = Start-Process -FilePath $exe -PassThru
try {
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 400
        $proc.Refresh()
        if ($proc.HasExited) { throw "the program exited with $($proc.ExitCode)" }
        if ($proc.MainWindowHandle -ne 0) { break }
    }
    if ($proc.MainWindowHandle -eq 0) { throw 'the main window never appeared' }

    [void][WheelInput]::ShowWindow($proc.MainWindowHandle, 3)
    [void][WheelInput]::SetForegroundWindow($proc.MainWindowHandle)
    Start-Sleep -Seconds 3

    $root = [System.Windows.Automation.AutomationElement]::FromHandle($proc.MainWindowHandle)
    $condition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::IsScrollPatternAvailableProperty, $true)
    $scrollables = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $condition)
    if ($scrollables.Count -eq 0) { throw 'the welcome page has no scroll view at all' }

    $page = $scrollables.Item(0)
    $scroll = $page.GetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern)
    if (-not $scroll.Current.VerticallyScrollable) {
        throw 'the welcome page is not scrollable - it fits the window, so there is nothing to prove'
    }

    $before = $scroll.Current.VerticalScrollPercent

    # A point inside the page's own rectangle, away from the header.
    $rect = $page.Current.BoundingRectangle
    [void][WheelInput]::SetCursorPos([int]($rect.X + $rect.Width / 2), [int]($rect.Y + $rect.Height / 2))
    Start-Sleep -Milliseconds 300
    [WheelInput]::Spin(-360)
    Start-Sleep -Milliseconds 900

    $after = $scroll.Current.VerticalScrollPercent
    if ($after -le $before + 1) {
        throw "the wheel did not move the page: $before% before, $after% after"
    }

    Write-Host ("PASS: the wheel scrolls the welcome page ({0:0}% -> {1:0}%)." -f $before, $after)
}
finally {
    if ($proc -and -not $proc.HasExited) { $proc.Kill() }
    Pop-Location
}
