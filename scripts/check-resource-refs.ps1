# Fails if any markup file references a resource key that no dictionary defines.
#
# A missing resource is a RUNTIME failure: `dotnet build` stays green, and the app throws
# while a view loads, i.e. it dies before the window appears or as soon as that view is
# opened. This has happened repeatedly in this project, so every reference is checked
# mechanically.
#
# Application-wide keys are the ones declared in App.axaml and in the style dictionaries it
# includes. Keys declared locally in a view are legal there and are collected separately,
# so a key that is local to one view but used in another is still reported.
#
# Usage:  pwsh -File scripts\check-resource-refs.ps1

# The harness console runs on code page 866, which turns every Cyrillic string this
# script prints into "?" and floods the console with mojibake. Force UTF-8 on
# both channels; a child powershell.exe resets these on its own, so it has to be set
# inside each script rather than once in the caller.
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

function Get-DefinedKeys([string]$path) {
    $text = [System.IO.File]::ReadAllText($path, [System.Text.Encoding]::UTF8)
    return @([regex]::Matches($text, 'x:Key\s*=\s*"([^"]+)"') | ForEach-Object { $_.Groups[1].Value })
}

# --- keys available application-wide ----------------------------------------
$defined = New-Object System.Collections.Generic.HashSet[string]
$shellDir = Join-Path $root 'src\PhotoBook.Desktop.Avalonia'
if (-not (Test-Path $shellDir)) { throw "no shell project at $shellDir" }

# App.axaml is the root dictionary; the style dictionaries it includes are part of it.
$appMarkup = Join-Path $shellDir 'App.axaml'
if (-not (Test-Path $appMarkup)) { throw "no application markup at $appMarkup" }
foreach ($k in Get-DefinedKeys $appMarkup) { [void]$defined.Add($k) }

$styleDir = Join-Path $shellDir 'Styles'
if (-not (Test-Path $styleDir)) { throw "no style dictionary directory at $styleDir" }
foreach ($f in Get-ChildItem $styleDir -Recurse -File -Include '*.xaml', '*.axaml') {
    foreach ($k in Get-DefinedKeys $f.FullName) { [void]$defined.Add($k) }
}

# --- every markup file in the project ---------------------------------------
$files = Get-ChildItem $shellDir -Recurse -File -Include '*.xaml', '*.axaml' |
    Where-Object { $_.FullName -notmatch '\\(obj|bin)\\' }

$problems = 0

foreach ($file in $files) {
    $text = [System.IO.File]::ReadAllText($file.FullName, [System.Text.Encoding]::UTF8)

    # Keys declared locally in this very file are legal.
    $local = New-Object System.Collections.Generic.HashSet[string]
    foreach ($k in Get-DefinedKeys $file.FullName) { [void]$local.Add($k) }

    # Avalonia resolves both forms; a key used only through DynamicResource is just as
    # capable of stopping the program.
    $used = [regex]::Matches($text, '\{(?:StaticResource|DynamicResource)\s+([A-Za-z0-9_.]+)\s*\}') |
        ForEach-Object { $_.Groups[1].Value } | Select-Object -Unique

    $missing = @()
    foreach ($u in $used) {
        if ($defined.Contains($u) -or $local.Contains($u)) { continue }
        $missing += $u
    }

    if ($missing.Count) {
        $problems++
        $rel = $file.FullName.Substring($root.Length + 1)
        foreach ($m in $missing) { Write-Host ("MISSING    {0}  -> {{{{{0}}}}}" -f $rel, $m) }
    }
}

Write-Host ''
Write-Host ("checked {0} markup file(s) against {1} application-wide key(s)" -f $files.Count, $defined.Count)

if ($problems -gt 0) {
    Write-Host "FAIL: $problems file(s) reference undefined resource keys."
    Write-Host 'The app will throw at runtime where those views load.'
    exit 1
}

Write-Host 'PASS: every resource reference resolves.'
