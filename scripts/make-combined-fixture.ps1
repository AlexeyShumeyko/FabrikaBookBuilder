# Creates a throwaway COMBINED project in the user's project list, with real photos,
# so the combined screen can be verified on live data. ASCII only on purpose: a .ps1
# with Cyrillic has to be UTF-8 with BOM, and the write tool cannot add one.
#
# Usage: make-combined-fixture.ps1          (app must be closed)
#        remove-combined-fixture.ps1 <id>

param(
    [string]$RemoveId = '',
    [int]$Books = 2,
    [int]$Spreads = 3,
    [switch]$EmptyLastBook,
    [string]$PhotoPath = '',
    [int]$PhotoW = 0,
    [int]$PhotoH = 0,
    [string]$CoverPhotoPath = '',
    [int]$CoverW = 0,
    [int]$CoverH = 0,
    [string]$OutlierPhotoPath = '',
    [int]$OutlierW = 0,
    [int]$OutlierH = 0,
    [switch]$AllEmpty,
    [switch]$OnlyCover,
    [ValidateSet('Unique', 'Example1', 'Example2', 'Override')]
    [string]$Pattern = 'Unique'
)

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

# -PhotoPath replaces the harvest with one real photo of a chosen format. That is how the
# narrow post-print case gets tested: a 0.67 photo makes the narrowest card the program
# has to lay out, and the path contains Cyrillic that a typed parameter would mangle, so
# the caller discovers the file and passes it in.
if ($PhotoPath -ne '' -and $PhotoW -gt 0 -and $PhotoH -gt 0) {
    $photos = 1..4 | ForEach-Object {
        @{ path = $PhotoPath; w = $PhotoW; h = $PhotoH; thumb = $null }
    }
}

$id = [guid]::NewGuid().ToString()
$bookList = @()
for ($b = 1; $b -le $Books; $b++) {
    # -EmptyLastBook leaves the final book without photos. That is the check for the
    # owner's rule: a run has ONE frame shape, so the empty book must end up the same
    # size as the filled one instead of falling back to the default 16:10.
    # -AllEmpty empties every book, -OnlyCover leaves a cover and no spreads at all.
    $fill = -not ($EmptyLastBook -and $b -eq $Books) -and -not $AllEmpty
    $pages = @()
    for ($s = 1; $s -le $Spreads; $s++) {
        # Spread 2 of every book gets the SAME photo on purpose: that is the run-wide
        # spread the owner asked to see labelled.
        $pick =
            if (-not $fill -or $OnlyCover) { $null }
            elseif ($Pattern -eq 'Example1') {
                # Cover and every spread but the last are shared by the whole run; the last
                # spread is one photo per book. The owner's first example.
                if ($s -lt $Spreads) { $photos[$s % $photos.Count] } else { $photos[($b - 1) % $photos.Count] }
            }
            elseif ($Pattern -eq 'Example2') {
                # Everything shared. Nothing in the set says how many books there are, which
                # is exactly the owner's second example.
                $photos[$s % $photos.Count]
            }
            elseif ($Pattern -eq 'Override') {
                # A shared spread that the LAST book replaced with its own photo: the run keeps
                # 000-FF for the others and 003-FF for the book that went its own way.
                if ($s -eq 1 -and $b -eq $Books) { $photos[3 % $photos.Count] } else { $photos[$s % $photos.Count] }
            }
            elseif ($s -eq 2) { $photos[1] }
            else { $photos[(($b - 1) * 3 + $s - 1) % $photos.Count] }
        $pages += [ordered]@{
            isCover = $false; index = $s; displayIndex = $s
            sourcePath = if ($pick) { $pick.path } else { $null }
            thumbnailPath = if ($pick) { $pick.thumb } else { $null }
            isLocked = $false
            exportFileName = ('{0:D3}-{1:D2}.jpg' -f $b, $s)
            imageWidth = if ($pick) { $pick.w } else { 0 }
            imageHeight = if ($pick) { $pick.h } else { 0 }
        }
    }

    # -OutlierPhotoPath puts a photo of a DIFFERENT format in the LAST spread. That is
    # the case the owner hit by accident: a cover pasted over a spread. A median would
    # average the two formats; the frames must stay on the first spread's format and crop
    # the odd one out.
    if ($fill -and $OutlierPhotoPath -ne '' -and $OutlierW -gt 0 -and $OutlierH -gt 0 -and $Spreads -ge 1) {
        $last = $pages[$pages.Count - 1]
        $last.sourcePath = $OutlierPhotoPath
        $last.thumbnailPath = $null
        $last.imageWidth = $OutlierW
        $last.imageHeight = $OutlierH
    }
    $cover = if ($fill) { $photos[0] } else { $null }

    # -CoverPhotoPath swaps the cover for a photo of a DIFFERENT format. That is the
    # owner's real pattern (a 1.9 cover above square spreads), and it is how the rule
    # "aligned by the spreads, the cover does not count" gets checked.
    if ($fill -and $CoverPhotoPath -ne '' -and $CoverW -gt 0 -and $CoverH -gt 0) {
        $cover = @{ path = $CoverPhotoPath; w = $CoverW; h = $CoverH; thumb = $null }
    }
    $bookList += [ordered]@{
        folderPath = $null
        name = "Book $b"
        bookIndex = $b
        cover = [ordered]@{
            isCover = $true; index = 0; displayIndex = 0
            sourcePath = if ($cover) { $cover.path } else { $null }
            thumbnailPath = if ($cover) { $cover.thumb } else { $null }
            isLocked = $false; exportFileName = ('{0:D3}-{1:D2}.jpg' -f $b, 0)
            imageWidth = if ($cover) { $cover.w } else { 0 }
            imageHeight = if ($cover) { $cover.h } else { 0 }
        }
        pages = $pages
    }
}

$proj = [ordered]@{
    mode = 3
    outputFolder = $null
    books = $bookList
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
"created combined fixture id=$id books=$Books spreads=$Spreads photos=$($photos.Count); index now $(@($check).Count) entries, parses OK"
