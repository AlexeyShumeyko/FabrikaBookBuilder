# Takes the reference screenshots of every screen in one command, so "the interface has
# not changed" can be checked instead of remembered.
#
# The refactor waves 1 and 2 are invisible to the user by design - that is the whole point
# of doing them - and an invisible change is exactly the kind nobody notices until a
# client does. These pictures are the receipt.
#
# Two rules the set follows:
#   * the window size is fixed and fits on a 1600x900 screen, so nothing of the desktop
#     ends up in the frame: the taskbar clock changes between runs and would make every
#     comparison fail for no reason;
#   * the narrow capture exists because the card row is sized from the window width, so a
#     maximized screenshot can never show what a small laptop shows.
#
# These files are local. doc/ is gitignored: they are a development instrument, not
# documentation for the repository.
#
# Usage:
#   pwsh -File scripts\capture-golden.ps1
#   pwsh -File scripts\capture-golden.ps1 -Skip dialogs
#   pwsh -File scripts\capture-golden.ps1 -WithUpdateDialog
#
# NOTE: this file contains Cyrillic literals and must stay UTF-8 **with BOM**.

param(
    [string]$OutDir = 'doc\shots\golden',
    [int]$Width = 1520,
    [int]$Height = 800,
    [string]$Skip = '',
    [switch]$WithUpdateDialog,
    [switch]$NoFixtures
)

[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root

$fixtures = 'Testovyy al-bom 2026 (test)'
$fixturesCombined = 'Kombinirovannyy tirazh (test)'
$skip = @($Skip -split ',' | ForEach-Object { $_.Trim().ToLower() } | Where-Object { $_ })

function Stop-App {
    Get-Process -Name PhotoBookRenamer -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 800
}

function Invoke-Script([string]$script, [string[]]$arguments) {
    Stop-App
    $output = & powershell -NoProfile -ExecutionPolicy Bypass -File "scripts\$script" @arguments 2>&1
    $line = @($output | Select-Object -Last 1)
    return [pscustomobject]@{
        Script = $script
        Detail = if ($line) { "$line" } else { '' }
        Ok = $LASTEXITCODE -eq 0 -or "$line" -match 'saved'
    }
}

$results = New-Object System.Collections.Generic.List[object]

Write-Host "reference screenshots -> $OutDir"
Write-Host "window: ${Width}x${Height}"
Write-Host ''

if (-not $NoFixtures) {
    Write-Host 'preparing fixtures...'
    Stop-App
    & powershell -NoProfile -ExecutionPolicy Bypass -File scripts\make-test-fixture.ps1 2>&1 |
        Select-String -Pattern 'unique project|combined proj' | ForEach-Object { Write-Host "   $($_.Line.Trim())" }
    Start-Sleep -Seconds 1
}

if ($skip -notcontains 'screens') {
    Write-Host 'screens'
    $results.Add((Invoke-Script 'capture-screen.ps1' @('-Tab', 'Мои проекты', '-Out', "$OutDir\01-projects.png", '-Width', "$Width", '-Height', "$Height")))
    $results.Add((Invoke-Script 'capture-screen.ps1' @('-Tab', 'Выбор режима', '-Out', "$OutDir\02-mode.png", '-Width', "$Width", '-Height', "$Height")))
    $results.Add((Invoke-Script 'capture-screen.ps1' @('-Tab', 'Уникальные папки', '-OpenProject', '-ProjectName', $fixtures, '-Out', "$OutDir\03-unique-folders.png", '-Width', "$Width", '-Height', "$Height", '-OpenWaitSeconds', '12')))
    $results.Add((Invoke-Script 'capture-screen.ps1' @('-Tab', 'Комбинированный', '-OpenProject', '-ProjectName', $fixturesCombined, '-Out', "$OutDir\04-combined.png", '-Width', "$Width", '-Height', "$Height", '-OpenWaitSeconds', '12')))
    $results.Add((Invoke-Script 'capture-screen.ps1' @('-Tab', 'Уникальные папки', '-OpenProject', '-ProjectName', $fixtures, '-Out', "$OutDir\06-unique-folders-narrow.png", '-Width', '1120', '-Height', "$Height", '-OpenWaitSeconds', '12')))
}

if ($skip -notcontains 'dialogs') {
    Write-Host 'dialogs'
    $results.Add((Invoke-Script 'capture-dialog.ps1' @('-ProjectName', $fixtures, '-Dialog', 'Export', '-Mode', 'Unique', '-Out', "$OutDir\07-export-dialog.png")))
    $results.Add((Invoke-Script 'capture-dialog.ps1' @('-ProjectName', $fixtures, '-Dialog', 'Folders', '-Mode', 'Unique', '-Out', "$OutDir\08-folders-dialog.png")))
    $results.Add((Invoke-Script 'capture-confirm.ps1' @('-Out', "$OutDir\09-confirm-delete.png")))

    # This one shrinks the structure of the combined fixture on purpose: the question only
    # appears when the structure gets smaller. It leaves the fixture altered, which is why
    # the fixtures are rebuilt at the start of every run.
    $results.Add((Invoke-Script 'capture-structure-confirm.ps1' @('-Out', "$OutDir\10-confirm-structure.png")))
}

if ($WithUpdateDialog) {
    Write-Host 'update window (rebuilds twice - slow, and it is the only way to reach it)'
    $results.Add((Invoke-Script 'capture-update-dialog.ps1' @('-Out', "$OutDir\11-update-dialog.png")))
}

Stop-App

Write-Host ''
Write-Host '--- результат ---'
foreach ($r in $results) {
    $mark = if ($r.Ok) { 'ok  ' } else { 'FAIL' }
    Write-Host "$mark $($r.Script)  $($r.Detail)"
}

$files = @(Get-ChildItem $OutDir -Filter '*.png' -ErrorAction SilentlyContinue)
Write-Host ''
Write-Host "файлов в $OutDir : $($files.Count)"

if (-not $NoFixtures) {
    Write-Host ''
    Write-Host 'фикстуры оставлены изменёнными. Убрать:'
    Write-Host '   pwsh -File scripts\clean-test-projects.ps1'
}

$failed = @($results | Where-Object { -not $_.Ok })
Pop-Location
exit $(if ($failed.Count -gt 0) { 1 } else { 0 })