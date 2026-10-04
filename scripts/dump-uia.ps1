# Dumps the UI Automation tree of the running app: control type + automation name.
# Used when a script cannot find a control - it answers "is the control there under
# another name, or is it not there at all" without guessing.
#
# Usage: pwsh -File scripts\dump-uia.ps1 -Mode Combined
#
# NOTE: this file contains Cyrillic literals and must stay UTF-8 **with BOM**.

param(
    [ValidateSet('Unique', 'Combined', 'None')]
    [string]$Mode = 'Combined',
    [int]$TimeoutSeconds = 45
)

[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @"
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public class Win6 {
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
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
}
"@

$openLabel = '*' + [char]0x041E + [char]0x0442 + [char]0x043A + [char]0x0440 + [char]0x044B + [char]0x0442 + [char]0x044C + ' ' + [char]0x041F + [char]0x0440 + [char]0x043E + [char]0x0435 + [char]0x043A + [char]0x0442 + '*'

if (Get-Process -Name PhotoBookRenamer -ErrorAction SilentlyContinue) { throw 'close the app first' }

$proc = $null
try {
    $exe = Join-Path $root 'src\PhotoBook.Desktop.Wpf\bin\Release\net8.0-windows\PhotoBookRenamer.exe'
    $proc = Start-Process -FilePath $exe -PassThru
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 500
        $proc.Refresh()
        if ($proc.HasExited) { throw "app exited $($proc.ExitCode)" }
        if ($proc.MainWindowHandle -ne 0) { break }
    }
    [void][Win6]::ShowWindow($proc.MainWindowHandle, 3)
    [void][Win6]::SetForegroundWindow($proc.MainWindowHandle)
    Start-Sleep -Seconds 3

    if ($Mode -ne 'None') {
        $indexPath = Join-Path $env:LOCALAPPDATA 'PhotoBookRenamer\Projects\projects.json'
        $parsed = Get-Content $indexPath -Raw -Encoding UTF8 | ConvertFrom-Json
        $all = @($parsed | ForEach-Object { $_ } | Sort-Object { [datetime]$_.lastModified } -Descending)
        $wantMode = if ($Mode -eq 'Combined') { 3 } else { 2 }
        $pick = @($all | Where-Object { $_.mode -eq $wantMode -and $_.bookCount -gt 0 })[0]
        if (-not $pick) { throw "no filled $Mode project" }
        $at = [array]::IndexOf($all, $pick)
        Write-Host "opening '$($pick.name)' (card $at)"

        $hwnd = [Win6]::FindTopLevel([uint32]$proc.Id)
        $el = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)
        $cond = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::Button)
        $open = $null
        $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
        while ($null -eq $open -and (Get-Date) -lt $deadline) {
            $cards = @($el.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond) |
                Where-Object { $_.Current.Name -like $openLabel })
            if ($cards.Count -gt $at) { $open = $cards[$at] }
            if ($null -eq $open) { Start-Sleep -Milliseconds 700 }
        }
        if ($null -eq $open) { throw 'no open button' }
        $open.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
        Start-Sleep -Seconds 8
    }

    $hwnd = [Win6]::FindTopLevel([uint32]$proc.Id)
    $el = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)
    Write-Host "--- window: $($el.Current.Name) ---"
    foreach ($e in $el.FindAll([System.Windows.Automation.TreeScope]::Descendants,
            [System.Windows.Automation.Condition]::TrueCondition)) {
        $n = $e.Current.Name
        $t = $e.Current.ControlType.ProgrammaticName -replace 'ControlType\.', ''
        if ($t -in @('Edit', 'Button', 'CheckBox', 'Text', 'ComboBox')) {
            $val = ''
            try { $val = $e.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value } catch { }
            $off = $e.Current.IsOffscreen
            Write-Host ("{0,-10} off={1,-5} '{2}' {3}" -f $t, $off, $n, $val)
        }
    }
    exit 0
}
finally {
    if ($proc) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue }
    Pop-Location
}
