<#
.SYNOPSIS
    Photographs the program as it is installed on this machine.

.DESCRIPTION
    There are two programs in this repository and one of them is also installed. The
    questions about how far apart things sit on screen are questions about the program the
    owner is looking at, which is the installed one, and answering them from a build in
    the working tree is answering a different question - so this starts that one.

    Nothing is taken from it but measurements. The file exists to be measured.

    Why the window is brought forward first
    ----------------------------------------
    Photographs are taken from the screen, so whatever is in front is what lands in the
    picture. This says so rather than quietly saving a picture of the wrong window.
#>
[CmdletBinding()]
param(
    # Where to look. The default is the installed release; -Exe overrides it for the build
    # in the working tree.
    [string]$Exe = 'C:\Program Files (x86)\Fabrika BookBuilder Studio\PhotoBookRenamer.exe',

    [string]$Out = 'doc\shots\installed-projects.png'
)

$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\common.ps1"

if (-not (Test-Path $Exe)) { throw "the program is not installed: $Exe" }

$appName = [System.IO.Path]::GetFileNameWithoutExtension($Exe)
Get-Process -Name $appName -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 1

$proc = Start-Process -FilePath $Exe -PassThru

$deadline = (Get-Date).AddSeconds(45)
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Milliseconds 500
    $proc.Refresh()
    if ($proc.HasExited) { throw "the installed program exited $($proc.ExitCode)" }
    if ($proc.MainWindowHandle -ne 0) { break }
}
if ($proc.MainWindowHandle -eq 0) { throw 'no window appeared' }

# The list is read from disk on startup, and a picture taken at the first frame shows an
# empty screen, which looks exactly like a program with no projects in it.
Start-Sleep -Seconds 8

Set-WindowInFront $proc.MainWindowHandle $proc.Id
Start-Sleep -Milliseconds 900

Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class InstalledShot {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
}
"@

$r = New-Object InstalledShot+RECT
[void][InstalledShot]::GetWindowRect($proc.MainWindowHandle, [ref]$r)
$w = $r.Right - $r.Left
$h = $r.Bottom - $r.Top

$bmp = New-Object System.Drawing.Bitmap $w, $h
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($r.Left, $r.Top, 0, 0, (New-Object System.Drawing.Size($w, $h)))
$g.Dispose()

New-Item -ItemType Directory -Force -Path (Split-Path $Out) | Out-Null
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()

Write-Host "saved $Out ($w x $h)"

Get-Process -Name $appName -ErrorAction SilentlyContinue | Stop-Process -Force