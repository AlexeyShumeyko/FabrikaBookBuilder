# Runs a real export through the UI and asserts the produced file names.
#
# This is the one thing a screenshot cannot verify: that the redesigned export flow
# still writes 001-00.jpg / 001-01.jpg ..., which is the contract the client's
# batch upload depends on.
#
# The ExportDialog defaults its target to %USERPROFILE%\Desktop\PhotoBookExport, so no
# file-picker automation is needed - it clicks through the actual buttons.
#
# Usage:
#   pwsh -File scripts\test-export.ps1
#   pwsh -File scripts\test-export.ps1 -PerBookSubfolders

# The harness console runs on code page 866, which turns every Cyrillic string this
# script prints into "?" and floods the agent context with mojibake. Force UTF-8 on
# both channels; a child powershell.exe resets these on its own, so it has to be set
# inside each script rather than once in the caller.
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8

param([switch]$PerBookSubfolders)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @"
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public class W7 {
    private delegate bool EP(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] static extern bool EnumWindows(EP cb, IntPtr l);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] static extern void mouse_event(uint f, uint dx, uint dy, uint d, IntPtr e);

    public static IntPtr[] Visible(int pid) {
        var f = new List<IntPtr>();
        EnumWindows((h, l) => { uint p; GetWindowThreadProcessId(h, out p);
            if (p == (uint)pid && IsWindowVisible(h)) f.Add(h); return true; }, IntPtr.Zero);
        return f.ToArray();
    }

    /// <summary>
    /// Physical click at a screen point. Needed for controls with a fully custom
    /// ControlTemplate: the Toggle/Invoke automation patterns are not always
    /// implemented, and the element reference can go stale when the template re-renders.
    /// </summary>
    public static void Click(int x, int y) {
        SetCursorPos(x, y);
        System.Threading.Thread.Sleep(120);
        mouse_event(0x0002, 0, 0, 0, IntPtr.Zero); // LEFTDOWN
        System.Threading.Thread.Sleep(80);
        mouse_event(0x0004, 0, 0, 0, IntPtr.Zero); // LEFTUP
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

$outDir = Join-Path ([Environment]::GetFolderPath('Desktop')) 'PhotoBookExport'
if (Test-Path $outDir) { Remove-Item $outDir -Recurse -Force }

$exe = Join-Path $root 'bin\Release\net8.0-windows\PhotoBookRenamer.exe'
$proc = Start-Process -FilePath $exe -PassThru
$mainHwnd = [intptr]::Zero

try {
    $deadline = (Get-Date).AddSeconds(30)
    while ((Get-Date) -lt $deadline -and $mainHwnd -eq [intptr]::Zero) {
        Start-Sleep -Milliseconds 500
        $proc.Refresh()
        if ($proc.HasExited) { throw "app exited $($proc.ExitCode)" }
        if ($proc.MainWindowHandle -ne 0) { $mainHwnd = [intptr]$proc.MainWindowHandle }
    }
    if ($mainHwnd -eq [intptr]::Zero) { throw 'no main window' }

    # Open the fixture project.
    $el = [System.Windows.Automation.AutomationElement]::FromHandle($mainHwnd)
    $textCond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::Text)
    $titleEl = $null
    foreach ($t in $el.FindAll([System.Windows.Automation.TreeScope]::Descendants, $textCond)) {
        if ($t.Current.Name -like '*Testovyy*') { $titleEl = $t; break }
    }
    if ($null -eq $titleEl) { throw 'fixture project not found - run make-test-fixture.ps1' }

    $walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker
    $node = $walker.GetParent($titleEl); $open = $null
    for ($i = 0; $i -lt 10 -and $null -ne $node -and $null -eq $open; $i++) {
        foreach ($b in $node.FindAll([System.Windows.Automation.TreeScope]::Descendants,
            (New-Object System.Windows.Automation.PropertyCondition(
                [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                [System.Windows.Automation.ControlType]::Button)))) {
            if ($b.Current.Name -like '*Открыть проект*') { $open = $b; break }
        }
        $node = $walker.GetParent($node)
    }
    if ($null -eq $open) { throw 'no Open button' }
    $open.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Seconds 8

    # Header Export button.
    $el = [System.Windows.Automation.AutomationElement]::FromHandle($mainHwnd)
    $exportBtn = $null
    foreach ($b in $el.FindAll([System.Windows.Automation.TreeScope]::Descendants,
        (New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::Button)))) {
        if ($b.Current.Name -eq 'Экспорт' -and -not $b.Current.IsOffscreen) { $exportBtn = $b; break }
    }
    if ($null -eq $exportBtn) { throw 'export button not found' }
    if (-not $exportBtn.Current.IsEnabled) { throw 'export button is disabled' }
    $exportBtn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()

    # Wait for the modal.
    $dlgHwnd = [intptr]::Zero
    $deadline = (Get-Date).AddSeconds(20)
    while ((Get-Date) -lt $deadline -and $dlgHwnd -eq [intptr]::Zero) {
        foreach ($h in [W7]::Visible($proc.Id)) { if ($h -ne $mainHwnd) { $dlgHwnd = $h; break } }
        if ($dlgHwnd -eq [intptr]::Zero) { Start-Sleep -Milliseconds 500 }
    }
    if ($dlgHwnd -eq [intptr]::Zero) { throw 'export dialog never appeared' }
    [void][W7]::SetForegroundWindow($dlgHwnd)
    Start-Sleep -Seconds 1

    $dlg = [System.Windows.Automation.AutomationElement]::FromHandle($dlgHwnd)
    if ($PerBookSubfolders) {
        $cb = Find-ByName $dlg 'Создавать отдельную папку' 'CheckBox'
        if ($null -eq $cb) { throw 'subfolder checkbox not found' }

        $pt = $cb.Current.BoundingRectangle
        if ($pt.Width -le 0) { throw 'subfolder checkbox has no clickable area' }
        [W7]::Click([int]($pt.X + 8), [int]($pt.Y + $pt.Height / 2))
        Start-Sleep -Milliseconds 800
        Write-Host 'subfolder option: toggled'
    }

    $start = Find-ByName $dlg 'Начать экспорт' 'Button'
    if ($null -eq $start) { throw 'start button not found' }
    $start.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()

    # Wait for the copy to finish (dialog closes on success).
    $deadline = (Get-Date).AddSeconds(90)
    while ((Get-Date) -lt $deadline) {
        $still = @([W7]::Visible($proc.Id) | Where-Object { $_ -ne $mainHwnd })
        if ($still.Count -eq 0) { break }
        Start-Sleep -Milliseconds 700
    }
    Start-Sleep -Seconds 3

    if (-not (Test-Path $outDir)) { throw "no output at $outDir" }

    $files = @(Get-ChildItem $outDir -Recurse -File | Sort-Object FullName)
    Write-Host ''
    Write-Host '--- produced files ---'
    $files | ForEach-Object {
        Write-Host ("  {0,-42} {1,8} bytes" -f $_.FullName.Substring($outDir.Length + 1), $_.Length)
    }

    Write-Host ''
    if ($PerBookSubfolders) {
        $dirs = @(Get-ChildItem $outDir -Directory)
        Write-Host "subfolder mode: $($dirs.Count) subfolder(s)"
        if ($dirs.Count -ne 3) { throw "expected 3 per-book subfolders, found $($dirs.Count)" }
        foreach ($d in $dirs) {
            $n = @(Get-ChildItem $d.FullName -File).Count
            Write-Host ("  {0}  ({1} files)" -f $d.Name, $n)
            if ($n -ne 6) { throw "book folder '$($d.Name)' has $n files, expected 6" }
        }
        Write-Host 'PASS: per-book subfolders produced one folder per book with all its files'
    }
    else {
        $stray = @($files | Where-Object { $_.DirectoryName -ne $outDir })
        if ($stray.Count -gt 0) { throw 'flat mode produced subfolders' }

        # 3 books x (cover + 5 spreads) = 18 files, named 001-00 .. 003-05.
        $expected = @()
        foreach ($b in 1..3) {
            foreach ($f in 0..5) { $expected += ('{0:d3}-{1:d2}.jpg' -f $b, $f) }
        }
        $actual = @($files | ForEach-Object { $_.Name })

        $missing = @($expected | Where-Object { $actual -notcontains $_ })
        $extra = @($actual | Where-Object { $expected -notcontains $_ })
        if ($missing.Count) { throw ('missing: ' + ($missing -join ', ')) }
        if ($extra.Count) { throw ('unexpected: ' + ($extra -join ', ')) }

        Write-Host 'PASS: flat layout matches the site contract exactly (001-00.jpg ... 003-05.jpg)'
    }
}
finally {
    Get-Process PhotoBookRenamer -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Pop-Location
}
