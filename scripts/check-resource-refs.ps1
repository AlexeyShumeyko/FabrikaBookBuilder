# Fails if any XAML file references a {StaticResource Key} that no dictionary defines.
#
# A missing StaticResource is a RUNTIME failure: `dotnet build` stays green, and the app
# throws "Cannot find resource named 'X'" while a view loads, i.e. it dies before the
# window appears or as soon as that view is opened. This has happened repeatedly in
# this project, so every reference is checked mechanically.
#
# Keys are resolved from Styles/*.xaml, which is what App.xaml merges application-wide.
# Locally declared keys inside a view (e.g. inside its own <Window.Resources>) are also
# collected, so a key that is locally scoped but used globally is reported correctly.
#
# Usage:  pwsh -File scripts\check-resource-refs.ps1

# The harness console runs on code page 866, which turns every Cyrillic string this
# script prints into "?" and floods the agent context with mojibake. Force UTF-8 on
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

function Get-ImplicitKeys([string]$path) {
    # A Style/Brush without x:Key is addressable by its TargetType name.
    $text = [System.IO.File]::ReadAllText($path, [System.Text.Encoding]::UTF8)
    $keys = @()
    foreach ($m in [regex]::Matches($text, '<(Style|SolidColorBrush|DropShadowEffect|FontFamily|Thickness|Brush)\b[^>]*>')) {
        $tag = $m.Value
        if ($tag -match 'x:Key\s*=') { continue }
        if ($tag -match 'TargetType\s*=\s*"\{?x:Type\s+([A-Za-z0-9_.]+)\}?"') { $keys += $Matches[1] }
        elseif ($tag -match 'TargetType\s*=\s*"([A-Za-z0-9_.]+)"') { $keys += $Matches[1] }
    }
    return $keys
}

# --- keys available application-wide ----------------------------------------
$defined = New-Object System.Collections.Generic.HashSet[string]
foreach ($f in Get-ChildItem (Join-Path $root 'Styles') -Filter '*.xaml' -File) {
    foreach ($k in Get-DefinedKeys $f.FullName) { [void]$defined.Add($k) }
    foreach ($k in Get-ImplicitKeys $f.FullName) { [void]$defined.Add($k) }
}

# --- every XAML in the project ----------------------------------------------
$files = Get-ChildItem $root -Recurse -Include '*.xaml' -File |
    Where-Object { $_.FullName -notmatch '\\(obj|bin)\\' }

$problems = 0

foreach ($file in $files) {
    $text = [System.IO.File]::ReadAllText($file.FullName, [System.Text.Encoding]::UTF8)

    # Keys declared locally in this very file are legal.
    $local = New-Object System.Collections.Generic.HashSet[string]
    foreach ($k in Get-DefinedKeys $file.FullName) { [void]$local.Add($k) }

    $used = [regex]::Matches($text, '\{StaticResource\s+([A-Za-z0-9_.]+)\s*\}') |
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
Write-Host ("checked {0} xaml file(s) against {1} application-wide key(s)" -f $files.Count, $defined.Count)

if ($problems -gt 0) {
    Write-Host "FAIL: $problems file(s) reference undefined StaticResource keys."
    Write-Host 'The app will throw at runtime where those views load.'
    exit 1
}

Write-Host 'PASS: every StaticResource reference resolves.'
