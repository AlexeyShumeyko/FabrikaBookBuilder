# Opens the help dialog and screenshots it. It is a top-level window of its own, so the main
# window capture scripts cannot see it.
#
# Usage: pwsh -File scripts\capture-help-dialog.ps1 -Out doc\shots\help.png
#
# NOTE: this file contains Cyrillic literals and must stay UTF-8 **with BOM**.

param(
    [string]$Out = 'doc\shots\help.png',
    [int]$TimeoutSeconds = 40
)

[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8

$ErrorActionPreference = 'Stop'

# One place that knows where the built program is.
. "$PSScriptRoot\common.ps1"
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @"
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public class WinHelp {
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
    private delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr h, uint cmd);
    [DllImport("user32.dll")] private static extern int GetWindowTextLength(IntPtr h);
    [DllImport("user32.dll")] private static extern int GetWindowText(IntPtr h, System.Text.StringBuilder s, int n);
    public static string Title(IntPtr h) {
        int n = GetWindowTextLength(h);
        if (n <= 0) return "";
        var sb = new System.Text.StringBuilder(n + 1);
        GetWindowText(h, sb, sb.Capacity);
        return sb.ToString();
    }
    public static IntPtr[] Visible(int pid) {
        var f = new List<IntPtr>();
        EnumWindows((h, l) => { uint p; GetWindowThreadProcessId(h, out p);
            if (p == (uint)pid && IsWindowVisible(h)) f.Add(h); return true; }, IntPtr.Zero);
        return f.ToArray();
    }
    public static IntPtr MainOf(int pid) {
        IntPtr best = IntPtr.Zero;
        foreach (var h in Visible(pid)) { if (GetWindow(h, 4) == IntPtr.Zero) best = h; }
        return best;
    }
}
"@

$exe = Get-AppExe
if (-not (Test-Path $exe)) { throw "no build at $exe" }

$proc = Start-Process -FilePath $exe -PassThru
try {
    $up = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $up) {
        Start-Sleep -Milliseconds 500
        $proc.Refresh()
        if ($proc.HasExited) { throw "app exited $($proc.ExitCode)" }
        if ($proc.MainWindowHandle -ne 0) { break }
    }
    if ($proc.MainWindowHandle -eq 0) { throw 'no main window' }
    [void][WinHelp]::ShowWindow($proc.MainWindowHandle, 3)
    [void][WinHelp]::SetForegroundWindow($proc.MainWindowHandle)
    Start-Sleep -Seconds 3

    $main = [WinHelp]::MainOf($proc.Id)
    $el = [System.Windows.Automation.AutomationElement]::FromHandle($main)

    $cond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::Button)
    $help = $null
    $dl = (Get-Date).AddSeconds($TimeoutSeconds)
    while ($null -eq $help -and (Get-Date) -lt $dl) {
        foreach ($b in $el.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)) {
            if ($b.Current.Name -eq 'Помощь') { $help = $b; break }
        }
        if ($null -eq $help) { Start-Sleep -Milliseconds 500 }
    }
    if ($null -eq $help) { throw 'the Помощь button was not found' }

    $help.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Seconds 2

    # The dialog is its own top-level window; a tooltip is a window too, so look for a title.
    $dialog = [intptr]::Zero
    $dl = (Get-Date).AddSeconds(20)
    while ($dialog -eq [intptr]::Zero -and (Get-Date) -lt $dl) {
        Start-Sleep -Milliseconds 400
        foreach ($h in [WinHelp]::Visible($proc.Id)) {
            if ($h -eq $main) { continue }
            if ([WinHelp]::Title($h) -eq 'Помощь') { $dialog = $h; break }
        }
    }
    if ($dialog -eq [intptr]::Zero) { throw 'the help dialog did not open' }

    # A StaticResource that does not exist only fails when the window loads, so reaching this
    # point already proves every key resolved.
    $dlgEl = [System.Windows.Automation.AutomationElement]::FromHandle($dialog)
    $r = $dlgEl.Current.BoundingRectangle
    Write-Host ("dialog: {0}x{1} at {2},{3}" -f [int]$r.Width, [int]$r.Height, [int]$r.X, [int]$r.Y)

    $pathText = ''
    # A read-only box has no accessible name of its own; its text is the value.
    $edits = $dlgEl.FindAll([System.Windows.Automation.TreeScope]::Descendants,
        (New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::Edit)))
    foreach ($t in $edits) {
        try {
            $v = $t.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value
            if ($v -match 'journal\.log') { $pathText = $v }
        }
        catch { }
    }
    if (-not $pathText) {
        Write-Host 'what the dialog actually exposes:'
        $texts = $dlgEl.FindAll([System.Windows.Automation.TreeScope]::Descendants,
            (New-Object System.Windows.Automation.PropertyCondition(
                [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                [System.Windows.Automation.ControlType]::Text)))
        foreach ($t in $texts) { Write-Host ("   [" + $t.Current.Name + "]") }
    }
    Write-Host ("journal shown in the dialog: {0}" -f $(if ($pathText) { $pathText } else { 'NOT FOUND' }))
    if (-not $pathText) { throw 'the dialog opened but does not show the journal path' }

    $bmp = New-Object System.Drawing.Bitmap([int]$r.Width, [int]$r.Height)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    # The picture is taken from the screen, so what is in front is what lands in it.
    Set-WindowInFront $dialog $proc.Id
    Assert-WindowInFront $dialog
    $g.CopyFromScreen([int]$r.X, [int]$r.Y, 0, 0, $bmp.Size)
    $outFull = Join-Path $root $Out
    $bmp.Save($outFull, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
    Write-Host "screenshot: $outFull"

    Write-Host ''
    Write-Host 'PASS: the help dialog opens, every resource resolved, and it shows the journal path.'
}
finally {
    if ($proc -and -not $proc.HasExited) { $proc.Kill() }
}
