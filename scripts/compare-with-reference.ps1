#  Photographs the released program and this build on ONE project, and compares them.
#
#    pwsh -File scripts\compare-with-reference.ps1 -Mode combined
#    pwsh -File scripts\compare-with-reference.ps1 -Mode unique
#    pwsh -File scripts\compare-with-reference.ps1 -Mode projects
#
#  Why this exists rather than running the two capture scripts by hand
#
#  Opening a project writes to the index, which changes the date the list is sorted by,
#  which changes which project "the most recent one" is. So taking a picture of the
#  released program, then taking one of this build with a switch that opens the most
#  recent project, photographs two different projects - and the photographs then differ by
#  every picture in them.
#
#  That happened three times. Twice it was read as broken styling, and once it sent a real
#  change in the wrong direction: the combined screen was measured at nineteen per cent
#  when both sides were on the same project it was five and a half.
#
#  So the project is chosen once, by name, and that same name is given to both sides.
#  Nothing here asks for "the most recent" of anything.
#
#  What it prints is one number per screen: the share of the picture that differs. It is a
#  count and not a judgement - a difference is a place to look, not proof of a fault - but
#  it is a number that goes down when something is fixed, which is what makes it worth
#  having.
[CmdletBinding()]
param(
    [ValidateSet('projects', 'combined', 'unique')]
    [string]$Mode = 'projects',

    # The shell whose markup is being transcribed, built from this repository.
    #
    # NOT the installed release. The release is an older build of the same product and
    # it differs - three and a half per cent of its projects screen is different from this
    # one. Comparing against it means comparing against a design that is not the one being
    # copied, and it has already sent two correct changes in the wrong direction.
    #
    # The markup is the design. This build is what the markup draws.
    [string]$ReferenceExe = '',

    # Name of the project to open. Left empty, the first project of this mode is used -
    # chosen once here and then handed to both sides, which is the part that matters.
    [string]$ProjectName = ''
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8
. "$PSScriptRoot\common.ps1"

# The last line, not every line. Anything that writes on its way out - a warning, a note -
# would otherwise make this an array, and an array silently becomes a list of paths.
$root = @(Get-RepoRoot)[-1]
$buildExe = @(Get-AppExe)[-1]

if (-not $ReferenceExe) {
    $ReferenceExe = @(Get-AppExe -Shell Wpf)[-1]
}

if (-not (Test-Path $ReferenceExe)) {
    throw 'the reference shell is not built - build the solution first'
}

function Stop-Anything {
    Get-Process -ErrorAction SilentlyContinue |
        Where-Object { $_.ProcessName -match 'PhotoBook' } |
        Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 1
}

function First-Of($v) { return @($v)[0] }

function Read-Index {
    $path = Join-Path $env:LOCALAPPDATA 'PhotoBookRenamer\Projects\projects.json'
    return ConvertFrom-Json -InputObject (Get-Content $path -Raw -Encoding UTF8)
}

# ---------------------------------------------------------------------------
$shots = Join-Path $root 'doc\shots'
$modeValue = @{ unique = 2; combined = 3 }[$Mode]

if ($Mode -eq 'projects') {
    # Relative, like everywhere else: the capture script resolves these against the
    # repository itself, and a path built here and a path built there are two chances to
    # disagree about where a file is.
    $referenceShot = 'doc\shots\ref-111-projects.png'
    $buildShot = 'doc\shots\av-list.png'

    Stop-Anything
    & "$PSScriptRoot\capture-avalonia.ps1" -Exe $ReferenceExe -Out $referenceShot | Out-Null

    Stop-Anything
    & "$PSScriptRoot\capture-avalonia.ps1" -Exe $buildExe -Out $buildShot | Out-Null
}
else {
    # Not wrapped: an array literal around a parsed array collapses it to one element, and that
    # one element is the whole file.
    $sorted = Read-Index | Sort-Object { [datetime](First-Of $_.lastModified) } -Descending

    if ($ProjectName) {
        $found = $null
        foreach ($e in $sorted) {
            if ((First-Of $e.name) -eq $ProjectName) { $found = $e; break }
        }
        if (-not $found) { throw "no project named '$ProjectName' in the index" }
    }
    else {
        $found = $null
        foreach ($e in $sorted) {
            if ([int](First-Of $e.mode) -eq $modeValue) { $found = $e; break }
        }
        if (-not $found) { throw "no project of mode $Mode in the index" }
    }

    $name = First-Of $found.name
    $referenceShot = Join-Path $shots ("ref-111-{0}.png" -f $Mode)
    $buildShot = Join-Path $shots ("av-{0}.png" -f $Mode)

    Write-Host ("project: {0} ({1} books x {2} spreads)" -f $name, (First-Of $found.bookCount), (First-Of $found.pageCount))

    Stop-Anything
    & "$PSScriptRoot\capture-reference-modes.ps1" -Exe $ReferenceExe -ProjectName $name -Out $referenceShot | Out-Null

    Stop-Anything
    & "$PSScriptRoot\capture-reference-modes.ps1" -Exe $buildExe -ProjectName $name -Out $buildShot | Out-Null
}

Stop-Anything
$result = & "$PSScriptRoot\compare-to-golden.ps1" -Build $buildShot -Golden $referenceShot -Out (Join-Path $shots ("{0}-vs-111.png" -f $Mode)) 2>&1 | Out-String

Write-Host ''
Write-Host "=== $Mode ==="
($result -split "`n") | Where-Object { $_ -match 'differing pixels|bands|saved|written' } | ForEach-Object { Write-Host "  $($_.Trim())" }