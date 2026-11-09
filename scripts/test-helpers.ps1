#  The parts every scenario needs before it can ask anything.
#
#  Loaded by common.ps1, so a script that dot-sources common.ps1 has these without asking.
#
#  They exist because twenty-one scenarios had twenty-one slightly different versions of
#  "start the program and find its window", and a scenario that fails during its startup
#  looks exactly like a scenario that fails at what it was written to check.
#
#  Nothing here knows anything about any particular screen. A scenario that needs a number
#  from the layout asks the layout; a scenario that needs a control asks for it by name.

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

if (-not ('TestInput' -as [type])) {
    Add-Type -Namespace 'TestInput' -Name 'Uia' -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
[DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, IntPtr e);
[DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
'@
}

<#
.SYNOPSIS
    Starts the program for a test and hands back the process, its window and the desktop.

.DESCRIPTION
    The window is brought to the front and given the focus. Synthetic input goes to whatever
    has the focus, so a wheel click sent at the right place on a window that is not in front
    goes nowhere at all - and the screen does not move, which reads as a feature that is
    broken rather than as a check that aimed badly.

.PARAMETER Open
    Which screen to open first: start, unique or combined. Left empty, the program opens on
    whatever it was last told to open.

.EXAMPLE
    $app = Start-TestApp -Open combined
    ...
    Stop-TestApp $app
#>
function Start-TestApp {
    [CmdletBinding()]
    param(
        [ValidateSet('start', 'unique', 'combined')]
        [string]$Open = '',

        [int]$TimeoutSeconds = 60
    )

    $exe = @(Get-AppExe)[-1]
    $proc = if ($Open) { Start-Process -FilePath $exe -PassThru -ArgumentList '--open', $Open }
            else { Start-Process -FilePath $exe -PassThru }

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 500
        $proc.Refresh()
        if ($proc.HasExited) { throw "the program exited $($proc.ExitCode) before a window appeared" }
        if ($proc.MainWindowHandle -ne 0) { break }
    }

    if ($proc.MainWindowHandle -eq 0) {
        Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
        throw 'no window appeared'
    }

    Set-WindowFocus $proc.MainWindowHandle

    return [pscustomobject]@{
        Process = $proc
        Window = [System.Windows.Automation.AutomationElement]::FromHandle($proc.MainWindowHandle)
        Root = [System.Windows.Automation.AutomationElement]::RootElement
    }
}

function Stop-TestApp($app) {
    if ($app -and $app.Process) {
        try { $app.Process.Refresh() } catch { }
        if (-not $app.Process.HasExited) {
            Stop-Process -Id $app.Process.Id -Force -ErrorAction SilentlyContinue
        }
    }
}

<#
.SYNOPSIS
    Every control of one kind on a window, in the order they are drawn.

.DESCRIPTION
    Order matters: a row of identical buttons can only be told apart by position, and the
    tree hands them back in the order they are on the screen.

    The name is matched here rather than by a property condition, because a condition
    compares for equality and has no idea what a star means. A condition built from '*' and
    a word matches nothing at all, and returns nothing rather than failing - which reads as
    "the control is missing" and sends the reader looking for the control.
#>
function Get-Controls {
    param(
        $Window,
        [string]$ControlType = 'Button',
        [string]$NameLike = ''
    )

    $type = switch ($ControlType) {
        'Button' { [System.Windows.Automation.ControlType]::Button }
        'Text' { [System.Windows.Automation.ControlType]::Text }
        'RadioButton' { [System.Windows.Automation.ControlType]::RadioButton }
        'Edit' { [System.Windows.Automation.ControlType]::Edit }
        'List' { [System.Windows.Automation.ControlType]::List }
        default { [System.Windows.Automation.ControlType]::$ControlType }
    }

    $all = $Window.FindAll(
        [System.Windows.Automation.TreeScope]::Descendants,
        (New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty, $type)))

    $found = New-Object System.Collections.Generic.List[object]
    for ($i = 0; $i -lt $all.Count; $i++) {
        if (-not $NameLike -or $all[$i].Current.Name -like $NameLike) { $found.Add($all[$i]) }
    }
    return $found
}

<#
.SYNOPSIS
    The names of every text on a window.

.DESCRIPTION
    What is on screen, in words. A label is a thing to read and compare, where a rectangle
    is a thing to guess about.
#>
function Get-Texts($Window) {
    $words = New-Object System.Collections.Generic.List[string]
    foreach ($t in (Get-Controls $Window 'Text')) {
        $n = $t.Current.Name
        if ($n) { $words.Add($n) }
    }
    return $words
}

<#
.SYNOPSIS
    Presses a control, whichever way this kind of control is pressed.

.DESCRIPTION
    A plain button answers to Invoke. A toggle button answers to Toggle. A radio button
    answers to SelectionItem. Asking in that order is cheaper than making every caller
    declare which kind it has, and a control that answers to none is reported as false
    rather than throwing from three levels down.
#>
function Invoke-Control($Control) {
    foreach ($pattern in @(
            [System.Windows.Automation.InvokePattern]::Pattern,
            [System.Windows.Automation.TogglePattern]::Pattern,
            [System.Windows.Automation.SelectionItemPattern]::Pattern)) {
        try {
            $p = $Control.GetCurrentPattern($pattern)
            if ($p -is [System.Windows.Automation.InvokePattern]) { $p.Invoke(); return $true }
            if ($p -is [System.Windows.Automation.TogglePattern]) { $p.Toggle(); return $true }
            if ($p -is [System.Windows.Automation.SelectionItemPattern]) { $p.Select(); return $true }
        }
        catch { }
    }
    return $false
}

<#
.SYNOPSIS
    Sends the wheel a number of notches at a point on the screen.
#>
function Send-WheelAt([int]$X, [int]$Y, [int]$Clicks) {
    [void][TestInput.Uia]::SetCursorPos($X, $Y)
    Start-Sleep -Milliseconds 300

    for ($i = 0; $i -lt $Clicks; $i++) {
        # One notch down, as the unsigned form of minus one hundred and twenty.
        [TestInput.Uia]::mouse_event(0x0800, 0, 0, [uint32]4294967176, [IntPtr]::Zero)
        Start-Sleep -Milliseconds 120
    }
    Start-Sleep -Seconds 1
}

<#
.SYNOPSIS
    Where a control is drawn, in whole pixels, or nothing if it is not on screen.

.DESCRIPTION
    Read off the control itself. A point worked out by adding offsets to something else is a
    guess about the layout, and it is wrong the moment the layout is a different size.
#>
function Get-Box($Control) {
    $r = $Control.Current.BoundingRectangle
    if ($r.IsEmpty) { return $null }

    return [pscustomobject]@{
        X = [int]$r.X
        Y = [int]$r.Y
        W = [int]$r.Width
        H = [int]$r.Height
        CentreX = [int]($r.X + $r.Width / 2)
        CentreY = [int]($r.Y + $r.Height / 2)
    }
}

<#
.SYNOPSIS
    Says PASS or FAIL and sets the exit code.

.DESCRIPTION
    Every scenario ends the same way, so that reading the end of twenty-one of them is one
    habit and not twenty-one. The first argument is the finding in a sentence; the rest are
    lines under it. A failure exits non-zero, so a run of them can be chained.
#>
function Write-Result([bool]$Passed, [string]$Finding, [string[]]$Detail = @()) {
    Write-Host ''
    foreach ($line in $Detail) { Write-Host "  $line" }

    Write-Host ''
    if ($Passed) {
        Write-Host "PASS: $Finding"
        exit 0
    }

    Write-Host "FAIL: $Finding"
    exit 1
}