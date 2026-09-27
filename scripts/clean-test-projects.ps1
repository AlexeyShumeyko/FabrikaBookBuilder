# Removes script-generated test projects from the index, and their files.
#
# The fixture scripts (make-test-fixture.ps1, make-combined-fixture.ps1) ADD a project every
# time they run, so a session that made twenty captures leaves twenty "TR-test" cards in the
# owner's project list. This is the cleanup that has to run afterwards, and it is a script
# rather than a habit because the index must be edited as RAW TEXT.
#
# Why raw text: projects.json stores every non-ASCII character as \uXXXX, so
# Get-Content | ConvertFrom-Json | ConvertTo-Json would rewrite the whole file, and the
# notes record that such a round-trip has mangled the Cyrillic before. Only whole entries
# are cut out here; everything else is left byte for byte.
#
# Usage: pwsh -File scripts\clean-test-projects.ps1 [-WhatIf] [-Names TR-test,'* (test)']

param(
    [string[]]$Names = @('TR-test', 'Testovyy*', 'Kombinirovannyy*'),
    [switch]$WhatIf
)

[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8

$ErrorActionPreference = 'Stop'

$indexPath = Join-Path $env:LOCALAPPDATA 'PhotoBookRenamer\Projects\projects.json'
if (-not (Test-Path $indexPath)) { throw "no index at $indexPath" }

$raw = [System.IO.File]::ReadAllText($indexPath)
$lines = $raw -split "`r?`n"

# Rebuild the array entry by entry, keeping each entry's own lines verbatim.
$entries = @()      # list of string[]
$current = $null
foreach ($line in $lines) {
    if ($line -match '^\s*\{\s*$') { $current = @($line); continue }
    if ($null -ne $current) {
        $current += $line
        if ($line -match '^\s*\},?\s*$') { $entries += ,$current; $current = $null }
    }
}

if ($entries.Count -eq 0) { throw 'no entries recognised in the index - refusing to rewrite it' }

$kept = @()
$dropped = @()
foreach ($e in $entries) {
    $text = $e -join "`n"
    $nameMatch = [regex]::Match($text, '"name"\s*:\s*"((?:[^"\\]|\\.)*)"')
    $name = if ($nameMatch.Success) { $nameMatch.Groups[1].Value } else { '' }
    $isTest = $false
    foreach ($pattern in $Names) {
        if ($name -eq $pattern.Trim()) { $isTest = $true; break }   # exact name first
        if ($name -like $pattern) { $isTest = $true; break }
    }
    if ($isTest) { $dropped += ,$e } else { $kept += ,$e }
}

Write-Host "index entries: $($entries.Count); removing $($dropped.Count) test project(s); keeping $($kept.Count)"

$out = New-Object System.Collections.Generic.List[string]
$out.Add('[')
for ($i = 0; $i -lt $kept.Count; $i++) {
    foreach ($l in $kept[$i]) { $out.Add($l) }
    # The comma is rebuilt, not inherited: dropping the last entry of the file leaves the
    # new last one carrying a comma that used to separate it from what followed, and a
    # trailing comma is a JSON syntax error rather than a cosmetic one.
    $last = $out[$out.Count - 1]
    $out[$out.Count - 1] = $last.TrimEnd(',') + $(if ($i -lt $kept.Count - 1) { ',' } else { '' })
}
$out.Add(']')
$out.Add('')
$newRaw = ($out -join "`n")

# Sanity: the result must still parse and must still be a JSON array.
$check = $newRaw | ConvertFrom-Json
if (-not $check) { throw 'the rewritten index does not parse' }

if ($WhatIf) {
    Write-Host '-WhatIf: nothing written'
    foreach ($e in $dropped) {
        $t = $e -join "`n"
        $id = [regex]::Match($t, '"id"\s*:\s*"([^"]+)"').Groups[1].Value
        $nm = [regex]::Match($t, '"name"\s*:\s*"((?:[^"\\]|\\.)*)"').Groups[1].Value
        Write-Host "  would remove $id ($nm)"
    }
    exit 0
}

[System.IO.File]::WriteAllText($indexPath, $newRaw, (New-Object System.Text.UTF8Encoding($false)))

foreach ($e in $dropped) {
    $t = $e -join "`n"
    $id = [regex]::Match($t, '"id"\s*:\s*"([^"]+)"').Groups[1].Value
    $nm = [regex]::Match($t, '"name"\s*:\s*"((?:[^"\\]|\\.)*)"').Groups[1].Value
    $file = Join-Path (Split-Path -Parent $indexPath) "$id.json"
    if (Test-Path $file) { Remove-Item $file -Force }
    Write-Host "  removed $id ($nm)"
}

Write-Host "index now has $(@($check).Count) entries"
