# Measures what opening a project actually costs, in a way that cannot flatter itself.
#
# A stopwatch around "open the project" says nothing: the click returns instantly and the
# work happens afterwards on the UI thread. What a person feels is how long the window stops
# answering, and what a slow PC suffers is CPU time and memory. So this samples the process
# while it opens the project, and then measures the round-trip of a UI Automation call - a
# call that has to be answered by the UI thread, so its latency IS the freeze the user saw.
#
# Usage:
#   pwsh -File scripts\perf-open.ps1 -Card 0 -Label "29x10 empty"
#   pwsh -File scripts\perf-open.ps1 -Card 0 -Label "after fix" -Out doc\shots\perf-after.png
#
# NOTE: this file contains Cyrillic literals and must stay UTF-8 **with BOM**.

param(
    [int]$Card = -1,
    [int]$Books = 0,
    [int]$Spreads = 0,
    [string]$Label = '',
    [int]$TimeoutSeconds = 120,
    [switch]$NoOpen
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
public class WinP {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
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
    $exe = Join-Path $root 'bin\Release\net8.0-windows\PhotoBookRenamer.exe'
    $proc = Start-Process -FilePath $exe -PassThru
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 400
        $proc.Refresh()
        if ($proc.HasExited) { throw "app exited $($proc.ExitCode)" }
        if ($proc.MainWindowHandle -ne 0) { break }
    }
    if ($proc.MainWindowHandle -eq 0) { throw 'no main window' }
    [void][WinP]::ShowWindow($proc.MainWindowHandle, 3)
    [void][WinP]::SetForegroundWindow($proc.MainWindowHandle)
    Start-Sleep -Seconds 3

    $hwnd = [WinP]::FindTopLevel([uint32]$proc.Id)
    $el = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)

    if ($NoOpen) {
        # Idle baseline: what the app costs before any project is opened.
        $p = $proc
        $idleCpu = $p.TotalProcessorTime.TotalMilliseconds
        $idleWs = $p.WorkingSet64
        Start-Sleep -Seconds 5
        $proc.Refresh()
        Write-Host ("idle 5s: cpu {0:N0} ms, working set {1:N0} MB, private {2:N0} MB" -f `
            ($proc.TotalProcessorTime.TotalMilliseconds - $idleCpu), `
            ($proc.WorkingSet64 / 1MB), ($proc.PrivateMemorySize64 / 1MB))
        exit 0
    }

    # Pick the card by the project's shape, not by its position. Every run bumps
    # LastModified on the project it opens, which moves that card to the top - so a
    # hardcoded index silently measures a different project on the next run, and the
    # comparison is meaningless.
    if ($Books -gt 0) {
        $indexPath = Join-Path $env:LOCALAPPDATA 'PhotoBookRenamer\Projects\projects.json'
        $entries = @((Get-Content $indexPath -Raw -Encoding UTF8 | ConvertFrom-Json) |
            Sort-Object { [datetime]$_.lastModified } -Descending)
        $at = -1
        for ($i = 0; $i -lt $entries.Count; $i++) {
            if ($entries[$i].mode -ne 3) { continue }
            if ($entries[$i].bookCount -ne $Books) { continue }
            if ($Spreads -gt 0 -and $entries[$i].pageCount -ne $Spreads) { continue }
            $at = $i; break
        }
        if ($at -lt 0) { throw "no combined project with $Books books and $Spreads spreads" }
        $Card = $at
        Write-Host "picked card $Card ('$($entries[$at].name)', $($entries[$at].bookCount) books x $($entries[$at].pageCount) spreads)"
    }
    if ($Card -lt 0) { throw 'give -Books/-Spreads, or -Card' }

    $btnCond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::Button)
    $open = $null
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ($null -eq $open -and (Get-Date) -lt $deadline) {
        $cards = @($el.FindAll([System.Windows.Automation.TreeScope]::Descendants, $btnCond) |
            Where-Object { $_.Current.Name -like $openLabel })
        if ($cards.Count -gt $Card) { $open = $cards[$Card] }
        if ($null -eq $open) { Start-Sleep -Milliseconds 600 }
    }
    if ($null -eq $open) { throw "no open button at card $Card" }

    # --- the measurement -------------------------------------------------------
    # Sampling happens in this thread, between the probes. A background sampler was the
    # first version and it could not work: a thread-pool callback has no runspace here, so
    # every sample it took went nowhere. The probes themselves are cross-process round
    # trips the UI thread must answer, so their latency is exactly the freeze.
    $cpu0 = $proc.TotalProcessorTime.TotalMilliseconds
    $ws0 = $proc.WorkingSet64
    $priv0 = $proc.PrivateMemorySize64
    $t0 = Get-Date

    $peakWs = $ws0; $peakPriv = $priv0; $worst = 0.0; $recovered = -1
    $lat = New-Object System.Collections.Generic.List[double]

    $open.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()

    $deadline2 = (Get-Date).AddSeconds(40)
    while ((Get-Date) -lt $deadline2) {
        $sw = [System.Diagnostics.Stopwatch]::StartNew()
        try { $null = $el.Current.Name } catch { }
        $sw.Stop()
        $ms = $sw.Elapsed.TotalMilliseconds
        $lat.Add($ms)
        if ($ms -gt $worst) { $worst = $ms }
        if ($recovered -lt 0 -and $ms -lt 120) { $recovered = [int]((Get-Date) - $t0).TotalMilliseconds }
        if ($recovered -gt 0 -and $lat.Count -ge 30) { break }

        try {
            $proc.Refresh()
            if ($proc.WorkingSet64 -gt $peakWs) { $peakWs = $proc.WorkingSet64 }
            if ($proc.PrivateMemorySize64 -gt $peakPriv) { $peakPriv = $proc.PrivateMemorySize64 }
        } catch { }

        Start-Sleep -Milliseconds 80
    }

    $proc.Refresh()
    $cpuUsed = $proc.TotalProcessorTime.TotalMilliseconds - $cpu0

    Write-Host ''
    if ($Label) { Write-Host "=== $Label ===" }
    Write-Host ("first UI response   : {0} ms after the click" -f $recovered)
    Write-Host ("worst UI stall      : {0:N0} ms" -f $worst)
    Write-Host ("CPU used            : {0:N0} ms" -f $cpuUsed)
    Write-Host ("working set         : {0:N0} -> {1:N0} MB (peak {2:N0} MB)" -f ($ws0 / 1MB), ($proc.WorkingSet64 / 1MB), ($peakWs / 1MB))
    Write-Host ("private bytes       : {0:N0} -> {1:N0} MB (peak {2:N0} MB)" -f ($priv0 / 1MB), ($proc.PrivateMemorySize64 / 1MB), ($peakPriv / 1MB))
    Write-Host ("handles / threads   : {0} / {1}" -f $proc.HandleCount, $proc.Threads.Count)    exit 0
}
finally {
    if ($proc) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue }
    Pop-Location
}
