#  The list of loaded photographs scrolls.
#
#    pwsh -File scripts\test-list-scroll.ps1
#
#  What this used to check, and why it failed
#
#  It worked out where the list was by arithmetic - the left edge from the heading's own
#  box, the top two hundred pixels below it, a width of two hundred and eighty - sent a
#  wheel click at that point, and compared the colour of a patch of pixels before and
#  after.
#
#  Three things in that were wrong at once. The two hundred pixels were a guess about where
#  the list sat inside the panel, and the panel is not the same height in either shell. The
#  wheel click goes to whatever window has the focus, so a point in the wrong place means
#  no input at all and the screen does not move. And the patch of pixels is a measure of
#  where things were drawn, which is the thing that had changed, not of whether they
#  scrolled.
#
#  What it checks now
#
#  The rows of the file list carry their file's name, and the name is in the automation
#  tree with its own box. So the check needs no arithmetic at all:
#
#    which names are on screen now
#    scroll, by aiming at a row that is actually on screen
#    which names are on screen afterwards
#
#  and it is a scroll if the two sets differ. Nothing here knows a coordinate, and nothing
#  here knows how tall a panel is.
#
#  Four things it had to be taught, and every one of them was found by the check passing
#  when it should have failed
#
#  A name has to be a name and not a path. Pointing the cursor at a row opens a tooltip,
#  and a tooltip carries the whole path - so every run reported one file newly shown, and a
#  run in which nothing moved at all looked exactly like a run in which something did.
#
#  There has to be anything to scroll, and more spreads is not more rows. The list has one
#  row per different file, and the run-shaped fixtures reuse the same handful of files in
#  every spread - which is what a real order looks like, and leaves four rows and nothing to
#  scroll. That was being read as a list that would not scroll.
#
#  And the project has to be the one that was chosen. "The first card" is not a project; it
#  is whichever project sorts first, which is not necessarily the newest.
#
#  So it builds its own: real files, copied from a real photograph already on this machine,
#  one per spread, and enough of them to go past the bottom of the panel. A check that only
#  runs on a machine someone has already prepared is a check that quietly stops being run.
[CmdletBinding()]
param(
    [int]$WheelClicks = 8,

    # Distinct photographs in the project. Comfortably more than fit on one screen.
    [int]$Photos = 30,

    # How long to let the photographs decode before reading the list.
    [int]$SettleSeconds = 25
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8
. "$PSScriptRoot\common.ps1"

$ErrorActionPreference = 'Continue'

$FixtureName = 'Scroll check'

function Get-PhotoRows($window) {
    # A row is a file's name and nothing else - see the note at the top about tooltips.
    $texts = $window.FindAll(
        [System.Windows.Automation.TreeScope]::Descendants,
        (New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::Text)))

    $rows = New-Object System.Collections.Generic.List[object]
    for ($i = 0; $i -lt $texts.Count; $i++) {
        $name = $texts[$i].Current.Name
        if ($name -match '^[^\\/:*?"<>|]+\.[A-Za-z]{3,4}$') {
            $rows.Add([pscustomobject]@{ Name = $name; Rect = $texts[$i].Current.BoundingRectangle })
        }
    }
    return $rows
}

# --- a real photograph to copy, found rather than assumed --------------------------------
function Find-SourcePhoto {
    $dir = Join-Path $env:LOCALAPPDATA 'PhotoBookRenamer\Projects'
    $indexPath = Join-Path $dir 'projects.json'
    if (-not (Test-Path $indexPath)) { return $null }

    $parsed = [System.IO.File]::ReadAllText($indexPath, [System.Text.Encoding]::UTF8) | ConvertFrom-Json
    foreach ($entry in $parsed) {
        $file = Join-Path $dir ($entry.id + '.json')
        if (-not (Test-Path $file)) { continue }

        try { $project = [System.IO.File]::ReadAllText($file, [System.Text.Encoding]::UTF8) | ConvertFrom-Json }
        catch { continue }

        foreach ($book in @($project.books)) {
            foreach ($page in @($book.pages)) {
                if ($page.sourcePath -and (Test-Path -LiteralPath $page.sourcePath)) {
                    return $page.sourcePath
                }
            }
        }
    }
    return $null
}

# --- the project ------------------------------------------------------------------------
$source = Find-SourcePhoto
if (-not $source) {
    Write-Host ''
    Write-Host 'FAIL: there is no photograph on this machine to copy, so a project with a long'
    Write-Host '      file list cannot be built. Open any project that has photographs in it first.'
    exit 1
}

$pool = Join-Path $env:TEMP 'FBR-ScrollFixture'
if (Test-Path $pool) { Remove-Item $pool -Recurse -Force }
New-Item -ItemType Directory -Path $pool -Force | Out-Null

$copies = @()
for ($i = 1; $i -le $Photos; $i++) {
    $to = Join-Path $pool ('scroll-{0:d3}.jpg' -f $i)
    Copy-Item -LiteralPath $source -Destination $to -Force
    $copies += $to
}

$pages = @()
for ($i = 0; $i -lt $Photos; $i++) {
    $page = @{}
    $page['sourcePath'] = $copies[$i]
    $page['thumbnailPath'] = $null
    $page['isCover'] = $false
    $page['index'] = $i + 1
    $page['displayIndex'] = $i + 1
    $page['isLocked'] = $false
    $page['fileName'] = [System.IO.Path]::GetFileName($copies[$i])

    $pages += ,$page
}

# Built one piece at a time rather than as nested literals: a hashtable written inside an
# array inside another hashtable throws "argument types do not match" on PowerShell 5.1, and
# the message says nothing about which of the three it is.
$cover = @{}
$cover['sourcePath'] = $copies[0]
$cover['thumbnailPath'] = $null
$cover['isCover'] = $true
$cover['index'] = 0
$cover['displayIndex'] = 0
$cover['isLocked'] = $false
$cover['fileName'] = [System.IO.Path]::GetFileName($copies[0])

$book = @{}
$book['folderPath'] = $null
$book['name'] = 'Book 1'
$book['bookIndex'] = 1
$book['cover'] = $cover
$book['pages'] = $pages

$books = @()
$books += $book

$project = @{}
$project['mode'] = 3
$project['outputFolder'] = $null
# The file list is this, not the slots. A project written with photographs in its slots and
# nothing here opens with an empty list, which looks exactly like a list that will not scroll.
$project['availableFiles'] = $copies
$project['books'] = $books

$dir = Join-Path $env:LOCALAPPDATA 'PhotoBookRenamer\Projects'
$indexPath = Join-Path $dir 'projects.json'
$id = [guid]::NewGuid().ToString()
$projectPath = Join-Path $dir ($id + '.json')

[System.IO.File]::WriteAllText(
    $projectPath,
    ($project | ConvertTo-Json -Depth 12),
    (New-Object System.Text.UTF8Encoding($false)))

$now = (Get-Date).ToString('o')
$entry = @{
    id = $id
    name = $FixtureName
    filePath = $projectPath
    mode = 3
    status = 0
    bookCount = 1
    pageCount = $Photos
    createdDate = $now
    lastModified = $now
}

$index = @()
if (Test-Path $indexPath) {
    $existing = [System.IO.File]::ReadAllText($indexPath, [System.Text.Encoding]::UTF8) | ConvertFrom-Json
    $index = @($existing | Where-Object { $_.name -ne $FixtureName })
}
$index = @($index) + @($entry)

[System.IO.File]::WriteAllText(
    $indexPath,
    (ConvertTo-Json -InputObject @($index) -Depth 6),
    (New-Object System.Text.UTF8Encoding($false)))

Write-Host ("project '{0}': {1} different photographs, one per spread" -f $FixtureName, $Photos)

Get-Process -ErrorAction SilentlyContinue |
    Where-Object { $_.ProcessName -match 'PhotoBook' } |
    Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 1

$app = $null
try {
    $app = Start-TestApp
    Start-Sleep -Seconds 4

    # Its card, found by the project's name on the screen rather than by counting cards.
    # Every card's open button is called exactly "Открыть проект", so the name is the only
    # thing on a card that says which project it belongs to - and the open button nearest to
    # that name is the one on the same card.
    $label = @(Get-Controls $app.Window 'Text' $FixtureName) | Select-Object -First 1
    if ($null -eq $label) {
        Write-Host ''
        Write-Host ("FAIL: no card on screen is called '{0}' - the project list does not show it" -f $FixtureName)
        exit 1
    }

    $at = $label.Current.BoundingRectangle
    $buttons = @(Get-Controls $app.Window 'Button' 'Открыть проект') | Sort-Object {
        $b = Get-Box $_
        $dx = $b.CentreX - $at.X
        $dy = $b.CentreY - $at.Y
        [math]::Sqrt($dx * $dx + $dy * $dy)
    }

    if ($buttons.Count -eq 0) { Write-Host 'FAIL: no card on screen has an open button'; exit 1 }

    Write-Host ("its card's open button is {0} px from the name" -f (Get-Box $buttons[0]).CentreX)
    [void](Invoke-Control $buttons[0])

    Start-Sleep -Seconds $SettleSeconds

    $app.Process.Refresh()
    if ($app.Process.HasExited) { Write-Host 'FAIL: the program stopped while opening the project'; exit 1 }

    # The header says which project is open. Checked here because every check below is about
    # this project's list, and a list belonging to some other project would still scroll.
    $opened = @(Get-Controls $app.Window 'Text' $FixtureName)
    if ($opened.Count -eq 0) {
        Write-Host ''
        Write-Host ("FAIL: the header does not say '{0}', so a different project was opened" -f $FixtureName)
        exit 1
    }

    $before = Get-PhotoRows $app.Window
    Write-Host ("rows on screen before : {0}" -f $before.Count)

    if ($before.Count -eq 0) { Write-Host 'FAIL: the file list showed no rows at all'; exit 1 }

    # Aimed at the middle of a row that is on screen. This is the whole difference between
    # this working and not: the previous version aimed at a number.
    $target = $before[[int]($before.Count / 2)]
    $x = [int]($target.Rect.X + $target.Rect.Width / 2)
    $y = [int]($target.Rect.Y + $target.Rect.Height / 2)
    Write-Host ("aiming at '{0}' at {1},{2}" -f $target.Name, $x, $y)

    Send-WheelAt $x $y $WheelClicks

    $app.Process.Refresh()
    if ($app.Process.HasExited) { Write-Host 'FAIL: the program stopped while scrolling'; exit 1 }

    $after = Get-PhotoRows $app.Window
    $namesBefore = @($before | ForEach-Object { $_.Name })
    $namesAfter = @($after | ForEach-Object { $_.Name })

    $newOnes = @($namesAfter | Where-Object { $namesBefore -notcontains $_ })
    $gone = @($namesBefore | Where-Object { $namesAfter -notcontains $_ })

    Write-Host ''
    Write-Host ("rows on screen after  : {0}" -f $after.Count)
    Write-Host ("first before : {0}" -f (($namesBefore | Select-Object -First 3) -join ', '))
    Write-Host ("first after  : {0}" -f (($namesAfter | Select-Object -First 3) -join ', '))
    Write-Host ("newly shown  : {0}" -f $newOnes.Count)
    Write-Host ("gone from view: {0}" -f $gone.Count)

    if ($newOnes.Count -eq 0 -and $gone.Count -eq 0) {
        Write-Host ''
        Write-Host ("FAIL: {0} wheel clicks over the list changed nothing" -f $WheelClicks)
        exit 1
    }

    Write-Host ''
    Write-Host ("PASS: the list moved - {0} rows gone, {1} newly on screen" -f $gone.Count, $newOnes.Count)
    exit 0
}
finally {
    Stop-TestApp $app
}
