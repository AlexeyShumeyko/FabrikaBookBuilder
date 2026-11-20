# Verifies the app actually starts, opens maximised, and checks for updates.
#
# A desktop app with a broken resource dictionary or a binding typo compiles fine and then
# dies before the window ever appears. This script launches the built exe, waits for a main
# window, and reports the .NET Runtime event on failure so the real exception is visible
# instead of "process exited".
#
# The two extra rules are here because both have already been broken once, silently:
#
#   * the window opens maximised - it opened at a fixed 1520x800 once, which on a smaller
#     second monitor put the minimise, maximise and close buttons past the edge of the screen;
#   * the update check runs - it was handed a null container and threw inside its own catch
#     block, so two releases went out and no installed copy was ever offered an update. The
#     program answers "no update" by design when it cannot reach GitHub, so the check below
#     asks only that the check happened and said something, not what it said.
#
# Usage:  pwsh -File scripts/verify-startup.ps1 [-Configuration Release] [-TimeoutSeconds 25]
#         pwsh -File scripts/verify-startup.ps1 -SkipUpdateCheck   (no network needed)

param(
    [string]$Configuration = 'Release',
    [int]$TimeoutSeconds = 25,
    [int]$UpdateSeconds = 25,
    [switch]$SkipUpdateCheck
)

# The harness console runs on code page 866, which turns every Cyrillic string this
# script prints into "?" and floods the console with mojibake. Force UTF-8 on
# both channels; a child powershell.exe resets these on its own, so it has to be set
# inside each script rather than once in the caller.
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8

$ErrorActionPreference = 'Stop'

# One place that knows where the built program is.
. "$PSScriptRoot\common.ps1"
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root

Add-Type @"
using System;
using System.Runtime.InteropServices;
public class StartupWindow {
    [DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr h);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
}
"@

# Information lines are dropped from the journal unless this is set, and the update check
# reports itself at that level: a customer's journal stays short, the check below sees it.
$journalPath = Join-Path $env:LOCALAPPDATA 'PhotoBookRenamer\journal.log'
$journalBefore = if (Test-Path $journalPath) { (Get-Item $journalPath).Length } else { 0 }

$process = $null
$verboseWas = $env:FBR_JOURNAL_VERBOSE
$env:FBR_JOURNAL_VERBOSE = '1'

try {
    # Get-AppExe throws when there is no build, so the explicit Test-Path below is the
    # friendlier second half of the same check: it gets to print where it looked.
    $exe = try { Get-AppExe -Configuration $Configuration } catch { $null }
    if (-not $exe -or -not (Test-Path $exe)) {
        Write-Host 'FAIL: no build to start. Build it first: dotnet build PhotoBookRenamer.sln -c Release'
        exit 1
    }

    $start = Get-Date
    $process = Start-Process -FilePath $exe -PassThru
    Write-Host "started pid $($process.Id), waiting up to $TimeoutSeconds s for a window..."

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    $process.Refresh()
    while ((Get-Date) -lt $deadline) {
        if ($process.HasExited) { break }
        $process.Refresh()
        if ($process.MainWindowHandle -ne 0) { break }
        Start-Sleep -Milliseconds 500
    }

    $process.Refresh()

    if ($process.HasExited) {
        Write-Host "FAIL: process exited with code $($process.ExitCode) before showing a window."
        Write-Host "---- .NET Runtime / Application Error events ----"
        Get-WinEvent -FilterHashtable @{ LogName = 'Application'; StartTime = $start } -ErrorAction SilentlyContinue |
            Where-Object { $_.ProviderName -in 'Application Error', '.NET Runtime' } |
            Select-Object -First 4 |
            ForEach-Object { Write-Host $_.Message; Write-Host '----' }
        exit 1
    }

    if ($process.MainWindowHandle -eq 0) {
        Write-Host "FAIL: no main window appeared within $TimeoutSeconds s (possible hang or silent exception)."
        exit 1
    }

    Write-Host "OK: window '$($process.MainWindowTitle)' is up (pid $($process.Id))."

    # Maximised is asked of the window manager, not of the window's size: a maximised window
    # on a second monitor is exactly as large as that monitor, which is not something this
    # script can predict. IsZoomed answers the question the report was about.
    if (-not [StartupWindow]::IsZoomed($process.MainWindowHandle)) {
        $rect = New-Object StartupWindow+RECT
        [void][StartupWindow]::GetWindowRect($process.MainWindowHandle, [ref]$rect)
        $w = $rect.Right - $rect.Left
        $h = $rect.Bottom - $rect.Top
        Write-Host "FAIL: the window opened unmaximised (${w}x${h}). It must open maximised - see MainWindow.axaml."
        exit 1
    }

    Write-Host 'OK: the window opened maximised.'

    if ($SkipUpdateCheck) { exit 0 }

    $deadline = (Get-Date).AddSeconds($UpdateSeconds)
    $said = $null
    while ((Get-Date) -lt $deadline -and -not $said) {
        Start-Sleep -Milliseconds 500
        if (-not (Test-Path $journalPath)) { continue }

        $length = (Get-Item $journalPath).Length
        
        # A journal that got shorter has been rotated (two megabytes is the threshold), and
        # the byte offset means nothing in the new file: read it from the start instead.
        $from = if ($length -lt $journalBefore) { 0 } else { $journalBefore }
        if ($length -le $from) { continue }

        $stream = [System.IO.File]::Open($journalPath, 'Open', 'Read', 'ReadWrite')
        try {
            [void]$stream.Seek($from, 'Begin')
            $reader = New-Object System.IO.StreamReader($stream)
            $tail = $reader.ReadToEnd()
        }
        finally {
            $stream.Dispose()
        }

        $line = ($tail -split "`r?`n" | Where-Object { $_.Contains('  update  ') } | Select-Object -First 1)
        if ($line) { $said = $line.Trim() }
    }

    if (-not $said) {
        Write-Host "FAIL: nothing was written about an update check within $UpdateSeconds s."
        Write-Host 'The check runs two seconds after the window and writes one journal line either way.'
        Write-Host 'No line means it did not run: see App.CheckForUpdatesLaterAsync and how it is started.'
        Write-Host "journal: $journalPath"
        exit 1
    }

    Write-Host "OK: update check ran - $said"
    exit 0
}
finally {
    if ($process) { Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue }
    $env:FBR_JOURNAL_VERBOSE = $verboseWas
    Pop-Location
}
