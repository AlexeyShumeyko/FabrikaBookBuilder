# Screenshots the "shrink the structure" confirmation in the combined mode.
#
# The owner asked for it in the app's own design instead of the system MessageBox, and the
# dialog is only reachable by making the structure smaller - so the check has to make the
# structure smaller. It types into the real field instead of calling the ViewModel, because
# the point of the check is that the field's change reaches the question at all.
#
# Usage: pwsh -File scripts\capture-structure-confirm.ps1 [-Out doc\shots\dlg-confirm-structure.png]
#
# NOTE: this file contains Cyrillic literals and must stay UTF-8 **with BOM**.

param(
    [string]$Out = 'doc\shots\dlg-confirm-structure.png',
    [int]$TimeoutSeconds = 60
)

[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @"
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public class Win5 {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
    private delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
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

function Find-ByName($rootEl, [string]$label, [string]$typeName) {
    $ct = [System.Windows.Automation.ControlType]::$typeName
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty, $ct)
    foreach ($b in $rootEl.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)) {
        if ($b.Current.Name -like "*$label*") { return $b }
    }
    return $null
}

# Literals, not code points: this file is UTF-8 with BOM, and a hand-written code point
# list is easy to get subtly wrong (an earlier attempt dropped one letter and the control
# silently was "not found").
$labelSpreads = 'Разворотов в книге'
$openLabel = '*Открыть проект*'

if (Get-Process -Name PhotoBookRenamer -ErrorAction SilentlyContinue) { throw 'close the app first' }

$proc = $null
try {
    $exe = Join-Path $root 'bin\Release\net8.0-windows\PhotoBookRenamer.exe'
    $proc = Start-Process -FilePath $exe -PassThru
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 500
        $proc.Refresh()
        if ($proc.HasExited) { throw "app exited $($proc.ExitCode)" }
        if ($proc.MainWindowHandle -ne 0) { break }
    }
    if ($proc.MainWindowHandle -eq 0) { throw 'no window' }
    [void][Win5]::ShowWindow($proc.MainWindowHandle, 3)
    [void][Win5]::SetForegroundWindow($proc.MainWindowHandle)
    Start-Sleep -Seconds 3

    # Open the newest filled combined project (the fixture), by card position: the name
    # would not survive the command line, and a hardcoded card number stops being right
    # after any project is opened, because opening rewrites LastModified.
    $indexPath = Join-Path $env:LOCALAPPDATA 'PhotoBookRenamer\Projects\projects.json'
    $parsed = Get-Content $indexPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $all = @($parsed | ForEach-Object { $_ } | Sort-Object { [datetime]$_.lastModified } -Descending)
    $wantMode = 3
    $pick = @($all | Where-Object { $_.mode -eq $wantMode -and $_.bookCount -gt 0 })[0]
    if (-not $pick) { throw 'no filled combined project in the index' }
    $at = [array]::IndexOf($all, $pick)
    Write-Host "opening '$($pick.name)' (card $at)"

    $hwnd = [Win5]::FindTopLevel([uint32]$proc.Id)
    $el = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)

    $btnCond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::Button)
    $open = $null
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ($null -eq $open -and (Get-Date) -lt $deadline) {
        $cards = @($el.FindAll([System.Windows.Automation.TreeScope]::Descendants, $btnCond) |
            Where-Object { $_.Current.Name -like $openLabel })
        if ($cards.Count -gt $at) { $open = $cards[$at] }
        if ($null -eq $open) { Start-Sleep -Milliseconds 700 }
    }
    if ($null -eq $open) { throw 'no open button for the chosen project' }
    $open.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Seconds 8

    # The window is recreated by opening, so re-resolve it.
    $hwnd = [Win5]::FindTopLevel([uint32]$proc.Id)
    $el = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)

    $field = Find-ByName $el $labelSpreads 'Edit'
    if ($null -eq $field) { throw "the '$labelSpreads' field was not found" }

    $current = $field.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value
    $target = [int]$current - 1
    if ($target -lt 1) { throw "the field reads '$current'; nothing to shrink" }
    Write-Host "spreads: $current -> $target"

    [void][Win5]::SetForegroundWindow($hwnd)
    $field.SetFocus()
    $field.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue("$target")

    # The field commits on LostFocus, not on SetValue: setting the text through UI
    # Automation does not move the caret away, so without this the ViewModel never hears
    # about the change and no question is ever asked.
    Start-Sleep -Milliseconds 400
    [System.Windows.Forms.SendKeys]::SendWait('{TAB}')
    Start-Sleep -Milliseconds 600

    # ConfirmStructureCommand may still need to be pressed: the number only becomes a
    # question when the button is invoked.
    $apply = Find-ByName $el ([char]0x0414 + [char]0x043E + [char]0x0431 + [char]0x0430 + [char]0x0432 + [char]0x0438 + [char]0x0442 + [char]0x044C) 'Button'
    if ($null -eq $apply) { $apply = Find-ByName $el ([char]0x041E + [char]0x0431 + [char]0x043D + [char]0x043E + [char]0x0432 + [char]0x0438 + [char]0x0442 + [char]0x044C) 'Button' }
    if ($null -ne $apply) {
        Write-Host "invoking '$($apply.Current.Name)'"
        $apply.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    }

    $dlg = [intptr]::Zero
    $deadline = (Get-Date).AddSeconds(25)
    while ((Get-Date) -lt $deadline -and $dlg -eq [intptr]::Zero) {
        $proc.Refresh()
        if ($proc.HasExited) { throw "the app died when the structure was shrunk - that is the bug being hunted" }
        # Scan by the process that owns the main window, not by the process that was
        # launched: they are the same here, but pinning it to the window is what makes
        # the scan correct if the app ever relaunches itself.
        $ownerPid = 0
        [void][Win5]::GetWindowThreadProcessId($hwnd, [ref]$ownerPid)
        foreach ($h in [Win5]::Visible($ownerPid)) { if ($h -ne $hwnd) { $dlg = $h; break } }
        if ($dlg -eq [intptr]::Zero) { Start-Sleep -Milliseconds 500 }
    }
    if ($dlg -eq [intptr]::Zero) {
        Write-Host "launched pid $($proc.Id), main hwnd $hwnd, exited=$($proc.HasExited)"
        Write-Host "visible top-level windows: $([Win5]::Visible($proc.Id).Count)"
        throw 'the confirmation never appeared'
    }

    [void][Win5]::SetForegroundWindow($dlg)
    Start-Sleep -Milliseconds 900

    $r = New-Object Win5+RECT
    [void][Win5]::GetWindowRect($dlg, [ref]$r)
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
