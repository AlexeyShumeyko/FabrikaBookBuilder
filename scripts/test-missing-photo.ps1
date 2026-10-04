# Proves two things about a filled slot whose photo cannot be read:
#   - the cell SAYS so instead of looking like an empty slot;
#   - the reason lands in %TEMP%\PhotoBookRenamer\image-errors.log.
#
# The fixture points one spread at a file that does not exist (make-combined-fixture
# -MissingPhoto), so nothing of the owner's is touched. The red pixels of the warning are
# counted in the screenshot of the live app.
#
# Usage: pwsh -File scripts/test-missing-photo.ps1
#
# NOTE: this file contains Cyrillic literals and must stay UTF-8 **with BOM**.

param([int]$TimeoutSeconds = 60)

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
            found.Add(h); return true;
        }, IntPtr.Zero);
        if (found.Count == 0) return IntPtr.Zero;
        IntPtr fg = GetForegroundWindow();
        foreach (var h in found) if (h == fg) return h;
        return found[0];
    }
}
"@

function Get-AllByName($rootEl, [string]$label, [string]$typeName) {
    $ct = [System.Windows.Automation.ControlType]::$typeName
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty, $ct)
    return @($rootEl.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond) |
        Where-Object { $_.Current.Name -like "*$label*" })
}

if (Get-Process -Name PhotoBookRenamer -ErrorAction SilentlyContinue) { throw 'close the app first' }

$fixtureId = $null
$proc = $null
try {
    $out = & powershell -NoProfile -ExecutionPolicy Bypass -File scripts\make-combined-fixture.ps1 -Books 1 -Spreads 2 -MissingPhoto 2>&1
    $line = ($out | Out-String).Trim()
    if ($line -notmatch 'id=([0-9a-f\-]+)') { throw "fixture creation failed: $line" }
    $fixtureId = $Matches[1]
    Write-Host "fixture ${fixtureId}: 1 book, 2 spreads, spread 1 points at a file that is not there"

    $logPath = Join-Path $env:TEMP 'PhotoBookRenamer\image-errors.log'
    if (Test-Path $logPath) { Remove-Item $logPath -Force }

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
    [void][Win2]::ShowWindow($proc.MainWindowHandle, 3)
    Start-Sleep -Seconds 2

    $el = [System.Windows.Automation.AutomationElement]::FromHandle($proc.MainWindowHandle)
    $openLabel = '*' + [char]0x041E + [char]0x0442 + [char]0x043A + [char]0x0440 + [char]0x044B + [char]0x0442 + [char]0x044C + ' ' + [char]0x041F + [char]0x0440 + [char]0x043E + [char]0x0435 + [char]0x043A + [char]0x0442 + '*'
    $btn = $null
    $loadDeadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ($null -eq $btn -and (Get-Date) -lt $loadDeadline) {
        Start-Sleep -Milliseconds 700
        $btns = @(Get-AllByName $el $openLabel 'Button')
        if ($btns.Count -gt 0) { $btn = $btns[0] }   # the fixture is the newest card
    }
    if ($null -eq $btn) { throw 'no project cards appeared' }
    $btn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Seconds 8

    $hwnd = [Win2]::FindTopLevel([uint32]$proc.Id)
    $r = New-Object Win2+RECT
    [void][Win2]::GetWindowRect($hwnd, [ref]$r)
    $w = $r.Right - $r.Left; $h = $r.Bottom - $r.Top
    $bmp = New-Object System.Drawing.Bitmap $w, $h
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($r.Left, $r.Top, 0, 0, (New-Object System.Drawing.Size($w, $h)))
    $g.Dispose()
    $shot = Join-Path $root 'doc\shots\missing-photo.png'
    $bmp.Save($shot, [System.Drawing.Imaging.ImageFormat]::Png)

    # Red pixels inside the book area. The warning is red-500/red-600; the only other red in
    # that area is the empty-slot bin, which is only on slots that HAVE a photo - and the
    # fixture's empty slots have none, so red means the warning.
    $red = 0
    for ($y = 300; $y -lt [int]($h * 0.85); $y += 2) {
        for ($x = 400; $x -lt [int]($w * 0.95); $x += 2) {
            $c = $bmp.GetPixel($x, $y)
            if ($c.R -ge 190 -and $c.G -le 90 -and $c.B -le 90) { $red++ }
        }
    }
    $bmp.Dispose()
    Write-Host "red pixels in the book area: $red (screenshot: $shot)"

    $logged = (Test-Path $logPath) -and ((Get-Content $logPath -Raw) -match 'no-such-photo')
    Write-Host "image-errors.log mentions the missing file: $logged"

    if ($red -lt 20) { Write-Host 'FAIL: no warning is visible in the book area'; exit 1 }
    if (-not $logged) { Write-Host 'FAIL: nothing was written to image-errors.log'; exit 1 }
    Write-Host 'PASS: an unreadable photo is announced in the cell and in the log'
    exit 0
}
finally {
    if ($proc) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue }
    if ($fixtureId) {
        & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'make-combined-fixture.ps1') -RemoveId $fixtureId | Out-Null
    }
    Pop-Location
}
