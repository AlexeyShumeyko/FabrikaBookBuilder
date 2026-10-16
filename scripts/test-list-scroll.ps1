#  The list of loaded photographs scrolls.
#
#    pwsh -File scripts\test-list-scroll.ps1
#
#  What this used to check, and why it failed
#
#  It worked out where the list was by arithmetic - the left edge from the heading's own
#  box, the top two hundred pixels below it, a width of two hundred and eighty - sent a
#  wheel click at that point, and compared the colour of a patch of pixels before and
#  after.
#
#  Three things in that were wrong at once. The two hundred pixels were a guess about where
#  the list sat inside the panel, and the panel is not the same height in either shell. The
#  wheel click goes to whatever window has the focus, so a point in the wrong place means
#  no input at all and the screen does not move. And the patch of pixels is a measure of
#  where things were drawn, which is the thing that had changed, not of whether they
#  scrolled.
#
#  What it checks now
#
#  The rows of the file list carry their file's name, and the name is in the automation
#  tree with its own box. So the check needs no arithmetic at all:
#
#    which names are on screen now
#    scroll, by aiming at a row that is actually on screen
#    which names are on screen afterwards
#
#  and it is a scroll if the two sets differ. Nothing here knows a coordinate, and nothing
#  here knows how tall a panel is.
[CmdletBinding()]
param(
    [int]$WheelClicks = 8,

    # How long to let the photographs decode before reading the list.
    [int]$SettleSeconds = 8
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8
. "$PSScriptRoot\common.ps1"

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

Add-Type -Namespace 'ScrollInput' -Name 'Native' -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
[DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, IntPtr e);
'@

function Get-PhotoRows($window) {
    # A row is a text that is a file name. Anything else on the screen - a heading, a
    # count, a slot label - is not a row and must not be mistaken for one.
    $texts = $window.FindAll(
        [System.Windows.Automation.TreeScope]::Descendants,
        (New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::Text)))

    $rows = New-Object System.Collections.Generic.List[object]
    for ($i = 0; $i -lt $texts.Count; $i++) {
        $name = $texts[$i].Current.Name
        if ($name -match '\.[A-Za-z]{3,4}$') {
            $rows.Add([pscustomobject]@{ Name = $name; Rect = $texts[$i].Current.BoundingRectangle })
        }
    }
    return $rows
}

function Send-Wheel([int]$x, [int]$y, [int]$clicks) {
    [void][ScrollInput.Native]::SetCursorPos($x, $y)
    Start-Sleep -Milliseconds 300

    for ($i = 0; $i -lt $clicks; $i++) {
        # 120 is one notch down, sent as the unsigned form of minus one hundred and twenty.
        [ScrollInput.Native]::mouse_event(0x0800, 0, 0, [uint32]4294967176, [IntPtr]::Zero)
        Start-Sleep -Milliseconds 120
    }
    Start-Sleep -Seconds 1
}

$ErrorActionPreference = 'Continue'

$proc = Start-Process -FilePath (Get-AppExe) -PassThru -ArgumentList '--open', 'combined'
$root = [System.Windows.Automation.AutomationElement]::RootElement

$deadline = (Get-Date).AddSeconds(60)
$window = $null
while (-not $window -and (Get-Date) -lt $deadline) {
    Start-Sleep -Milliseconds 500
    $proc.Refresh()
    if ($proc.HasExited) { throw "the program exited $($proc.ExitCode) before a window appeared" }
    if ($proc.MainWindowHandle -ne 0) {
        $window = [System.Windows.Automation.AutomationElement]::FromHandle($proc.MainWindowHandle)
    }
}
if (-not $window) { throw 'no window' }

Set-WindowFocus $proc.MainWindowHandle
Start-Sleep -Seconds $SettleSeconds

$before = Get-PhotoRows $window
Write-Host ("rows on screen before : {0}" -f $before.Count)

if ($before.Count -eq 0) { Stop-Process -Id $proc.Id -Force; throw 'the file list showed no rows at all' }

# Aimed at the middle of a row that is on screen. This is the whole difference between
# this working and not: the previous version aimed at a number.
$target = $before[[int]($before.Count / 2)]
$x = [int]($target.Rect.X + $target.Rect.Width / 2)
$y = [int]($target.Rect.Y + $target.Rect.Height / 2)
Write-Host ("aiming at '{0}' at {1},{2}" -f $target.Name, $x, $y)

Send-Wheel $x $y $WheelClicks

$proc.Refresh()
if ($proc.HasExited) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue; throw 'the program stopped while scrolling' }

$after = Get-PhotoRows $window
$namesBefore = @($before | ForEach-Object { $_.Name })
$namesAfter = @($after | ForEach-Object { $_.Name })

Write-Host ("rows on screen after  : {0}" -f $after.Count)

$newOnes = @($namesAfter | Where-Object { $namesBefore -notcontains $_ })
$gone = @($namesBefore | Where-Object { $namesAfter -notcontains $_ })

Write-Host ''
Write-Host ("first before : {0}" -f (($namesBefore | Select-Object -First 3) -join ', '))
Write-Host ("first after  : {0}" -f (($namesAfter | Select-Object -First 3) -join ', '))
Write-Host ("newly shown  : {0}" -f $newOnes.Count)
Write-Host ("gone from view: {0}" -f $gone.Count)

Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue

Write-Host ''
if ($newOnes.Count -eq 0 -and $gone.Count -eq 0) {
    Write-Host ("FAIL: {0} wheel clicks over the list changed nothing" -f $WheelClicks)
    exit 1
}

Write-Host ("PASS: the list moved - {0} rows gone, {1} newly on screen" -f $gone.Count, $newOnes.Count)