# Checks that every `run:` block in the GitHub Actions workflows is valid PowerShell.
#
# Why this exists: a scripted text replacement once wrote a fragment of PowerShell string
# syntax into the middle of a publish command. The YAML still parsed - it is a legal
# literal block - so "the YAML is valid" proved nothing, and the release failed at that
# step with a message about publishing rather than about the file being wrong.
#
# So this extracts each run block the way the runner does, substitutes the GitHub
# expressions with a placeholder, and asks the PowerShell parser whether the result is
# valid. A broken block fails here instead of on the build server.
#
# Usage:  pwsh -File scripts\check-workflow-scripts.ps1
# NOTE: this file contains Cyrillic and must stay UTF-8 **with BOM**.

[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root

$files = @(Get-ChildItem .github\workflows -Filter *.yml)
if ($files.Count -eq 0) { throw 'no workflow files found' }

$failed = 0
$checked = 0

foreach ($file in $files) {
    $text = [System.IO.File]::ReadAllText($file.FullName)
    $lines = $text -split "`r?`n"

    $inRun = $false
    $indent = 0
    $buffer = New-Object System.Collections.Generic.List[string]
    $name = ''

    function Test-Block([string[]]$body, [string]$blockName, [string]$fileName, [int]$blockIndent) {
        # GitHub strips the block indentation of `run: |` before the shell sees the text.
        # Without doing the same here, a here-string terminator that is indented in the
        # YAML looks like a broken script and the check reports a fault that does not
        # exist - which is how a good check becomes one people stop running.
        # YAML strips the COMMON leading indentation of the block, which is the smallest
        # one among the non-empty lines - not the indentation of the `run: |` key itself.
        $width = ($body | Where-Object { $_.Trim() -ne '' } |
                  ForEach-Object { $_.Length - $_.TrimStart().Length } |
                  Measure-Object -Minimum).Minimum
        if (-not $width) { $width = 0 }
        $trimmed = @($body | ForEach-Object {
            if ($_.Length -ge $width) { $_.Substring($width) } else { $_.TrimStart() }
        })
        $script = ($trimmed -join "`n")

        # A syntax check cannot catch every mistake, and one class slipped through: a
        # fragment of scripting syntax that is still VALID PowerShell, so the parser
        # accepts it, while the command it builds is nonsense. `dotnet publish ...
        # /p:Version=1.1.0' + "` + a newline parses as a multi-line string and quietly
        # turns the version argument into rubbish. The release failed there.
        #
        # These shapes never occur in a hand-written workflow step, so their presence
        # means a scripted edit leaked part of itself into the file.
        $leaks = @(
            "' + `"",
            '" + `"',
            "@'",
            "'@",
            '$(''',
            '`$(@'
        )
        foreach ($leak in $leaks) {
            $hit = $trimmed | Where-Object { $_ -match [regex]::Escape($leak) }
            if ($hit) {
                Write-Host "  FAIL  $fileName -> '$blockName': looks like leaked edit syntax" -ForegroundColor Red
                foreach ($h in $hit | Select-Object -First 2) { Write-Host "        >>> $h" }
                return 1
            }
        }
        # GitHub substitutes these before the shell sees them. A placeholder keeps the
        # line syntactically valid; the point is to catch anything else that is broken.
        $script = $script -replace '\$\{\{[^}]*\}\}', 'XSUBSTX'
        $errors = $null
        $tokens = $null
        [void][System.Management.Automation.Language.Parser]::ParseInput($script, [ref]$tokens, [ref]$errors)
        if ($errors -and $errors.Count -gt 0) {
            Write-Host "  FAIL  $fileName -> '$blockName'" -ForegroundColor Red
            foreach ($e in $errors | Select-Object -First 3) {
                Write-Host "        line $($e.Extent.StartLineNumber): $($e.Message)"
                $text = $e.Extent.Text
                if ($text) { Write-Host "        >>> $text" }
            }
            return 1
        }
        return 0
    }

    for ($i = 0; $i -lt $lines.Count; $i++) {
        $line = $lines[$i]

        if (-not $inRun -and $line -match '^(\s*)run:\s*\|\s*$') {
            $inRun = $true
            $indent = $Matches[1].Length
            $buffer = New-Object System.Collections.Generic.List[string]
            # step name is the closest "name:" above
            for ($j = $i - 1; $j -ge 0; $j--) {
                if ($lines[$j] -match '^\s*-\s*name:\s*(.+)$') { $name = $Matches[1].Trim('"', ' '); break }
                if ($lines[$j] -match '^\s*-\s+[a-zA-Z]') { break }
            }
            continue
        }

        if ($inRun) {
            if ($line.Trim() -eq '') { $buffer.Add($line); continue }
            $leading = $line.Length - $line.TrimStart().Length
            if ($leading -le $indent) {
                $checked++
                $failed += (Test-Block $buffer.ToArray() $name $file.Name $indent)
                $inRun = $false
            }
            else {
                $buffer.Add($line)
            }
        }
    }

    if ($inRun -and $buffer.Count -gt 0) {
        $checked++
        $failed += (Test-Block $buffer.ToArray() $name $file.Name $indent)
    }
}

Pop-Location

if ($failed -gt 0) {
    Write-Host ""
    Write-Host "FAIL: $failed of $checked run blocks are not valid PowerShell" -ForegroundColor Red
    exit 1
}

Write-Host "PASS: all $checked run blocks in $($files.Count) workflow file(s) parse as PowerShell"
exit 0
