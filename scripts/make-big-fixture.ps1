# Creates a throwaway COMBINED project shaped like a real mid-sized order: a hundred
# photos in the pool, ten books of five spreads, every spread filled.
#
# This is the owner's own worry - "если я туда загружу сотню файлов, а ещё и распределю
# по книгам, прога вообще зависнет" - turned into a repeatable case. The photos are real
# files on disk, copied from one of the owner's own pictures, so the decoders, the
# thumbnail builder and the file list all do the work they would do in production.
#
# Usage: pwsh -File scripts\make-big-fixture.ps1 -Photos 100 -Books 10 -Spreads 5
#        pwsh -File scripts\make-big-fixture.ps1 -Remove <id>
#
# NOTE: this file contains Cyrillic literals and must stay UTF-8 **with BOM**.

param(
    [int]$Photos = 100,
    [int]$Books = 10,
    [int]$Spreads = 5,
    [string]$Remove = '',
    [string]$OutOf = ''
)

[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8

$ErrorActionPreference = 'Stop'
$dir = Join-Path $env:LOCALAPPDATA 'PhotoBookRenamer\Projects'
$indexPath = Join-Path $dir 'projects.json'
$pool = Join-Path $env:TEMP 'FBR-BigFixture'

# ------------------------------------------------------------------ remove
if ($Remove) {
    $raw = [System.IO.File]::ReadAllText($indexPath)
    $lines = $raw -split "`r?`n"

    $entries = @(); $current = $null
    foreach ($line in $lines) {
        if ($line -match '^\s*\{\s*$') { $current = @($line); continue }
        if ($null -ne $current) {
            $current += $line
            if ($line -match '^\s*\},?\s*$') { $entries += ,$current; $current = $null }
        }
    }

    $kept = @(); $removed = 0
    foreach ($e in $entries) {
        $text = $e -join "`n"
        $id = [regex]::Match($text, '"id"\s*:\s*"([^"]+)"').Groups[1].Value
        if ($id -eq $Remove) { $removed++; continue }
        $kept += ,$e
    }
    if ($removed -eq 0) { throw "no entry with id $Remove" }

    $out = New-Object System.Collections.Generic.List[string]
    $out.Add('[')
    for ($i = 0; $i -lt $kept.Count; $i++) {
        foreach ($l in $kept[$i]) { $out.Add($l) }
        $last = $out[$out.Count - 1]
        $out[$out.Count - 1] = $last.TrimEnd(',') + $(if ($i -lt $kept.Count - 1) { ',' } else { '' })
    }
    $out.Add(']'); $out.Add('')
    $newRaw = ($out -join "`n")
    $check = $newRaw | ConvertFrom-Json
    [System.IO.File]::WriteAllText($indexPath, $newRaw, (New-Object System.Text.UTF8Encoding($false)))

    $f = Join-Path $dir "$Remove.json"
    if (Test-Path $f) { Remove-Item $f -Force }
    # The photo pool is NOT deleted here. Every fixture made by this script copies its
    # photos into that one folder, so removing one fixture used to delete the photos of
    # all the others and leave them pointing at nothing - which the app then correctly
    # showed as "Загружено: 0". Remove the folder by hand, or re-create a fixture.
    Write-Host "removed $Remove; index now has $(@($check).Count) entries, parses OK"
    exit 0
}

# A candidate is only accepted if a real decoder can read it. The first version of this
# script took the first .jpg over 200KB it found, which turned out to be a truncated
# 5 MB file: the app then correctly said "Файл не читается" on all 100 slots, and the
# measurement was of a broken fixture rather than of a hundred photos. A fixture that
# cannot be decoded measures nothing.
Add-Type -AssemblyName System.Drawing
function Test-Readable([string]$path) {
    try { $i = [System.Drawing.Image]::FromFile($path); $i.Dispose(); return $true }
    catch { return $false }
}

# One real photo, copied N times. Real bytes matter: a stub file would make the decoder
# fail fast and the measurement would be a lie.
$source = $OutOf
if (-not $source) {
    $candidates = @(Get-ChildItem "$env:USERPROFILE" -Recurse -Filter *.jpg -ErrorAction SilentlyContinue |
        Where-Object { $_.Length -gt 100KB -and $_.Length -lt 20MB })
    $source = ($candidates | Where-Object { Test-Readable $_.FullName } | Select-Object -First 1).FullName
    if (-not $source) { throw 'no readable .jpg found to copy; pass -OutOf <path>' }
}
if (-not (Test-Readable $source)) { throw "$source cannot be decoded; pass another -OutOf" }
Write-Host ("source photo: {0} ({1} MB)" -f $source, [math]::Round((Get-Item $source).Length / 1MB, 1))

if (Test-Path $pool) { Remove-Item $pool -Recurse -Force }
New-Item -ItemType Directory -Force -Path $pool | Out-Null

$paths = @()
for ($i = 0; $i -lt $Photos; $i++) {
    $name = 'big-{0:D3}.jpg' -f ($i + 1)
    Copy-Item $source (Join-Path $pool $name) -Force
    $paths += (Join-Path $pool $name)
}
$bad = @($paths | Where-Object { -not (Test-Readable $_) })
if ($bad.Count -gt 0) { throw "$($bad.Count) of the copies cannot be decoded - the fixture would be broken" }
Write-Host "photos: $($paths.Count) in $pool (every copy verified readable)"

# ------------------------------------------------------------------ project
function Esc([string]$s) { '"' + ($s -replace '\\', '\\') + '"' }

$pageJson = @()
for ($s = 0; $s -lt $Spreads; $s++) {
    $src = $paths[($s) % $paths.Count]
    $pageJson += ('{{ "sourcePath": {0}, "thumbnailPath": null, "isCover": false, "index": {1}, "displayIndex": {1}, "isLocked": false, "fileName": {2} }}' -f `
        (Esc $src), ($s + 1), (Esc (Split-Path $src -Leaf)))
}
$coverSrc = $paths[0]
$coverJson = ('{{ "sourcePath": {0}, "thumbnailPath": null, "isCover": true, "index": 0, "displayIndex": 0, "isLocked": false, "fileName": {1} }}' -f `
    (Esc $coverSrc), (Esc (Split-Path $coverSrc -Leaf)))

$bookJson = @()
for ($b = 0; $b -lt $Books; $b++) {
    $bookJson += ('{{ "folderPath": null, "name": {0}, "bookIndex": {1}, "cover": {2}, "pages": [{3}] }}' -f `
        (Esc ("Book 0" + ($b + 1))), ($b + 1), $coverJson, ($pageJson -join ', '))
}

$id = [guid]::NewGuid().ToString()
$file = Join-Path $dir "$id.json"
$poolJson = ($paths | ForEach-Object { Esc $_ }) -join ', '
[System.IO.File]::WriteAllText($file,
    ('{{ "mode": 3, "outputFolder": null, "books": [{0}], "availableFiles": [{1}] }}' -f ($bookJson -join ', '), $poolJson),
    (New-Object System.Text.UTF8Encoding($false)))

# ------------------------------------------------------------------ index, as raw text
$raw = [System.IO.File]::ReadAllText($indexPath)
$lines = $raw -split "`r?`n"
$entries = @(); $current = $null
foreach ($line in $lines) {
    if ($line -match '^\s*\{\s*$') { $current = @($line); continue }
    if ($null -ne $current) {
        $current += $line
        if ($line -match '^\s*\},?\s*$') { $entries += ,$current; $current = $null }
    }
}

$nl = if ($raw.Contains("`r`n")) { "`r`n" } else { "`n" }
$entry = @(
'  {',
('    "id": ' + (Esc $id) + ','),
'    "name": "Big order (test)",',
('    "filePath": ' + (Esc $file) + ','),
('    "bookCount": ' + $Books + ','),
('    "pageCount": ' + $Spreads + ','),
'    "status": 1,',
('    "lastModified": ' + (Esc ([DateTimeOffset]::Now.ToString('o'))) + ','),
'    "mode": 3,',
'    "createdDate": "0001-01-01T00:00:00"',
'  }'
)
$entries += ,$entry

$out = New-Object System.Collections.Generic.List[string]
$out.Add('[')
for ($i = 0; $i -lt $entries.Count; $i++) {
    foreach ($l in $entries[$i]) { $out.Add($l) }
    $last = $out[$out.Count - 1]
    $out[$out.Count - 1] = $last.TrimEnd(',') + $(if ($i -lt $entries.Count - 1) { ',' } else { '' })
}
$out.Add(']'); $out.Add('')
$newRaw = ($out -join $nl)
$check = $newRaw | ConvertFrom-Json
[System.IO.File]::WriteAllText($indexPath, $newRaw, (New-Object System.Text.UTF8Encoding($false)))

Write-Host "project: $Books books x $Spreads spreads, $($paths.Count) photos in the pool"
Write-Host "id     : $id"
Write-Host "index  : $(@($check).Count) entries, parses OK"
