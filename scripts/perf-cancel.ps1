# Measures what it costs to change your mind.
#
# Opening a project starts two preparations in the background: reduced copies for the slots
# and decoded bitmaps so the first render does not read an 18 MB scan. Together they are the
# longest thing the program does - tens of seconds for a full run - and nobody is waiting
# for them, because the window is already usable.
#
# So they are cancellable, and this is the script that checks that the cancellation is real
# rather than declared: it opens a project, immediately goes back to the list the way a
# photographer who opened the wrong folder would, and then watches the CPU. Without
# cancellation the process keeps a core busy decoding photographs for a screen that is no
# longer on show; with it, the work stops within a photograph or two.
#
# The number that matters is "CPU in the 10 s after leaving". Compare it with the same
# measurement on a build without cancellation - which is what this script is for, because
# "the token is passed" proves nothing about what the machine does.
#
# Usage:
#   pwsh -File scripts\perf-cancel.ps1 -Books 29 -Spreads 10
#   pwsh -File scripts\perf-cancel.ps1 -Books 29 -Spreads 10 -Label "with cancellation"
#
# NOTE: this file contains Cyrillic literals and must stay UTF-8 **with BOM**.

param(
    [int]$Books = 0,
    [int]$Spreads = 0,
    [int]$Card = -1,
    [string]$Label = '',
    [int]$WatchSeconds = 10,
    [int]$StaySeconds = 0
)

[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

# "Открыть проект" and "Мои проекты", spelled with char codes because this console is code
# page 866 and a literal arrives as question marks.
$openLabel = '*' + [char]0x041E + [char]0x0442 + [char]0x043A + [char]0x0440 + [char]0x044B + [char]0x0442 + [char]0x044C + ' ' + [char]0x041F + [char]0x0440 + [char]0x043E + [char]0x0435 + [char]0x043A + [char]0x0442 + '*'
$backLabel = [char]0x041C + [char]0x043E + [char]0x0438 + ' ' + [char]0x043F + [char]0x0440 + [char]0x043E + [char]0x0435 + [char]0x043A + [char]0x0442 + [char]0x044B

$proc = $null
try {
    if (Get-Process -Name PhotoBookRenamer -ErrorAction SilentlyContinue) { throw 'close the app first' }

    $exe = Join-Path $root 'src\PhotoBook.Desktop.Wpf\bin\Release\net8.0-windows\PhotoBookRenamer.exe'
    if (-not (Test-Path $exe)) { throw "no build at $exe" }

    $proc = Start-Process -FilePath $exe -PassThru
    $deadline = (Get-Date).AddSeconds(40)
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 400
        $proc.Refresh()
        if ($proc.HasExited) { throw "app exited $($proc.ExitCode)" }
        if ($proc.MainWindowHandle -ne 0) { break }
    }
    if ($proc.MainWindowHandle -eq 0) { throw 'no window' }
    Start-Sleep -Seconds 4

    $hwnd = $proc.MainWindowHandle
    $el = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)

    # The header tabs are RadioButtons, not Buttons, so they do not answer InvokePattern. A
# helper that tries what each control type does support is shorter than a switch per control.
function Activate-Element($element) {
    foreach ($pattern in @(
        [System.Windows.Automation.InvokePattern]::Pattern,
        [System.Windows.Automation.SelectionItemPattern]::Pattern,
        [System.Windows.Automation.TogglePattern]::Pattern)) {
        $got = $null
        if ($element.TryGetCurrentPattern($pattern, [ref]$got)) {
            if ($pattern -eq [System.Windows.Automation.TogglePattern]::Pattern) { $got.Toggle() }
            else { $got.Select() }
            return $true
        }
    }
    return $false
}

# Pick the card by the project's shape, not by its position. Every run bumps
    # LastModified on the project it opens, which moves that card to the top - and the list
    # on screen is in that order, so the index has to be read the same way or the number
    # refers to a different project than the one measured.
    if ($Books -gt 0) {
        $indexPath = Join-Path $env:LOCALAPPDATA 'PhotoBookRenamer\Projects\projects.json'
        $entries = @((Get-Content $indexPath -Raw -Encoding UTF8 | ConvertFrom-Json) |
            Sort-Object { [datetime]$_.lastModified } -Descending)
        $at = -1
        for ($i = 0; $i -lt $entries.Count; $i++) {
            if ($Books -gt 0 -and $entries[$i].bookCount -ne $Books) { continue }
            if ($Spreads -gt 0 -and $entries[$i].pageCount -ne $Spreads) { continue }
            $at = $i; break
        }
        if ($at -lt 0) { throw "no project with $Books books and $Spreads spreads in the index" }
        $Card = $at
        Write-Host "picked card $Card ('$($entries[$at].name)', $($entries[$at].bookCount) books x $($entries[$at].pageCount) spreads)"
    }
    if ($Card -lt 0) { throw 'give -Books/-Spreads, or -Card' }

    $btnCond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::Button)
    $cards = @($el.FindAll([System.Windows.Automation.TreeScope]::Descendants, $btnCond) |
        Where-Object { $_.Current.Name -like $openLabel })
    if ($cards.Count -le $Card) { throw "no open button at card $Card (found $($cards.Count))" }
    $open = $cards[$Card]

    $started = Get-Date
    Write-Host "opening card $Card, then leaving as soon as the editor is up"

    # Open, then leave as soon as the editor is on screen. The gap is one dispatcher pass:
    # long enough for the editor to exist, short enough that the preparation has barely
    # started - which is the case worth being able to stop.
    # Open, then wait for the editor itself. Waiting a fixed 700 ms is not enough and not
# honest: the project list has the same "Мои проекты" tab, so a script that only waits for
# the tab will leave the list without ever entering the editor, and then measure a
# preparation that never started. The export button only exists once a project is open.
$editorLabel = [char]0x042D + [char]0x043A + [char]0x0441 + [char]0x043F + [char]0x043E + [char]0x0440 + [char]0x0442 + [char]0x0438 + [char]0x0440 + [char]0x043E + [char]0x0432 + [char]0x0430 + [char]0x0442 + [char]0x044C
$open.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()

$editorUp = -1
$ready = (Get-Date).AddSeconds(60)
while ((Get-Date) -lt $ready) {
    Start-Sleep -Milliseconds 300
    $probe = [System.Windows.Automation.AutomationElement]::FromHandle($proc.MainWindowHandle)
    foreach ($b in $probe.FindAll([System.Windows.Automation.TreeScope]::Descendants, $btnCond)) {
        if ($b.Current.Name -like "*$editorLabel*") { $editorUp = [int]((Get-Date) - $started).TotalMilliseconds; break }
    }
    if ($editorUp -ge 0) { break }
}
if ($editorUp -lt 0) { throw "the editor never appeared (no '" + $editorLabel + "' button)" }
Write-Host "editor on screen after $editorUp ms"

if ($StaySeconds -gt 0) {
        # The other half of the question: is there any preparation running at all? Open a
        # project and simply watch. Without this, "the work stopped when I left" cannot be
        # told apart from "there was no work".
        $proc.Refresh()
        $cpu0 = $proc.TotalProcessorTime.TotalMilliseconds
        $per = New-Object System.Collections.Generic.List[double]
        $mark = $cpu0
        while ($true) {
            Start-Sleep -Seconds 1
            $proc.Refresh()
            $now = $proc.TotalProcessorTime.TotalMilliseconds
            $per.Add($now - $mark)
            $mark = $now
            if ($per.Count -ge $StaySeconds) { break }
        }
        Write-Host ''
        if ($Label) { Write-Host "=== $Label ===" }
        Write-Host ("CPU per second while sitting on the editor: {0}" -f (($per | ForEach-Object { '{0:N0} ms' -f $_ }) -join ', '))
        Write-Host ("total: {0:N0} ms" -f ($proc.TotalProcessorTime.TotalMilliseconds - $cpu0))
        return
    }

    $el = [System.Windows.Automation.AutomationElement]::FromHandle($proc.MainWindowHandle)
    $back = $null
    foreach ($t in $el.FindAll([System.Windows.Automation.TreeScope]::Descendants,
            [System.Windows.Automation.Condition]::TrueCondition)) {
        if ($t.Current.Name -like "*$backLabel*" -and $t.Current.ControlType -ne
            [System.Windows.Automation.ControlType]::Text) {
            $back = $t
            break
        }
    }
    if ($null -eq $back) { throw 'the "my projects" tab was not found - the editor did not open' }

    if (-not (Activate-Element $back)) { throw 'the "my projects" tab does not answer any of the usual patterns' }
    $left = Get-Date

    # Now watch. Anything the preparation still does shows up here.
    $proc.Refresh()
    $cpuAtLeave = $proc.TotalProcessorTime.TotalMilliseconds
    $samples = New-Object System.Collections.Generic.List[double]
    $cpuMark = $cpuAtLeave

    while (((Get-Date) - $left).TotalSeconds -lt $WatchSeconds) {
        Start-Sleep -Seconds 1
        $proc.Refresh()
        $now = $proc.TotalProcessorTime.TotalMilliseconds
        $samples.Add($now - $cpuMark)
        $cpuMark = $now
    }

    $afterLeave = $proc.TotalProcessorTime.TotalMilliseconds - $cpuAtLeave
    $first = if ($samples.Count -gt 0) { $samples[0] } else { 0 }
    $rest = if ($samples.Count -gt 1) { ($samples[1..($samples.Count - 1)] | Measure-Object -Sum).Sum } else { 0 }

    Write-Host ''
    if ($Label) { Write-Host "=== $Label ===" }
    Write-Host ("CPU in the {0:N0} s after leaving : {1:N0} ms" -f $WatchSeconds, $afterLeave)
    Write-Host ("  of which the first second      : {0:N0} ms" -f $first)
    Write-Host ("  and the seconds after that      : {0:N0} ms" -f $rest)
    Write-Host ("per second                       : {0}" -f (($samples | ForEach-Object { '{0:N0} ms' -f $_ }) -join ', '))
    Write-Host ''
    if ($rest -lt 2000) {
        Write-Host 'PASS: the preparation stopped within a second or two of leaving.'
    } else {
        Write-Host 'SUSPECT: work is still running after the screen was left. Either the cancellation is not wired, or something else is busy.'
    }
}
finally {
    if ($proc) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue }
    Pop-Location
}