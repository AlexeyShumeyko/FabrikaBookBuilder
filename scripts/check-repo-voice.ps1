# The repository is a portfolio piece. It is written in the voice of the product, and it
# carries no trace of how it was produced or of who reviewed it.
#
# This gate exists because saying so is not enough. The rule was restated several times and
# broken anyway, which is the definition of a rule that needs a machine behind it: a reviewer
# reading a diff will not catch a word in a comment, and a future contributor will not know.
#
# Two things are checked, and both fail the build:
#
#   1. every tracked file, for the words themselves - text, comments, file names;
#   2. every commit message added since this branch left master, because a message is as
#      visible on the repository page as a file is.
#
# Messages already reachable from master are grandfathered. They are released history and are
# not this gate's to rewrite - that is a decision for the person who owns the repository.
#
# Usage:  pwsh -File scripts\check-repo-voice.ps1
# Exit 1 on any hit.

[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

# Matched case-insensitively but only as whole words, so that a legitimate word containing
# the letters is not caught: "ИИ" inside "файловый", "AI" inside a base64 blob, "Cursor" the
# WPF property that sets the shape of the mouse pointer.
$words = @(
    'агент\w*', 'ИИ', 'ChatGPT', 'GPT\w*', 'нейросет\w*', 'LLM', 'ассистент\w*', 'промпт\w*',
    '(?<![A-Za-z])AI(?![A-Za-z])', '(?<![A-Za-z])owners?(?![A-Za-z])',
    '(?<![A-Za-z])agents?(?![A-Za-z])', '(?<![A-Za-z])assistant(?![A-Za-z])'
)
$pattern = '^(?:' + ($words -join '|') + ')$'

# In files, a hit is the word appearing anywhere as a whole word, except inside the XAML
# property Cursor="..." which is the mouse pointer and has nothing to do with anything.
$filePattern = '(?<![А-Яа-яA-Za-z_])(?:' + ($words -join '|') + ')(?![А-Яа-яA-Za-z_])'

$problems = New-Object System.Collections.Generic.List[string]

Write-Host ''
Write-Host '=== 1. Отслеживаемые файлы ========================================='

$tracked = @(git ls-files)
$fileHits = 0
foreach ($f in $tracked) {
    if (-not (Test-Path $f)) { continue }
    if ($f -notmatch '\.(md|cs|xaml|ps1|yml|yaml|json|props|txt|csproj|sln|editorconfig|gitignore)$') { continue }
    if ($f -eq 'scripts\check-repo-voice.ps1') { continue }   # this file names them on purpose

    try { $text = [System.IO.File]::ReadAllText($f, [System.Text.Encoding]::UTF8) } catch { continue }
    if (-not $text) { continue }

    $lines = $text -split "`r?`n"
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $line = $lines[$i]
        # Only comments are prose. Window.Owner, Octokit's owner parameter and the HTTP
        # User-Agent header are names that come from the platform: renaming them to satisfy a
        # writing rule would break working code, and that is the wrong trade in either
        # direction. Code is not the place where a person is named; comments are.
        if ($line -notmatch '^\s*(//|///|<!--|#)') { continue }
        if ($line -match '\bCursor\s*=') { continue }
        foreach ($m in [regex]::Matches($line, $filePattern, 'IgnoreCase')) {
            $problems.Add("файл  ${f}:$($i + 1)  «$($m.Value.Trim())»")
            $fileHits++
        }
    }
}
if ($fileHits -eq 0) { Write-Host '  чисто' } else { Write-Host "  нарушений: $fileHits" }

Write-Host ''
Write-Host '=== 2. Сообщения коммитов после расхождения с master ================'

$base = git merge-base HEAD master 2>$null
if (-not $base) {
    Write-Host '  master недостижим — проверка сообщений пропущена'
} else {
    $shas = @(git log --format='%H' "$base..HEAD")
    $msgHits = 0
    foreach ($sha in $shas) {
        $msg = git log -1 --format='%B' $sha 2>$null
        foreach ($line in ($msg -split "`r?`n")) {
            if ($line -match $filePattern) {
                $short = ($line -replace $filePattern, '«$0»').Trim()
                $problems.Add("сообщение  $($sha.Substring(0, 8))  «$short»")
                $msgHits++
            }
        }
    }
    if ($msgHits -eq 0) {
        Write-Host ("  чисто, проверено коммитов: {0}" -f $shas.Count)
    } else {
        Write-Host "  нарушений: $msgHits из $($shas.Count) коммитов"
    }
}

Write-Host ''
Write-Host '=================================================================='
if ($problems.Count -eq 0) {
    Write-Host 'PASS: репозиторий написан от лица продукта.'
    exit 0
}

Write-Host "FAIL: найдено упоминаний того, как велась работа: $($problems.Count)"
foreach ($p in ($problems | Select-Object -First 40)) { Write-Host ("  " + $p) }
if ($problems.Count -gt 40) { Write-Host ("  ... и ещё " + ($problems.Count - 40)) }
Write-Host ''
Write-Host 'Текст, имена файлов и сообщения коммитов пишутся от лица продукта.'
exit 1