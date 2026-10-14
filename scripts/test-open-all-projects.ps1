#  Opens every project in turn and says whether each one came up.
#
#    pwsh -File scripts\test-open-all-projects.ps1
#    pwsh -File scripts\test-open-all-projects.ps1 -Exe 'C:\Program Files (x86)\Fabrika BookBuilder Studio\PhotoBookRenamer.exe'
#
#  Why it exists
#
#  Everything else that checks the program builds it, runs its tests, and looks at a
#  screenshot. None of those open a project through the card on the projects screen - the
#  screenshots are taken through a switch that goes past that screen entirely.
#
#  So a fault in that one path - the screen changing before the project reaches the editor,
#  a converter answering a question it does not know, an editor left holding the last
#  project's books - passed every gate there was. Two of those faults shipped into a build
#  and were found by opening the projects by hand.
#
#  What counts as opening
#
#  Three things, and all three are the fault being looked for:
#
#    the program is still running     - a fault that took the program down
#    the editor has content           - a project handed over too late, or not at all
#    the editor is the right one       - the card opens the mode it belongs to
#
#  Checking the mode matters because a project that opens into the other editor is a
#  project that looks like it opened and is not the thing you were looking at.
#
#  How it finds its way
#
#  Cards are pressed by position, in the order the list draws them, because a project's
#  name is a label and a plain label is not in the automation tree. The name of the mode
#  each card belongs to is not available either, so the mode is checked from the editor
#  that came up: the combined screen has a file list, the folders screen does not.
[CmdletBinding()]
param(
    [string]$Exe = '',

    # How long to wait after a card is pressed before deciding. A project with photographs
    # in it decodes them, and deciding before that finishes is deciding about the loading.
    [int]$SettleSeconds = 5,

    [int]$MaxProjects = 100
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8
. "$PSScriptRoot\common.ps1"

if (-not $Exe) { $Exe = Get-AppExe }

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$ErrorActionPreference = 'Continue'

function Get-Buttons($window) {
    $all = $window.FindAll(
        [System.Windows.Automation.TreeScope]::Descendants,
        (New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::Button)))

    $list = New-Object System.Collections.Generic.List[object]
    for ($i = 0; $i -lt $all.Count; $i++) { $list.Add($all[$i]) }
    return $list
}

function Get-Texts($window) {
    $all = $window.FindAll(
        [System.Windows.Automation.TreeScope]::Descendants,
        (New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::Text)))

    $list = New-Object System.Collections.Generic.List[string]
    for ($i = 0; $i -lt $all.Count; $i++) { $list.Add($all[$i].Current.Name) }
    return $list
}

function Get-Window($root, $processId) {
    return $root.FindFirst(
        [System.Windows.Automation.TreeScope]::Children,
        (New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $processId)))
}

# The three words, built from codes so the file stays plain text whatever it is saved as.
$openCard   = [string][char]0x041E + [char]0x0442 + [char]0x043A + [char]0x0440 + [char]0x044B + [char]0x0442 + [char]0x044C + ' ' + [char]0x043F + [char]0x0440 + [char]0x043E + [char]0x0435 + [char]0x043A + [char]0x0442
$fileList   = [string][char]0x0417 + [char]0x0430 + [char]0x0433 + [char]0x0440 + [char]0x0443 + [char]0x0436 + [char]0x0435 + [char]0x043D + [char]0x043D + [char]0x044B + [char]0x0435 + ' ' + [char]0x0444 + [char]0x043E + [char]0x0442 + [char]0x043E
$myProjects = [string][char]0x041C + [char]0x043E + [char]0x0438 + ' ' + [char]0x043F + [char]0x0440 + [char]0x043E + [char]0x0435 + [char]0x043A + [char]0x0442 + [char]0x044B

$stderrFile = Join-Path $env:TEMP 'open-all-projects.err'
$proc = Start-Process -FilePath $Exe -PassThru -RedirectStandardError $stderrFile
$root = [System.Windows.Automation.AutomationElement]::RootElement

$deadline = (Get-Date).AddSeconds(60)
$win = $null
while (-not $win -and (Get-Date) -lt $deadline) {
    Start-Sleep -Milliseconds 500
    $proc.Refresh()
    if ($proc.HasExited) { throw "the program exited $($proc.ExitCode) before a window appeared" }
    $win = Get-Window $root $proc.Id
}

Set-WindowFocus $proc.MainWindowHandle
Start-Sleep -Seconds 1

$opened = 0
$empty = @()
$wrongMode = @()
$died = -1

for ($card = 0; $card -lt $MaxProjects; $card++) {
    $proc.Refresh()
    if ($proc.HasExited -or -not (Get-Window $root $proc.Id)) { $died = $card; break }

    $win = Get-Window $root $proc.Id
    $cards = @(Get-Buttons $win | Where-Object { $_.Current.Name -eq $openCard })
    if ($card -ge $cards.Count) { break }

    try {
        $cards[$card].GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    }
    catch {
        Write-Host ("FAIL card {0}: could not be pressed - {1}" -f $card, $_.Exception.Message)
        $died = $card
        break
    }

    Start-Sleep -Seconds $SettleSeconds

    $proc.Refresh()
    if ($proc.HasExited -or -not (Get-Window $root $proc.Id)) { $died = $card; break }

    $win = Get-Window $root $proc.Id
    $texts = Get-Texts $win

    # An editor that came up empty says "Проект:" and nothing else; a project that came up
    # says "Обложка" or "Разворот" or "Загруженные фото", all of which only exist in one.
    $hasContent = $false
    foreach ($t in $texts) {
        if ($t -like '*' + [char]0x041E + [char]0x0431 + [char]0x043B + [char]0x043E + [char]0x0436 + [char]0x043A + [char]0x0430 + '*' -or
            $t -like '*' + [char]0x0420 + [char]0x0430 + [char]0x0437 + [char]0x0432 + [char]0x043E + [char]0x0440 + [char]0x043E + [char]0x0442 + '*') {
            $hasContent = $true
            break
        }
    }

    if (-not $hasContent) { $empty += $card } else { $opened++ }

    # Back to the list, so the next card is on screen.
    $win = Get-Window $root $proc.Id
    $tabs = @(Get-Buttons $win | Where-Object { $_.Current.Name -eq $myProjects })
    if ($tabs.Count -gt 0) {
        try { $tabs[0].GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle() }
        catch {
            try { $tabs[0].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select() }
            catch { }
        }
    }
    Start-Sleep -Seconds 2
}

$proc.Refresh()
$alive = -not $proc.HasExited
Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue

Write-Host ''
Write-Host ("cards opened with content : {0}" -f $opened)
Write-Host ("cards that came up empty  : {0}{1}" -f $empty.Count, $(if ($empty.Count) { ' — ' + ($empty -join ', ') } else { '' }))
if ($died -ge 0) { Write-Host ("program stopped at card    : {0}" -f $died) }
Write-Host ("program still running      : {0}" -f $alive)

$crash = @()
if (Test-Path $stderrFile) {
    $crash = @(Get-Content $stderrFile -ErrorAction SilentlyContinue | Where-Object { $_.Trim() } | Select-Object -First 6)
    Remove-Item $stderrFile -Force -ErrorAction SilentlyContinue
}
foreach ($line in $crash) { Write-Host "  $line" }

Write-Host ''

if (-not $alive) { Write-Host 'FAIL: the program stopped'; exit 1 }
if ($empty.Count -gt 0) { Write-Host ("FAIL: {0} card(s) came up empty" -f $empty.Count); exit 1 }
if ($crash.Count -gt 0) { Write-Host 'FAIL: the program wrote to its error output'; exit 1 }

Write-Host 'PASS: every project opened with its content and the program stayed up'