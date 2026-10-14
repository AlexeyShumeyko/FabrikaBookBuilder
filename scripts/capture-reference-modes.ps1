#  Photographs the released program on the screens that need a project opened first.
#
#    pwsh -File scripts\capture-reference-modes.ps1 -Card 4 -Out doc\shots\ref-111-combined.png
#    pwsh -File scripts\capture-reference-modes.ps1 -Card 2 -Out doc\shots\ref-111-unique.png
#
#  Why it exists
#
#  The projects screen needs nothing but starting the program. The two editor screens do:
#  they are reached through a project, and a project is reached through a card. So the only
#  way to photograph them is to press the card - which is what this does, through the same
#  system automation the other scripts use.
#
#  Which card is which
#
#  The list is taken from the index the program itself reads, so the number printed at the
#  start is the number to pass. It is the position on screen from the left, top row first.
#  It is printed rather than guessed because a card with no photographs in it and a card
#  with twenty are the same size on screen and the same line in the file, and the only
#  thing that tells them apart is the order they are shown in.
#
#  Why the card is found by its name and not by its position among buttons
#
#  Every card's button is named the same, so there is nothing to tell one from another by
#  name. What is different is the card's own text. So the tree is walked for the text of the
#  project, and the button inside that card is pressed - a card is found by what is written
#  on it rather than by counting, which is the same reason a person finds it.
[CmdletBinding()]
param(
    # Which card to open, counting from the left, top row first. -1 asks for the first one
    # whose mode matches -Mode.
    [int]$Card = -1,

    # unique | combined - only used when Card is -1.
    [string]$Mode = 'combined',

    # Which project to open when Card is -1: its name from the index.
    [string]$ProjectName = '',

    [string]$Out = '',
    [string]$Exe = '',
    [int]$TimeoutSeconds = 90
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8
. "$PSScriptRoot\common.ps1"

if (-not $Exe) { $Exe = 'C:\Program Files (x86)\Fabrika BookBuilder Studio\PhotoBookRenamer.exe' }
if (-not (Test-Path $Exe)) { throw "no released program at $Exe" }
if (-not $Out) { $Out = Join-Path (Get-RepoRoot) ("doc\shots\ref-111-{0}.png" -f $Mode) }

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Drawing

$modeValue = @{ unique = 2; combined = 3 }[$Mode.ToLowerInvariant()]

# --- which project -------------------------------------------------------------
$indexPath = Join-Path $env:LOCALAPPDATA 'PhotoBookRenamer\Projects\projects.json'
# Not wrapped in @(). The parser hands back an array, and putting that inside an array
# literal wraps it again - the result has one element, and that element is the whole file.
$entries = ConvertFrom-Json -InputObject (Get-Content $indexPath -Raw -Encoding UTF8)

# Some fields come back as one-element lists rather than as values. Written as an index
# rather than as a cast, because a cast of a list is an error and a cast of a value is not.
function First-Of($v) { return @($v)[0] }

$target = $null
$chosen = ''
$position = -1

# The list on screen is ordered by when the project was last touched, newest first, and the
# automation tree hands the buttons back in the order they are drawn. So the n-th button is
# the n-th project in that same order.
#
# A project's name is a label, and a plain label is not in the automation tree at all - only
# controls that were given a name to be found are. So the card cannot be found by what is
# written on it, only by where it stands. That is the one place this script counts rather
# than reads, and the number it counts to is printed.
$sorted = @($entries | Sort-Object { [datetime](@($_.lastModified)[0]) } -Descending)

if ($ProjectName) {
    for ($i = 0; $i -lt $sorted.Count; $i++) {
        if ((@($sorted[$i].name)[0]) -eq $ProjectName) { $target = $sorted[$i]; $position = $i; break }
    }
    if (-not $target) { throw "no project named '$ProjectName' in the index" }
}
elseif ($Card -ge 0) {
    $position = $Card
    if ($position -ge $sorted.Count) { throw "no card at $Card (there are $($sorted.Count))" }
    $target = $sorted[$position]
}
else {
    for ($i = 0; $i -lt $sorted.Count; $i++) {
        if ([int](@($sorted[$i].mode)[0]) -eq $modeValue) { $target = $sorted[$i]; $position = $i; break }
    }
    if (-not $target) { throw "no project of mode $Mode in the index" }
}

$chosen = @($target.name)[0]
Write-Host ("card {0}: '{1}' ({2} books x {3} spreads, mode {4})" -f `
    $position, $chosen, (@($target.bookCount)[0]), (@($target.pageCount)[0]), (@($target.mode)[0]))

# --- start it -----------------------------------------------------------------
Get-Process -Name ([System.IO.Path]::GetFileNameWithoutExtension($Exe)) -ErrorAction SilentlyContinue |
    Stop-Process -Force
Start-Sleep -Seconds 1

$proc = Start-Process -FilePath $Exe -PassThru

$deadline = (Get-Date).AddSeconds($TimeoutSeconds)
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Milliseconds 500
    $proc.Refresh()
    if ($proc.HasExited) { throw "the program exited $($proc.ExitCode)" }
    if ($proc.MainWindowHandle -ne 0) { break }
}
if ($proc.MainWindowHandle -eq 0) { throw 'no window' }

Set-WindowFocus $proc.MainWindowHandle

$root = [System.Windows.Automation.AutomationElement]::RootElement
$win = [System.Windows.Automation.AutomationElement]::FromHandle($proc.MainWindowHandle)

# --- press the card this project is written on ----------------------------------
# Every card's button carries the same name, so the buttons cannot be told apart by it.
# What is different on each card is the name written on it. So: find that text, then find
# the button directly below it and inside its card - which is what a person does, and which
# does not depend on the cards coming back in any particular order.
Add-Type -Namespace 'Capture' -Name 'Native' -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
[StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
'@ -UsingNamespace System.Drawing

function Find-OpenButtonFor($window, [string]$name) {
    $nameCondition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty, $name)

    $label = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $nameCondition)
    if (-not $label) { return $null }
    $at = $label.Current.BoundingRectangle

    $openName = '*' + [char]0x041E + [char]0x0442 + [char]0x043A + [char]0x0440 + [char]0x044B + [char]0x0442 + [char]0x044C + ' ' + [char]0x043F + [char]0x0440 + [char]0x043E + [char]0x0435 + [char]0x043A + [char]0x0442

    $buttons = $window.FindAll(
        [System.Windows.Automation.TreeScope]::Descendants,
        (New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::NameProperty, $openName)))

    $best = $null
    $bestGap = [double]::MaxValue

    for ($i = 0; $i -lt $buttons.Count; $i++) {
        $r = $buttons[$i].Current.BoundingRectangle
        if ($r.IsEmpty) { continue }

        # Below the name, and on the same card: the button is inside the card's own column.
        if ($r.Top -lt $at.Top) { continue }
        $centre = $r.Left + $r.Width / 2
        $gap = [Math]::Abs($centre - ($at.Left + $at.Width / 2))

        if ($gap -lt $bestGap) { $bestGap = $gap; $best = $buttons[$i] }
    }

    return $best
}

$target_button = $null
$deadline = (Get-Date).AddSeconds($TimeoutSeconds)
while (-not $target_button -and (Get-Date) -lt $deadline) {
    $openName = '*' + [char]0x041E + [char]0x0442 + [char]0x043A + [char]0x0440 + [char]0x044B + [char]0x0442 + [char]0x044C + ' ' + [char]0x043F + [char]0x0440 + [char]0x043E + [char]0x0435 + [char]0x043A + [char]0x0442

    # Re-read every pass. An element taken from a handle once keeps pointing at whatever
    # that handle held at the time, and a window that has not finished drawing has nothing
    # in it - so a search on a stale element finds nothing forever and looks like a missing
    # button rather than a window that was not ready yet.
    $proc.Refresh()
    if ($proc.MainWindowHandle -eq 0) { Start-Sleep -Milliseconds 500; continue }
    $win = [System.Windows.Automation.AutomationElement]::FromHandle($proc.MainWindowHandle)

    # Every button, then the ones whose name matches, in the order they are drawn.
    #
    # Not a property condition on the name. That compares for equality and has no idea what
    # a star means, so a condition built from '*' + the name matches nothing at all - and it
    # fails quietly, as zero results rather than as an error, which is the worst way for a
    # search to fail.
    $all = $win.FindAll(
        [System.Windows.Automation.TreeScope]::Descendants,
        (New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::Button)))

    $matched = New-Object System.Collections.Generic.List[object]
    for ($i = 0; $i -lt $all.Count; $i++) {
        if ($all[$i].Current.Name -like $openName) { $matched.Add($all[$i]) }
    }
    $buttons = $matched

    if ($buttons.Count -gt 0 -and $position -lt $buttons.Count) {
        $candidate = $buttons[$position]
        if (-not $candidate.Current.BoundingRectangle.IsEmpty) { $target_button = $candidate }
    }
    $seen = $buttons.Count

    if (-not $target_button) { Start-Sleep -Seconds 1 }
}
if (-not $target_button) { throw "no card at position $position ('$chosen'); the tree offered $seen buttons" }

$target_button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
Write-Host 'card pressed'

# Let it settle: a project with photographs in it decodes them, and a picture taken while
# that is happening photographs the loading rather than the screen.
Start-Sleep -Seconds 12

# --- the picture ---------------------------------------------------------------
$proc.Refresh()
$rect = New-Object Capture.Native+RECT
[void][Capture.Native]::GetWindowRect($proc.MainWindowHandle, [ref]$rect)
$w = $rect.Right - $rect.Left
$h = $rect.Bottom - $rect.Top

$bmp = New-Object System.Drawing.Bitmap $w, $h
$g = [System.Drawing.Graphics]::FromImage($bmp)
try {
    $g.CopyFromScreen($rect.Left, $rect.Top, 0, 0, $bmp.Size)
    $bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
}
finally { $g.Dispose(); $bmp.Dispose() }

Write-Host "saved $Out"

Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue