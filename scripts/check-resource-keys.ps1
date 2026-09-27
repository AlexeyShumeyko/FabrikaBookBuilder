# Fails the build if any ResourceDictionary declares the same x:Key twice.
#
# Duplicate keys in one ResourceDictionary are a RUNTIME failure, not a compile error:
# the app throws XamlParseException ("Item has already been added") while loading the
# dictionary and dies before any window appears, with `dotnet build` still green.
# That has bitten this project twice already, so it is checked mechanically.
#
# Usage:  pwsh -File scripts\check-resource-keys.ps1
# Add to CI: after `dotnet build`.

# The harness console runs on code page 866, which turns every Cyrillic string this
# script prints into "?" and floods the agent context with mojibake. Force UTF-8 on
# both channels; a child powershell.exe resets these on its own, so it has to be set
# inside each script rather than once in the caller.
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$styleDir = Join-Path $root 'Styles'

$dictionaries = Get-ChildItem -Path $styleDir -Filter '*.xaml' -File
if (-not $dictionaries) { Write-Host 'no resource dictionaries found'; exit 0 }

$problems = 0

foreach ($file in $dictionaries) {
    $text = [System.IO.File]::ReadAllText($file.FullName, [System.Text.Encoding]::UTF8)

    # Only top-level keys matter: a key is unique per dictionary, not per nesting level
    # for our purposes, since all of ours live at the root.
    $keys = [regex]::Matches($text, 'x:Key\s*=\s*"([^"]+)"') |
        ForEach-Object { $_.Groups[1].Value }

    $dupes = $keys | Group-Object | Where-Object { $_.Count -gt 1 }

    if ($dupes) {
        $problems++
        foreach ($d in $dupes) {
            Write-Host ("DUPLICATE  {0}  x:Key='{1}'" -f $file.Name, $d.Name)
        }
    }
    else {
        Write-Host ("ok         {0}  ({1} keys)" -f $file.Name, $keys.Count)
    }
}

if ($problems -gt 0) {
    Write-Host ''
    Write-Host "FAIL: $problems dictionary/dictionaries with duplicate x:Key."
    Write-Host 'The app will crash at startup with XamlParseException.'
    exit 1
}

Write-Host ''
Write-Host 'PASS: no duplicate x:Key in any resource dictionary.'
