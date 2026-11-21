# Verifies that the stack of photographs on the welcome page answers the pointer.
#
# The prototype lifts the front sheet ten pixels when the panel is under the pointer, and
# nothing about that is visible in a still capture, in the markup or in a build: it only
# exists while a real pointer is over a real window. So the check is the pointer - park it
# outside the panel, capture, move it onto the panel, capture, and compare the pixels of
# the stack; then take it away again and require the sheet to come back.
#
# Usage:  pwsh -File scripts\verify-welcome-hover.ps1 [-TimeoutSeconds 40]
#
# NOTE: this file contains Cyrillic literals and must stay UTF-8 **with BOM**.

param(
    [int]$TimeoutSeconds = 40
)

[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8

$ErrorActionPreference = 'Stop'

. "$PSScriptRoot\common.ps1"
Push-Location (Split-Path -Parent $PSScriptRoot)

Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class HoverInput {
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
}
"@

function Save-Shot {
    param([System.Drawing.Rectangle]$Rect, [string]$Path)
    $bmp = New-Object System.Drawing.Bitmap $Rect.Width, $Rect.Height
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($Rect.Left, $Rect.Top, 0, 0, (New-Object System.Drawing.Size($Rect.Width, $Rect.Height)))
    $g.Dispose()
    $bmp.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}

# How many pixels of the stack differ between two shots, ignoring the one-step noise of a
# redrawn edge: a pixel counts only when a channel moves by more than eight.
function Compare-Shot {
    param([string]$A, [string]$B)
    $one = New-Object System.Drawing.Bitmap $A
    $two = New-Object System.Drawing.Bitmap $B
    $changed = 0
    for ($y = 0; $y -lt $one.Height; $y++) {
        for ($x = 0; $x -lt $one.Width; $x++) {
            $p = $one.GetPixel($x, $y)
            $q = $two.GetPixel($x, $y)
            if ([Math]::Abs($p.R - $q.R) -gt 8 -or [Math]::Abs($p.G - $q.G) -gt 8 -or [Math]::Abs($p.B - $q.B) -gt 8) {
                $changed++
            }
        }
    }
    $one.Dispose(); $two.Dispose()
    return $changed
}

$exe = Get-AppExe
if (-not (Test-Path $exe)) { throw "no build at $exe" }

$proc = Start-Process -FilePath $exe -PassThru
$temp = Join-Path ([System.IO.Path]::GetTempPath()) 'photobook-hover'
try {
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 400
        $proc.Refresh()
        if ($proc.HasExited) { throw "the program exited with $($proc.ExitCode)" }
        if ($proc.MainWindowHandle -ne 0) { break }
    }
    if ($proc.MainWindowHandle -eq 0) { throw 'the main window never appeared' }

    [void][HoverInput]::ShowWindow($proc.MainWindowHandle, 3)
    [void][HoverInput]::SetForegroundWindow($proc.MainWindowHandle)
    Start-Sleep -Seconds 3

    $r = New-Object HoverInput+RECT
    [void][HoverInput]::GetWindowRect($proc.MainWindowHandle, [ref]$r)
    $w = $r.Right - $r.Left
    $h = $r.Bottom - $r.Top
    if ($w -le 0 -or $h -le 0) { throw 'the window has no rectangle' }

    # The stack of pages, and only it: the text beside it never moves, so a difference here
    # is the sheet and nothing else.
    $stack = New-Object System.Drawing.Rectangle(
        ($r.Left + [int]($w * 0.60)), ($r.Top + [int]($h * 0.34)),
        [int]($w * 0.30), [int]($h * 0.30))

    New-Item -ItemType Directory -Force -Path $temp | Out-Null
    $rest = Join-Path $temp 'rest.png'
    $over = Join-Path $temp 'over.png'
    $back = Join-Path $temp 'back.png'

    # Away from the panel: the header is the part of the window the panel does not own.
    [void][HoverInput]::SetCursorPos(($r.Right - 60), ($r.Top + 50))
    Start-Sleep -Milliseconds 900
    Save-Shot $stack $rest

    # Onto the panel, over the words - the prototype lifts the sheet from anywhere on it.
    [void][HoverInput]::SetCursorPos(($r.Left + [int]($w * 0.30)), ($r.Top + [int]($h * 0.45)))
    Start-Sleep -Milliseconds 1200
    Save-Shot $stack $over

    $lifted = Compare-Shot $rest $over
    if ($lifted -lt 200) {
        throw "the front sheet did not move under the pointer: $lifted pixels changed"
    }

    # And back out: a sheet that stays lifted is a sheet that has been dropped.
    [void][HoverInput]::SetCursorPos(($r.Right - 60), ($r.Top + 50))
    Start-Sleep -Milliseconds 1200
    Save-Shot $stack $back

    $settled = Compare-Shot $rest $back
    if ($settled -gt 60) {
        throw "the front sheet did not come back: $settled pixels still differ"
    }

    Write-Host ("PASS: the pointer lifts the front photograph and puts it back ($lifted px moved, $settled px left).")
}
finally {
    if ($proc -and -not $proc.HasExited) { $proc.Kill() }
    Pop-Location
}
