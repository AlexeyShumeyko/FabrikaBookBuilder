# One-off probe: what window, if any, appears when the run is GREWN?
# Prints the title of every visible top-level window after the apply, and saves a shot.
param([int]$Card = 0, [int]$Spreads = 12, [string]$Out = 'doc\shots\probe-structure.png')

[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms
Add-Type @"
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
public class WinP8 {
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc cb, IntPtr l);
    public delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint p);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] private static extern int GetWindowTextLength(IntPtr h);
    [DllImport("user32.dll")] private static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] private static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr h, uint cmd);
    [DllImport("user32.dll")] private static extern bool IsWindowEnabled(IntPtr h);
    public static string Describe(IntPtr h) {
        int n = GetWindowTextLength(h);
        var t = new StringBuilder(n + 1); GetWindowText(h, t, t.Capacity);
        var c = new StringBuilder(256); GetClassName(h, c, 256);
        return h.ToInt64() + " | title='" + t.ToString() + "' | class=" + c.ToString()
             + " | owner=" + GetWindow(h, 4).ToInt64() + " | enabled=" + IsWindowEnabled(h)
             + " | rect=" + Rect(h);
    }
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out RECT r);
    private static string Rect(IntPtr h) { RECT r; GetWindowRect(h, out r); return r.L + "," + r.T + "," + (r.R - r.L) + "x" + (r.B - r.T); }
    public static string[] Titles(uint target) {
        var list = new List<string>();
        EnumWindows((h, l) => {
            uint p; GetWindowThreadProcessId(h, out p);
            if (p != target || !IsWindowVisible(h)) return true;
            int n = GetWindowTextLength(h);
            var sb = new StringBuilder(n + 1);
            GetWindowText(h, sb, sb.Capacity);
            list.Add(Describe(h));
            return true;
        }, IntPtr.Zero);
        return list.ToArray();
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

$openLabel = '*' + [char]0x041E + [char]0x0442 + [char]0x043A + [char]0x0440 + [char]0x044B + [char]0x0442 + [char]0x044C + ' ' + [char]0x041F + [char]0x0440 + [char]0x043E + [char]0x0435 + [char]0x043A + [char]0x0442 + '*'
$labelSpreads = [char]0x0420 + [char]0x0430 + [char]0x0437 + [char]0x0432 + [char]0x043E + [char]0x0440 + [char]0x0442 + [char]0x043E + [char]0x0432 + ' ' + [char]0x0432 + ' ' + [char]0x043A + [char]0x043D + [char]0x0438 + [char]0x0433 + [char]0x0435

if (Get-Process -Name PhotoBookRenamer -ErrorAction SilentlyContinue) { throw 'close the app first' }
$proc = $null
try {
    $exe = Join-Path (Split-Path -Parent $PSScriptRoot) 'src\PhotoBook.Desktop.Wpf\bin\Release\net8.0-windows\PhotoBookRenamer.exe'
    $proc = Start-Process -FilePath $exe -PassThru
    $deadline = (Get-Date).AddSeconds(60)
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 400
        $proc.Refresh()
        if ($proc.HasExited) { throw "app exited $($proc.ExitCode)" }
        if ($proc.MainWindowHandle -ne 0) { break }
    }
    [void][WinP8]::ShowWindow($proc.MainWindowHandle, 3)
    [void][WinP8]::SetForegroundWindow($proc.MainWindowHandle)
    Start-Sleep -Seconds 3

    $hwnd = $proc.MainWindowHandle
    $el = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)
    $open = $null
    $deadline = (Get-Date).AddSeconds(60)
    while ($null -eq $open -and (Get-Date) -lt $deadline) {
        $cards = @($el.FindAll([System.Windows.Automation.TreeScope]::Descendants,
            (New-Object System.Windows.Automation.PropertyCondition(
                [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                [System.Windows.Automation.ControlType]::Button))) |
            Where-Object { $_.Current.Name -like $openLabel })
        if ($cards.Count -gt $Card) { $open = $cards[$Card] }
        if ($null -eq $open) { Start-Sleep -Milliseconds 600 }
    }
    if ($null -eq $open) { throw 'no open button' }
    $open.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Seconds 8

    $el = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)
    $field = Find-ByName $el $labelSpreads 'Edit'
    $apply = Find-ByName $el ([char]0x0441 + [char]0x0442 + [char]0x0440 + [char]0x0443 + [char]0x043A + [char]0x0442 + [char]0x0443 + [char]0x0440 + [char]0x0443) 'Button'
    Write-Host '--- windows after opening the project ---'
    foreach ($t2 in [WinP8]::Titles([uint32]$proc.Id)) { Write-Host "  $t2" }
    Write-Host '--- edit controls ---'
    $editCond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::Edit)
    foreach ($e in $el.FindAll([System.Windows.Automation.TreeScope]::Descendants, $editCond)) {
        $v = ''
        try { $v = $e.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value } catch { }
        Write-Host ("  '{0}' = {1}" -f $e.Current.Name, $v)
    }
    if ($null -eq $field) { throw 'spreads field not found' }
    if ($null -eq $apply) { throw 'apply button not found' }

    Write-Host "field value now: $($field.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value)"
    $field.SetFocus()
    $field.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue("$Spreads")
    Start-Sleep -Milliseconds 300
    [System.Windows.Forms.SendKeys]::SendWait('{TAB}')
    Start-Sleep -Milliseconds 500
    Write-Host "applying $Spreads"
    $apply.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()

    Start-Sleep -Seconds 6
    Write-Host '--- visible windows after applying ---'
    foreach ($t in [WinP8]::Titles([uint32]$proc.Id)) { Write-Host "  $t" }
    exit 0
}
finally {
    if ($proc) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue }
}
