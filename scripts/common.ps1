# One place that knows where the built program is.
#
# Twenty-two verification scripts used to spell the path out themselves, in three slightly
# different spellings, and every one of them named the WPF project and the Release
# configuration and the output framework version in full. When the shell moves to another UI
# framework that is twenty-two edits, and the kind that get missed: a script that keeps
# pointing at the old build still runs, it just quietly verifies a program nobody ships.
#
# Dot-source this and call Get-AppExe. The function fails loudly instead of returning a
# path that is not there, because a capture of a stale binary is worse than no capture.
#
#   . "$PSScriptRoot\common.ps1"
#   $exe = Get-AppExe
#   $exe = Get-AppExe -Configuration Debug

Set-StrictMode -Off

# The repository root. $PSScriptRoot here is scripts\, whoever dot-sourced us.
$script:RepoRoot = Split-Path -Parent $PSScriptRoot

<#
.SYNOPSIS
    Path to the built shell.

.DESCRIPTION
    Fails if the binary is not there. Callers that want to tolerate a missing build should
    test the result with Test-Path themselves, which several of them already do.

.PARAMETER Configuration
    Release unless asked otherwise - the configuration the owner runs and the one the
    golden screenshots are taken from.
#>
function Get-AppExe {
    [CmdletBinding()]
    param(
        [ValidateSet('Release', 'Debug')]
        [string]$Configuration = 'Release'
    )

    # The shell project moves during the Avalonia migration. Everything downstream asks this
    # function, so that move is one edit rather than twenty-two.
    $project = 'src\PhotoBook.Desktop.Wpf'
    $candidate = Join-Path $script:RepoRoot "$project\bin\$Configuration\net8.0-windows\PhotoBookRenamer.exe"

    if (-not (Test-Path $candidate)) {
        throw "no build at $candidate`nBuild it first:  dotnet build PhotoBookRenamer.sln -c $Configuration"
    }

    return $candidate
}

<#
.SYNOPSIS
    Repository root, for scripts that need more than the binary.

.EXAMPLE
    $root = Get-RepoRoot
#>
function Get-RepoRoot {
    return $script:RepoRoot
}

<#
.SYNOPSIS
    Stop any running copy and wait for the process to actually go.

.DESCRIPTION
    Captures run back to back and a still-closing window shifts the next frame by a pixel,
    which shows up later as a phantom difference. Every capture script needs this.
#>
function Stop-RunningApp {
    Get-Process -Name 'PhotoBookRenamer' -ErrorAction SilentlyContinue |
        Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 2
}