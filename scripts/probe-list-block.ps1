#  Is the window frozen during the wait, or is it waiting?
#
#    pwsh -File scripts\probe-list-block.ps1
#
#  The list takes about 2.7 seconds and every card appears at the very end of it, which is
#  the signature of one long block rather than of slow incremental work. That is worth
#  knowing, because the two have completely different fixes:
#
#    the window stops answering      -> something is running on the UI thread. Nothing can
#                                       be done for the user until it finishes, and the fix
#                                       is to make it smaller or move it off the thread.
#    the window keeps answering      -> the work is elsewhere - a task, a wait, a lock -
#                                       and the UI is merely showing nothing, which is a
#                                       different and often easier problem.
#
#  So: press the tab, and ask the window whether it is alive, every 50 milliseconds. A
#  message that is answered in under a millisecond means the pump is running.
[CmdletBinding()]
param([int]$TimeoutSeconds = 120)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8
. "$PSScriptRoot\common.ps1"

$ErrorActionPreference = 'Continue'

Add-Type -Namespace 'Alive' -Name 'W' -MemberDefinition @'
[DllImport("user32.dll", CharSet=CharSet.Auto)] public static extern IntPtr SendMessageTimeout(IntPtr h, uint msg, IntPtr wp, IntPtr lp, uint flags, uint timeout, out IntPtr result);
[DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
[DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, IntPtr e);
'@

function Test-Alive([IntPtr]$Handle, [int]$BudgetMs = 300) {
    $result = [IntPtr]::Zero
    # SMTO_ABORTIFHUNG = 0x0002. A hung window does not answer at all and the call returns
    # zero - which is the reading we are after.
    $ok = [Alive.W]::SendMessageTimeout($Handle, 0x0000, [IntPtr]::Zero, [IntPtr]::Zero, 0x0002, [uint32]$BudgetMs, [ref]$result)
    return $ok -ne [IntPtr]::Zero
}

Get-Process -ErrorAction SilentlyContinue |
    Where-Object { $_.ProcessName -match 'PhotoBook' } |
    Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 1

$app = $null
try {
    $app = Start-TestApp
    Start-Sleep -Seconds 9
    $w = $app.Window
    $hwnd = $app.Process.MainWindowHandle

    $open = @(Get-Controls $w 'Button' 'Открыть проект') | Select-Object -First 1
    [void](Invoke-Control $open)

    $until = (Get-Date).AddSeconds(60)
    while ((Get-Date) -lt $until) {
        Start-Sleep -Milliseconds 400
        if (@(Get-Controls $w 'Button' 'Добавить структуру').Count -gt 0) { break }
    }
    Start-Sleep -Seconds 3

    Write-Host ''
    Write-Host ('the window while the project is open: alive = {0}' -f (Test-Alive $hwnd))

    $tab = @(Get-Controls $w 'Button' 'Мои проекты') | Select-Object -First 1
    Write-Host 'pressing "Мои проекты" and asking the window whether it is alive, every 50 ms'
    Write-Host ''

    $watch = [System.Diagnostics.Stopwatch]::StartNew()
    [void](Invoke-Control $tab)

    $rows = New-Object System.Collections.Generic.List[object]
    $firstFrozen = $null
    $frozenRuns = 0
    $lastCards = 0

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        $at = $watch.ElapsedMilliseconds
        $alive = Test-Alive $hwnd 300
        $cards = @(Get-Controls $w 'Button' 'Открыть проект').Count

        if (-not $alive) {
            $frozenRuns++
            if ($null -eq $firstFrozen) { $firstFrozen = $at }
        }
        elseif ($null -ne $firstFrozen) {
            # it came back
        }

        $rows.Add([pscustomobject]@{
                At = $at; Alive = $alive; Cards = $cards
            })

        if ($cards -gt 0 -and $cards -eq $lastCards -and $watch.ElapsedMilliseconds -gt 2500) { break }
        $lastCards = $cards
        Start-Sleep -Milliseconds 50
    }

    $firstCards = $null
    foreach ($r in $rows) { if ($r.Cards -gt 0) { $firstCards = $r.At; break } }

    Write-Host ("first sample where the window did NOT answer : {0} ms" -f $(if ($null -ne $firstFrozen) { $firstFrozen } else { 'never' }))
    Write-Host ("samples where the window did not answer     : {0} of {1}" -f $frozenRuns, $rows.Count)
    Write-Host ("first card visible at                        : {0} ms" -f $(if ($null -ne $firstCards) { $firstCards } else { 'never' }))
    Write-Host ''
    Write-Host '  at(ms)  alive  cards'
    foreach ($r in $rows) {
        if ($r.At % 500 -gt 260) {
            Write-Host ('  {0,6}  {1,-5}  {2}' -f $r.At, $r.Alive, $r.Cards)
        }
    }
}
finally {
    Stop-TestApp $app
}
