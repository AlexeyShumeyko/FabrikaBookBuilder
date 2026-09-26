# Removes everything scripts/make-test-fixture.ps1 created.
#
# Like the fixture script, this edits projects.json as text. A ConvertTo-Json round-trip
# previously corrupted the index; see scripts/repair-projects-json.ps1.
#
# Usage:  pwsh -File scripts\remove-test-fixture.ps1

# The harness console runs on code page 866, which turns every Cyrillic string this
# script prints into "?" and floods the agent context with mojibake. Force UTF-8 on
# both channels; a child powershell.exe resets these on its own, so it has to be set
# inside each script rather than once in the caller.
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8

$ErrorActionPreference = 'Stop'

$fixture = Join-Path $env:TEMP 'FBR-TestFixture'
$projectsDir = Join-Path $env:LOCALAPPDATA 'PhotoBookRenamer\Projects'
$indexPath = Join-Path $projectsDir 'projects.json'

if (Test-Path $fixture) {
    Remove-Item $fixture -Recurse -Force
    Write-Host "removed fixture folder: $fixture"
}

if (-not (Test-Path $indexPath)) { Write-Host 'no projects.json'; exit 0 }

$raw = [System.IO.File]::ReadAllText($indexPath, [System.Text.Encoding]::UTF8)
$nl = [Environment]::NewLine

# Extract top-level-ish entry blocks as raw text, dropping the ones marked as test data.
$kept = New-Object System.Collections.ArrayList
$removed = 0
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
            if ($block -match '\(test\)') {
                $removed++
                # Delete the project file it pointed at, if it is still there.
                $fp = ([regex]::Match($block, '"filePath"\s*:\s*"((?:[^"\\]|\\.)*)"')).Groups[1].Value
                if ($fp) {
                    $decoded = $fp -replace '\\', ([char]92) -replace '"', ''
                    if (Test-Path $decoded) { Remove-Item $decoded -Force -ErrorAction SilentlyContinue }
                }
            }
            else {
                [void]$kept.Add(($block -replace '\s+', ' '))
            }
        }
    }
}

if ($removed -eq 0) {
    Write-Host 'no test projects found'
    exit 0
}

if ($kept.Count -gt 0) {
    $text = '[' + $nl + ($kept -join (',' + $nl)) + $nl + ']'
}
else {
    $text = '[]'
}
[System.IO.File]::WriteAllText($indexPath, $text, (New-Object System.Text.UTF8Encoding($false)))
Write-Host "removed $removed test project(s); $($kept.Count) left in the index"
