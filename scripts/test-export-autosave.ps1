# Proves that pressing "Export all books" SAVES the project first.
#
# The owner's bug: build a run, press export without ever pressing save, and the
# project list then shows the project as "ready to print" while the project FILE is
# still the empty one - reopening it gives nothing back. The export updated the index
# and ended the session, and nothing ever wrote the project.
#
# Checking "the export produced files" cannot see this: the copy works on the in-memory
# project. So the test attacks the file instead:
#
#   1. a fully filled combined fixture, so the export button is enabled;
#   2. the app opens it and holds it in memory;
#   3. the project file ON DISK is emptied while the app is running - the in-memory copy
#      is untouched, so nothing but a save can put the books back;
#   4. "Export all books" is pressed and the export dialog is left open;
#   5. the file is read again. It must contain every book again.
#
# Step 4 stops at the dialog on purpose: the save happens before the copy, so the
# dialog does not have to be completed, and a half-run export cannot be mistaken for
# the save.
#
# NOTE: this file contains Cyrillic literals and must stay UTF-8 **with BOM**.
#
# Usage: pwsh -File scripts/test-export-autosave.ps1

param(
    [int]$Books = 3,
    [int]$Spreads = 3,
    [int]$TimeoutSeconds = 60
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
public class Win2 {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);

    private delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr h, uint cmd);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();

    public static IntPtr FindTopLevel(uint target) {
        var found = new List<IntPtr>();
        EnumWindows((h, l) => {
            uint pid; GetWindowThreadProcessId(h, out pid);
            if (pid != target) return true;
            if (GetWindow(h, 4) != IntPtr.Zero) return true;
            if (!IsWindowVisible(h)) return true;
            found.Add(h);
            return true;
        }, IntPtr.Zero);
        if (found.Count == 0) return IntPtr.Zero;
        IntPtr fg = GetForegroundWindow();
        foreach (var h in found) if (h == fg) return h;
        return found[0];
    }
}
"@

function Get-ByName($rootEl, [string]$label, [string]$typeName) {
    $ct = [System.Windows.Automation.ControlType]::$typeName
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty, $ct)
    foreach ($b in $rootEl.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)) {
        if ($b.Current.Name -like "*$label*") { return $b }
    }
    return $null
}

function Invoke-Element($el) {
    $el.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
}

$dir = Join-Path $env:LOCALAPPDATA 'PhotoBookRenamer\Projects'
$indexPath = Join-Path $dir 'projects.json'
$app = Get-Process -Name PhotoBookRenamer -ErrorAction SilentlyContinue
if ($app) { throw 'close the app first: this test rewrites the project index' }

$fixtureId = $null
$proc = $null
try {
    # ---- 1. a filled fixture -------------------------------------------------
    $out = & powershell -NoProfile -ExecutionPolicy Bypass -File scripts\make-combined-fixture.ps1 -Books $Books -Spreads $Spreads 2>&1
    $line = ($out | Out-String).Trim()
    if ($line -notmatch 'id=([0-9a-f\-]+)') { throw "fixture creation failed: $line" }
    $fixtureId = $Matches[1]
    Write-Host "fixture ${fixtureId}: $Books books x $Spreads spreads"

    $index = Get-Content $indexPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $projPath = (($index | Where-Object { $_.id -eq $fixtureId }).filePath)
    if (-not (Test-Path -LiteralPath $projPath)) { throw "fixture file missing: $projPath" }

    # ---- 2. open it ----------------------------------------------------------
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
    [void][Win2]::ShowWindow($proc.MainWindowHandle, 3)
    [void][Win2]::SetForegroundWindow($proc.MainWindowHandle)
    Start-Sleep -Seconds 2

    $hwnd = $proc.MainWindowHandle
    $el = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)
    $openLabel = '*' + [char]0x041E + [char]0x0442 + [char]0x043A + [char]0x0440 + [char]0x044B + [char]0x0442 + [char]0x044C + ' ' + [char]0x041F + [char]0x0440 + [char]0x043E + [char]0x0435 + [char]0x043A + [char]0x0442 + '*'
    $target = $null
    $loadDeadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ($null -eq $target -and (Get-Date) -lt $loadDeadline) {
        Start-Sleep -Milliseconds 700
        $buttons = @(Get-ByName $el $openLabel 'Button')
        # The fixture was written last, so it is the newest card in a list sorted by
        # LastModified - which is what the app shows.
        if ($buttons.Count -gt 0) { $target = $buttons[0] }
    }
    if ($null -eq $target) { throw 'no project cards appeared' }
    Invoke-Element $target
    Start-Sleep -Seconds 7

    $hwnd = [Win2]::FindTopLevel([uint32]$proc.Id)
    $el = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)
    $exportBtn = Get-ByName $el 'Экспортировать все книги' 'Button'
    if ($null -eq $exportBtn) { throw 'the export button is not on screen' }
    if ($exportBtn.Current.IsEnabled -ne $true) { throw 'the export button is disabled - the fixture is not fully filled' }

    # ---- 3. empty the project file behind the app's back ---------------------
    $before = Get-Content $projPath -Raw -Encoding UTF8 | ConvertFrom-Json
    Write-Host "on disk before: $(@($before.books).Count) books"
    [System.IO.File]::WriteAllText($projPath, '{"mode":3,"books":[],"availableFiles":[]}', (New-Object System.Text.UTF8Encoding($false)))
    $emptied = Get-Content $projPath -Raw -Encoding UTF8 | ConvertFrom-Json
    Write-Host "on disk now: $(@($emptied.books).Count) books (the app still holds $($Books))"

    # ---- 4. press export -----------------------------------------------------
    [void][Win2]::SetForegroundWindow($hwnd)
    Invoke-Element $exportBtn
    Start-Sleep -Seconds 6

    # ---- 5. read the file again ---------------------------------------------
    $after = Get-Content $projPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $bookCount = @($after.books).Count
    $spreadCount = if ($bookCount -gt 0) { @($after.books[0].pages).Count } else { 0 }
    Write-Host "on disk after export: $bookCount books, $spreadCount spreads in book 1"

    if ($bookCount -ne $Books) {
        Write-Host "FAIL: the export did not save the project - the file still holds $bookCount books instead of $Books."
        exit 1
    }
    if ($spreadCount -ne $Spreads) {
        Write-Host "FAIL: book 1 holds $spreadCount spreads instead of $Spreads."
        exit 1
    }

    Write-Host 'PASS: pressing export wrote the project to disk before copying anything.'
    exit 0
}
finally {
    if ($proc) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue }
    Pop-Location
}
