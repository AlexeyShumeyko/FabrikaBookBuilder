# Verifies the app actually starts.
#
# A WPF app with a broken ResourceDictionary or a binding typo compiles fine and then
# dies with an unhandled XamlParseException before the window ever appears. This script
# launches the built exe, waits for a main window, and reports the .NET Runtime event
# on failure so the real exception is visible instead of "process exited".
#
# Usage:  pwsh -File scripts/verify-startup.ps1 [-Configuration Release] [-TimeoutSeconds 25]

param(
    [string]$Configuration = 'Release',
    [int]$TimeoutSeconds = 25
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

try {
    # Get-AppExe throws when there is no build, so the explicit Test-Path below is the
    # friendlier second half of the same check: it gets to print where it looked.
    $exe = try { Get-AppExe -Configuration $Configuration } catch { $null }
    if (-not $exe -or -not (Test-Path $exe)) {
        $exe = Join-Path $root "src\PhotoBook.Desktop.Wpf\bin\$Configuration\net8.0-windows\PhotoBookRenamer.exe"
        Write-Host "FAIL: exe not found at $exe"
        exit 1
    }

    $start = Get-Date
    $proc = Start-Process -FilePath $exe -PassThru
    Write-Host "started pid $($proc.Id), waiting up to $TimeoutSeconds s for a window..."

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    $proc.Refresh()
    while ((Get-Date) -lt $deadline) {
        if ($proc.HasExited) { break }
        $proc.Refresh()
        if ($proc.MainWindowHandle -ne 0) { break }
        Start-Sleep -Milliseconds 500
    }

    $proc.Refresh()

    if ($proc.HasExited) {
        Write-Host "FAIL: process exited with code $($proc.ExitCode) before showing a window."
        Write-Host "---- .NET Runtime / Application Error events ----"
        Get-WinEvent -FilterHashtable @{ LogName = 'Application'; StartTime = $start } -ErrorAction SilentlyContinue |
            Where-Object { $_.ProviderName -in 'Application Error', '.NET Runtime' } |
            Select-Object -First 4 |
            ForEach-Object { Write-Host $_.Message; Write-Host '----' }
        exit 1
    }

    if ($proc.MainWindowHandle -eq 0) {
        Write-Host "FAIL: no main window appeared within $TimeoutSeconds s (possible hang or silent exception)."
        exit 1
    }

    Write-Host "OK: window '$($proc.MainWindowTitle)' is up (pid $($proc.Id))."
    Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
    exit 0
}
finally {
    Pop-Location
}
