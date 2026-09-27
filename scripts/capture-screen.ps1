# Captures the app on a given screen by driving the tab bar with UI Automation,
# then screenshots the window. Verifying a redesign by eye needs this: the tabs are
# RadioButtons with no automation id, so they are found by their label text.
#
# NOTE: this file contains Cyrillic literals and must stay UTF-8 **with BOM**.
# Windows PowerShell 5.1 reads a BOM-less UTF-8 .ps1 as ANSI, which silently turns
# every Russian string above into mojibake and makes tab lookup fail.
#
# Usage:
#   pwsh -File scripts/capture-screen.ps1 -Tab "Мои проекты"      -Out doc\shots\projects.png
#   pwsh -File scripts/capture-screen.ps1 -Tab "Выбор режима"     -Out doc\shots\mode.png
#   pwsh -File scripts/capture-screen.ps1 -Tab "Уникальные папки" -Out doc\shots\unique.png
#   pwsh -File scripts/capture-screen.ps1 -Tab "Комбинированный"  -Out doc\shots\combined.png
#
#   # open a project first so the editor screens show real data
#   pwsh -File scripts/capture-screen.ps1 -Tab "Уникальные папки" -OpenProject -ProjectName "X" -Out ...
#   pwsh -File scripts/capture-screen.ps1 -Tab "Комбинированный"  -OpenProject -OpenIndex 0    -Out ...

param(
    [string]$Tab = 'Мои проекты',
    [string]$Out = 'doc\shots\screen.png',
    [int]$TimeoutSeconds = 30,
    [switch]$OpenProject,
    [int]$OpenIndex = 0,
    [string]$ProjectName = '',
    [int]$HoverX = -1,
    [int]$HoverY = -1,
    [string]$HoverOut = '',
    [switch]$WithDialog
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
public class Win2 {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);

    private delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr h, uint cmd);

    /// <summary>
    /// Top-level visible window of a process, preferring the foreground one.
    /// Opening a project destroys and recreates the WPF window, so a cached HWND
    /// goes stale and Process.MainWindowHandle can report 0; enumerating by PID
    /// always finds the live one.
    /// </summary>
    public static IntPtr FindTopLevel(uint target) {
        var found = new List<IntPtr>();
        EnumWindows((h, l) => {
            uint pid; GetWindowThreadProcessId(h, out pid);
            if (pid != target) return true;
            if (GetWindow(h, 4 /*GW_OWNER*/) != IntPtr.Zero) return true;  // skip owned popups
            if (!IsWindowVisible(h)) return true;
            found.Add(h);
            return true;
        }, IntPtr.Zero);
        if (found.Count == 0) return IntPtr.Zero;
        IntPtr fg = GetForegroundWindow();
        foreach (var h in found) if (h == fg) return h;
        return found[0];
    }
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
}
"@

# The label every project card's open button carries.
$script:OpenCardLabel = '*' + [char]0x041E + [char]0x0442 + [char]0x043A + [char]0x0440 + [char]0x044B + [char]0x0442 + [char]0x044C + ' ' + [char]0x043F + [char]0x0440 + [char]0x043E + [char]0x0435 + [char]0x043A + [char]0x0442 + '*'  # *Открыть проект*

function Get-ByName($rootEl, [string]$label, [string]$typeName) {
    $ct = [System.Windows.Automation.ControlType]::$typeName
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty, $ct)
    foreach ($b in $rootEl.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)) {
        if ($b.Current.Name -like "*$label*") { return $b }
    }
    return $null
}

function Get-TabElement($rootEl, [string]$label) { Get-ByName $rootEl $label 'RadioButton' }

function Get-OpenButtons($rootEl) {
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::Button)
    return @($rootEl.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond) |
        Where-Object { $_.Current.Name -like $script:OpenCardLabel })
}

function Invoke-Element($el) {
    $p = $el.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
    $p.Invoke()
}

try {
    $exe = Join-Path $root 'bin\Release\net8.0-windows\PhotoBookRenamer.exe'
    $proc = Start-Process -FilePath $exe -PassThru
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 500
        $proc.Refresh()
        if ($proc.HasExited) { Write-Host "FAIL: app exited $($proc.ExitCode)"; exit 1 }
        if ($proc.MainWindowHandle -ne 0) { break }
    }
    if ($proc.MainWindowHandle -eq 0) { Write-Host 'FAIL: no window'; Stop-Process -Id $proc.Id -Force; exit 1 }

    # Pin the HWND. WPF keeps one top-level window for the app's lifetime, but
    # Process.MainWindowHandle can report 0 once a project has been opened, so the
    # handle is read once here and reused for every later call.
    $hwnd = $proc.MainWindowHandle

    [void][Win2]::ShowWindow($hwnd, 3)
    [void][Win2]::SetForegroundWindow($hwnd)
    Start-Sleep -Seconds 2

    # The project is opened BEFORE the tab is switched: opening needs the project
    # list on screen, so switching first would hide the cards and leave nothing to
    # click. A fresh launch always starts on the projects screen.
    if ($OpenProject) {
        $el = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)

        # The project list is populated asynchronously, so its cards are not there the
        # instant the window appears. Poll until they show up.
        $loadDeadline = (Get-Date).AddSeconds($TimeoutSeconds)
        $openButtons = @()
        do {
            Start-Sleep -Milliseconds 700
            $openButtons = Get-OpenButtons $el
        } while ($openButtons.Count -eq 0 -and (Get-Date) -lt $loadDeadline)

        if ($openButtons.Count -eq 0) {
            Write-Host 'FAIL: no project cards appeared within the timeout'
            Stop-Process -Id $proc.Id -Force
            exit 1
        }

        if ($ProjectName) {
            # Walk up from the card's title text until a container holding an open
            # button is found. Index-based selection is fragile because the list is
            # ordered by LastModified.
            $textCond = New-Object System.Windows.Automation.PropertyCondition(
                [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                [System.Windows.Automation.ControlType]::Text)
            $titleEl = $null
            foreach ($t in $el.FindAll([System.Windows.Automation.TreeScope]::Descendants, $textCond)) {
                if ($t.Current.Name -like "*$ProjectName*") { $titleEl = $t; break }
            }
            if ($null -eq $titleEl) {
                Write-Host "FAIL: no project titled '$ProjectName'"
                Stop-Process -Id $proc.Id -Force
                exit 1
            }

            $walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker
            $node = $walker.GetParent($titleEl)
            $target = $null
            for ($up = 0; $up -lt 10 -and $null -ne $node; $up++) {
                foreach ($b in Get-OpenButtons $node) { $target = $b; break }
                if ($null -ne $target) { break }
                $node = $walker.GetParent($node)
            }
            if ($null -eq $target) {
                Write-Host "FAIL: found the title '$ProjectName' but no Open button in its card"
                Stop-Process -Id $proc.Id -Force
                exit 1
            }
        }
        else {
            if ($OpenIndex -ge $openButtons.Count) {
                Write-Host "FAIL: -OpenIndex $OpenIndex but only $($openButtons.Count) project cards exist"
                Stop-Process -Id $proc.Id -Force
                exit 1
            }
            $target = $openButtons[$OpenIndex]
        }

        Invoke-Element $target
        # Project loading is async (thumbnails, double BeginInvoke), so give it room.
        Start-Sleep -Seconds 7
    }

    # Switch to the requested screen only after any project is open.
    if ($Tab -ne 'Мои проекты') {
        # Opening a project recreates the top-level window, so the pinned HWND is
        # stale. Re-resolve the live window for this process instead of reusing it.
        for ($try = 0; $try -lt 20; $try++) {
            $hwnd = [Win2]::FindTopLevel([uint32]$proc.Id)
            if ($hwnd -ne [IntPtr]::Zero) { break }
            Start-Sleep -Milliseconds 500
        }
        if ($hwnd -eq [IntPtr]::Zero) {
            Write-Host 'FAIL: the application window disappeared after opening the project'
            Stop-Process -Id $proc.Id -Force
            exit 1
        }
        $el = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)
        $tabEl = Get-TabElement $el $Tab
        if ($null -eq $tabEl) {
            Write-Host "FAIL: tab '$Tab' not found. Available:"
            $cond = New-Object System.Windows.Automation.PropertyCondition(
                [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                [System.Windows.Automation.ControlType]::RadioButton)
            $el.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond) |
                ForEach-Object { Write-Host "  - $($_.Current.Name)" }
            Stop-Process -Id $proc.Id -Force
            exit 1
        }
        $sel = $tabEl.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
        $sel.Select()
        Start-Sleep -Seconds 2
    }

    $r = New-Object Win2+RECT
    [void][Win2]::GetWindowRect($hwnd, [ref]$r)
    $w = $r.Right - $r.Left; $h = $r.Bottom - $r.Top

    if ($w -le 0 -or $h -le 0) {
        Write-Host "FAIL: window rect is ${w}x${h} - the app probably crashed while opening the project."
        Get-WinEvent -FilterHashtable @{ LogName = 'Application'; StartTime = (Get-Date).AddMinutes(-3) } -ErrorAction SilentlyContinue |
            Where-Object { $_.ProviderName -in 'Application Error', '.NET Runtime' } |
            Select-Object -First 2 | ForEach-Object { Write-Host $_.Message }
        Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
        exit 1
    }

    if ($WithDialog) {
        # Open the OS file picker and photograph the WHOLE screen, dialog included. The
        # picker is a modal, so it is the one moment where a veil drawn over our own
        # window becomes visible - which is how the owner saw a grey background appear.
        $pick = Get-ByName $el 'Загрузить фото' 'Button'
        if ($null -eq $pick) { Write-Host 'FAIL: no load button to open the picker with' }
        else { Invoke-Element $pick; Start-Sleep -Seconds 4 }

        Add-Type -AssemblyName System.Windows.Forms
        $vs = [System.Windows.Forms.SystemInformation]::VirtualScreen
        $dbmp = New-Object System.Drawing.Bitmap $vs.Width, $vs.Height
        $dg = [System.Drawing.Graphics]::FromImage($dbmp)
        $dg.CopyFromScreen($vs.X, $vs.Y, 0, 0, $dbmp.Size)
        $dfull = Join-Path $root 'doc\shots\with-dialog.png'
        $dbmp.Save($dfull, [System.Drawing.Imaging.ImageFormat]::Png)
        $dg.Dispose(); $dbmp.Dispose()
        Write-Host "saved $dfull with the picker open"
        Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
        exit 0
    }

    $bmp = New-Object System.Drawing.Bitmap $w, $h
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($r.Left, $r.Top, 0, 0, (New-Object System.Drawing.Size($w, $h)))

    $full = Join-Path $root $Out
    $dir = Split-Path -Parent $full
    if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
    $bmp.Save($full, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()

    # Optional hover pass. A hover state that changes the layout can only be judged
    # against the resting shot, so both are taken from the same live window: the first
    # one above, this one below, with the pointer parked where the caller asked.
    if ($HoverX -ge 0) {
        [void][Win2]::SetForegroundWindow($hwnd)
        [void][Win2]::SetCursorPos($r.Left + $HoverX, $r.Top + $HoverY)
        Start-Sleep -Milliseconds 900

        $hbmp = New-Object System.Drawing.Bitmap $w, $h
        $hg = [System.Drawing.Graphics]::FromImage($hbmp)
        $hg.CopyFromScreen($r.Left, $r.Top, 0, 0, (New-Object System.Drawing.Size($w, $h)))
        $hoverFull = if ($HoverOut) { Join-Path $root $HoverOut } else { $full.Replace('.png', '-hover.png') }
        $hdir = Split-Path -Parent $hoverFull
        if ($hdir -and -not (Test-Path $hdir)) { New-Item -ItemType Directory -Force -Path $hdir | Out-Null }
        $hbmp.Save($hoverFull, [System.Drawing.Imaging.ImageFormat]::Png)
        $hg.Dispose(); $hbmp.Dispose()
        Write-Host "saved $hoverFull (hover at ${HoverX},${HoverY})"
    }

    Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue

    Write-Host "saved $full (${w}x${h}) for tab '$Tab'"
    exit 0
}
finally {
    Pop-Location
}
