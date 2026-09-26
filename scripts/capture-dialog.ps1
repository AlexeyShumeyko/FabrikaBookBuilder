# Opens a dialog and screenshots it. Dialogs are separate top-level windows, so the main
# window capture scripts cannot see them.
#
# Usage:
#   pwsh -File scripts\capture-dialog.ps1 -ProjectName "Testovyy" -Dialog Export -Out doc\shots\export.png
#   pwsh -File scripts\capture-dialog.ps1 -ProjectName "Testovyy" -Dialog Folders -Out doc\shots\folders.png

param(
    [string]$ProjectName = '',
    [string]$Dialog = 'Export',
    [string]$Out = 'doc\shots\dialog.png',
    [int]$TimeoutSeconds = 30
)

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
public class Win3 {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);

    private delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);

    /// <summary>
    /// All visible top-level windows of a process. A dialog created with
    /// WindowStyle=None + AllowsTransparency is NOT exposed as a child of the desktop
    /// root in the UI Automation tree, so AutomationElement.RootElement cannot see it;
    /// EnumWindows can.
    /// </summary>
    public static IntPtr[] TopLevelWindows(int processId) {
        var found = new List<IntPtr>();
        EnumWindows((h, l) => {
            uint pid;
            GetWindowThreadProcessId(h, out pid);
            if (pid == (uint)processId && IsWindowVisible(h)) found.Add(h);
            return true;
        }, IntPtr.Zero);
        return found.ToArray();
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

function Get-MainWindow($proc) {
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 500
        $proc.Refresh()
        if ($proc.HasExited) { throw "app exited $($proc.ExitCode)" }
        if ($proc.MainWindowHandle -ne 0) { return $true }
    }
    throw 'no main window'
}

try {
    $exe = Join-Path $root 'bin\Release\net8.0-windows\PhotoBookRenamer.exe'
    $proc = Start-Process -FilePath $exe -PassThru
    Get-MainWindow $proc | Out-Null

    [void][Win3]::ShowWindow($proc.MainWindowHandle, 3)
    [void][Win3]::SetForegroundWindow($proc.MainWindowHandle)
    Start-Sleep -Seconds 2

    # Open a project so the export button is enabled.
    if ($ProjectName) {
        $el = [System.Windows.Automation.AutomationElement]::FromHandle($proc.MainWindowHandle)
        $textCond = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::Text)
        $titleEl = $null
        foreach ($t in $el.FindAll([System.Windows.Automation.TreeScope]::Descendants, $textCond)) {
            if ($t.Current.Name -like "*$ProjectName*") { $titleEl = $t; break }
        }
        if ($null -eq $titleEl) { throw "no project titled '$ProjectName'" }

        $walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker
        $node = $walker.GetParent($titleEl)
        $btn = $null
        for ($up = 0; $up -lt 10 -and $null -ne $node -and $null -eq $btn; $up++) {
            $b2 = $node.FindAll([System.Windows.Automation.TreeScope]::Descendants,
                (New-Object System.Windows.Automation.PropertyCondition(
                    [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                    [System.Windows.Automation.ControlType]::Button)))
            foreach ($b in $b2) { if ($b.Current.Name -like '*Открыть проект*') { $btn = $b; break } }
            $node = $walker.GetParent($node)
        }
        if ($null -eq $btn) { throw 'no Open button in the card' }
        $btn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
        Start-Sleep -Seconds 7
    }

    # Trigger the dialog.
    $main = [System.Windows.Automation.AutomationElement]::FromHandle($proc.MainWindowHandle)
    $trigger = switch ($Dialog) {
        'Export'  { Find-ByName $main 'Экспорт' 'Button' }
        'Folders' { Find-ByName $main 'Выбор папок' 'Button' }
        default   { throw "unknown -Dialog '$Dialog'" }
    }
    if ($null -eq $trigger) { throw "could not find the '$Dialog' trigger button" }
    $trigger.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Seconds 4

    # The dialog is a separate top-level window owned by the app.
    $mainHwnd = [intptr]$proc.MainWindowHandle
    $dlgHwnd = [intptr]::Zero
    $deadline = (Get-Date).AddSeconds(15)
    while ((Get-Date) -lt $deadline -and $dlgHwnd -eq [intptr]::Zero) {
        foreach ($h in [Win3]::TopLevelWindows($proc.Id)) {
            if ($h -ne $mainHwnd) { $dlgHwnd = $h; break }
        }
        if ($dlgHwnd -eq [intptr]::Zero) { Start-Sleep -Milliseconds 500 }
    }
    if ($dlgHwnd -eq [intptr]::Zero) { throw 'dialog window never appeared' }

    [void][Win3]::SetForegroundWindow($dlgHwnd)
    Start-Sleep -Seconds 1

    $r = New-Object Win3+RECT
    [void][Win3]::GetWindowRect($dlgHwnd, [ref]$r)
    $w2 = $r.Right - $r.Left; $h2 = $r.Bottom - $r.Top
    if ($w2 -le 0 -or $h2 -le 0) { throw "bad dialog rect ${w2}x${h2}" }

    $bmp = New-Object System.Drawing.Bitmap $w2, $h2
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($r.Left, $r.Top, 0, 0, (New-Object System.Drawing.Size($w2, $h2)))

    $full = Join-Path $root $Out
    $dir = Split-Path -Parent $full
    if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
    $bmp.Save($full, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
    Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue

    Write-Host "saved $full (${w2}x${h2})"
    exit 0
}
finally { Pop-Location }
