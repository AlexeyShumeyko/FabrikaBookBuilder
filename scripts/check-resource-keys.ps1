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
# script prints into "?" and floods the console with mojibake. Force UTF-8 on
# both channels; a child powershell.exe resets these on its own, so it has to be set
# inside each script rather than once in the caller.
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
# Was Join-Path $root 'Styles', which does not exist - the dictionaries live under the
# WPF project - so Get-ChildItem threw and this check had never actually run.
$styleDir = Join-Path $root 'src\PhotoBook.Desktop.Wpf\Styles'
if (-not (Test-Path $styleDir)) { throw "no style dictionary directory at $styleDir" }

$dictionaries = Get-ChildItem -Path $styleDir -Filter '*.xaml' -File
if (-not $dictionaries) { Write-Host 'no resource dictionaries found'; exit 0 }

$problems = 0

# Where each key lives. Since Theme.xaml was split by concern, the same key can now end up
# in two files: that is not a crash, but the dictionary merged last silently wins, which
# is exactly the kind of thing that makes a style look wrong with no error anywhere.
$owner = @{}

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

    foreach ($k in ($keys | Select-Object -Unique)) {
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
Write-Host 'PASS: no x:Key is declared twice, in one dictionary or across several.'
