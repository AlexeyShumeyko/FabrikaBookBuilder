#  The list of books down the side scrolls.
#
#    pwsh -File scripts\test-books-scroll.ps1
#
#  What this used to check, and why it failed
#
#  It photographed the whole window before and after eight wheel clicks and compared the
#  two pictures. "The pixels changed" is the claim.
#
#  It is not a claim about the book list. Something else on the screen changing - a
#  thumbnail finishing, a clock, a tooltip - changes pixels too, and the check cannot tell
#  the difference, so it passes on a list that never moved. And the wheel click goes to
#  whatever window has the focus, so when the window is not in front the input lands
#  nowhere and the photograph comes back unchanged, which reads as "scrolling is broken"
#  rather than as "the check aimed at the wrong window".
#
#  What it checks now
#
#  Each book is a card with its name on it, and the name is in the automation tree with its
#  own box:
#
#    which books are on screen now
#    scroll, by aiming at a card that is actually on screen
#    which books are on screen afterwards
#
#  and it is a scroll if the two sets differ. No photograph, no patch of colour, no
#  arithmetic.
#
#  It builds its own project, for the same reason the file-list check does: the books that
#  happen to exist on this machine are not the books that need testing, and a list of three
#  has nothing to scroll.
[CmdletBinding()]
param(
    [int]$WheelClicks = 8,

    # Books in the run. Comfortably more than fit beside the editor.
    [int]$Books = 30,

    [int]$SettleSeconds = 25
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8
. "$PSScriptRoot\common.ps1"

$ErrorActionPreference = 'Continue'

$FixtureName = 'Books scroll check'

function Get-BookNames($window) {
    # A book is identified on its card by its number - the card says "the number IS the name"
    # - so the badge is what is read. Anything else on the screen is ignored.
    #
    # Only books actually on screen are counted. The tree lists every item of a long list,
    # including the ones a long way below the bottom edge, and a scroll changes nothing at
    # all about that set - so counting them would report "the list did not move" for a list
    # that moved perfectly well, and would aim the wheel at a card nobody can see.
    $texts = $window.FindAll(
        [System.Windows.Automation.TreeScope]::Descendants,
        (New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::Text)))

    $bounds = $window.Current.BoundingRectangle

    $books = New-Object System.Collections.Generic.List[object]
    for ($i = 0; $i -lt $texts.Count; $i++) {
        $name = $texts[$i].Current.Name
        if ($name -notmatch '^\p{L}+ \d{2}$') { continue }
        if ($texts[$i].Current.IsOffscreen) { continue }

        $rect = $texts[$i].Current.BoundingRectangle
        if ($rect.Top -lt $bounds.Top -or $rect.Bottom -gt $bounds.Bottom) { continue }

        $books.Add([pscustomobject]@{ Name = $name; Rect = $rect })
    }
    return $books
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
    Write-Host 'FAIL: there is no photograph on this machine to copy, so a run with books to'
    Write-Host '      scroll cannot be built. Open any project that has photographs in it first.'
    exit 1
}

$pool = Join-Path $env:TEMP 'FBR-BooksFixture'
if (Test-Path $pool) { Remove-Item $pool -Recurse -Force }
New-Item -ItemType Directory -Path $pool -Force | Out-Null

$copy = Join-Path $pool 'book.jpg'
Copy-Item -LiteralPath $source -Destination $copy -Force

$madeBooks = @()
for ($b = 1; $b -le $Books; $b++) {
    $pages = @()
    for ($s = 1; $s -le 2; $s++) {
        $page = @{}
        $page['sourcePath'] = $copy
        $page['thumbnailPath'] = $null
        $page['isCover'] = $false
        $page['index'] = $s
        $page['displayIndex'] = $s
        $page['isLocked'] = $false
        $page['fileName'] = [System.IO.Path]::GetFileName($copy)

        $pages += ,$page
    }

    $cover = @{}
    $cover['sourcePath'] = $copy
    $cover['thumbnailPath'] = $null
    $cover['isCover'] = $true
    $cover['index'] = 0
    $cover['displayIndex'] = 0
    $cover['isLocked'] = $false
    $cover['fileName'] = [System.IO.Path]::GetFileName($copy)

    $book = @{}
    $book['folderPath'] = $null
    $book['name'] = "Book $b"
    $book['bookIndex'] = $b
    $book['cover'] = $cover
    $book['pages'] = $pages

    $madeBooks += ,$book
}

$project = @{}
$project['mode'] = 3
$project['outputFolder'] = $null
$project['availableFiles'] = @($copy)
$project['books'] = $madeBooks

$dir = Join-Path $env:LOCALAPPDATA 'PhotoBookRenamer\Projects'
$indexPath = Join-Path $dir 'projects.json'
$id = [guid]::NewGuid().ToString()
$projectPath = Join-Path $dir ($id + '.json')

[System.IO.File]::WriteAllText(
    $projectPath,
    ($project | ConvertTo-Json -Depth 12),
    (New-Object System.Text.UTF8Encoding($false)))

$now = (Get-Date).ToString('o')
$entry = @{}
$entry['id'] = $id
$entry['name'] = $FixtureName
$entry['filePath'] = $projectPath
$entry['mode'] = 3
$entry['status'] = 0
$entry['bookCount'] = $Books
$entry['pageCount'] = 2
$entry['createdDate'] = $now
$entry['lastModified'] = $now

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

Write-Host ("project '{0}': {1} books of two spreads" -f $FixtureName, $Books)

Get-Process -ErrorAction SilentlyContinue |
    Where-Object { $_.ProcessName -match 'PhotoBook' } |
    Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 1

$app = $null
try {
    $app = Start-TestApp
    Start-Sleep -Seconds 4

    # Its card, found by the project's name on the screen rather than by counting cards.
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

    [void](Invoke-Control $buttons[0])
    Start-Sleep -Seconds $SettleSeconds

    $app.Process.Refresh()
    if ($app.Process.HasExited) { Write-Host 'FAIL: the program stopped while opening the project'; exit 1 }

    $opened = @(Get-Controls $app.Window 'Text' $FixtureName)
    if ($opened.Count -eq 0) {
        Write-Host ''
        Write-Host ("FAIL: the header does not say '{0}', so a different project was opened" -f $FixtureName)
        exit 1
    }

    $before = Get-BookNames $app.Window
    Write-Host ("books on screen before : {0}" -f $before.Count)

    if ($before.Count -eq 0) {
        Write-Host ''
        Write-Host 'FAIL: no book names are in the tree at all, so there is nothing to read.'
        Write-Host '      What IS on screen:'
        foreach ($t in (Get-Texts $app.Window) | Select-Object -First 25) { Write-Host "        $t" }
        exit 1
    }

    $target = $before[[int]($before.Count / 2)]
    $x = [int]($target.Rect.X + $target.Rect.Width / 2)
    $y = [int]($target.Rect.Y + $target.Rect.Height / 2)
    Write-Host ("aiming at '{0}' at {1},{2}" -f $target.Name, $x, $y)

    Send-WheelAt $x $y $WheelClicks

    $app.Process.Refresh()
    if ($app.Process.HasExited) { Write-Host 'FAIL: the program stopped while scrolling'; exit 1 }

    $after = Get-BookNames $app.Window
    $namesBefore = @($before | ForEach-Object { $_.Name })
    $namesAfter = @($after | ForEach-Object { $_.Name })

    $newOnes = @($namesAfter | Where-Object { $namesBefore -notcontains $_ })
    $gone = @($namesBefore | Where-Object { $namesAfter -notcontains $_ })

    Write-Host ''
    Write-Host ("books on screen after  : {0}" -f $after.Count)
    Write-Host ("first before : {0}" -f (($namesBefore | Select-Object -First 4) -join ', '))
    Write-Host ("first after  : {0}" -f (($namesAfter | Select-Object -First 4) -join ', '))
    Write-Host ("newly shown  : {0}" -f $newOnes.Count)
    Write-Host ("gone from view: {0}" -f $gone.Count)

    if ($newOnes.Count -eq 0 -and $gone.Count -eq 0) {
        Write-Host ''
        Write-Host ("FAIL: {0} wheel clicks over the book list changed nothing" -f $WheelClicks)
        exit 1
    }

    Write-Host ''
    Write-Host ("PASS: the book list moved - {0} gone, {1} newly on screen" -f $gone.Count, $newOnes.Count)
    exit 0
}
finally {
    Stop-TestApp $app
}
