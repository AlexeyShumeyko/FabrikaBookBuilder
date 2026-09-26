# Creates throwaway projects on disk so the redesigned screens can be screenshotted with
# real books, spreads and thumbnails instead of only empty states.
#
# Writes to %LOCALAPPDATA%\PhotoBookRenamer\Projects (the app's real store).
# Run scripts/remove-test-fixture.ps1 to clean up.
#
# IMPORTANT: projects.json is assembled as TEXT here on purpose. An earlier version used
# ConvertTo-Json, which wrapped arrays in a {"value": [...], "Count": N} envelope and
# corrupted the real project index. PowerShell's JSON round-trip is not safe for this
# file. scripts/repair-projects-json.ps1 exists to undo that damage.
#
# Usage:  pwsh -File scripts\make-test-fixture.ps1

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$fixture = Join-Path $env:TEMP 'FBR-TestFixture'
$projectsDir = Join-Path $env:LOCALAPPDATA 'PhotoBookRenamer\Projects'
$indexPath = Join-Path $projectsDir 'projects.json'

$nl = [Environment]::NewLine

function Esc([string]$s) {
    if ($null -eq $s) { return 'null' }
    return '"' + ($s -replace '\\', '\\\\' -replace '"', '\"') + '"'
}

function New-TestImage([string]$path, [int]$w, [int]$h, [string]$hue) {
    $bmp = New-Object System.Drawing.Bitmap $w, $h
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.ColorTranslator]::FromHtml($hue))
    $b1 = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(90, 255, 255, 255))
    $g.FillEllipse($b1, [int]($w * 0.18), [int]($h * 0.22), [int]($w * 0.4), [int]($h * 0.4))
    $b2 = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(110, 0, 0, 0))
    $g.FillRectangle($b2, 0, [int]($h * 0.78), $w, [int]($h * 0.22))
    $g.Dispose()
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Jpeg)
    $bmp.Dispose()
}

$hues = @('#4F46E5', '#059669', '#D97706', '#DC2626', '#0891B2', '#7C3AED', '#DB2777', '#65A30D')

# ---------------------------------------------------------------- fixture folders
if (Test-Path $fixture) { Remove-Item $fixture -Recurse -Force }
New-Item -ItemType Directory -Force -Path $fixture | Out-Null
if (-not (Test-Path $projectsDir)) { New-Item -ItemType Directory -Force -Path $projectsDir | Out-Null }

$bookFolders = @('11A_Ivanov_Sergey', '11A_Petrova_Alena', '11A_Sidorov_Maxim')
$fileIndex = 0

foreach ($folderName in $bookFolders) {
    $folder = Join-Path $fixture $folderName
    New-Item -ItemType Directory -Force -Path $folder | Out-Null

    for ($i = 1; $i -le 6; $i++) {
        $name = ('{0:d2}_photo.jpg' -f $i)
        $p = Join-Path $folder $name
        if ($i -eq 6) { New-TestImage $p 2400 1800 $hues[$fileIndex % $hues.Count] }
        else { New-TestImage $p (900 + $i * 40) (700 + $i * 20) $hues[$fileIndex % $hues.Count] }
        $fileIndex++
    }
}

# ------------------------------------------------------- index text manipulation
# Read existing entries as raw text blocks and keep them verbatim, so nothing already in
# the index can be reshaped by this script.
function Get-ExistingEntryBlocks {
    if (-not (Test-Path $indexPath)) { return @() }
    $raw = [System.IO.File]::ReadAllText($indexPath, [System.Text.Encoding]::UTF8)
    if ([string]::IsNullOrWhiteSpace($raw)) { return @() }

    $out = New-Object System.Collections.ArrayList
    $stack = New-Object System.Collections.Stack
    $inString = $false; $escaped = $false
    for ($i = 0; $i -lt $raw.Length; $i++) {
        $ch = $raw[$i]
        if ($inString) {
            if ($escaped) { $escaped = $false; continue }
            if ($ch -eq '\') { $escaped = $true; continue }
            if ($ch -eq '"') { $inString = $false }
            continue
        }
        if ($ch -eq '"') { $inString = $true; continue }
        if ($ch -eq '{') { $stack.Push($i); continue }
        if ($ch -eq '}' -and $stack.Count -gt 0) {
            $s = [int]$stack.Pop()
            $block = $raw.Substring($s, $i - $s + 1)
            if (($block -match '"filePath"') -and ($block -match '"mode"') -and ($block -notmatch '"value"\s*:')) {
                [void]$out.Add(($block -replace '\s+', ' '))
            }
        }
    }
    return @($out)
}

function Write-Index([string[]]$entryBlocks) {
    # If the previous file was mangled, keep a copy before replacing it.
    if ((Test-Path $indexPath) -and
        ([System.IO.File]::ReadAllText($indexPath, [System.Text.Encoding]::UTF8) -match '"value"\s*:')) {
        Copy-Item $indexPath "$indexPath.corrupt-backup" -Force
        Write-Host "NOTE: previous projects.json was mangled; saved as projects.json.corrupt-backup"
    }

    $body = ($entryBlocks -join (',' + $nl))
    $text = '[' + $nl + $body + $nl + ']'
    [System.IO.File]::WriteAllText($indexPath, $text, (New-Object System.Text.UTF8Encoding($false)))
}

function New-EntryText([string]$id, [string]$name, [string]$filePath,
                       [int]$books, [int]$pages, [int]$status, [int]$mode, [datetime]$modified) {
    return ('{{ "id": {0}, "name": {1}, "filePath": {2}, "bookCount": {3}, "pageCount": {4}, "status": {5}, "createdDate": {6}, "lastModified": {7}, "mode": {8} }}' -f `
        (Esc $id), (Esc $name), (Esc $filePath), $books, $pages, $status,
        (Esc $modified.AddDays(-2).ToString('o')), (Esc $modified.ToString('o')), $mode)
}

# ------------------------------------------------------- unique-folders project
$uBooks = New-Object System.Collections.ArrayList
$bi = 0
foreach ($folderName in $bookFolders) {
    $folder = Join-Path $fixture $folderName
    $pages = New-Object System.Collections.ArrayList

    $pageJson = @()
    for ($i = 1; $i -le 5; $i++) {
        $n = ('{0:d2}_photo.jpg' -f $i)
        $pageJson += ('{{ "sourcePath": {0}, "thumbnailPath": null, "isCover": false, "index": {1}, "displayIndex": {1}, "isLocked": false, "fileName": {2} }}' -f `
            (Esc (Join-Path $folder $n)), $i, (Esc $n))
    }

    $coverJson = ('{{ "sourcePath": {0}, "thumbnailPath": null, "isCover": true, "index": 0, "displayIndex": 0, "isLocked": false, "fileName": "06_photo.jpg" }}' -f `
        (Esc (Join-Path $folder '06_photo.jpg')))

    [void]$uBooks.Add(('{{ "folderPath": {0}, "name": {1}, "bookIndex": {2}, "cover": {3}, "pages": [{4}] }}' -f `
        (Esc $folder), (Esc $folderName), ($bi + 1), $coverJson, ($pageJson -join ', ')))
    $bi++
}

$uId = [guid]::NewGuid().ToString()
$uFile = Join-Path $projectsDir "$uId.json"
[System.IO.File]::WriteAllText($uFile, ('{{ "mode": 2, "outputFolder": null, "books": [{0}], "availableFiles": [] }}' -f ($uBooks -join ', ')), (New-Object System.Text.UTF8Encoding($false)))

# ------------------------------------------------------------- combined project
$poolFiles = @()
foreach ($folderName in $bookFolders) {
    $poolFiles += Get-ChildItem -Path (Join-Path $fixture $folderName) -Filter *.jpg |
        Sort-Object Name | ForEach-Object { $_.FullName }
}

$cBooks = New-Object System.Collections.ArrayList
for ($b = 0; $b -lt 2; $b++) {
    $pageJson = @()
    for ($i = 0; $i -lt 3; $i++) {
        # Leave the last spread of the second book empty so empty-slot styling shows up.
        $empty = ($b -eq 1 -and $i -eq 2)
        $src = $poolFiles[($b * 3 + $i) % $poolFiles.Count]
        $pageJson += ('{{ "sourcePath": {0}, "thumbnailPath": null, "isCover": false, "index": {1}, "displayIndex": {1}, "isLocked": false, "fileName": {2} }}' -f `
            $(if ($empty) { 'null' } else { Esc $src }), ($i + 1),
            $(if ($empty) { 'null' } else { Esc (Split-Path $src -Leaf) }))
    }
    $coverSrc = $poolFiles[($b * 5) % $poolFiles.Count]
    $coverJson = ('{{ "sourcePath": {0}, "thumbnailPath": null, "isCover": true, "index": 0, "displayIndex": 0, "isLocked": false, "fileName": {1} }}' -f `
        (Esc $coverSrc), (Esc (Split-Path $coverSrc -Leaf)))
    [void]$cBooks.Add(('{{ "folderPath": null, "name": {0}, "bookIndex": {1}, "cover": {2}, "pages": [{3}] }}' -f `
        (Esc ("Book 0" + ($b + 1))), ($b + 1), $coverJson, ($pageJson -join ', ')))
}

$cId = [guid]::NewGuid().ToString()
$cFile = Join-Path $projectsDir "$cId.json"
$poolJson = ($poolFiles | ForEach-Object { Esc $_ }) -join ', '
[System.IO.File]::WriteAllText($cFile, ('{{ "mode": 3, "outputFolder": null, "books": [{0}], "availableFiles": [{1}] }}' -f ($cBooks -join ', '), $poolJson), (New-Object System.Text.UTF8Encoding($false)))

# ------------------------------------------------------------------ write index
$existing = Get-ExistingEntryBlocks
$kept = @($existing | Where-Object { $_ -notmatch [regex]::Escape($uId) -and $_ -notmatch [regex]::Escape($cId) -and $_ -notmatch '\(test\)' })
$new = @(
    (New-EntryText $uId 'Testovyy al-bom 2026 (test)' $uFile $bookFolders.Count 5 1 2 (Get-Date).AddHours(-2)),
    (New-EntryText $cId 'Kombinirovannyy tirazh (test)' $cFile 2 3 1 3 (Get-Date).AddMinutes(-30))
)
Write-Index ($kept + $new)

Write-Host "fixture folder : $fixture"
Write-Host "unique project : $uFile  ($($bookFolders.Count) books x 5 spreads + cover)"
Write-Host "combined proj  : $cFile  (2 books x 3 spreads, one slot empty on purpose)"
Write-Host "index entries  : kept $($kept.Count), added 2"
