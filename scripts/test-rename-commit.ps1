# Asserts that renaming a project applies when the user clicks anywhere - not only when
# the click happens to land on something that takes keyboard focus.
#
# The owner reported it in the Unique Folders mode: the new name stuck when they clicked
# the books area and was silently dropped when they clicked the control panel. LostFocus
# alone cannot explain "sometimes", because a click only raises it when it lands on a
# focusable control; the panel background and its labels are not focusable, so the rename
# box stayed focused and the name was lost. The combined mode has always had a
# preview-click handler that commits on any click, and the folders mode now has the same
# one.
#
# So the click that matters here is a click on something NON-focusable - the summary line
# under the project name - which is the case that used to fail.
#
# Usage: pwsh -File scripts\test-rename-commit.ps1
#
# NOTE: this file contains Cyrillic literals and must stay UTF-8 **with BOM**.

param(
    [int]$TimeoutSeconds = 60
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
public class Win8 {
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
    public static IntPtr[] Visible(int pid) {
        var f = new List<IntPtr>();
        EnumWindows((h, l) => { uint p; GetWindowThreadProcessId(h, out p);
            if (p == (uint)pid && IsWindowVisible(h)) f.Add(h); return true; }, IntPtr.Zero);
        return f.ToArray();
    }
    public static IntPtr FindTopLevel(uint pid) {
        IntPtr best = IntPtr.Zero;
        var all = Visible((int)pid);
        foreach (var h in all) { if (GetWindow(h, 4) == IntPtr.Zero) best = h; }
        return best != IntPtr.Zero ? best : (all.Length > 0 ? all[0] : IntPtr.Zero);
    }
    public static void Click(int x, int y) {
        SetCursorPos(x, y);
        System.Threading.Thread.Sleep(120);
        mouse_event(0x0002, 0, 0, 0, IntPtr.Zero);
        mouse_event(0x0004, 0, 0, 0, IntPtr.Zero);
    }
}
"@

function Find-ByName($rootEl, [string]$label, [string]$typeName) {
    $ct = [System.Windows.Automation.ControlType]::$typeName
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty, $ct)
    foreach ($b in $rootEl.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)) {
        if ($b.Current.Name -like "*$label*") { return $b }
    }
    return $null
}

$labelPencil = 'Переименовать проект'
$newName = 'Rename-check'
$openLabel = '*Открыть проект*'

if (Get-Process -Name PhotoBookRenamer -ErrorAction SilentlyContinue) { throw 'close the app first' }

$proc = $null
try {
    $exe = Join-Path $root 'bin\Release\net8.0-windows\PhotoBookRenamer.exe'
    $proc = Start-Process -FilePath $exe -PassThru
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 500
        $proc.Refresh()
        if ($proc.HasExited) { throw "app exited $($proc.ExitCode)" }
        if ($proc.MainWindowHandle -ne 0) { break }
    }
    if ($proc.MainWindowHandle -eq 0) { throw 'no main window' }
    [void][Win8]::ShowWindow($proc.MainWindowHandle, 3)
    [void][Win8]::SetForegroundWindow($proc.MainWindowHandle)
    Start-Sleep -Seconds 3

    # The newest filled project of the folders mode.
    $indexPath = Join-Path $env:LOCALAPPDATA 'PhotoBookRenamer\Projects\projects.json'
    $all = @((Get-Content $indexPath -Raw -Encoding UTF8 | ConvertFrom-Json) |
        Sort-Object { [datetime]$_.lastModified } -Descending)
    $at = 0
    for ($i = 0; $i -lt $all.Count; $i++) { if ($all[$i].mode -eq 2 -and $all[$i].bookCount -gt 0) { $at = $i; break } }
    Write-Host "opening '$($all[$at].name)' (card $at)"

    $hwnd = [Win8]::FindTopLevel([uint32]$proc.Id)
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
        if ($null -eq $open) { Start-Sleep -Milliseconds 700 }
    }
    if ($null -eq $open) { throw 'no open button' }
    $open.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Seconds 8

    $hwnd = [Win8]::FindTopLevel([uint32]$proc.Id)
    $el = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)

    $pencil = Find-ByName $el $labelPencil 'Button'
    if ($null -eq $pencil) { throw 'the rename (pencil) button was not found' }
    $pencil.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Seconds 1

    # The visible Edit is the rename box; a collapsed one may still be in the tree, and
    # typing into THAT is how a test can pass while the app never saw a keystroke.
    $edit = $null
    $editCond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::Edit)
    foreach ($e in $el.FindAll([System.Windows.Automation.TreeScope]::Descendants, $editCond)) {
        $er = $e.Current.BoundingRectangle
        if (-not $e.Current.IsOffscreen -and $er.Width -gt 40) { $edit = $e; break }
    }
    if ($null -eq $edit) { throw 'the visible rename text box did not appear' }

    $vp = $edit.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
    $vp.SetValue($newName)
    Start-Sleep -Milliseconds 500
    $back = $edit.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value
    Write-Host "rename box now reads: '$back'"
    if ($back -ne $newName) { throw "SetValue did not reach the rename box (it reads '$back')" }

    # The click that used to do nothing: a label in the control panel. A TextBlock is not
    # focusable, so nothing but an explicit "click anywhere commits" handler can save it.
    $summary = Find-ByName $el 'Проект:' 'Text'
    if ($null -eq $summary) { $summary = Find-ByName $el 'обложек' 'Text' }
    if ($null -eq $summary) { throw 'no non-focusable text found in the control panel to click' }
    $r = $summary.Current.BoundingRectangle
    [void][Win8]::SetForegroundWindow($hwnd)
    Start-Sleep -Milliseconds 500
    # Twice on purpose: when the app is not the foreground window the first click only
    # activates it, and a single click then looks exactly like a click that did nothing.
    [Win8]::Click([int]($r.X + 10), [int]($r.Y + $r.Height / 2))
    Start-Sleep -Milliseconds 500
    [Win8]::Click([int]($r.X + 10), [int]($r.Y + $r.Height / 2))
    Start-Sleep -Seconds 3

    Write-Host "clicked at $([int]($r.X + 10)),$([int]($r.Y + $r.Height / 2)) (label rect $([int]$r.X),$([int]$r.Y),$([int]$r.Width)x$([int]$r.Height))"
    $proc.Refresh()
    if ($proc.HasExited) { throw 'the app died on the click' }
    $rr = New-Object Win8+RECT
    [void][Win8]::GetWindowRect($hwnd, [ref]$rr)
    $bmp = New-Object System.Drawing.Bitmap ($rr.Right - $rr.Left), ($rr.Bottom - $rr.Top)
    $gg = [System.Drawing.Graphics]::FromImage($bmp)
    $gg.CopyFromScreen($rr.Left, $rr.Top, 0, 0, $bmp.Size)
    $gg.Dispose()
    $bmp.Save((Join-Path $root 'doc\shots\rename-debug.png'), [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Host 'debug shot: doc\shots\rename-debug.png'

    $shown = $false
    $inHeader = $false
    foreach ($t in $el.FindAll([System.Windows.Automation.TreeScope]::Descendants,
            (New-Object System.Windows.Automation.PropertyCondition(
                [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                [System.Windows.Automation.ControlType]::Text)))) {
        if ($t.Current.Name -eq $newName) { $shown = $true; if ($t.Current.BoundingRectangle.Y -lt 90) { $inHeader = $true } }
    }
    if (-not $shown) { throw "the name did not apply: '$newName' is nowhere on screen after the click" }
    if (-not $inHeader) { throw 'the panel took the new name but the top bar still shows the old one' }

    Write-Host ''
    Write-Host 'PASS: clicking the control panel applies the new project name'
    exit 0
}
finally {
    if ($proc) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue }
    Pop-Location
}
