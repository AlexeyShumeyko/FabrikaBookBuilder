# Fails the build if any resource dictionary declares the same x:Key twice.
#
# Duplicate keys in one dictionary are a RUNTIME failure, not a compile error: the app
# throws while loading the dictionary and dies before any window appears, with
# `dotnet build` still green. That has happened in this project before, so it is checked
# mechanically.
#
# Since the palette became two theme dictionaries, the same key is declared once per theme
# on purpose - that is what makes one screen work in both. So a key is checked for
# uniqueness inside the dictionary that holds it, not across the whole file, and the two
# theme dictionaries of one file are required to hold the same names: a token that exists
# in the light theme and not in the dark one is a control that loses its colour at dusk,
# with nothing in the build to say so.
#
# Usage:  pwsh -File scripts\check-resource-keys.ps1
# Add to CI: after `dotnet build`.

# The harness console runs on code page 866, which turns every Cyrillic string this
# script prints into "?" and floods the console with mojibake. Force UTF-8 on
# both channels; a child powershell.exe resets these on its own, so it has to be set
# inside each script rather than once in the caller.
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$shellDir = Join-Path $root 'src\PhotoBook.Desktop.Avalonia'
if (-not (Test-Path $shellDir)) { throw "no shell project at $shellDir" }

# Every markup file in the shell can declare a key; the application-wide dictionaries and
# the views are checked together so a key cannot be duplicated across the boundary.
$dictionaries = Get-ChildItem $shellDir -Recurse -File -Include '*.xaml', '*.axaml' |
    Where-Object { $_.FullName -notmatch '\\(obj|bin)\\' }
if (-not $dictionaries) { Write-Host 'no resource dictionaries found'; exit 0 }

$problems = 0

# Where each key lives. The same key in two files does not crash - the dictionary merged
# last silently wins - which is exactly the kind of thing that makes a style look wrong
# with no error anywhere. Theme variants of one file count as one place here.
$owner = @{}

function Get-Key([System.Xml.XmlElement]$element) {
    $attribute = $element.Attributes | Where-Object { $_.LocalName -eq 'Key' } | Select-Object -First 1
    if ($null -eq $attribute) { return $null }
    return $attribute.Value
}

function Get-Scopes([string]$text) {
    # One scope per dictionary that owns keys: the root, plus one per theme dictionary.
    # A file that will not parse as XML falls back to a single scope of every key, which
    # is the older and stricter reading.
    try {
        $doc = New-Object System.Xml.XmlDocument
        $doc.LoadXml($text)
    }
    catch {
        $keys = [regex]::Matches($text, 'x:Key\s*=\s*"([^"]+)"') | ForEach-Object { $_.Groups[1].Value }
        return @([pscustomobject]@{ Name = ''; Keys = @($keys) })
    }

    $element = $doc.DocumentElement
    if ($null -eq $element -or $element.LocalName -ne 'ResourceDictionary') {
        $keys = [regex]::Matches($text, 'x:Key\s*=\s*"([^"]+)"') | ForEach-Object { $_.Groups[1].Value }
        return @([pscustomobject]@{ Name = $element.LocalName; Keys = @($keys) })
    }

    $scopes = @()
    $rootKeys = @()

    foreach ($child in $element.ChildNodes) {
        if ($child -isnot [System.Xml.XmlElement]) { continue }

        # A property element keeps its parent in the name: ResourceDictionary.ThemeDictionaries.
        if ($child.LocalName -eq 'ThemeDictionaries' -or $child.LocalName -like '*.ThemeDictionaries') {
            foreach ($theme in $child.ChildNodes) {
                if ($theme -isnot [System.Xml.XmlElement]) { continue }

                $themeKeys = @()
                foreach ($entry in $theme.ChildNodes) {
                    if ($entry -isnot [System.Xml.XmlElement]) { continue }
                    $key = Get-Key $entry
                    if ($key) { $themeKeys += $key }
                }

                $scopes += [pscustomobject]@{ Name = 'theme:' + (Get-Key $theme); Keys = $themeKeys }
            }
        }
        else {
            $key = Get-Key $child
            if ($key) { $rootKeys += $key }
        }
    }

    $scopes = @([pscustomobject]@{ Name = 'root'; Keys = $rootKeys }) + $scopes
    return $scopes
}

foreach ($file in $dictionaries) {
    $text = [System.IO.File]::ReadAllText($file.FullName, [System.Text.Encoding]::UTF8)
    $scopes = Get-Scopes $text

    $fileProblems = 0
    $allKeys = @()

    foreach ($scope in $scopes) {
        $dupes = $scope.Keys | Group-Object | Where-Object { $_.Count -gt 1 }
        foreach ($d in $dupes) {
            Write-Host ("DUPLICATE  {0}  [{1}]  x:Key='{2}'" -f $file.Name, $scope.Name, $d.Name)
            $fileProblems++
        }
        $allKeys += $scope.Keys
    }

    # The theme dictionaries of one file must carry the same names. A missing token is not
    # a crash: the control simply keeps a colour from the other theme, and only on one
    # theme, which is the hardest kind of difference to notice in review.
    $themes = @($scopes | Where-Object { $_.Name -like 'theme:*' })
    if ($themes.Count -gt 1) {
        $expected = @($themes[0].Keys | Sort-Object -Unique)
        foreach ($theme in $themes) {
            $actual = @($theme.Keys | Sort-Object -Unique)
            $missing = @($expected | Where-Object { $actual -notcontains $_ })
            $extra = @($actual | Where-Object { $expected -notcontains $_ })
            foreach ($m in $missing) {
                Write-Host ("THEME-GAP  {0}  [{1}]  missing x:Key='{2}'" -f $file.Name, $theme.Name, $m)
                $fileProblems++
            }
            foreach ($e in $extra) {
                Write-Host ("THEME-GAP  {0}  [{1}]  only here: x:Key='{2}'" -f $file.Name, $theme.Name, $e)
                $fileProblems++
            }
        }
    }

    if ($fileProblems -gt 0) { $problems += $fileProblems }
    else { Write-Host ("ok         {0}  ({1} keys)" -f $file.Name, @($allKeys | Sort-Object -Unique).Count) }

    foreach ($k in ($allKeys | Select-Object -Unique)) {
        if ($owner.ContainsKey($k)) { $owner[$k] += ', ' + $file.Name }
        else { $owner[$k] = $file.Name }
    }
}

$shadowed = @($owner.Keys | Where-Object { $owner[$_].Contains(', ') })
foreach ($k in ($shadowed | Sort-Object)) {
    Write-Host ("SHADOWED   x:Key='{0}' defined in {1} - the last one merged wins" -f $k, $owner[$k])
}
if ($shadowed.Count -gt 0) { $problems++ }

if ($problems -gt 0) {
    Write-Host ''
    Write-Host "FAIL: $problems problem(s) with x:Key."
    Write-Host 'A duplicate inside one dictionary crashes at startup with XamlParseException;'
    Write-Host 'the same key in two dictionaries does not crash, it silently shadows.'
    exit 1
}

Write-Host ''
Write-Host 'PASS: no x:Key is declared twice in one dictionary, and every theme carries the same names.'
