# Opens one project in a running app instance and reports whether the window survived.
# Used when a view throws at runtime: the capture script can only say "the window
# disappeared", which is not a diagnosis.
#
# Usage: open-project-probe.ps1 -Index 0

param(
    [int]$Index = 0,
    [int]$WaitSeconds = 12
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

Add-Type @"
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public static class Probe {
    private delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);

    // Enumerating by PID, not by title: opening a project destroys and recreates the
    // WPF window, so any cached handle goes stale.
    public static IntPtr FindTopLevel(uint target) {
        var found = new List<IntPtr>();
        EnumWindows((h, l) => {
            uint pid; GetWindowThreadProcessId(h, out pid);
            if (pid != target) return true;
            if (IsWindowVisible(h)) found.Add(h);
            return true;
        }, IntPtr.Zero);
        return found.Count > 0 ? found[0] : IntPtr.Zero;
    }
}
"@

Get-Process PhotoBookRenamer -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 1
$exe = Join-Path (Split-Path -Parent $PSScriptRoot) 'src\PhotoBook.Desktop.Wpf\bin\Release\net8.0-windows\PhotoBookRenamer.exe'
$proc = Start-Process -FilePath $exe -PassThru

$hwnd = [IntPtr]::Zero
for ($i = 0; $i -lt 40; $i++) {
    Start-Sleep -Milliseconds 500
    $hwnd = [Probe]::FindTopLevel([uint32]$proc.Id)
    if ($hwnd -ne [IntPtr]::Zero) { break }
}
if ($hwnd -eq [IntPtr]::Zero) { Write-Host 'FAIL: no window'; exit 1 }

$root = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)
$cond = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
    [System.Windows.Automation.ControlType]::Button)
$buttons = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)

$open = @()
foreach ($b in $buttons) {
    if ($b.Current.Name -like 'Открыть*') { $open += $b }
}
if ($open.Count -le $Index) { Write-Host "FAIL: only $($open.Count) open buttons"; exit 1 }

$pattern = $open[$Index].GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
$pattern.Invoke()

$alive = $true
for ($i = 0; $i -lt ($WaitSeconds * 2); $i++) {
    Start-Sleep -Milliseconds 500
    $proc.Refresh()
    if ($proc.HasExited) { $alive = $false; break }
    if ([Probe]::FindTopLevel([uint32]$proc.Id) -eq [IntPtr]::Zero) { $alive = $false; break }
}

if ($alive) {
    Write-Host "OK: the window survived opening project #$Index"
} else {
    $code = try { $proc.ExitCode } catch { 'n/a' }
    Write-Host "CRASH: the process exited (code $code) after opening project #$Index"
}
