# Removes everything scripts/make-test-fixture.ps1 created.
#
# Usage:  pwsh -File scripts\remove-test-fixture.ps1 [-KeepProjects]

param([switch]$KeepProjects)

$ErrorActionPreference = 'Stop'

$fixture = Join-Path $env:TEMP 'FBR-TestFixture'
$projectsDir = Join-Path $env:LOCALAPPDATA 'PhotoBookRenamer\Projects'

if (Test-Path $fixture) {
    Remove-Item $fixture -Recurse -Force
    Write-Host "removed fixture folder: $fixture"
}

if (-not $KeepProjects) {
    $indexPath = Join-Path $projectsDir 'projects.json'
    if (Test-Path $indexPath) {
        $list = @()
        try { $list = @(Get-Content $indexPath -Raw -Encoding UTF8 | ConvertFrom-Json) } catch { $list = @() }

        $testEntries = @($list | Where-Object { $_.name -like '*тест*' })
        if ($testEntries.Count -gt 0) {
            foreach ($e in $testEntries) {
                if ($e.filePath -and (Test-Path $e.filePath)) {
                    Remove-Item $e.filePath -Force -ErrorAction SilentlyContinue
                }
            }
            $kept = @($list | Where-Object { $_.name -notlike '*тест*' })
            if ($kept.Count -gt 0) { $kept | ConvertTo-Json -Depth 6 | Set-Content $indexPath -Encoding UTF8 }
            else { Remove-Item $indexPath -Force }
            Write-Host "removed $($testEntries.Count) test project(s) from the index"
        }
        else {
            Write-Host 'no test projects found in the index'
        }
    }
}
