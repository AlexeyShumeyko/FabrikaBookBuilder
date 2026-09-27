# Repairs projects.json if a previous run of make-test-fixture.ps1 wrapped the array in
# PowerShell's {"value": [...], "Count": N} envelope.
#
# Symptom: the app shows no projects, or blank ones, and projects.json contains
# "value"/"Count" keys. The app's own serializer (System.Text.Json) never writes those,
# so their presence means something else mangled the file.
#
# Works on the raw text with a brace-depth scan rather than ConvertFrom-Json /
# ConvertTo-Json: those are exactly what produced the mess, and round-tripping through
# them can produce it again.
#
# Always leaves a .corrupt-backup next to the original before touching it.
#
# Usage:  pwsh -File scripts\repair-projects-json.ps1 [-WhatIf]

# The harness console runs on code page 866, which turns every Cyrillic string this
# script prints into "?" and floods the agent context with mojibake. Force UTF-8 on
# both channels; a child powershell.exe resets these on its own, so it has to be set
# inside each script rather than once in the caller.
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8

param([switch]$WhatIf)

$ErrorActionPreference = 'Stop'
$dir = Join-Path $env:LOCALAPPDATA 'PhotoBookRenamer\Projects'
$indexPath = Join-Path $dir 'projects.json'

if (-not (Test-Path $indexPath)) { Write-Host "nothing to repair: $indexPath not found"; exit 0 }

$raw = [System.IO.File]::ReadAllText($indexPath, [System.Text.Encoding]::UTF8)

if ($raw -notmatch '"value"\s*:') {
    Write-Host 'projects.json looks clean (no PowerShell value/Count envelope). Nothing to do.'
    exit 0
}

# --- brace-depth scan, string-aware -------------------------------------------
# A stack (not a single depth counter) is required: the PowerShell envelope nests the
# real entries several levels deep, so every '{' has to be a candidate, not just the
# outermost one.
$blocks = New-Object System.Collections.ArrayList
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
        $start = [int]$stack.Pop()
        $block = $raw.Substring($start, $i - $start + 1)

        # A ProjectInfo entry has id/name/filePath/bookCount/pageCount/status/mode and
        # must NOT be a PowerShell array wrapper, which is identifiable by its own
        # "value" key. Rejecting wrappers is what stops the envelope from being captured
        # along with the real entries nested inside it.
        $isWrapper = $block -match '"value"\s*:'
        $hasFields = $block -match '"filePath"' -and $block -match '"mode"' -and $block -match '"id"'
        if ($hasFields -and -not $isWrapper) {
            [void]$blocks.Add($block)
        }
    }
}

Write-Host "recovered $($blocks.Count) project entr(ies)"

# --- de-duplicate by id, keeping the last occurrence -------------------------
$byId = [ordered]@{}
foreach ($b in $blocks) {
    $id = ([regex]::Match($b, '"id"\s*:\s*"([^"]*)"')).Groups[1].Value
    if ($id) { $byId[$id] = $b }
}

$rebuilt = "[" + [Environment]::NewLine +
           (($byId.Values -join (',' + [Environment]::NewLine))) +
           [Environment]::NewLine + "]"

Write-Host '--- rebuilt projects.json ---'
Write-Host $rebuilt

if ($WhatIf) { Write-Host '-WhatIf: not writing.'; exit 0 }

Copy-Item $indexPath "$indexPath.corrupt-backup" -Force
[System.IO.File]::WriteAllText($indexPath, $rebuilt, (New-Object System.Text.UTF8Encoding($false)))
Write-Host "repaired. Original saved as projects.json.corrupt-backup"
