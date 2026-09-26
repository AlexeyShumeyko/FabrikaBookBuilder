# Creates a throwaway COMBINED project in the user's project list, with real photos,
# so the combined screen can be verified on live data. ASCII only on purpose: a .ps1
# with Cyrillic has to be UTF-8 with BOM, and the write tool cannot add one.
#
# Usage: make-combined-fixture.ps1          (app must be closed)
#        remove-combined-fixture.ps1 <id>

param([string]$RemoveId = '')

$ErrorActionPreference = 'Stop'
$dir = Join-Path $env:LOCALAPPDATA 'PhotoBookRenamer\Projects'
$indexPath = Join-Path $dir 'projects.json'

function Read-Json($p) { [System.IO.File]::ReadAllText($p) | ConvertFrom-Json }
function Write-Json($p, $o) {
    $s = $o | ConvertTo-Json -Depth 12
    [System.IO.File]::WriteAllText($p, $s, (New-Object System.Text.UTF8Encoding($false)))
}

if ($RemoveId -ne '') {
    # Drop the fixture from the index and delete its file. The index is only touched
    # with the app closed, then verified by parsing it back.
    $idx = Read-Json $indexPath
    $kept = @($idx | Where-Object { $_.id -ne $RemoveId })
    Write-Json $indexPath $kept
    $f = Join-Path $dir "$RemoveId.json"
    if (Test-Path -LiteralPath $f) { Remove-Item -LiteralPath $f -Force }
    $check = Read-Json $indexPath
    "removed $RemoveId; index now has $(@($check).Count) entries, parses OK"
    return
}

$index = Read-Json $indexPath
$combined = $index | Where-Object { $_.mode -eq 3 } | Select-Object -First 1
$unique = $index | Where-Object { $_.mode -eq 2 } | Select-Object -First 1
if (-not $combined) { throw 'no combined project to copy as a template' }
if (-not $unique) { throw 'no unique project to take photos from' }

# Real photos with their real sizes, harvested from the unique project.
$src = Read-Json $unique.filePath
$photos = @($src.books[0].pages | Where-Object { $_.sourcePath } | ForEach-Object {
    @{ path = $_.sourcePath; w = $_.imageWidth; h = $_.imageHeight; thumb = $_.thumbnailPath }
})
if ($photos.Count -lt 4) { throw "only $($photos.Count) photos with sizes in the source project" }

$id = [guid]::NewGuid().ToString()
$books = @()
for ($b = 1; $b -le 2; $b++) {
    $pages = @()
    for ($s = 1; $s -le 3; $s++) {
        # Spread 2 of every book gets the SAME photo on purpose: that is the run-wide
        # spread the owner asked to see labelled.
        $pick = if ($s -eq 2) { $photos[1] } else { $photos[(($b - 1) * 3 + $s - 1) % $photos.Count] }
        $pages += [ordered]@{
            isCover = $false; index = $s; displayIndex = $s
            sourcePath = $pick.path; thumbnailPath = $pick.thumb
            isLocked = $false
            exportFileName = ('{0:D3}-{1:D2}.jpg' -f $b, $s)
            imageWidth = $pick.w; imageHeight = $pick.h
        }
    }
    $cover = $photos[0]
    $books += [ordered]@{
        folderPath = $null
        name = "Book $b"
        bookIndex = $b
        cover = [ordered]@{
            isCover = $true; index = 0; displayIndex = 0
            sourcePath = $cover.path; thumbnailPath = $cover.thumb
            isLocked = $false; exportFileName = ('{0:D3}-{1:D2}.jpg' -f $b, 0)
            imageWidth = $cover.w; imageHeight = $cover.h
        }
        pages = $pages
    }
}

$proj = [ordered]@{
    mode = 3
    outputFolder = $null
    books = $books
    availableFiles = @($photos | ForEach-Object { $_.path })
}

$projPath = Join-Path $dir "$id.json"
Write-Json $projPath $proj

$now = (Get-Date).ToString('o')
$new = [ordered]@{
    id = $id
    name = 'TR-test'
    filePath = $projPath
    mode = 3
    status = 0
    bookCount = 2
    pageCount = 3
    createdDate = $now
    lastModified = $now
}
$all = @($index) + @($new)
Write-Json $indexPath $all

$check = Read-Json $indexPath
"created combined fixture id=$id books=2 spreads=3 photos=$($photos.Count); index now $(@($check).Count) entries, parses OK"
