# Screenshots the Avalonia shell.
#
# The WPF capture scripts drive the app through UI Automation and assume WPF's automation
# tree. This one only takes the picture: finding the window by process id and copying the
# pixels. That is deliberate - the first thing to be checked is whether the screen comes up
# and looks right, and a capture that also drove the UI would report "cannot find the button"
# when what it should have reported was a red banner.
#
#   pwsh -File scripts\capture-avalonia.ps1 -Out doc\shots\avalonia-projects.png
#
[CmdletBinding()]
param(
    [string]$Out = 'doc\shots\avalonia.png',
    [int]$TimeoutSeconds = 30,
    [string]$Exe,
    # start | unique | combined - which screen to open before the window appears, so any of
    # them can be photographed. See App.OpenNewest.
    [string]$Open = ''
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8

. "$PSScriptRoot\common.ps1"

$root = Get-RepoRoot
if (-not $Exe) {
    $Exe = Join-Path $root 'src\PhotoBook.Desktop.Avalonia\bin\Release\net8.0\PhotoBook.Desktop.Avalonia.exe'
}

if (-not (Test-Path $Exe)) {
    throw "the shell is not built: $Exe"
}

# Whatever the binary is called. The installed release is the other name, and a capture
# that leaves it running photographs whichever window it found first.
$appName = [System.IO.Path]::GetFileNameWithoutExtension($Exe)
Get-Process -Name $appName -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 1

# -ArgumentList rejects an empty array, so the two cases are spelled out rather than
# built as a list that is empty half the time.
# The switch that opens a screen straight away belongs to the shell being built here. A
# released build has never heard of it, and passing it anyway produces a process that
# exits before a window appears.
$isDevBuild = $Exe -like '*PhotoBook.Desktop.Avalonia.exe'

$proc = if ($Open -and $isDevBuild) { Start-Process -FilePath $Exe -PassThru -ArgumentList '--open', $Open }
        else { Start-Process -FilePath $Exe -PassThru }

$deadline = (Get-Date).AddSeconds($TimeoutSeconds)
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Milliseconds 500
    $proc.Refresh()
    if ($proc.HasExited) { throw "the shell exited $($proc.ExitCode)" }
    if ($proc.MainWindowHandle -ne 0) { break }
}

if ($proc.MainWindowHandle -eq 0) { throw 'no window appeared' }

# Give the cards a moment: the list is loaded asynchronously from disk, so a capture taken
# at the first frame shows an empty screen and looks like a broken binding.
Start-Sleep -Seconds 3

Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class AvaloniaCapture {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int n);
}
"@

[void][AvaloniaCapture]::ShowWindow($proc.MainWindowHandle, 3)
[void][AvaloniaCapture]::SetForegroundWindow($proc.MainWindowHandle)
Start-Sleep -Milliseconds 700

$r = New-Object AvaloniaCapture+RECT
[void][AvaloniaCapture]::GetWindowRect($proc.MainWindowHandle, [ref]$r)
$w = $r.Right - $r.Left
$h = $r.Bottom - $r.Top
if ($w -le 0 -or $h -le 0) { throw "window has no size ($w x $h)" }

$bmp = New-Object System.Drawing.Bitmap $w, $h
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($r.Left, $r.Top, 0, 0, (New-Object System.Drawing.Size($w, $h)))
$g.Dispose()

$outPath = Join-Path $root $Out
New-Item -ItemType Directory -Force -Path (Split-Path $outPath) | Out-Null
$bmp.Save($outPath, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()

Write-Host "saved $outPath ($w x $h)"

$proc | Stop-Process -Force
exit 0