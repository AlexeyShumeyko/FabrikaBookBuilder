#  Measures what opening a project costs in the Avalonia shell, in the same terms as
#  scripts\perf-open.ps1 measures the other one, so the two can be read side by side.
#
#    pwsh -File scripts\perf-open-avalonia.ps1 -Label "после правки"
#    pwsh -File scripts\perf-open-avalonia.ps1 -Card 5
#
#  What it does, and why each part is here:
#
#    time to window     the launch cost - the window appearing, nothing else
#    first response     how long the drawing thread was unavailable after the screen appeared
#    worst stall        the longest single moment the window did not answer
#    cpu / memory       what the work underneath actually cost
#
#  The honest measure is not "how long does opening take". The click returns at once either
#  way, because the work happens afterwards; the number a person feels is the stall. So the
#  stall is measured first and everything else is context.
#
#  It reads the project index rather than taking a project path, for the same reason the
#  other script does: a path typed by hand measures a different project next time.
[CmdletBinding()]
param(
    # Index position of the project to open. Defaults to the first one.
    [int]$Card = -1,
    [string]$Mode = 'combined',
    [string]$Label = ''
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8

. "$PSScriptRoot\common.ps1"

$root = Get-RepoRoot
$exe = Join-Path $root 'src\PhotoBook.Desktop.Avalonia\bin\Release\net8.0\PhotoBook.Desktop.Avalonia.exe'

if (-not (Test-Path $exe)) {
    throw "the shell is not built: $exe"
}

# --- read the index so the choice is printed and repeatable --------------------------
$indexPath = Join-Path $env:LOCALAPPDATA 'PhotoBookRenamer\Projects\projects.json'
if (-not (Test-Path $indexPath)) {
    throw "no project index at $indexPath"
}

# The index stores the mode as its number, because that is what it is on disk. Accepting a
# name as well keeps the switch readable; mapping it here means the script does not have to
# be rewritten if the enum is ever renumbered - it reads the number from the same place the
# product does.
$modeNames = @{ start = 0; projects = 1; unique = 2; combined = 3 }
$modeValue = if ($modeNames.ContainsKey($Mode.ToLowerInvariant())) { $modeNames[$Mode.ToLowerInvariant()] } else { [int]$Mode }

$index = Get-Content $indexPath -Raw -Encoding UTF8 | ConvertFrom-Json
$entries = @($index | Where-Object { $_.mode -eq $modeValue })
if ($entries.Count -eq 0) { throw "no project of mode $Mode ($modeValue) in the index" }

if ($Card -lt 0) { $Card = 0 }
if ($Card -ge $entries.Count) { throw "no project at position $Card (there are $($entries.Count))" }

$picked = $entries[$Card]
Write-Host ("picked card {0} ('{1}', {2} books x {3} spreads)" -f `
    $Card, $picked.name, $picked.bookCount, $picked.pageCount)

# --- the measurement -----------------------------------------------------------------
Get-Process -ErrorAction SilentlyContinue |
    Where-Object { $_.ProcessName -match 'PhotoBook' } |
    Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 1

$before = Get-Process -Id $PID
$proc = Start-Process -FilePath $exe -PassThru -ArgumentList '--open', $Mode

$sw = [System.Diagnostics.Stopwatch]::StartNew()
$windowAt = $null
while ($sw.ElapsedMilliseconds -lt 60000) {
    Start-Sleep -Milliseconds 100
    $proc.Refresh()
    if ($proc.MainWindowHandle -ne 0) { $windowAt = $sw.ElapsedMilliseconds; break }
    if ($proc.HasExited) { throw "the shell exited before a window appeared (code $($proc.ExitCode))" }
}

if ($null -eq $windowAt) { Stop-Process -Id $proc.Id -Force; throw 'no window appeared' }

# --- how long the drawing thread is unavailable, and how bad the worst moment is -------
# The baseline is taken now, before anything has been drawn. Reporting only the total would
# flatter or damn the shell depending on what it weighs before it starts: a figure that
# includes the runtime, the font and the browser-sized heap of a graphics library is mostly
# a measure of the framework, and the other script reports the growth, so this one does too.
$baselineSet = [math]::Round($proc.WorkingSet64 / 1MB)
$baselinePrivate = [math]::Round($proc.PrivateMemorySize64 / 1MB)

$firstResponse = $null
$worstStall = 0
$stallStart = $null
$peakWorking = 0
$peakPrivate = 0

$sample = [System.Diagnostics.Stopwatch]::StartNew()
while ($sample.ElapsedMilliseconds -lt 8000) {
    Start-Sleep -Milliseconds 100
    $proc.Refresh()
    if ($proc.HasExited) { break }

    $nowWorking = [math]::Round($proc.WorkingSet64 / 1MB)
    if ($nowWorking -gt $peakWorking) { $peakWorking = $nowWorking }
    $nowPrivate = [math]::Round($proc.PrivateMemorySize64 / 1MB)
    if ($nowPrivate -gt $peakPrivate) { $peakPrivate = $nowPrivate }

    if ($proc.Responding) {
        if ($null -ne $stallStart) {
            $length = $sample.ElapsedMilliseconds - $stallStart
            if ($length -gt $worstStall) { $worstStall = $length }
            $stallStart = $null
        }
        if ($null -eq $firstResponse) { $firstResponse = $sample.ElapsedMilliseconds }
    }
    elseif ($null -eq $stallStart) {
        $stallStart = $sample.ElapsedMilliseconds
    }
}

# The process's own clock starts when it does, so the difference is its CPU. Subtracting
# the shell's start time as well would be wrong: that is a wall-clock instant, not a
# duration, and the shell refuses to treat it as one.
$cpu = [math]::Round($proc.TotalProcessorTime.TotalMilliseconds)

Write-Host ''
if ($Label) { Write-Host "=== $Label ===" }
Write-Host ("window appeared        : {0} ms after the launch" -f $windowAt)
Write-Host ("first UI response      : {0} ms after the window" -f $firstResponse)
Write-Host ("worst UI stall         : {0} ms" -f $worstStall)
Write-Host ("cpu used               : {0:N0} ms" -f $cpu)
Write-Host ("working set            : {0:N0} -> {1:N0} MB (peak {2:N0})" -f $baselineSet, $peakWorking, $peakWorking)
Write-Host ("private bytes          : {0:N0} -> {1:N0} MB (peak {2:N0})" -f $baselinePrivate, $peakPrivate, $peakPrivate)

Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
Write-Host ''
Write-Host 'Read the stall first. Everything else is what it cost.'