#  What a person actually sees: when do the project cards appear on screen?
#
#    pwsh -File scripts\measure-list-pixels.ps1
#
#  Why this script exists
#
#  Two earlier attempts measured this screen through the automation tree and both were wrong
#  in the same direction. A FindAll over this tree is not indexed by control type, so asking
#  "how many cards are there" materialises several thousand elements across a process
#  boundary - which costs more than a second, on the application's own thread. The first
#  version reported 2 740 ms for a list that was on screen in 25; the second reported 2 570.
#  Both were timing the instrument.
#
#  So: take pictures. A screen grab does not ask the program anything, and "the cards are on
#  screen" is a claim about pixels - which is what the complaint was in the first place.
#
#  How it decides
#
#  A frame is taken once the list has settled and kept as the reference. The tab is pressed,
#  frames are taken as fast as a grab can be taken, and each one is compared with the
#  reference. The first frame that is within a small difference IS the list being up. The
#  threshold is deliberately not tiny - text antialiasing on a repaint can differ slightly -
#  and deliberately not large - the editor screen and the list differ by tens of thousands of
#  pixels.
[CmdletBinding()]
param(
    [int]$TimeoutSeconds = 60,
    [double]$TolerancePercent = 0.5,
    [int]$SettleSeconds = 4
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8
. "$PSScriptRoot\common.ps1"

$ErrorActionPreference = 'Continue'

Add-Type -AssemblyName System.Drawing
Add-Type -Namespace 'Shot' -Name 'W' -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);

[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
'@

function Get-Frame([IntPtr]$Handle) {
    $r = New-Object Shot.W+RECT
    [void][Shot.W]::GetWindowRect($Handle, [ref]$r)
    $w = $r.Right - $r.Left
    $h = $r.Bottom - $r.Top
    if ($w -le 0 -or $h -le 0) { return $null }

    $bmp = New-Object System.Drawing.Bitmap $w, $h
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($r.Left, $r.Top, 0, 0, $bmp.Size)
    $g.Dispose()
    return $bmp
}

function Get-Difference($a, $b) {
    if ($null -eq $a -or $null -eq $b) { return 100.0 }
    if ($a.Width -ne $b.Width -or $a.Height -ne $b.Height) { return 100.0 }

    # A grid, not a stride through the whole buffer: a stride lands past the end of the
    # last row and reads outside the picture.
    $stepX = [math]::Max(1, [int]($a.Width / 200))
    $stepY = [math]::Max(1, [int]($a.Height / 200))
    $sampled = 0
    $different = 0

    for ($y = 0; $y -lt $a.Height; $y += $stepY) {
        for ($x = 0; $x -lt $a.Width; $x += $stepX) {
            $ca = $a.GetPixel($x, $y)
            $cb = $b.GetPixel($x, $y)
            $sampled++
            if ([math]::Abs($ca.R - $cb.R) -gt 12 -or
                [math]::Abs($ca.G - $cb.G) -gt 12 -or
                [math]::Abs($ca.B - $cb.B) -gt 12) { $different++ }
        }
    }

    return 100.0 * $different / [math]::Max($sampled, 1)
}

Get-Process -ErrorAction SilentlyContinue |
    Where-Object { $_.ProcessName -match 'PhotoBook' } |
    Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 1

$app = $null
try {
    $app = Start-TestApp
    Set-WindowInFront $app.Process.MainWindowHandle $app.Process.Id
    Start-Sleep -Seconds $SettleSeconds
    $hwnd = $app.Process.MainWindowHandle

    # Open a project, so the measurement is of the way back - which is the way that was
    # reported, and the one that has to rebuild the screen.
    $open = @(Get-Controls $app.Window 'Button' 'Открыть проект' 'Открыть проект*') | Select-Object -First 1
    if (-not $open) { throw 'no card on screen' }
    [void](Invoke-Control $open)

    $until = (Get-Date).AddSeconds(60)
    while ((Get-Date) -lt $until) {
        Start-Sleep -Milliseconds 500
        if (@(Get-Controls $app.Window 'Button' 'Добавить структуру').Count -gt 0) { break }
    }
    Start-Sleep -Seconds 2

    Write-Host ''
    Write-Host 'in the editor; going back to the list and photographing it until it settles'

    $tab = @(Get-Controls $app.Window 'Button' 'Мои проекты') | Select-Object -First 1
    if (-not $tab) { throw 'the projects tab is not there' }

    [void](Invoke-Control $tab)
    Start-Sleep -Seconds 3

    Set-WindowInFront $hwnd $app.Process.Id
    $reference = Get-Frame $hwnd
    if ($null -eq $reference) { throw 'no frame' }

    # Now the same journey again, photographed.
    $open = @(Get-Controls $app.Window 'Button' 'Открыть проект' 'Открыть проект*') | Select-Object -First 1
    [void](Invoke-Control $open)
    $until = (Get-Date).AddSeconds(60)
    while ((Get-Date) -lt $until) {
        Start-Sleep -Milliseconds 500
        if (@(Get-Controls $app.Window 'Button' 'Добавить структуру').Count -gt 0) { break }
    }
    Start-Sleep -Seconds 2

    Set-WindowInFront $hwnd $app.Process.Id
    $watch = [System.Diagnostics.Stopwatch]::StartNew()
    [void](Invoke-Control (@(Get-Controls $app.Window 'Button' 'Мои проекты') | Select-Object -First 1))

    $settled = $null
    $rows = New-Object System.Collections.Generic.List[object]
    $previous = $null

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        $frame = Get-Frame $hwnd
        if ($null -eq $frame) { Start-Sleep -Milliseconds 100; continue }

        $at = $watch.ElapsedMilliseconds
        $vsReference = Get-Difference $frame $reference
        $vsPrevious = if ($previous) { Get-Difference $frame $previous } else { 100.0 }

        $rows.Add([pscustomobject]@{
                At = $at; VsReference = $vsReference; VsPrevious = $vsPrevious
            })

        if ($null -eq $settled -and $vsReference -le $TolerancePercent) { $settled = $at }
        $previous = $frame

        if ($null -ne $settled -and $at - $settled -gt 800) { break }
        Start-Sleep -Milliseconds 30
    }

    $previous.Dispose()
    $reference.Dispose()

    Write-Host ''
    Write-Host ("cards on screen at            : {0} ms" -f $(if ($null -ne $settled) { $settled } else { 'never' }))
    Write-Host ''
    Write-Host '  at(ms)  vs the settled list   changed since the last frame'
    foreach ($r in $rows) {
        Write-Host ('  {0,6}  {1,8:N2}%            {2,8:N2}%' -f $r.At, $r.VsReference, $r.VsPrevious)
    }
}
finally {
    Stop-TestApp $app
}
