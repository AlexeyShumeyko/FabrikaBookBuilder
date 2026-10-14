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
    Release unless asked otherwise - the configuration that ships and the one the
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
    #
    # It now points at the shell that ships. The old one is still in the tree while its
    # replacement is compared against it screen by screen; when the comparison is done this
    # function is the only thing that has to know.
    $project = 'src\PhotoBook.Desktop.Avalonia'
    $candidate = Join-Path $script:RepoRoot "$project\bin\$Configuration\net8.0\PhotoBook.Desktop.Avalonia.exe"

    if (-not (Test-Path $candidate)) {
        throw "no build at $candidate`nBuild it first:  dotnet build PhotoBookRenamer.sln -c $Configuration"
    }

    return $candidate
}

<#
.SYNOPSIS
    Brings a window to the front and gives it the keyboard focus.

.DESCRIPTION
    Synthetic input - a wheel, a click - goes to the window that has focus. Every scenario
    that sends one has to ask for that first, or the events land on whatever else happens to
    be in front and the screen does not move, which reads as "the feature does not work".

    It failed exactly once, on the shell that replaced the old one, and the symptom was a
    signature that did not move by a single pixel: not a scroll that was too small, but no
    input at all.

.PARAMETER Handle
    The window to raise.

.EXAMPLE
    Set-WindowFocus $proc.MainWindowHandle
#>
function Set-WindowFocus {
    [CmdletBinding()]
    param([Parameter(Mandatory)][IntPtr]$Handle)

    if (-not ('WindowInput' -as [type])) {
        Add-Type -Namespace 'WindowInput' -Name 'Native' -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
[DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int n);
[DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr h);
'@
    }

    [void][WindowInput.Native]::ShowWindow($Handle, 3)   # SW_MAXIMIZE
    [void][WindowInput.Native]::BringWindowToTop($Handle)
    [void][WindowInput.Native]::SetForegroundWindow($Handle)
    Start-Sleep -Milliseconds 400
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