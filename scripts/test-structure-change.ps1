# Checks the structure-change contract in the combined mode, in both directions.
#
# The bug this exists for: the "Разворотов" field used to apply a smaller number the
# moment it lost focus, deleting the last spreads (and the photos in them) BEFORE the
# button was pressed. The confirmation then found nothing left to lose and returned
# silently, so a filled spread was thrown away with no question asked - the very thing
# the confirmation exists to prevent.
#
# So three things have to hold, and none of them can be seen in a screenshot:
#   1. growing asks nothing and applies;
#   2. shrinking asks, and "Отмена" leaves the structure exactly as it was;
#   3. shrinking and confirming really removes the spreads.
#
# Usage: pwsh -File scripts\test-structure-change.ps1
#
# NOTE: this file contains Cyrillic literals and must stay UTF-8 **with BOM**.

param(
    [int]$Books = 3,
    [int]$Spreads = 4,
    [int]$TimeoutSeconds = 90
)

[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @"
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public class Win7 {
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
    private delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr h, uint cmd);
    // A WPF ToolTip is a real top-level window: 69x25 pixels, empty title, owned by the
    // main window. It appears on its own whenever the pointer rests on a slot, so a test
    // that treats "another window appeared" as "a question was asked" fails at random -
    // and it did, here. A dialog has a title; a tooltip does not.
    [DllImport("user32.dll")] private static extern int GetWindowTextLength(IntPtr h);
    [DllImport("user32.dll")] private static extern int GetWindowText(IntPtr h, System.Text.StringBuilder s, int n);
    public static bool HasTitle(IntPtr h) {
        int n = GetWindowTextLength(h);
        if (n <= 0) return false;
        var sb = new System.Text.StringBuilder(n + 1);
        GetWindowText(h, sb, sb.Capacity);
        return sb.Length > 0;
    }
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

$labelSpreads = 'Разворотов в книге'
$labelApply = 'структуру'
$labelOpen = '*Открыть проект*'
$labelCancel = 'Отмена'
$labelShrink = 'Уменьшить'

$indexPath = Join-Path $env:LOCALAPPDATA 'PhotoBookRenamer\Projects\projects.json'

function Get-FixtureEntry {
    $parsed = Get-Content $indexPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $sorted = @($parsed | ForEach-Object { $_ } | Sort-Object { [datetime]$_.lastModified } -Descending)
    $hit = @($sorted | Where-Object { $_.mode -eq 3 -and $_.name -like 'TR-test*' })[0]
    if (-not $hit) { throw 'no combined fixture in the index - run make-combined-fixture.ps1' }
    return @{ Entry = $hit; Card = [array]::IndexOf($sorted, $hit) }
}

function Get-FixturePath {
    $hit = (Get-FixtureEntry).Entry
    # Projects are one flat file per project: Projects\<id>.json
    return Join-Path (Join-Path $env:LOCALAPPDATA 'PhotoBookRenamer\Projects') "$($hit.id).json"
}

function Get-SavedSpreads {
    $json = Get-Content (Get-FixturePath) -Raw -Encoding UTF8 | ConvertFrom-Json
    $first = @($json.books)[0]
    return @(@($first.pages) | Where-Object { -not $_.isCover }).Count
}

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
    if ($proc.MainWindowHandle -eq 0) { throw 'no main window' }
    [void][Win7]::ShowWindow($proc.MainWindowHandle, 3)
    [void][Win7]::SetForegroundWindow($proc.MainWindowHandle)
    Start-Sleep -Seconds 3

    $hwnd = [Win7]::FindTopLevel([uint32]$proc.Id)
    $el = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)

    $open = $null
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ($null -eq $open -and (Get-Date) -lt $deadline) {
        $cards = @($el.FindAll([System.Windows.Automation.TreeScope]::Descendants,
            (New-Object System.Windows.Automation.PropertyCondition(
                [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                [System.Windows.Automation.ControlType]::Button))) |
            Where-Object { $_.Current.Name -like $labelOpen })
        # The card of THIS fixture, not the first card. The list is ordered by
        # LastModified, and every test run moves its own project to the top - so "the
        # first card" is whichever project some other test opened last, and the test
        # then reads one project's spread count and edits another's.
        $want = (Get-FixtureEntry).Card
        if ($cards.Count -gt $want) { $open = $cards[$want] }
        if ($null -eq $open) { Start-Sleep -Milliseconds 700 }
    }
    if ($null -eq $open) { throw 'the combined fixture card did not appear' }
    $open.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Seconds 8

    $hwnd = [Win7]::FindTopLevel([uint32]$proc.Id)
    $el = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)
    $field = Find-ByName $el $labelSpreads 'Edit'
    if ($null -eq $field) { throw "the '$labelSpreads' field was not found" }

    $apply = Find-ByName $el $labelApply 'Button'
    if ($null -eq $apply) { throw 'the structure button was not found' }

    function Set-Spreads([int]$value) {
        [void][Win7]::SetForegroundWindow($hwnd)
        $field.SetFocus()
        $field.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue("$value")
        Start-Sleep -Milliseconds 300
        [System.Windows.Forms.SendKeys]::SendWait('{TAB}')
        Start-Sleep -Milliseconds 500
        $apply.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    }

    function Wait-Dialog([int]$seconds = 12) {
        $deadline = (Get-Date).AddSeconds($seconds)
        while ((Get-Date) -lt $deadline) {
            $proc.Refresh()
            if ($proc.HasExited) { throw 'the app died while the structure question was open' }
            foreach ($h in [Win7]::Visible($proc.Id)) { if ($h -ne $hwnd -and [Win7]::HasTitle($h)) { return $h } }
            Start-Sleep -Milliseconds 400
        }
        return [intptr]::Zero
    }

    $start = Get-SavedSpreads
    Write-Host "fixture on disk: $start spreads"

    # --- 1. growing asks nothing -------------------------------------------------
    Set-Spreads ($start + 1)
    $dlg = Wait-Dialog 6
    if ($dlg -ne [intptr]::Zero) {
        [System.Windows.Forms.SendKeys]::SendWait('{ESC}')
        Start-Sleep -Seconds 1
        throw 'growing the run asked a question - it must not'
    }
    Start-Sleep -Seconds 2
    $grown = Get-SavedSpreads
    if ($grown -ne $start + 1) { throw "growing did not apply: on disk $grown, expected $($start + 1)" }
    Write-Host "PASS: growing $start -> $grown asks nothing and applies"

    # --- 2. shrinking asks, and cancel changes nothing ----------------------------
    Set-Spreads ($grown - 1)
    $dlg = Wait-Dialog 12
    if ($dlg -eq [intptr]::Zero) { throw 'shrinking asked NO question - photos would vanish silently' }

    $dlgEl = [System.Windows.Automation.AutomationElement]::FromHandle($dlg)
    $cancel = Find-ByName $dlgEl $labelCancel 'Button'
    if ($null -eq $cancel) { throw 'the question has no cancel button' }
    $cancel.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Seconds 3

    $afterCancel = Get-SavedSpreads
    if ($afterCancel -ne $grown) { throw "cancel did not stop it: on disk $afterCancel, expected $grown" }
    Write-Host "PASS: shrinking $grown -> $($grown - 1) asks, and cancel leaves $afterCancel"

    # --- 3. shrinking and confirming really removes ------------------------------
    Set-Spreads ($afterCancel - 1)
    $dlg = Wait-Dialog 12
    if ($dlg -eq [intptr]::Zero) { throw 'the second shrink asked nothing' }

    $dlgEl = [System.Windows.Automation.AutomationElement]::FromHandle($dlg)
    $yes = Find-ByName $dlgEl $labelShrink 'Button'
    if ($null -eq $yes) { throw 'the question has no confirm button' }
    $yes.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Seconds 4

    $afterYes = Get-SavedSpreads
    if ($afterYes -ne $afterCancel - 1) { throw "confirm did not apply: on disk $afterYes, expected $($afterCancel - 1)" }
    Write-Host "PASS: confirming takes it to $afterYes"

    Write-Host ''
    Write-Host 'PASS: the structure question asks before losing a spread, and both answers work'
    exit 0
}
finally {
    if ($proc) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue }
    Pop-Location
}
