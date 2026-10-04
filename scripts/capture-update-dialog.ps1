# Screenshots the update window - the one screen a person cannot reach on demand.
#
# It only appears when a NEWER release exists on GitHub than the one running, so
# capturing it means pretending to be an old installation: the build is repeated with an
# old version number, which is a build-time property and no code change. The real version
# is read from the project file and the build is put back afterwards, because leaving a
# 1.0.0 build in bin\ would quietly break the next release: the pipeline publishes the
# version from the project file, not from what happens to be compiled.
#
# The window also proves something a screenshot of the code cannot: that the text a
# client reads really is free of links and of the author's account. The release body of
# 1.1.0 still carries GitHub's generated block, and this is what proves the dialog
# removes it rather than showing it.
#
# Usage:
#   pwsh -File scripts\capture-update-dialog.ps1 -Out doc\shots\golden\11-update-dialog.png
#
# NOTE: this file contains Cyrillic literals and must stay UTF-8 **with BOM**.

param(
    [string]$Out = 'doc\shots\golden\11-update-dialog.png',
    [string]$PretendVersion = '1.0.0',
    [int]$TimeoutSeconds = 90
)

[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root

Add-Type -AssemblyName System.Drawing
Add-Type @"
using System; using System.Collections.Generic; using System.Runtime.InteropServices; using System.Text;
public class UpdWin {
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint p);
  [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] private static extern int GetWindowTextLength(IntPtr h);
  [DllImport("user32.dll")] private static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  public static IntPtr Find(uint pid, string title) {
    IntPtr found = IntPtr.Zero;
    EnumWindows((h, l) => {
      uint p; GetWindowThreadProcessId(h, out p);
      if (p != pid || !IsWindowVisible(h)) return true;
      int n = GetWindowTextLength(h);
      var sb = new StringBuilder(n + 1); GetWindowText(h, sb, sb.Capacity);
      if (sb.ToString() == title) { found = h; return false; }
      return true;
    }, IntPtr.Zero);
    return found;
  }
}
"@

# The dialog's own title, spelled out so the script keeps working if the wording changes:
# a mismatch here is a real finding, not something to paper over.
$dialogTitle = -join ([char]0x0414, [char]0x043E, [char]0x0441, [char]0x0442, [char]0x0443, [char]0x043F, [char]0x043D, [char]0x043E, ' ', [char]0x043E, [char]0x0431, [char]0x043D, [char]0x043E, [char]0x0432, [char]0x043B, [char]0x0435, [char]0x043D, [char]0x0438, [char]0x0435)

$csproj = Join-Path $root 'src\PhotoBook.Desktop.Wpf\PhotoBook.Desktop.Wpf.csproj'
$realVersion = (Select-Xml -Path $csproj -XPath '//Version').Node.InnerText
Write-Host "project version: $realVersion   pretending to be: $PretendVersion"

Get-Process -Name PhotoBookRenamer -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 1

$proc = $null
try {
    Write-Host 'building the older version...'
    $build = dotnet build -c Release "/p:Version=$PretendVersion" --nologo 2>&1
    if ($LASTEXITCODE -ne 0) {
        $build | Select-Object -Last 8 | ForEach-Object { Write-Host $_ }
        throw 'build failed'
    }

    $exe = Join-Path $root 'src\PhotoBook.Desktop.Wpf\bin\Release\net8.0-windows\PhotoBookRenamer.exe'
    $proc = Start-Process -FilePath $exe -PassThru
    Write-Host "started pid $($proc.Id), waiting up to $TimeoutSeconds s for the update window..."

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    $dlg = [IntPtr]::Zero
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Seconds 1
        $proc.Refresh()
        if ($proc.HasExited) { throw "app exited with $($proc.ExitCode)" }
        $dlg = [UpdWin]::Find([uint32]$proc.Id, $dialogTitle)
        if ($dlg -ne [IntPtr]::Zero) { break }
    }

    if ($dlg -eq [IntPtr]::Zero) {
        throw "the update window never appeared. Either there is no newer release on GitHub than $PretendVersion, or the check failed - look for it in the app log"
    }

    [void][UpdWin]::SetForegroundWindow($dlg)
    Start-Sleep -Seconds 2

    $r = New-Object UpdWin+RECT
    [void][UpdWin]::GetWindowRect($dlg, [ref]$r)
    $bmp = New-Object System.Drawing.Bitmap ($r.R - $r.L), ($r.B - $r.T)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($r.L, $r.T, 0, 0, $bmp.Size)
    $g.Dispose()

    $dir = Split-Path -Parent $Out
    if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
    $bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Host "saved $((Resolve-Path $Out).Path) ($($r.R - $r.L)x$($r.B - $r.T))"
}
finally {
    if ($proc) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue }
    Start-Sleep -Seconds 1

    # Put the real version back. Skipping this is how a 1.0.0 build ends up being the one
    # somebody tests, and the next release then ships an old version number.
    Write-Host "restoring the build to $realVersion..."
    dotnet build -c Release "/p:Version=$realVersion" --nologo 2>&1 |
        Select-String -Pattern 'Ошибок:|error' | ForEach-Object { Write-Host "   $($_.Line.Trim())" }
    Pop-Location
}