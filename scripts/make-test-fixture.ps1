# Creates a throwaway project on disk so the redesigned screens can be screenshotted
# with real books, spreads and thumbnails instead of only empty states.
#
# Writes to %LOCALAPPDATA%\PhotoBookRenamer\Projects (the app's real store) and to a
# temp fixture folder. Run scripts/remove-test-fixture.ps1 to clean up.
#
# Usage:  pwsh -File scripts\make-test-fixture.ps1

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$fixture = Join-Path $env:TEMP 'FBR-TestFixture'
$projectsDir = Join-Path $env:LOCALAPPDATA 'PhotoBookRenamer\Projects'

function New-TestImage([string]$path, [int]$w, [int]$h, [string]$hue) {
    $bmp = New-Object System.Drawing.Bitmap $w, $h
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.ColorTranslator]::FromHtml($hue))
    # A couple of shapes so the images are visually distinguishable in screenshots.
    $brush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(90, 255, 255, 255))
    $g.FillEllipse($brush, [int]($w * 0.18), [int]($h * 0.22), [int]($w * 0.4), [int]($h * 0.4))
    $brush2 = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(110, 0, 0, 0))
    $g.FillRectangle($brush2, 0, [int]($h * 0.78), $w, [int]($h * 0.22))
    $g.Dispose()
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Jpeg)
    $bmp.Dispose()
}

$hues = @('#4F46E5', '#059669', '#D97706', '#DC2626', '#0891B2', '#7C3AED', '#DB2777', '#65A30D')

# ---------------------------------------------------------------- fixture folders
if (Test-Path $fixture) { Remove-Item $fixture -Recurse -Force }
New-Item -ItemType Directory -Force -Path $fixture | Out-Null

$bookFolders = @('11A_Иванов_Сергей', '11A_Петрова_Алена', '11A_Сидоров_Максим')
$books = @()
$fileIndex = 0

for ($b = 0; $b -lt $bookFolders.Count; $b++) {
    $folder = Join-Path $fixture $bookFolders[$b]
    New-Item -ItemType Directory -Force -Path $folder | Out-Null

    $pages = @()
    # 6 files: the last one is the largest, so DetectCoverAsync picks it as the cover.
    for ($i = 1; $i -le 6; $i++) {
        $name = ('{0:d2}_photo.jpg' -f $i)
        $p = Join-Path $folder $name
        $isBig = ($i -eq 6)
        if ($isBig) { New-TestImage $p 2400 1800 $hues[$fileIndex % $hues.Count] }
        else { New-TestImage $p (900 + $i * 40) (700 + $i * 20) $hues[$fileIndex % $hues.Count] }
        $fileIndex++
        if (-not $isBig) {
            $pages += [ordered]@{
                sourcePath   = $p
                thumbnailPath = $null
                isCover      = $false
                index        = ($i)
                displayIndex = ($i)
                isLocked     = $false
                fileName     = $name
            }
        }
    }

    $coverName = '06_photo.jpg'
    $books += [ordered]@{
        folderPath = $folder
        name       = $bookFolders[$b]
        bookIndex  = ($b + 1)
        cover      = [ordered]@{
            sourcePath    = (Join-Path $folder $coverName)
            thumbnailPath = $null
            isCover       = $true
            index         = 0
            displayIndex  = 0
            isLocked      = $false
            fileName      = $coverName
        }
        pages      = $pages
    }
}

# ---------------------------------------------------------------- project file
if (-not (Test-Path $projectsDir)) { New-Item -ItemType Directory -Force -Path $projectsDir | Out-Null }

$id = [guid]::NewGuid().ToString()
$projectFile = Join-Path $projectsDir "$id.json"

$project = [ordered]@{
    mode           = 2            # AppMode.UniqueFolders
    outputFolder   = $null
    books          = $books
    availableFiles = @()
}
$project | ConvertTo-Json -Depth 8 | Set-Content -Path $projectFile -Encoding UTF8

# ---------------------------------------------------------------- index entry
$indexPath = Join-Path $projectsDir 'projects.json'
$list = @()
if (Test-Path $indexPath) {
    try { $list = @(Get-Content $indexPath -Raw -Encoding UTF8 | ConvertFrom-Json) } catch { $list = @() }
}

$entry = [ordered]@{
    id           = $id
    name         = 'Выпускной Альбом 2026 (тест)'
    filePath     = $projectFile
    bookCount    = $bookFolders.Count
    pageCount    = 5
    status       = 1              # ProjectStatus.Ready
    createdDate  = (Get-Date).AddDays(-3).ToString('o')
    lastModified = (Get-Date).AddHours(-2).ToString('o')
    mode         = 2
}

$existing = $list | Where-Object { $_.id -eq $id }
if (-not $existing) { $list = @($list) + $entry }
$list | ConvertTo-Json -Depth 6 | Set-Content -Path $indexPath -Encoding UTF8

Write-Host "fixture folder : $fixture"
Write-Host "project file   : $projectFile"
Write-Host "books          : $($bookFolders.Count) x 5 spreads + cover"
