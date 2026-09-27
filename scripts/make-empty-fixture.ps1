# Creates an EMPTY project (no books) for each mode, so the two empty states can be
# looked at. A filled fixture cannot show them: the empty card is hidden as soon as the
# project has structure.
#
# The index is edited as raw text (see clean-test-projects.ps1 and trap 9): it stores
# non-ASCII as \uXXXX and a ConvertTo-Json round trip has mangled the Cyrillic before.
#
# Usage: pwsh -File scripts\make-empty-fixture.ps1 -Remove <id>
#
# NOTE: this file contains Cyrillic literals and must stay UTF-8 **with BOM**.

param(
    [string]$Remove = ''
)

[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8

$ErrorActionPreference = 'Stop'
$dir = Join-Path $env:LOCALAPPDATA 'PhotoBookRenamer\Projects'
$indexPath = Join-Path $dir 'projects.json'

$raw = [System.IO.File]::ReadAllText($indexPath)
$lines = $raw -split "`r?`n"

# Entry-by-entry, keeping every line verbatim.
$entries = @()
$current = $null
foreach ($line in $lines) {
    if ($line -match '^\s*\{\s*$') { $current = @($line); continue }
    if ($null -ne $current) {
        $current += $line
        if ($line -match '^\s*\},?\s*$') { $entries += ,$current; $current = $null }
    }
}

if ($Remove) {
    $kept = @()
    $removed = 0
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
    Write-Host "removed $Remove; index now has $(@($check).Count) entries, parses OK"
    exit 0
}

# Newest existing project of each mode, to copy the shape from.
$all = $raw | ConvertFrom-Json
$srcCombined = @($all | Where-Object { $_.mode -eq 3 } | Sort-Object { [datetime]$_.lastModified } -Descending)[0]
$srcUnique = @($all | Where-Object { $_.mode -eq 2 } | Sort-Object { [datetime]$_.lastModified } -Descending)[0]
if (-not $srcCombined -or -not $srcUnique) { throw 'need one project of each mode to copy from' }

$created = @()
foreach ($src in @($srcCombined, $srcUnique)) {
    $id = [guid]::NewGuid().ToString()
    $json = [System.IO.File]::ReadAllText((Join-Path $dir "$($src.id).json")) | ConvertFrom-Json
    # The project file has no id or name: both live in the index entry. Only the
    # structure has to be emptied here.
    $json.outputFolder = $null
    $json.books = @()

    # Written with the same escaping the app uses for this file; the empty fixture has no
    # Cyrillic in its data beyond the name, which is ASCII on purpose.
    $body = ($json | ConvertTo-Json -Depth 12)
    [System.IO.File]::WriteAllText((Join-Path $dir "$id.json"), $body, (New-Object System.Text.UTF8Encoding($false)))

    $entry = @"
  {
    "id": "$id",
    "name": "Empty-fx",
    "filePath": "$(($dir -replace '\\','\\'))\\$id.json",
    "bookCount": 0,
    "pageCount": 0,
    "status": 0,
    "lastModified": "$([DateTimeOffset]::Now.ToString('o'))",
    "mode": $($src.mode),
    "createdDate": "0001-01-01T00:00:00"
  }
"@
    $entries += ,($entry.TrimEnd("`r","`n") -split "`r?`n")
    $created += "$id (mode $($src.mode))"
}

$out = New-Object System.Collections.Generic.List[string]
$out.Add('[')
for ($i = 0; $i -lt $entries.Count; $i++) {
    foreach ($l in $entries[$i]) { $out.Add($l) }
    $last = $out[$out.Count - 1]
    $out[$out.Count - 1] = $last.TrimEnd(',') + $(if ($i -lt $entries.Count - 1) { ',' } else { '' })
}
$out.Add(']'); $out.Add('')
$newRaw = ($out -join "`n")
$check = $newRaw | ConvertFrom-Json
[System.IO.File]::WriteAllText($indexPath, $newRaw, (New-Object System.Text.UTF8Encoding($false)))

Write-Host "created: $($created -join ', ')"
Write-Host "index now has $(@($check).Count) entries, parses OK"
