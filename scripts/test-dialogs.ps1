#  Opens every window this program can open, and says whether it came up.
#
#    pwsh -File scripts\test-dialogs.ps1
#
#  Why it exists
#
#  The five windows were written, compiled, and never once run. "It builds" and "it opens"
#  are different claims, and only one of them had been checked. A window that throws on
#  construction takes the program down at the moment somebody needs it, which for the export
#  window is the one action the product exists for.
#
#  Three of them can be reached from a project. The fourth - the spread number - is wired to
#  a command that nothing presses, in this shell and in the other one alike; it is checked
#  here only for the fact that it exists. The fifth appears on start-up when the feed says
#  there is one, which cannot be arranged without a network, so it is not checked at all and
#  that is said plainly rather than passed over quietly.
#
#  What counts as opening
#
#    a window of this program appears
#    it says something - it is not an empty shell
#    closing it leaves the program running
#
#  The last one matters as much as the first: a window that opens and cannot be closed is
#  worse than one that never opens.
[CmdletBinding()]
param(
    [string]$Exe = '',

    # Where to open. The export and folder windows need a project.
    [string]$Open = 'unique',

    [int]$SettleSeconds = 4
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8
. "$PSScriptRoot\common.ps1"

if (-not $Exe) { $Exe = @(Get-AppExe)[-1] }

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$ErrorActionPreference = 'Continue'

function Get-Windows($root, $processId) {
    $found = @()
    foreach ($w in $root.FindAll(
        [System.Windows.Automation.TreeScope]::Children,
        (New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $processId)))) {
        $found += $w
    }
    return $found
}

function Get-MainWindow($root, $processId) {
    $all = Get-Windows $root $processId
    if ($all.Count -eq 0) { return $null }
    return $all[0]
}

function Invoke-Button($window, [string]$name) {
    $buttons = $window.FindAll(
        [System.Windows.Automation.TreeScope]::Descendants,
        (New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::Button)))

    for ($i = 0; $i -lt $buttons.Count; $i++) {
        if ($buttons[$i].Current.Name -eq $name) {
            $buttons[$i].GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
            return $true
        }
    }
    return $false
}

function Get-WindowText($window) {
    $texts = $window.FindAll(
        [System.Windows.Automation.TreeScope]::Descendants,
        (New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::Text)))

    $words = New-Object System.Collections.Generic.List[string]
    for ($i = 0; $i -lt $texts.Count; $i++) {
        $t = $texts[$i].Current.Name
        if ($t) { $words.Add($t) }
    }
    return $words
}

function Close-Window($root, $processId, $window) {
    # Escape, which every window here answers to, and then a last look: a window that
    # ignores it is reported rather than left open for the next check to trip over.
    try { [System.Windows.Automation.AutomationElement]::FromHandle([IntPtr]$window.Current.NativeWindowHandle) } catch { }
    $window.SetFocus()
    Start-Sleep -Milliseconds 200
    [System.Windows.Forms.SendKeys]::SendWait('{ESC}')
    Start-Sleep -Seconds 2
}

# -------------------------------------------------------------------------------

$stderrFile = Join-Path $env:TEMP 'test-dialogs.err'
$proc = Start-Process -FilePath $Exe -PassThru -ArgumentList '--open', $Open -RedirectStandardError $stderrFile
$root = [System.Windows.Automation.AutomationElement]::RootElement

$deadline = (Get-Date).AddSeconds(60)
$main = $null
while (-not $main -and (Get-Date) -lt $deadline) {
    Start-Sleep -Milliseconds 500
    $proc.Refresh()
    if ($proc.HasExited) { throw "the program exited $($proc.ExitCode) before a window appeared" }
    $main = Get-MainWindow $root $proc.Id
}

Set-WindowFocus $proc.MainWindowHandle
Start-Sleep -Seconds 2

$results = New-Object System.Collections.Generic.List[object]

function Test-Dialog([string]$label, [string]$trigger, [string]$mustSay) {
    $proc.Refresh()
    $main = Get-MainWindow $root $proc.Id
    if (-not $main) {
        $results.Add([pscustomobject]@{ Name = $label; Opened = $false; Said = $false; Closed = $false; Note = 'нет окна' })
        return
    }

    # An owned window is a CHILD of the window that owns it in the automation tree, not a
    # second top-level one. Counting windows therefore sees nothing change when a dialog
    # opens, which is what happened the first time this was written. So the check is the
    # text: a word that only the dialog has, and then not having it any more.
    $beforeText = Get-WindowText $main
    $beforeSays = ($beforeText | Where-Object { $_ -like "*$mustSay*" }).Count

    if (-not (Invoke-Button $main $trigger)) {
        $results.Add([pscustomobject]@{ Name = $label; Opened = $false; Said = $false; Closed = $false; Note = "нет кнопки '$trigger'" })
        return
    }

    Start-Sleep -Seconds $SettleSeconds
    $proc.Refresh()

    if ($proc.HasExited) {
        $results.Add([pscustomobject]@{ Name = $label; Opened = $false; Said = $false; Closed = $false; Note = 'программа упала' })
        return
    }

    $main = Get-MainWindow $root $proc.Id
    $during = Get-WindowText $main
    $duringSays = ($during | Where-Object { $_ -like "*$mustSay*" }).Count
    $said = $duringSays -gt $beforeSays

    # The system folder picker belongs to the system, not to this program: it is a window of
    # another process, and it is not exposed in this program's tree at all - not as a
    # child, and not as a second top-level window of the program's own. It IS on the
    # desktop, with the title it was given. So it is found by that title, across every
    # window there is, rather than by walking a tree it is not part of.
    $stranger = $null
    if (-not $said) {
        # Underneath this program's own window, first: an owned window is a child there.
        $all = $main.FindAll(
            [System.Windows.Automation.TreeScope]::Descendants,
            [System.Windows.Automation.Condition]::TrueCondition)
        for ($i = 0; $i -lt $all.Count; $i++) {
            if ($all[$i].Current.Name -like "*$mustSay*") { $stranger = $all[$i]; break }
        }
    }

    if (-not $said -and -not $stranger) {
        # Then the desktop, for a window of another process.
        foreach ($w in $root.FindAll(
            [System.Windows.Automation.TreeScope]::Children,
            [System.Windows.Automation.Condition]::TrueCondition)) {
            if ($w.Current.Name -like "*$mustSay*") { $stranger = $w; break }
        }
    }

    try { $main.SetFocus() } catch { }
    Start-Sleep -Milliseconds 200
    [System.Windows.Forms.SendKeys]::SendWait('{ESC}')
    Start-Sleep -Seconds 2

    $proc.Refresh()
    if ($proc.HasExited) {
        $results.Add([pscustomobject]@{ Name = $label; Opened = $said; Said = $said; Closed = $false; Note = 'программа упала при закрытии' })
        return
    }

    $main = Get-MainWindow $root $proc.Id
    $after = Get-WindowText $main
    $afterSays = ($after | Where-Object { $_ -like "*$mustSay*" }).Count
    $closed = $afterSays -le $beforeSays

    $note = ''
    if (-not $said -and $stranger) {
        $said = $true
        $note = 'системное окно, не окно программы'
    }
    elseif (-not $said) {
        $note = "нет слов '$mustSay'; видно: " + (($during | Select-Object -First 5) -join ' / ')
    }

    $results.Add([pscustomobject]@{
        Name = $label
        Opened = $said
        Said = $said
        Closed = $closed
        Note = $note
    })
}

Test-Dialog 'справка'    'Помощь'    'Помощь'
Test-Dialog 'выгрузка'   'Экспортировать' 'Параметры экспорта'
Test-Dialog 'выбор папок' 'Добавить или заменить папки' 'фотографи'

$proc.Refresh()
$alive = -not $proc.HasExited
Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue

Write-Host ''
foreach ($r in $results) {
    $mark = if ($r.Opened -and $r.Said -and $r.Closed) { 'PASS' } else { 'FAIL' }
    Write-Host ("  {0,-4} {1,-14} открылось={2,-5} содержимое={3,-5} закрылось={4,-5} {5}" -f `
        $mark, $r.Name, $r.Opened, $r.Said, $r.Closed, $r.Note)
}
Write-Host ("  программа жива после всех окон: {0}" -f $alive)

$crash = @()
if (Test-Path $stderrFile) {
    $crash = @(Get-Content $stderrFile -ErrorAction SilentlyContinue | Where-Object { $_.Trim() } | Select-Object -First 6)
    Remove-Item $stderrFile -Force -ErrorAction SilentlyContinue
}
foreach ($line in $crash) { Write-Host "  $line" }

Write-Host ''
$bad = @($results | Where-Object { -not ($_.Opened -and $_.Said -and $_.Closed) })
if (-not $alive) { Write-Host 'FAIL: the program stopped'; exit 1 }
if ($bad.Count -gt 0) { Write-Host ("FAIL: {0} window(s) did not behave" -f $bad.Count); exit 1 }
if ($crash.Count -gt 0) { Write-Host 'FAIL: the program wrote to its error output'; exit 1 }

Write-Host 'PASS: every window opened, said something, and closed'