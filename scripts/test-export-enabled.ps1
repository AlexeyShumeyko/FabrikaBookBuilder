# Checks that the export button comes alive when the run becomes complete.
#
# The defect this exists for: the editor's export button carries TWO bindings to the same
# question -
#   Command="{Binding ExportCommand}"   - the truth. Its CanExecute is
#                                        () => Project?.IsValid, and UpdateExportCommands()
#                                        re-asks it whenever IsValid changes.
#   IsEnabled="{Binding CanExport}"     - a second copy of the same answer, and it is raised
#                                        only when the Project property is REASSIGNED.
#
# WPF coerces IsEnabled: if the command says "no" the button goes dark even when the binding
# says "yes", and if the binding says "no" the button stays dark even when the command says
# "yes". So the stale copy can only keep the button TOO DARK - never too bright.
#
# Filling a slot is exactly that case. DropFileOnSlot() writes page.SourcePath in place and
# calls UpdateExportCommands(); it never reassigns Project and never raises CanExport. A
# photographer who fills the last empty slot is left with a run that is ready and a button
# that has not changed since the run was not ready. The button cannot be pressed and gives
# no reason.
#
# Changing the structure does NOT show this, because that path reassigns Project
# (CombinedModeViewModel.Structure, "Project = new Project {") and so refreshes the copy.
#
# Usage: pwsh -File scripts\test-export-enabled.ps1
# Needs a combined fixture; this script makes its own, with one slot emptied in the file.
#
# NOTE: this file contains Cyrillic literals and must stay UTF-8 **with BOM**.

param([int]$TimeoutSeconds = 90)

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
public class WinA {
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint x, uint y, uint d, IntPtr e);
    private delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr h, uint cmd);
    [DllImport("user32.dll")] private static extern int GetWindowTextLength(IntPtr h);
    [DllImport("user32.dll")] private static extern int GetWindowText(IntPtr h, System.Text.StringBuilder s, int n);
    public static string Title(IntPtr h) {
        int n = GetWindowTextLength(h);
        if (n <= 0) return "";
        var sb = new System.Text.StringBuilder(n + 1);
        GetWindowText(h, sb, sb.Capacity);
        return sb.ToString();
    }
    public static void Click(int x, int y) {
        SetCursorPos(x, y);
        System.Threading.Thread.Sleep(120);
        mouse_event(0x0002, 0, 0, 0, IntPtr.Zero);
        mouse_event(0x0004, 0, 0, 0, IntPtr.Zero);
    }
    public static IntPtr FindTopLevel(uint pid) {
        IntPtr best = IntPtr.Zero;
        var all = new List<IntPtr>();
        EnumWindows((h, l) => { uint p; GetWindowThreadProcessId(h, out p);
            if (p == (uint)pid && IsWindowVisible(h)) { all.Add(h);
                if (GetWindow(h, 4) == IntPtr.Zero) best = h; }
            return true; }, IntPtr.Zero);
        return best != IntPtr.Zero ? best : (all.Count > 0 ? all[0] : IntPtr.Zero);
    }
    public static IntPtr[] Visible() {
        var f = new List<IntPtr>();
        EnumWindows((h, l) => { if (IsWindowVisible(h)) f.Add(h); return true; }, IntPtr.Zero);
        return f.ToArray();
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

$labelExport  = 'Экспортировать'
$labelSave    = 'Сохранить проект'
$labelOpen    = '*Открыть проект*'
$labelEmpty   = 'Пустой слот'
$pickerTitle  = 'Выберите фото для слота'

$indexPath = Join-Path $env:LOCALAPPDATA 'PhotoBookRenamer\Projects\projects.json'

# The fixture this run works on, made here rather than borrowed. An earlier scenario may have
# left the shared one incomplete, and blanking a slot in an incomplete run proves nothing:
# filling one slot would not complete it. Owning the fixture also means nothing outside this
# run is touched.
$fixtureId = ''
$makeLine = & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'make-combined-fixture.ps1') | Select-Object -Last 1
if ($makeLine -notmatch 'id=([0-9a-f\-]+)') { throw "fixture creation failed: $makeLine" }
$fixtureId = $Matches[1]
Write-Host "fixture ${fixtureId}: a complete run of its own"

function Get-Fixture {
    $parsed = Get-Content $indexPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $sorted = @($parsed | ForEach-Object { $_ } | Sort-Object { [datetime]$_.lastModified } -Descending)
    $hit = @($sorted | Where-Object { $_.id -eq $fixtureId })[0]
    if (-not $hit) { throw "the fixture $fixtureId is not in the index" }
    return @{ Entry = $hit; Card = [array]::IndexOf($sorted, $hit) }
}

function Get-Slots {
    $id = (Get-Fixture).Entry.id
    $path = Join-Path (Join-Path $env:LOCALAPPDATA 'PhotoBookRenamer\Projects') "$id.json"
    if (-not (Test-Path $path)) { return $null }
    $p = [System.IO.File]::ReadAllText($path) | ConvertFrom-Json
    $out = @()
    $b = 0
    foreach ($bk in $p.books) {
        $b++
        $out += "book $b cover=" + $(if ($null -eq $bk.cover -or [string]::IsNullOrEmpty($bk.cover.sourcePath)) { 'EMPTY' } else { 'ok' })
        $i = 0
        foreach ($pg in $bk.pages) {
            $i++
            if ($pg.isCover) { continue }
            $out += "book $b spread $i=" + $(if ([string]::IsNullOrEmpty($pg.sourcePath)) { 'EMPTY' } else { 'ok' })
        }
    }
    return , $out
}

function Test-Complete {
    $slots = Get-Slots
    if ($null -eq $slots) { return $null }
    return (@($slots | Where-Object { $_ -match 'EMPTY' }).Count -eq 0)
}

# Empty exactly one spread of the fixture, so that filling it back completes the run.
# This is test data the script made itself.
function Blank-OneSlot {
    $id = (Get-Fixture).Entry.id
    $path = Join-Path (Join-Path $env:LOCALAPPDATA 'PhotoBookRenamer\Projects') "$id.json"
    $p = [System.IO.File]::ReadAllText($path) | ConvertFrom-Json

    $donor = $null
    foreach ($bk in $p.books) {
        foreach ($pg in $bk.pages) {
            if (-not $pg.isCover -and $pg.sourcePath) { $donor = $pg.sourcePath; break }
        }
        if ($donor) { break }
    }
    if (-not $donor) { throw 'the fixture holds no photograph to copy' }

    $blanked = $false
    $b = 0
    foreach ($bk in $p.books) {
        $b++
        foreach ($pg in $bk.pages) {
            if ($pg.isCover) { continue }
            if (-not $blanked -and $pg.sourcePath) {
                $pg.sourcePath = $null
                # The file name is omitted from the JSON when there is none, so the property
                # may not be there to clear.
                if ($pg.PSObject.Properties.Name -contains 'fileName') { $pg.fileName = $null }
                $blanked = $true
                Write-Host ("blanked book {0} spread {1} in the fixture file" -f $b, $pg.index)
            }
        }
    }
    [System.IO.File]::WriteAllText($path, ($p | ConvertTo-Json -Depth 12), (New-Object System.Text.UTF8Encoding($false)))
    return $donor
}

$exe = Join-Path $root 'src\PhotoBook.Desktop.Wpf\bin\Release\net8.0-windows\PhotoBookRenamer.exe'
if (-not (Test-Path $exe)) { throw "no build at $exe" }

$donor = Blank-OneSlot
Write-Host ("a photograph to put in the slot: {0}" -f $donor)

$proc = Start-Process -FilePath $exe -PassThru
try {
    $up = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $up) {
        Start-Sleep -Milliseconds 500
        $proc.Refresh()
        if ($proc.HasExited) { throw "app exited $($proc.ExitCode)" }
        if ($proc.MainWindowHandle -ne 0) { break }
    }
    if ($proc.MainWindowHandle -eq 0) { throw 'no main window' }
    [void][WinA]::ShowWindow($proc.MainWindowHandle, 3)
    [void][WinA]::SetForegroundWindow($proc.MainWindowHandle)
    Start-Sleep -Seconds 3

    $hwnd = [WinA]::FindTopLevel([uint32]$proc.Id)
    $el = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)

    $open = $null
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ($null -eq $open -and (Get-Date) -lt $deadline) {
        $cards = @($el.FindAll([System.Windows.Automation.TreeScope]::Descendants,
            (New-Object System.Windows.Automation.PropertyCondition(
                [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                [System.Windows.Automation.ControlType]::Button))) |
            Where-Object { $_.Current.Name -like $labelOpen })
        $want = (Get-Fixture).Card
        if ($cards.Count -gt $want) { $open = $cards[$want] }
        if ($null -eq $open) { Start-Sleep -Milliseconds 700 }
    }
    if ($null -eq $open) { throw 'the combined fixture card did not appear' }
    $open.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Seconds 8

    $hwnd = [WinA]::FindTopLevel([uint32]$proc.Id)
    function Get-Root { [System.Windows.Automation.AutomationElement]::FromHandle([WinA]::FindTopLevel([uint32]$proc.Id)) }

    function Get-ExportEnabled {
        $b = Find-ByName (Get-Root) $labelExport 'Button'
        if ($null -eq $b) { throw "the '$labelExport' button was not found" }
        return $b.Current.IsEnabled
    }

    $before = Get-ExportEnabled
    Write-Host ("run holds every photograph (before) : {0}" -f (Test-Complete))
    Write-Host ("export button enabled (before)      : {0}" -f $before)

    if ($before) { Write-Host ''; Write-Host 'FAIL: the button is already enabled on an incomplete run'; exit 1 }

    # Click the empty slot. Its footer reads "Пустой слот", and the click handler is on the
    # card's MouseLeftButtonDown, which the footer click bubbles into.
    $empty = Find-ByName (Get-Root) $labelEmpty 'Text'
    if ($null -eq $empty) { throw "no '$labelEmpty' label - the run has no empty slot to fill" }
    $r = $empty.Current.BoundingRectangle
    Write-Host ("empty slot label at {0},{1} - clicking it" -f [int]$r.X, [int]$r.Y)
    [void][WinA]::SetForegroundWindow([WinA]::FindTopLevel([uint32]$proc.Id))
    [WinA]::Click([int]($r.X + $r.Width / 2), [int]($r.Y + $r.Height / 2))

    # The picker is the OS dialog, not one of ours: wait for it by its title.
    $dlg = [intptr]::Zero
    $dl = (Get-Date).AddSeconds(20)
    while ($dlg -eq [intptr]::Zero -and (Get-Date) -lt $dl) {
        Start-Sleep -Milliseconds 400
        foreach ($h in [WinA]::Visible()) {
            if ([WinA]::Title($h) -eq $pickerTitle) { $dlg = $h; break }
        }
    }
    if ($dlg -eq [intptr]::Zero) { throw 'the file picker did not open' }
    Write-Host 'picker is open, naming the photograph'

    # The picker puts the caret in the file-name box by itself; typing the full path and
    # pressing Enter is the one way that works in any language.
    [System.Windows.Forms.SendKeys]::SendWait($donor)
    Start-Sleep -Milliseconds 400
    [System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
    Start-Sleep -Seconds 5

    $after = Get-ExportEnabled
    Write-Host ("export button enabled (after fill)  : {0}" -f $after)

    # Assigning a photograph is deliberately not auto-saved, so ask for a save before
    # reading the file back - otherwise the fixture on disk still shows the empty slot.
    $save = Find-ByName (Get-Root) $labelSave 'Button'
    if ($null -ne $save -and $save.Current.IsEnabled) {
        $save.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
        Start-Sleep -Seconds 4
    }
    $completeAfter = Test-Complete
    Write-Host ("run holds every photograph (after)  : {0}" -f $completeAfter)
    Write-Host ''
    Write-Host 'what is on disk now:'
    Get-Slots | ForEach-Object { Write-Host "  $_" }

    if ($null -eq $completeAfter) { throw 'the project file was not written; the run is unknown' }

    $problems = @()
    if (-not $completeAfter) { $problems += 'the slot was not filled - the run is still incomplete, so nothing was proven' }
    if (-not $after) {
        $problems += 'the run is complete and the button is STILL DISABLED - the photographer cannot export'
    }

    Write-Host ''
    if ($problems.Count -gt 0) {
        Write-Host 'FAIL:'
        $problems | ForEach-Object { Write-Host "  - $_" }
        exit 1
    }

    Write-Host 'PASS: filling the last empty slot turned the button on without the Project being'
    Write-Host 'reassigned.'
}
finally {
    if ($proc -and -not $proc.HasExited) { $proc.Kill() }
    if ($fixtureId) {
        & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'make-combined-fixture.ps1') -RemoveId $fixtureId | Out-Null
    }
}