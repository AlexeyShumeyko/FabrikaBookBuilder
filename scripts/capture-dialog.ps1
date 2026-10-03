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
    [ValidateSet('Unique', 'Combined')]
    [string]$Mode = 'Unique',
    [switch]$NoProject,
    [string]$Toggle = '',
    [int]$TimeoutSeconds = 30
)

# The harness console runs on code page 866, which turns every Cyrillic string this
# script prints into "?" and floods the agent context with mojibake. Force UTF-8 on
# both channels; a child powershell.exe resets these on its own, so it has to be set
# inside each script rather than once in the caller.
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

    /// <summary>
    /// The main window: the visible top-level one with no owner. Opening a project destroys
    /// and recreates the window, so the handle captured at launch is stale afterwards.
    /// </summary>
    public static IntPtr FindTopLevel(uint processId) {
        var found = TopLevelWindows((int)processId);
        IntPtr best = IntPtr.Zero;
        foreach (var h in found) {
            if (GetWindow(h, 4) != IntPtr.Zero) continue;   // GW_OWNER: a dialog, not the app
            best = h;
        }
        if (best != IntPtr.Zero) return best;
        return found.Length > 0 ? found[0] : IntPtr.Zero;
    }
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr h, uint cmd);
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
    if (-not $NoProject) {
        $el = [System.Windows.Automation.AutomationElement]::FromHandle($proc.MainWindowHandle)
        $openLabel = '*' + [char]0x041E + [char]0x0442 + [char]0x043A + [char]0x0440 + [char]0x044B + [char]0x0442 + [char]0x044C + ' ' + [char]0x041F + [char]0x0440 + [char]0x043E + [char]0x0435 + [char]0x043A + [char]0x0442 + '*'

        if (-not $ProjectName) {
            # Pick the newest project of the right mode from the index. A hardcoded index
            # stops being one after any project is opened, because opening rewrites
            # LastModified and moves that card to the top of the list.
            $indexPath = Join-Path $env:LOCALAPPDATA 'PhotoBookRenamer\Projects\projects.json'
            $parsed = Get-Content $indexPath -Raw -Encoding UTF8 | ConvertFrom-Json
            $all = @($parsed | ForEach-Object { $_ } | Sort-Object { [datetime]$_.lastModified } -Descending)
            $wantMode = if ($Mode -eq 'Combined') { 3 } else { 2 }
            $pick = @($all | Where-Object { $_.mode -eq $wantMode -and $_.bookCount -gt 0 })[0]
            if (-not $pick) { throw "no filled $Mode project in the index" }
            $OpenAt = [array]::IndexOf($all, $pick)
            Write-Host "opening '$($pick.name)' ($Mode, card $OpenAt)"
        }

        $btn = $null
        if ($ProjectName) {
            $titleEl = $null
            # Match on the name of any descendant, not on a control type: the project title
            # is a TextBlock inside the card, and the element that reports it as its name
            # depends on how the card template is built. $textCond used to be referenced
            # here and never defined, so passing -ProjectName always threw and this path
            # was never exercised until a capture actually needed it.
            $anyCond = [System.Windows.Automation.Condition]::TrueCondition
            foreach ($t in $el.FindAll([System.Windows.Automation.TreeScope]::Descendants, $anyCond)) {
                if ($t.Current.Name -like "*$ProjectName*") { $titleEl = $t; break }
            }
            if ($null -eq $titleEl) { throw "no project titled '$ProjectName'" }

            $walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker
            $node = $walker.GetParent($titleEl)
            for ($up = 0; $up -lt 10 -and $null -ne $node -and $null -eq $btn; $up++) {
                $b2 = $node.FindAll([System.Windows.Automation.TreeScope]::Descendants,
                    (New-Object System.Windows.Automation.PropertyCondition(
                        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                        [System.Windows.Automation.ControlType]::Button)))
                foreach ($b in $b2) { if ($b.Current.Name -like '*Открыть проект*') { $btn = $b; break } }
                $node = $walker.GetParent($node)
            }
        }
        else {
            # Open by position: the newest project of the wanted mode.
            $openCond = New-Object System.Windows.Automation.PropertyCondition(
                [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                [System.Windows.Automation.ControlType]::Button)
            $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
            while ($null -eq $btn -and (Get-Date) -lt $deadline) {
                $cards = @($el.FindAll([System.Windows.Automation.TreeScope]::Descendants, $openCond) |
                    Where-Object { $_.Current.Name -like $openLabel })
                if ($cards.Count -gt $OpenAt) { $btn = $cards[$OpenAt] }
                if ($null -eq $btn) { Start-Sleep -Milliseconds 700 }
            }
        }
        if ($null -eq $btn) { throw 'no Open button for the chosen project' }
        $btn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
        Start-Sleep -Seconds 7
    }

    # Trigger the dialog. Opening a project recreates the top-level window, so the handle
    # captured at launch is stale by now - re-resolve it, or the search runs against a
    # window that no longer exists and finds nothing.
    $mainHwndLive = [intptr]::Zero
    for ($try = 0; $try -lt 20; $try++) {
        $mainHwndLive = [Win3]::FindTopLevel([uint32]$proc.Id)
        if ($mainHwndLive -ne [intptr]::Zero) { break }
        Start-Sleep -Milliseconds 500
    }
    if ($mainHwndLive -eq [intptr]::Zero) { throw 'the application window disappeared after opening the project' }
    $main = [System.Windows.Automation.AutomationElement]::FromHandle($mainHwndLive)
    $trigger = switch ($Dialog) {
        # The two modes label the export button differently: the folders mode says
        # "Экспорт", the combined one "Экспортировать все книги". Try both.
        'Export'  { $a = Find-ByName $main 'Экспортировать все книги' 'Button'
                    if ($null -eq $a) { $a = Find-ByName $main 'Экспорт' 'Button' }
                    $a }
        'Folders' { Find-ByName $main 'Добавить или заменить папки' 'Button' }
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

    # Optional control to work before the screenshot. The export dialog's subfolder
    # checkbox is the case: ticking it used to throw, and "does clicking it kill the app"
    # cannot be seen in a screenshot of the dialog before the click.
    if ($Toggle) {
        $dlgEl = [System.Windows.Automation.AutomationElement]::FromHandle($dlgHwnd)
        $target = $null
        foreach ($type in 'CheckBox', 'Button') {
            $target = Find-ByName $dlgEl $Toggle $type
            if ($null -ne $target) { break }
        }
        if ($null -eq $target) { throw "could not find anything named '$Toggle' in the dialog" }
        $proc.Refresh()
        if ($proc.HasExited) { throw 'the app already died before the toggle' }
        $target.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle()
        Start-Sleep -Seconds 2
        $proc.Refresh()
        if ($proc.HasExited) { throw "the app died when '$Toggle' was used - that is the bug being hunted" }
        Write-Host "toggled '$Toggle'; the app is still alive"
    }

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
