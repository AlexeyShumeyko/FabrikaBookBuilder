# Opens the account button in the header and screenshots whichever window answers: the
# sign-in form for a machine without a session, the account card for one with a session.
#
# The sign-in form is a separate top-level window, so the main-window capture scripts cannot
# see it. Both states are captured in one run, in that order, because a signed-in machine has
# to visit the account card - and leave it - before the form can be photographed at all.
#
# Usage:
#   pwsh -File scripts/capture-login.ps1
#   pwsh -File scripts/capture-login.ps1 -SignOut            # may clear the stored session
#   pwsh -File scripts/capture-login.ps1 -OutLogin doc\shots\login.png

param(
    [string]$OutLogin = 'doc\shots\login.png',
    [string]$OutAccount = 'doc\shots\account.png',
    # The account card's "Выйти" is the only way to reach the form from a signed-in machine.
    # It is a switch rather than the default because it drops the stored token: the customer
    # types the password again, nothing else is lost.
    [switch]$SignOut,
    # One deliberate attempt with an address nobody holds, to see the refusal drawn in the
    # window: the error line, the emptied password field, the button back to "Войти". It is a
    # switch because it costs two requests to the shared platform and sends a password, and a
    # check that runs on every capture is a check nobody meant to run.
    [switch]$TryWrongPassword,
    [string]$OutError = 'doc\shots\login-error.png',
    [int]$TimeoutSeconds = 30
)

# The harness console runs on code page 866, which turns every Cyrillic string this
# script prints into "?" and floods the console with mojibake. Force UTF-8 on
# both channels; a child powershell.exe resets these on its own, so it has to be set
# inside each script rather than once in the caller.
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8

$ErrorActionPreference = 'Stop'

# One place that knows where the built program is.
. "$PSScriptRoot\common.ps1"
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @"
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
public class WinLogin {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr h, uint cmd);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    private delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc cb, IntPtr l);

    /// <summary>
    /// Visible top-level windows of a process, the main window excluded. A right answer here
    /// is "one more window than before", so the caller compares counts rather than titles.
    /// </summary>
    public static IntPtr[] DialogWindows(int processId, IntPtr main) {
        var found = new List<IntPtr>();
        EnumWindows((h, l) => {
            uint pid;
            GetWindowThreadProcessId(h, out pid);
            if (pid == (uint)processId && h != main && IsWindowVisible(h)) found.Add(h);
            return true;
        }, IntPtr.Zero);
        return found.ToArray();
    }

    public static string TitleOf(IntPtr h) {
        var s = new StringBuilder(256);
        GetWindowText(h, s, s.Capacity);
        return s.ToString();
    }
}
"@

function Find-Button($rootEl, [string]$label) {
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::Button)
    foreach ($b in $rootEl.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)) {
        if ($b.Current.Name -like "*$label*") { return $b }
    }
    return $null
}

function Invoke-Button($el, [string]$label) {
    if ($null -eq $el) { throw "no button named '$label'" }
    $el.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
}

# The header button that opens these windows. Its automation name is the invitation, and it
# stays that way after signing in, so it is the same click in both states.
function Open-AccountWindow($proc) {
    $main = [System.Windows.Automation.AutomationElement]::FromHandle([intptr]$proc.MainWindowHandle)
    Invoke-Button (Find-Button $main 'Вход') 'Вход'

    $deadline = (Get-Date).AddSeconds(15)
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 500
        $dlg = [WinLogin]::DialogWindows($proc.Id, [intptr]$proc.MainWindowHandle)
        if ($dlg.Length -gt 0) { return $dlg[0] }
    }
    throw 'the sign-in window never appeared'
}

function Find-Edit($rootEl, [int]$index) {
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::Edit)
    $edits = @($rootEl.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond))
    if ($edits.Count -le $index) { throw "expected at least $($index + 1) fields, found $($edits.Count)" }
    return $edits[$index]
}

function Set-EditText($edit, [string]$text) {
    $edit.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($text)
}

function Get-Texts($rootEl) {
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::Text)
    foreach ($t in $rootEl.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)) {
        if ($t.Current.Name) { Write-Host "text: $($t.Current.Name)" }
    }
}

function Raise-Window([IntPtr]$hwnd) {
    # Set-WindowInFront maximizes, which is right for a screenshot of the whole main window
    # and wrong here: these windows size themselves, and a maximized card is not the card.
    if (-not ('WinRaise' -as [type])) {
        Add-Type @"
using System;
using System.Runtime.InteropServices;
public class WinRaise {
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr h);
    [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, IntPtr extra);
}
"@
    }

    for ($i = 0; $i -lt 8; $i++) {
        # See Set-WindowInFront: tapping alt is how a process becomes the last one with input.
        [void][WinRaise]::keybd_event(0x12, 0, 0, [IntPtr]::Zero)
        [void][WinRaise]::keybd_event(0x12, 0, 2, [IntPtr]::Zero)
        [void][WinRaise]::BringWindowToTop($hwnd)
        [void][WinRaise]::SetForegroundWindow($hwnd)
        Start-Sleep -Milliseconds 300
        try { Assert-WindowInFront $hwnd; return } catch { }
    }
    throw 'the window could not be brought to the front'
}

function Save-Shot([IntPtr]$hwnd, [string]$out) {
    # Photographed from the screen, so the window has to be the one in front.
    Raise-Window $hwnd

    $r = New-Object WinLogin+RECT
    [void][WinLogin]::GetWindowRect($hwnd, [ref]$r)
    $w = $r.Right - $r.Left; $h = $r.Bottom - $r.Top
    if ($w -le 0 -or $h -le 0) { throw "bad window rect ${w}x${h}" }

    $bmp = New-Object System.Drawing.Bitmap $w, $h
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($r.Left, $r.Top, 0, 0, (New-Object System.Drawing.Size($w, $h)))

    $full = Join-Path $root $out
    $dir = Split-Path -Parent $full
    if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
    $bmp.Save($full, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()

    Write-Host "saved $full (${w}x${h})"
}

try {
    $exe = Get-AppExe
    $proc = Start-Process -FilePath $exe -PassThru
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 500
        $proc.Refresh()
        if ($proc.HasExited) { throw "app exited $($proc.ExitCode)" }
        if ($proc.MainWindowHandle -ne 0) { break }
    }
    if ($proc.MainWindowHandle -eq 0) { throw 'no main window' }

    Set-WindowFocus $proc.MainWindowHandle
    Start-Sleep -Seconds 2

    $dlg = Open-AccountWindow $proc
    Start-Sleep -Seconds 1

    $title = [WinLogin]::TitleOf($dlg)
    Write-Host "opened '$title'"

    if ($title -eq 'Аккаунт') {
        Save-Shot $dlg $OutAccount

        if (-not $SignOut) {
            Write-Host 'signed in: only the account card was captured; pass -SignOut for the form'
            exit 0
        }

        # Signing out drops the token and closes the card; the form is one more click away.
        $card = [System.Windows.Automation.AutomationElement]::FromHandle($dlg)
        Invoke-Button (Find-Button $card 'Выйти') 'Выйти'
        Start-Sleep -Seconds 2

        $dlg = Open-AccountWindow $proc
        Start-Sleep -Seconds 1
        $title = [WinLogin]::TitleOf($dlg)
    }

    if ($title -ne 'Вход в аккаунт') { throw "unexpected window '$title'" }
    Save-Shot $dlg $OutLogin

    if ($TryWrongPassword) {
        $form = [System.Windows.Automation.AutomationElement]::FromHandle($dlg)
        Set-EditText (Find-Edit $form 0) 'no-such-account@example.com'
        Set-EditText (Find-Edit $form 1) 'not-the-password'
        Invoke-Button (Find-Button $form 'Войти') 'Войти'

        # The sites are asked, not polled: one attempt, then a wait long enough for a refusal.
        Start-Sleep -Seconds 8
        $proc.Refresh()
        if ($proc.HasExited) { throw 'the app died while the password was refused' }

        Save-Shot $dlg $OutError
        $form = [System.Windows.Automation.AutomationElement]::FromHandle($dlg)
        Get-Texts $form
        $password = (Find-Edit $form 1).GetCurrentPattern(
            [System.Windows.Automation.ValuePattern]::Pattern).Current.Value
        if ($password) { throw 'the password field still holds the refused password' }
        Write-Host 'the password field was emptied after the refusal'
    }

    exit 0
}
finally {
    Get-Process -Id $proc.Id -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Pop-Location
}
