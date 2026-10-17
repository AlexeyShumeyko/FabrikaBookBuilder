<#
.SYNOPSIS
    The three control panels are the same height.

.DESCRIPTION
    The panel on the projects screen, the panel on the folders screen and the panel on the
    combined screen are one structure, and this is what holds them to it.

    Why it needs a measurement
    --------------------------
    All three Borders carry Padding="20" and that has never been the thing that made them
    differ. A panel is as tall as its tallest part, and the three hold different parts - a
    heading on one, a 48-pixel tile on another - so identical padding gave three different
    heights. Measured off the screen before this gate existed: 86, 89 and 91.

    What it photographs
    -------------------
    The shell's own --open switch, which is the only way to reach the two editor screens
    without driving the interface. Nothing here presses a card or walks the automation
    tree, so the photographs are of the program drawing, not of the program answering
    questions.

    How it reads the height
    -----------------------
    measure-panel-pixels.ps1 does that, and the reason is in that file: the window and the
    panel are both #FFFFFF, so there is no fill to find, only a border - and a Border puts
    no node in the automation tree at all, so there is nothing to ask either.
#>
[CmdletBinding()]
param(
    [string]$Shots = '',
    [switch]$KeepShots
)

$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\common.ps1"

$root = Split-Path -Parent $PSScriptRoot
if (-not $Shots) { $Shots = 'doc\shots' }

if (-not $KeepShots -and -not (Test-Path $Shots)) {
    New-Item -ItemType Directory -Force -Path $Shots | Out-Null
}

# capture-avalonia.ps1 joins whatever it is given onto the repository root, so it takes a
# path relative to here and not an absolute one. Passing an absolute path produces
# root\C:\...\file, which is not a path at all.
$targets = @(
    @{ Screen = ''; Name = 'projects'; File = 'gate-panels-projects.png' }
    @{ Screen = 'unique'; Name = 'unique'; File = 'gate-panels-unique.png' }
    @{ Screen = 'combined'; Name = 'combined'; File = 'gate-panels-combined.png' }
)

$captured = @()

try {
    foreach ($t in $targets) {
        $out = Join-Path $Shots $t.File

        if ($t.Screen) {
            & (Join-Path $PSScriptRoot 'capture-avalonia.ps1') -Open $t.Screen -Out $out | Out-Null
        }
        else {
            & (Join-Path $PSScriptRoot 'capture-avalonia.ps1') -Out $out | Out-Null
        }

        if (-not (Test-Path $out)) { throw "the $($t.Name) screen was not photographed" }
        $captured += @{ Name = $t.Name; Path = $out }
    }

    $files = @($captured | ForEach-Object { $_.Path })
    $names = @($captured | ForEach-Object { $_.Name })

    & (Join-Path $PSScriptRoot 'measure-panel-pixels.ps1') -Png $files -Label $names -RequireEqual
    $code = $LASTEXITCODE
}
finally {
    if (-not $KeepShots) {
        foreach ($c in $captured) {
            Remove-Item $c.Path -ErrorAction SilentlyContinue
        }
    }
}

if ($code -ne 0) {
    Write-Host ''
    Write-Host 'FAIL: the three control panels are not one height'
    exit 1
}

Write-Host ''
Write-Host 'PASS: the three control panels are the same height'
exit 0