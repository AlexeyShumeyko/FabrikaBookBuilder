# Screenshots a custom confirmation dialog by triggering it, so the replacement for the
# system MessageBox can be looked at rather than assumed.
#
# The delete button used to have no automation name at all, which is why this had to wait
# for one to be added - a control with only a ToolTip is unreachable by name.
#
# Usage: pwsh -File scripts/capture-confirm.ps1 [-Out doc\shots\confirm.png]
#
# NOTE: this file contains Cyrillic literals and must stay UTF-8 **with BOM**.

param(
    [string]$Out = 'doc\shots\confirm.png',
    [int]$TimeoutSeconds = 45
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
public class Win4 {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
    private delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr h, uint cmd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextW(IntPtr h, System.Text.StringBuilder s, int max);
    public static string Title(IntPtr h) {
        var sb = new System.Text.StringBuilder(512);
        GetWindowTextW(h, sb, sb.Capacity);
        return sb.ToString();
    }
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();

    public static IntPtr[] Visible(int pid) {
        var f = new List<IntPtr>();
        EnumWindows((h, l) => { uint p; GetWindowThreadProcessId(h, out p);
            if (p == (uint)pid && IsWindowVisible(h)) f.Add(h); return true; }, IntPtr.Zero);
        return f.ToArray();
    }
    public static IntPtr FindTopLevel(uint pid) {
        IntPtr best = IntPtr.Zero;
        foreach (var h in Visible((int)pid)) { if (GetWindow(h, 4) == IntPtr.Zero) best = h; }
        return best != IntPtr.Zero ? best : (Visible((int)pid).Length > 0 ? Visible((int)pid)[0] : IntPtr.Zero);
    }
}
"@

# The window title this dialog carries. Anything else on screen is a different
# The text on the button that confirms this dialog. A window is identified by
# what it contains rather than by its caption: these windows draw their own
# chrome and Win32 reports an empty title for them, so a caption check would
# match nothing - or, worse, the wrong window.
$confirmLabel = 'Удалить'

function Find-ByName($rootEl, [string]$label, [string]$typeName) {
    $ct = [System.Windows.Automation.ControlType]::$typeName
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty, $ct)
    foreach ($b in $rootEl.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)) {
        if ($b.Current.Name -eq $label) { return $b }
    }
    return $null
}

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
    if ($proc.MainWindowHandle -eq 0) { throw 'no window' }
    [void][Win4]::ShowWindow($proc.MainWindowHandle, 3)
    [void][Win4]::SetForegroundWindow($proc.MainWindowHandle)
    Start-Sleep -Seconds 3

    $hwnd = [Win4]::FindTopLevel([uint32]$proc.Id)
    $el = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)

    # The project cards load asynchronously.
    $del = $null
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ($null -eq $del -and (Get-Date) -lt $deadline) {
        $del = Find-ByName $el ([char]0x0423 + [char]0x0434 + [char]0x0430 + [char]0x043B + [char]0x0438 + [char]0x0442 + [char]0x044C + ' ' + [char]0x043F + [char]0x0440 + [char]0x043E + [char]0x0435 + [char]0x043A + [char]0x0442) 'Button'
        if ($null -eq $del) { Start-Sleep -Milliseconds 700 }
    }
    if ($null -eq $del) { throw 'the delete button never appeared' }

    $del.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()

    # The confirmation is its own top-level window, and it is identified by the button
    # that confirms it. These windows draw their own chrome, so Win32 reports an empty
    # title for them, and "the first window that is not the main one" is not a check:
    # during the golden run it photographed the update window and reported success.
    $dlg = [intptr]::Zero
    $mainHwnd = $hwnd
    $deadline = (Get-Date).AddSeconds(30)
    $offered = @()
    while ((Get-Date) -lt $deadline -and $dlg -eq [intptr]::Zero) {
        foreach ($h in [Win4]::Visible($proc.Id)) {
            if ($h -eq $mainHwnd) { continue }
            $candidate = [System.Windows.Automation.AutomationElement]::FromHandle($h)
            if ($null -ne (Find-ByName $candidate $confirmLabel 'Button')) { $dlg = $h; break }
            $names = @()
            $buttons = $candidate.FindAll([System.Windows.Automation.TreeScope]::Descendants,
                (New-Object System.Windows.Automation.PropertyCondition(
                    [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                    [System.Windows.Automation.ControlType]::Button)))
            foreach ($b in $buttons) { $names += $b.Current.Name }
            $offered += ($names -join ', ')
        }
        if ($dlg -eq [intptr]::Zero) { Start-Sleep -Milliseconds 500 }
    }
    if ($dlg -eq [intptr]::Zero) {
        throw ("the confirmation dialog never appeared. Buttons found in the other windows: " + ($offered -join ' | '))
    }

    [void][Win4]::SetForegroundWindow($dlg)
    Start-Sleep -Milliseconds 800

    $r = New-Object Win4+RECT
    [void][Win4]::GetWindowRect($dlg, [ref]$r)
    $w = $r.Right - $r.Left; $h = $r.Bottom - $r.Top
    $bmp = New-Object System.Drawing.Bitmap $w, $h
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($r.Left, $r.Top, 0, 0, (New-Object System.Drawing.Size($w, $h)))
    $g.Dispose()

    $full = Join-Path $root $Out
    $dir = Split-Path -Parent $full
    if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
    $bmp.Save($full, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()

    Write-Host "saved $full (${w}x${h})"
    exit 0
}
finally {
    if ($proc) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue }
    Pop-Location
}
