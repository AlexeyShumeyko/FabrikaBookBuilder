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
        [string]$Configuration = 'Release',

        # Which shell. The one that ships is the default; the one being compared against is
        # asked for by name, because both are built from this repository and comparing the
        # two means being able to point at either.
        [ValidateSet('Avalonia', 'Wpf')]
        [string]$Shell = 'Avalonia'
    )

    # The shell project moves during the Avalonia migration. Everything downstream asks this
    # function, so that move is one edit rather than twenty-two.
    #
    # No script names a path to a built program. A path written into a script is a path that
    # goes stale the moment a folder is renamed, and the audit fails the build for it.
    if ($Shell -eq 'Wpf') {
        $relative = 'src\PhotoBook.Desktop.Wpf\bin\{0}\net8.0-windows\PhotoBookRenamer.exe' -f $Configuration
    }
    else {
        $relative = 'src\PhotoBook.Desktop.Avalonia\bin\{0}\net8.0\PhotoBook.Desktop.Avalonia.exe' -f $Configuration
    }

    $candidate = Join-Path $script:RepoRoot $relative

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
# The pieces every scenario needs before it can ask anything. Loaded here so that a script
# which dot-sources this one has them without knowing they exist.
. (Join-Path $PSScriptRoot 'test-helpers.ps1')

<#
.SYNOPSIS
    Brings a window to the front and insists it got there.

.DESCRIPTION
    SetForegroundWindow can be refused. Windows does not always let a process take the
    foreground away from another, and the call reports that it did not - which is easy to
    ignore, because the window still exists and still has a rectangle.

    That matters anywhere a photograph is taken from the screen rather than from the
    window. A shot of a window that is behind something else is a picture of whatever was
    in front of it, and it compares perfectly well against another shot of the same thing.
    The number comes out tiny and looks like success.
#>
function Set-WindowInFront {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][IntPtr]$Handle,
        [int]$ProcessId,
        [int]$Attempts = 8
    )

    if (-not ('WindowFront' -as [type])) {
        Add-Type -Namespace 'WindowFront' -Name 'Native' -MemberDefinition @"
[DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
[DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int n);
[DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr h);
[DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
[DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
[DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, IntPtr extra);
"@
    }

    $onTop = $false
    for ($i = 0; $i -lt $Attempts -and -not $onTop; $i++) {
        # Windows only lets a process take the foreground if that process was the last one to
        # have input. Pressing and releasing alt is the long-standing way to be the last one:
        # it costs nothing, it is invisible, and it hands the right over.
        [void][WindowFront.Native]::keybd_event(0x12, 0, 0, [IntPtr]::Zero)   # alt down
        [void][WindowFront.Native]::keybd_event(0x12, 0, 2, [IntPtr]::Zero)   # alt up

        [void][WindowFront.Native]::ShowWindow($Handle, 3)   # SW_MAXIMIZE
        [void][WindowFront.Native]::BringWindowToTop($Handle)
        [void][WindowFront.Native]::SetForegroundWindow($Handle)
        Start-Sleep -Milliseconds 400

        $owner = 0
        [void][WindowFront.Native]::GetWindowThreadProcessId(
            [WindowFront.Native]::GetForegroundWindow(), [ref]$owner)

        $onTop = if ($ProcessId -gt 0) { $owner -eq $ProcessId } else { $false }
    }

    if (-not $onTop) {
        $blocker = 0
        [void][WindowFront.Native]::GetWindowThreadProcessId(
            [WindowFront.Native]::GetForegroundWindow(), [ref]$blocker)

        $name = try { (Get-Process -Id $blocker -ErrorAction Stop).ProcessName } catch { 'unknown' }
        throw ("the program could not be brought to the front after {0} tries; '{1}' is in front of it." -f
            $Attempts, $name) + "`nA photograph taken now would be of that window, not this one."
    }
}
<#
.SYNOPSIS
    Refuses to go on unless this window is the one in front.

.DESCRIPTION
    The photograph is taken from the screen, not from the window, so whatever is on top is
    what lands in the picture. A picture of the wrong window is not obviously wrong: it is
    the right size, it has content in it, and it compares well against another picture of the
    same wrong window. That is how a run once reported 0.02% between two programs that had
    not changed.

    Unlike Set-WindowInFront this does not try to fix anything. It asks, and stops.

.PARAMETER Handle
    The window about to be photographed.

.PARAMETER What
    How to name it when complaining, so the message says which picture is suspect.
#>
function Assert-WindowInFront {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][IntPtr]$Handle,
        [string]$What = 'this window'
    )

    if (-not ('WindowAssert' -as [type])) {
        Add-Type -Namespace 'WindowAssert' -Name 'Native' -MemberDefinition @"
[DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
[DllImport("user32.dll")] public static extern IntPtr GetParent(IntPtr h);
[DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
"@
    }

    $front = [WindowAssert.Native]::GetForegroundWindow()
    $node = $front
    for ($i = 0; $i -lt 4 -and $node -ne [IntPtr]::Zero; $i++) {
        if ($node -eq $Handle) { return }
        $node = [WindowAssert.Native]::GetParent($node)
    }

    $blocker = 0
    [void][WindowAssert.Native]::GetWindowThreadProcessId($front, [ref]$blocker)
    $name = try { (Get-Process -Id $blocker -ErrorAction Stop).ProcessName } catch { 'something else' }

    throw ("{0} is not in front - '{1}' is. The photograph would be of that instead." -f $What, $name)
}