#  How long the saved projects take to appear, and in what order.
#
#    pwsh -File scripts\measure-project-load.ps1
#
#  What it measures, and why it is worth having
#
#  The list opens by reading and parsing **every project file** - not the index, the
#  projects themselves - because each card wants two numbers and three thumbnail paths.
#  So the cost of the list grows with the total size of every project on the machine, not
#  with the number of cards.
#
#  Three moments, because they are three different complaints:
#
#    window to first card    - how long before anything appears at all
#    window to last card     - how long before the list is complete
#    loading message visible - how long the "Загрузка проектов..." plate is on screen,
#                              which is the part a person watches and cannot click through
#
#  Measured from the automation tree, not from a stopwatch in the program: the claim is
#  about what a person sees, and the program's own idea of when it finished is not the
#  moment the pixels changed.
[CmdletBinding()]
param(
    [int]$TimeoutSeconds = 120
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8
. "$PSScriptRoot\common.ps1"

$ErrorActionPreference = 'Continue'

function Get-State($window) {
    $texts = $window.FindAll(
        [System.Windows.Automation.TreeScope]::Descendants,
        (New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::Text)))

    $loading = $false
    $cards = 0
    $names = New-Object System.Collections.Generic.List[string]

    for ($i = 0; $i -lt $texts.Count; $i++) {
        $name = $texts[$i].Current.Name
        if (-not $name) { continue }
        if ($name -like 'Загрузка проектов*') { $loading = $true }
        if ($name -eq 'Открыть проект') { $cards++ }
        $names.Add($name)
    }

    return [pscustomobject]@{ Loading = $loading; Cards = $cards; Names = $names }
}

Get-Process -ErrorAction SilentlyContinue |
    Where-Object { $_.ProcessName -match 'PhotoBook' } |
    Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 1

# How much there is to read, before anything is measured.
$ip = Join-Path $env:LOCALAPPDATA 'PhotoBookRenamer\Projects\projects.json'
$entries = [System.IO.File]::ReadAllText($ip, [System.Text.Encoding]::UTF8) | ConvertFrom-Json
$bytes = 0
foreach ($e in @($entries)) {
    if ($e.filePath -and (Test-Path -LiteralPath $e.filePath)) {
        $bytes += (Get-Item -LiteralPath $e.filePath).Length
    }
}

Write-Host ''
Write-Host ("projects in the index : {0}" -f @($entries).Count)
Write-Host ("their files, total    : {0} MB" -f [math]::Round($bytes / 1MB, 2))
Write-Host ("the index itself      : {0} KB" -f [math]::Round((Get-Item $ip).Length / 1KB, 1))
Write-Host ''

$watch = [System.Diagnostics.Stopwatch]::StartNew()
$app = $null
try {
    $app = Start-TestApp
    $window = $app.Window
    $ready = (Get-Date).AddSeconds($TimeoutSeconds)

    $firstCard = $null
    $lastCard = $null
    $loadingSeen = $false
    $loadingGone = $null
    $peak = 0
    $sawCount = -1

    while ((Get-Date) -lt $ready) {
        $state = Get-State $window
        if ($state.Loading) { $loadingSeen = $true }

        if ($state.Cards -gt 0 -and $null -eq $firstCard) {
            $firstCard = $watch.ElapsedMilliseconds
        }

        # Cards arriving one at a time is the complaint, so the count is sampled rather
        # than the end state: a list that arrives complete in one go and a list that
        # trickles in both finish at the same moment, and only the samples tell them
        # apart.
        if ($state.Cards -gt $peak) { $peak = $state.Cards }
        if ($state.Cards -gt 0 -and $state.Cards -ne $sawCount) {
            $sawCount = $state.Cards
        }

        $total = $state.Cards + $state.Names.Count
        if ($loadingSeen -and -not $state.Loading -and $null -eq $loadingGone) {
            $loadingGone = $watch.ElapsedMilliseconds
        }

        if ($state.Cards -ge @($entries).Count -and @($entries).Count -gt 0 -and -not $state.Loading) {
            $lastCard = $watch.ElapsedMilliseconds
            break
        }

        Start-Sleep -Milliseconds 50
    }

    Write-Host ''
    Write-Host ("window to first card      : {0,6} ms" -f $(if ($null -ne $firstCard) { $firstCard } else { 'never' }))
    Write-Host ("window to loading message : {0,6} ms   visible from here" -f $(if ($loadingSeen) { 'yes' } else { 'no' }))
    Write-Host ("loading message gone by   : {0,6} ms" -f $(if ($null -ne $loadingGone) { $loadingGone } else { 'never' }))
    Write-Host ("window to all cards       : {0,6} ms" -f $(if ($null -ne $lastCard) { $lastCard } else { 'not reached' }))
    Write-Host ("cards seen at the end     : {0} of {1}" -f $peak, @($entries).Count)
    Write-Host ''

    # ---------------------------------------------------------------------------
    #  Coming back to the list from a project - the case that was reported.
    #
    #  Not "list, other screen, list". That route can be served from the screen that
    #  already exists, and then it measures a swap rather than a load. The report is
    #  about opening a project and coming back, so that is what is timed.
    # ---------------------------------------------------------------------------
    $open = @(Get-Controls $window 'Button' 'Открыть проект') | Select-Object -First 1
    if ($open) {
        [void](Invoke-Control $open)

        $inEditor = (Get-Date).AddSeconds(60)
        while ((Get-Date) -lt $inEditor) {
            Start-Sleep -Milliseconds 500
            if (@(Get-Controls $window 'Button' 'Мои проекты').Count -gt 0 -and
                @(Get-Controls $window 'Button' 'Добавить структуру').Count -gt 0) { break }
        }

        $projectsTab = @(Get-Controls $window 'Button' 'Мои проекты') | Select-Object -First 1
        if ($projectsTab) {
            Start-Sleep -Seconds 2

            $round = [System.Diagnostics.Stopwatch]::StartNew()
            [void](Invoke-Control $projectsTab)

            $seenFirst = $null
            $seenAll = $null
            $plateSeen = $false
            $plateGone = $null
            $counts = New-Object System.Collections.Generic.List[int]

            $until = (Get-Date).AddSeconds($TimeoutSeconds)
            while ((Get-Date) -lt $until) {
                $state = Get-State $window
                if ($state.Loading) { $plateSeen = $true }
                if ($plateSeen -and -not $state.Loading -and $null -eq $plateGone) {
                    $plateGone = $round.ElapsedMilliseconds
                }
                if ($state.Cards -gt 0) {
                    if ($null -eq $seenFirst) { $seenFirst = $round.ElapsedMilliseconds }
                    $counts.Add($state.Cards)
                }

                if ($state.Cards -ge @($entries).Count -and @($entries).Count -gt 0 -and -not $state.Loading) {
                    $seenAll = $round.ElapsedMilliseconds
                    break
                }

                Start-Sleep -Milliseconds 50
            }

            Write-Host 'coming back to the list from a project'
            Write-Host ("  press to first card     : {0,6} ms" -f $(if ($null -ne $seenFirst) { $seenFirst } else { 'never' }))
            Write-Host ("  press to all cards      : {0,6} ms" -f $(if ($null -ne $seenAll) { $seenAll } else { 'not reached' }))
            Write-Host ("  loading plate shown     : {0}" -f $plateSeen)
            Write-Host ("  loading plate gone by   : {0} ms" -f $(if ($null -ne $plateGone) { $plateGone } else { 'never' }))
            Write-Host ('  distinct card counts    : ' +
                (@($counts | Sort-Object -Unique) -join ', '))
            Write-Host ''
        }
    }
}
finally {
    Stop-TestApp $app
}
