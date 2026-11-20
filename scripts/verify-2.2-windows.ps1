# Verifies the two page windows of 2.2 on a real launch: the guide and the support page.
#
# Both are windows that only exist when the program runs, so a build says nothing about them:
# a resource key that resolves in one theme, a spine button that is wired to nothing, a channel
# row whose address is empty - all of it compiles. This script asks the running program:
#
#   * the guide opens from the header and shows its first chapter;
#   * the spine moves the page: after a click on the last section the chapter is on screen,
#     which is the only proof that the click is connected to anything;
#   * the support page opens from the header, carries the hours and four channels;
#   * each channel names an address, so no row leads nowhere.
#
# Usage:  pwsh -File scripts\verify-2.2-windows.ps1 [-TimeoutSeconds 40]
#
# NOTE: this file contains Cyrillic literals and must stay UTF-8 **with BOM**.

param(
    [int]$TimeoutSeconds = 40
)

[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8

$ErrorActionPreference = 'Stop'

. "$PSScriptRoot\common.ps1"
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class Win2 {
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
    private delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] private static extern int GetWindowTextLength(IntPtr h);
    [DllImport("user32.dll")] private static extern int GetWindowText(IntPtr h, System.Text.StringBuilder s, int n);
    public static string Title(IntPtr h) {
        int n = GetWindowTextLength(h);
        var sb = new System.Text.StringBuilder(n + 2);
        GetWindowText(h, sb, sb.Capacity);
        return sb.ToString();
    }
    public static System.Collections.Generic.List<IntPtr> Visible(uint pid) {
        var found = new System.Collections.Generic.List<IntPtr>();
        EnumWindows((h, l) => {
            uint owner;
            GetWindowThreadProcessId(h, out owner);
            if (owner == pid && IsWindowVisible(h)) found.Add(h);
            return true;
        }, IntPtr.Zero);
        return found;
    }
}
"@

$checks = 0
function Pass([string]$what) {
    $script:checks++
    Write-Host "  ok  $what"
}

function Wait-Window([int]$processId, [string]$title, [int]$seconds) {
    $deadline = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 300
        foreach ($h in [Win2]::Visible($processId)) {
            if ([Win2]::Title($h) -eq $title) { return $h }
        }
    }
    return [intptr]::Zero
}

function Find-Button($element, [string]$name) {
    $condition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::Button)
    foreach ($b in $element.FindAll([System.Windows.Automation.TreeScope]::Descendants, $condition)) {
        if ($b.Current.Name -like "*$name*") { return $b }
    }
    return $null
}

function Wait-Button($element, [string]$name, [int]$seconds) {
    $deadline = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $deadline) {
        $found = Find-Button $element $name
        if ($null -ne $found) { return $found }
        Start-Sleep -Milliseconds 300
    }
    return $null
}

function Find-Scrollable($element) {
    $condition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::IsScrollPatternAvailableProperty, $true)
    $all = $element.FindAll([System.Windows.Automation.TreeScope]::Descendants, $condition)
    if ($all.Count -eq 0) { return $null }
    return $all.Item(0)
}

function Find-Text($element, [string]$name) {
    $condition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::Text)
    foreach ($t in $element.FindAll([System.Windows.Automation.TreeScope]::Descendants, $condition)) {
        if ($t.Current.Name -like "*$name*") { return $t }
    }
    return $null
}

$exe = Get-AppExe
if (-not (Test-Path $exe)) { throw "no build at $exe" }

$proc = Start-Process -FilePath $exe -PassThru
try {
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 400
        $proc.Refresh()
        if ($proc.HasExited) { throw "the program exited with $($proc.ExitCode)" }
        if ($proc.MainWindowHandle -ne 0) { break }
    }
    if ($proc.MainWindowHandle -eq 0) { throw 'the main window never appeared' }
    [void][Win2]::ShowWindow($proc.MainWindowHandle, 3)
    [void][Win2]::SetForegroundWindow($proc.MainWindowHandle)
    Start-Sleep -Seconds 3

    $main = [System.Windows.Automation.AutomationElement]::FromHandle($proc.MainWindowHandle)

    # ---- the guide ------------------------------------------------------------------------
    $help = Wait-Button $main 'Помощь' 20
    if ($null -eq $help) { throw 'the Помощь button was not found' }
    $help.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()

    $guideHandle = Wait-Window $proc.Id 'Руководство' 20
    if ($guideHandle -eq [intptr]::Zero) { throw 'the guide did not open' }
    $guide = [System.Windows.Automation.AutomationElement]::FromHandle($guideHandle)
    Pass 'the guide opens from the header'

    $first = Find-Text $guide 'Что делает программа'
    if ($null -eq $first) { throw 'the guide does not show its first chapter' }
    Pass 'the guide shows its first chapter'

    # The page's own scroll position is the honest measure: a text element's IsOffscreen is
    # reported loosely inside a scroll view, but the scroll percentage is what a person sees.
    $scroll = Find-Scrollable $guide
    if ($null -eq $scroll) { throw 'the guide has no scrollable page' }
    $before = $scroll.GetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern).Current.VerticalScrollPercent
    if ($before -gt 5) { throw "the page is already scrolled to $before% before anything was clicked" }

    $spine = Wait-Button $guide 'Раздел 07' 10
    if ($null -eq $spine) { throw 'the spine has no section 07' }
    $spine.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Milliseconds 900

    $after = $scroll.GetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern).Current.VerticalScrollPercent
    if ($after -le $before + 5) { throw "the spine click moved the page from $before% to $after%" }
    Pass "a spine section scrolls the page (from $([int]$before)% to $([int]$after)%)"

    [void][Win2]::SetForegroundWindow($guideHandle)
    $guide.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
    Start-Sleep -Seconds 1

    # ---- the support page -----------------------------------------------------------------
    $contacts = Wait-Button $main 'Контакты' 10
    if ($null -eq $contacts) { throw 'the Контакты button was not found' }
    $contacts.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()

    $supportHandle = Wait-Window $proc.Id 'Поддержка' 20
    if ($supportHandle -eq [intptr]::Zero) { throw 'the support page did not open' }
    $support = [System.Windows.Automation.AutomationElement]::FromHandle($supportHandle)
    Pass 'the support page opens from the header'

    if ($null -eq (Find-Text $support '9:00 — 21:00')) { throw 'the support page does not show the hours' }
    Pass 'the support page shows the hours'

    $channels = @('Telegram', 'Почта', 'ВКонтакте', 'MAX')
    foreach ($name in $channels) {
        $row = Find-Button $support $name
        if ($null -eq $row) { throw "the support page has no $name channel" }
    }
    Pass 'all four channels are there'

    # An address on the row is what makes it a channel rather than a label.
    $addresses = @('@fabrika_support', 'support@fabrika-fotoknigi.com', 'vk.com/fabrika_fotoknigi', 'max.ru/fabrika')
    foreach ($address in $addresses) {
        if ($null -eq (Find-Text $support $address)) { throw "no channel shows $address" }
    }
    Pass 'every channel names its address'

    $support.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
    Start-Sleep -Milliseconds 500

    Write-Host ''
    Write-Host "PASS: $checks checks on the guide and the support page."
}
finally {
    if ($proc -and -not $proc.HasExited) { $proc.Kill() }
    Pop-Location
}
