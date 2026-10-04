# Asserts that the file list actually scrolls with the wheel.
#
# The list sat inside an Auto-height row after the upload controls were merged, so the
# ScrollViewer never got a shorter viewport: the scrollbar could not appear and the wheel
# did nothing. A screenshot cannot see that, so this drives the wheel and compares pixels.
#
# Usage: pwsh -File scripts/test-list-scroll.ps1 [-OpenIndex N]
#
# NOTE: this file contains Cyrillic literals and must stay UTF-8 **with BOM**.

param(
    [int]$OpenIndex = -1,
    [int]$WheelClicks = 6
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
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, IntPtr e);

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
    public static void Wheel(int x, int y, int clicks) {
        SetCursorPos(x, y);
        System.Threading.Thread.Sleep(200);
        for (int i = 0; i < clicks; i++) {
            mouse_event(0x0800, 0, 0, unchecked((uint)-120), IntPtr.Zero);   // WHEEL down
            System.Threading.Thread.Sleep(120);
        }
    }
}
"@

function Get-AllByName($rootEl, [string]$label, [string]$typeName) {
    $ct = [System.Windows.Automation.ControlType]::$typeName
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty, $ct)
    return @($rootEl.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond) |
        Where-Object { $_.Current.Name -like "*$label*" })
}

function Get-ByName($rootEl, [string]$label, [string]$typeName) {
    $ct = [System.Windows.Automation.ControlType]::$typeName
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty, $ct)
    foreach ($b in $rootEl.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)) {
        if ($b.Current.Name -like "*$label*") { return $b }
    }
    return $null
}

# Any control type: the drop zone is a Border, which UAutomation reports as Group or Custom,
# and a filtered search never finds it.
function Get-Named($rootEl, [string]$label) {
    $all = $rootEl.FindAll([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.Condition]::TrueCondition)
    foreach ($b in $all) {
        if ($b.Current.Name -eq $label) { return $b }
    }
    return $null
}

# A cheap signature of the list area: the colour of a few pixels inside the first row.
function Get-ListSignature($bmp, [int]$x0, [int]$y0, [int]$w, [int]$h) {
    $sum = 0
    for ($y = $y0; $y -lt ($y0 + $h); $y += 7) {
        for ($x = $x0; $x -lt ($x0 + $w); $x += 7) {
            $c = $bmp.GetPixel($x, $y)
            $sum += $c.R * 3 + $c.G * 5 + $c.B * 7
        }
    }
    return $sum
}

if (Get-Process -Name PhotoBookRenamer -ErrorAction SilentlyContinue) { throw 'close the app first' }

$proc = $null
try {
    if ($OpenIndex -lt 0) {
        $indexPath = Join-Path $env:LOCALAPPDATA 'PhotoBookRenamer\Projects\projects.json'
        $parsed = Get-Content $indexPath -Raw -Encoding UTF8 | ConvertFrom-Json
        $all = @($parsed | ForEach-Object { $_ } | Sort-Object { [datetime]$_.lastModified } -Descending)
        $combined = @($all | Where-Object { $_.mode -eq 3 -and $_.bookCount -gt 0 })
        if ($combined.Count -eq 0) { throw 'no filled combined project in the index' }
        $OpenIndex = [array]::IndexOf($all, $combined[0])
        Write-Host "using '$($combined[0].name)' (index $OpenIndex, $($combined[0].bookCount) books)"
    }

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

    $el = [System.Windows.Automation.AutomationElement]::FromHandle($proc.MainWindowHandle)
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
    $tab.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Seconds 3

    # Where the list is. The list column is a fixed 320px wide, so its left edge is found
    # from the column's own heading, and the rows start a fixed distance below it: the panel
    # above the two areas is a fixed 88px tall, so the vertical layout does not move with the
    # window height. A Border carries no UIA peer, so the drop zone cannot be used as the
    # anchor even though it has an AutomationProperties.Name.
    $header = Get-Named $el ([char]0x0417 + [char]0x0430 + [char]0x0433 + [char]0x0440 + [char]0x0443 + [char]0x0436 + [char]0x0435 + [char]0x043D + [char]0x043D + [char]0x044B + [char]0x0435 + ' ' + [char]0x0444 + [char]0x043E + [char]0x0442 + [char]0x043E)
    if ($null -eq $header) { throw 'the loaded-photo list heading is not on screen' }
    $hr = $header.Current.BoundingRectangle

    $r = New-Object Win2+RECT
    [void][Win2]::GetWindowRect($hwnd, [ref]$r)
    $ww = $r.Right - $r.Left; $hh = $r.Bottom - $r.Top

    $x0 = [int]$hr.Left - 4
    $w = 280
    $y0 = [int]$hr.Bottom + 200
    $h = 200
    if (($y0 + $h) -gt $hh) { $h = $hh - $y0 - 20 }
    if ($h -lt 80) { throw "the list area is only ${h}px tall - the layout moved" }
    Write-Host "list area at $x0,$y0 $w x $h (window ${ww}x${hh})"

    $before = New-Object System.Drawing.Bitmap $ww, $hh
    $g = [System.Drawing.Graphics]::FromImage($before)
    $g.CopyFromScreen($r.Left, $r.Top, 0, 0, (New-Object System.Drawing.Size($ww, $hh)))
    $g.Dispose()
    $sigBefore = Get-ListSignature $before $x0 $y0 $w $h
    $before.Save((Join-Path $root 'doc\shots\list-scroll-before.png'), [System.Drawing.Imaging.ImageFormat]::Png)
    $before.Dispose()

    [void][Win2]::SetForegroundWindow($hwnd)
    [Win2]::Wheel(($r.Left + $x0 + ($w / 2)), ($r.Top + $y0 + ($h / 2)), $WheelClicks)
    Start-Sleep -Milliseconds 800

    $after = New-Object System.Drawing.Bitmap $ww, $hh
    $g = [System.Drawing.Graphics]::FromImage($after)
    $g.CopyFromScreen($r.Left, $r.Top, 0, 0, (New-Object System.Drawing.Size($ww, $hh)))
    $g.Dispose()
    $sigAfter = Get-ListSignature $after $x0 $y0 $w $h
    $after.Save((Join-Path $root 'doc\shots\list-scroll-after.png'), [System.Drawing.Imaging.ImageFormat]::Png)
    $after.Dispose()

    Write-Host "signature before=$sigBefore after=$sigAfter"
    if ($sigBefore -eq $sigAfter) {
        Write-Host 'FAIL: $WheelClicks wheel clicks over the list changed nothing'
        exit 1
    }
    Write-Host 'PASS: the wheel scrolls the loaded-photo list'
    exit 0
}
finally {
    if ($proc) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue }
    Pop-Location
}
