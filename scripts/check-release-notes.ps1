# The release notes are shown to the user in the update dialog, so they are a user-facing
# text, not a developer log. Two rules follow from that, and this checks both:
#
#   1. The notes must not contain links, @mentions or repository references. Clients of the
#      program do not need the address of the developer's GitHub, and showing it there was
#      called a mistake by the author - correctly.
#   2. The notes must only use markdown the dialog can render. A link, an image, a table or a
#      quote would reach the screen as raw text, asterisks included. Headings, bold, inline
#      code and bullets are fine: they are rendered.
#
# It checks the source files, and with -Body the body of a published release.
#
# Usage:
#   pwsh -File scripts\check-release-notes.ps1                     # every file in docs/release-notes
#   pwsh -File scripts\check-release-notes.ps1 -Body .\body.md    # one body
#   pwsh -File scripts\check-release-notes.ps1 -Tag v1.1.0         # the published release
#
# NOTE: this file contains Cyrillic and must stay UTF-8 **with BOM**.

param(
    [string]$Body = '',
    [string]$Tag = '',
    [string]$Source = 'docs\release-notes',
    [string]$Repository = 'AlexeyShumeyko/FabrikaBookBuilder'
)

[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root

# The rules are re-implemented here rather than called from the app: a check that runs the
# code it is checking only proves that the code returns something.
function Get-VisibleText([string]$markdown) {
    $noise = @("what's changed", 'new contributors', 'full changelog')
    $kept = New-Object System.Collections.Generic.List[string]
    $stop = $false

    foreach ($raw in ($markdown -replace "`r`n", "`n") -split "`n") {
        $line = $raw.TrimEnd()
        $probe = ($line -replace '^[#*\s]+', '' -replace '\*\*', '').Trim().ToLowerInvariant()

        if ($noise | Where-Object { $probe.StartsWith($_) }) { $stop = $true; continue }
        if ($stop) { continue }

        # A generated bullet: "* something by @someone in https://github.com/..."
        if ($line.TrimStart().StartsWith('* ') -and $line.Contains('@') -and $line.Contains('http')) { continue }

        $line = $line -replace '@[A-Za-z0-9][\w-]*', ''
        $line = ($line -replace 'https?://\S+', '').TrimEnd()
        if ($line.Trim().Length -eq 0) { continue }
        $kept.Add($line)
    }

    return $kept
}

function Test-Notes([string]$text) {
    $problems = New-Object System.Collections.Generic.List[string]
    $kept = Get-VisibleText $text
    $visible = $kept -join "`n"

    if ($visible -match 'github\.com|githubusercontent') {
        $problems.Add('a link to the repository survives into the dialog')
    }
    if ($visible -match '@[A-Za-z0-9]') {
        $problems.Add('an @mention survives into the dialog')
    }
    if ($visible -match "what's changed|new contributors|full changelog") {
        $problems.Add("GitHub's generated block is not cut off")
    }

    # Markdown the dialog does NOT render, so it would be shown as typed.
    $unsupported = [ordered]@{
        '\[[^\]]+\]\([^)]+\)'   = 'markdown link'
        '!\['                   = 'image'
        '^\s*\|'                = 'table row'
        '^\s*>'                 = 'quote'
        '^\s*(---+|\*\*\*+)\s*$' = 'horizontal rule'
    }
    foreach ($pattern in $unsupported.Keys) {
        $hit = $kept | Where-Object { $_ -match $pattern } | Select-Object -First 1
        if ($hit) { $problems.Add("$($unsupported[$pattern]) would be shown as raw text: $hit") }
    }

    if ($kept.Count -eq 0) { $problems.Add('nothing readable is left after cleaning') }

    return $problems
}

$failures = 0

# --- the source files -------------------------------------------------------------------
if (-not $Body -and -not $Tag) {
    if (-not (Test-Path $Source)) {
        Write-Host "FAIL: no notes directory '$Source'"
        Pop-Location
        exit 1
    }

    $files = @(Get-ChildItem $Source -Filter 'v*.md' -File | Sort-Object Name)
    if ($files.Count -eq 0) {
        Write-Host "FAIL: no release notes in '$Source' - every release needs its notes file"
        Pop-Location
        exit 1
    }

    foreach ($file in $files) {
        $text = [System.IO.File]::ReadAllText($file.FullName, [System.Text.Encoding]::UTF8)
        $problems = Test-Notes $text

        if ($problems.Count -eq 0) {
            Write-Host "PASS  $($file.Name)"
        }
        else {
            $failures++
            Write-Host "FAIL  $($file.Name)"
            foreach ($p in $problems) { Write-Host "        $p" }
        }
    }

    # The pipeline must not append GitHub's own block to the body. The dialog would clean it,
    # but the release page would show the author's account and commit links to every visitor.
    $workflow = '.github\workflows\build-and-release.yml'
    if (Test-Path $workflow) {
        if ((Get-Content $workflow -Raw) -match 'generate_release_notes:\s*true') {
            $failures++
            Write-Host 'FAIL  build-and-release.yml'
            Write-Host '        generate_release_notes: true appends GitHub''s "What''s Changed" block,'
            Write-Host '        with commit links and the author''s account, to every release body.'
        }
        else {
            Write-Host 'PASS  build-and-release.yml (generate_release_notes is off)'
        }
    }

    Write-Host ''
    if ($failures -eq 0) {
        Write-Host "PASS: $($files.Count) release note files carry no links and no raw markdown"
    }
    else {
        Write-Host "FAIL: $failures problem(s) in the release notes"
    }

    Pop-Location
    exit ([int]($failures -gt 0))
}

# --- one body: a file or a published release --------------------------------------------
if (-not $Body) {
    $tmp = Join-Path $env:TEMP "release-body-$Tag.md"
    $headers = @{ 'User-Agent' = 'check'; 'Accept' = 'application/vnd.github+json' }
    $release = Invoke-RestMethod -Uri "https://api.github.com/repos/$Repository/releases/tags/$Tag" -Headers $headers -TimeoutSec 30
    [System.IO.File]::WriteAllText($tmp, $release.body, (New-Object System.Text.UTF8Encoding($false)))
    $Body = $tmp
}

$text = [System.IO.File]::ReadAllText($Body, [System.Text.Encoding]::UTF8)
$kept = Get-VisibleText $text
$problems = Test-Notes $text

Write-Host "body: $Body ($($text.Length) chars)"
Write-Host "строк, которые увидит пользователь: $($kept.Count) из $(@($text -split "`n").Count)"
Write-Host ''
($kept | Select-Object -First 6) | ForEach-Object { Write-Host "  $_" }
Write-Host '  ...'
Write-Host ''

if ($problems.Count -eq 0) {
    Write-Host 'PASS: the dialog text carries no links, no account name and no raw markdown'
    Pop-Location
    exit 0
}

foreach ($p in $problems) { Write-Host "FAIL: $p" }
Pop-Location
exit 1