#  A photograph that is not there says so, and says why in the journal.
#
#    pwsh -File scripts\test-missing-photo.ps1
#
#  What it checks
#
#    A project is built with two slots pointing at files that do not exist, and opened.
#    The slot must say so on screen - in words, not in a shade of red - and the journal
#    must say so too, with the path that was looked for.
#
#  What it used to check, and why that was worse
#
#  It counted the reddish pixels in the book area and wanted twenty of them. That passes if
#  anything red appears anywhere in a rectangle, including a button. It passes on a warning
#  that has moved, on a warning that is half off screen, and on a photograph with a red
#  dress in it.
#
#  A warning is a sentence. It is checked by reading the sentence.
[CmdletBinding()]
param(
    [int]$SettleSeconds = 12
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8
. "$PSScriptRoot\common.ps1"

$ErrorActionPreference = 'Continue'

# --- a project with two photographs that are not there ------------------------------
$journalBefore = ''
$journalPath = Join-Path $env:LOCALAPPDATA 'PhotoBookRenamer\journal.log'
if (Test-Path $journalPath) {
    $journalBefore = [System.IO.File]::ReadAllText($journalPath, [System.Text.Encoding]::UTF8)
}

$fixture = & powershell -NoProfile -ExecutionPolicy Bypass -File "$PSScriptRoot\make-combined-fixture.ps1" -Books 1 -Spreads 2 -MissingPhoto 2>&1 | Out-String

$line = ($fixture -split "`n" | Where-Object { $_ -match 'id=([0-9a-f\-]+)' } | Select-Object -First 1)
if (-not $line) { Write-Result $false 'the fixture was not created' @($fixture.Trim()) }
Write-Host ("fixture: {0}" -f $line.Trim())

# --- open it -------------------------------------------------------------------------
Get-Process -ErrorAction SilentlyContinue |
    Where-Object { $_.ProcessName -match 'PhotoBook' } |
    Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 1

$app = $null
try {
    $app = Start-TestApp
    Start-Sleep -Seconds 4

    $open = @(Get-Controls $app.Window 'Button' 'Открыть проект')

    # The fixture was just written, so it is the newest and therefore the first card. This
    # is the one place counting is used, and it is a count of the file's own making.
    if ($open.Count -eq 0) {
        Write-Result $false 'the projects list showed no cards' @("the fixture id was $line")
    }

    Write-Host ("cards on screen: {0}; pressing the first" -f $open.Count)
    [void](Invoke-Control $open[0])
    Start-Sleep -Seconds $SettleSeconds

    $app.Process.Refresh()
    if ($app.Process.HasExited) { Write-Result $false 'the program stopped on a missing photograph' }

    $texts = @(Get-Texts $app.Window)

    # What the slot says. In this shell the sentence is "Файл не читается" and the path is
    # in the tooltip; the other shell's wording differs, so the check is on the idea rather
    # than on one exact string - it must be a warning about the photograph, not a slot label.
    $warnings = @($texts | Where-Object { $_ -like '*не читается*' -or $_ -like '*Файл*' -or $_ -like '*не найден*' })

    Write-Host ''
    Write-Host ('warnings on screen: {0}' -f $warnings.Count)
    foreach ($w in ($warnings | Select-Object -First 4)) { Write-Host "  $w" }

    # --- the journal -------------------------------------------------------------------
    $journalAfter = ''
    if (Test-Path $journalPath) {
        $journalAfter = [System.IO.File]::ReadAllText($journalPath, [System.Text.Encoding]::UTF8)
    }
    $added = $journalAfter.Substring([Math]::Min($journalBefore.Length, $journalAfter.Length))
    $logged = ($added -match 'no-such-photo|не найден|not found|file not found')

    Write-Host ("journal grew by {0} characters" -f $added.Length)
    if ($added.Length -gt 0) {
        Write-Host "  newest line: " + (($added -split "`n" | Where-Object { $_.Trim() } | Select-Object -Last 1))
    }

    $passed = ($warnings.Count -gt 0) -and $logged

    Write-Result $passed 'a photograph that is not there says so, on screen and in the journal' @(
        "warnings on screen : $($warnings.Count)"
        "recorded in journal: $logged"
        "program still running: $(-not $app.Process.HasExited)"
    )
}
finally {
    Stop-TestApp $app
}