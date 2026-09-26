# Captures the app on a given screen by driving the tab bar with UI Automation,
# then screenshots the window. Verifying a redesign by eye needs this: the tabs are
# RadioButtons with no automation id, so they are found by their label text.
#
# Usage:
#   pwsh -File scripts/capture-screen.ps1 -Tab "Мои проекты"       -Out doc\shots\projects.png
#   pwsh -File scripts/capture-screen.ps1 -Tab "Выбор режима"      -Out doc\shots\mode.png
#   pwsh -File scripts/capture-screen.ps1 -Tab "Уникальные папки"  -Out doc\shots\unique.png
#   pwsh -File scripts/capture-screen.ps1 -Tab "Комбинированный"   -Out doc\shots\combined.png

param(
    [string]$Tab = 'Мои проекты',
    [string]$Out = 'doc\shots\screen.png',
    [int]$TimeoutSeconds = 30,
    [switch]$OpenProject,
    [int]$OpenIndex = 0,
    [string]$ProjectName = ''
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class Win2 {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
}
"@

function Get-ByName($rootEl, [string]$label, [string]$typeName) {
    $ct = [System.Windows.Automation.ControlType]::$typeName
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty, $ct)
    foreach ($b in $rootEl.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)) {
        if ($b.Current.Name -like "*$label*") { return $b }
    }
    return $null
}

function Get-TabElement($rootEl, [string]$label) { Get-ByName $rootEl $label 'RadioButton' }

function Invoke-Element($el) {
    $p = $el.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
    $p.Invoke()
}

try {
    $exe = Join-Path $root 'bin\Release\net8.0-windows\PhotoBookRenamer.exe'
    $proc = Start-Process -FilePath $exe -PassThru
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 500
        $proc.Refresh()
        if ($proc.HasExited) { Write-Host "FAIL: app exited $($proc.ExitCode)"; exit 1 }
        if ($proc.MainWindowHandle -ne 0) { break }
    }
    if ($proc.MainWindowHandle -eq 0) { Write-Host 'FAIL: no window'; Stop-Process -Id $proc.Id -Force; exit 1 }

    [void][Win2]::ShowWindow($proc.MainWindowHandle, 3)
    [void][Win2]::SetForegroundWindow($proc.MainWindowHandle)
    Start-Sleep -Seconds 2

    if ($Tab -ne 'Мои проекты') {
        $el = [System.Windows.Automation.AutomationElement]::FromHandle($proc.MainWindowHandle)
        $tabEl = Get-TabElement $el $Tab
        if ($null -eq $tabEl) {
            Write-Host "FAIL: tab '$Tab' not found. Available:"
            $cond = New-Object System.Windows.Automation.PropertyCondition(
                [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                [System.Windows.Automation.ControlType]::RadioButton)
            $el.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond) |
                ForEach-Object { Write-Host "  - $($_.Current.Name)" }
            Stop-Process -Id $proc.Id -Force
            exit 1
        }
        $sel = $tabEl.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
        $sel.Select()
        Start-Sleep -Seconds 2
    }

    # Optionally open a project so the editor screens show real data.
    if ($OpenProject) {
        $el = [System.Windows.Automation.AutomationElement]::FromHandle($proc.MainWindowHandle)
        $target = $null

        if ($ProjectName) {
            # Walk up from the card's title text until a container holding an
            # "Открыть проект" button is found. Index-based selection is fragile
            # because the list is ordered by LastModified.
            $textCond = New-Object System.Windows.Automation.PropertyCondition(
                [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                [System.Windows.Automation.ControlType]::Text)
            $titleEl = $null
            foreach ($t in $el.FindAll([System.Windows.Automation.TreeScope]::Descendants, $textCond)) {
                if ($t.Current.Name -like "*$ProjectName*") { $titleEl = $t; break }
            }
            if ($null -eq $titleEl) {
                Write-Host "FAIL: no project titled '$ProjectName'"
                Stop-Process -Id $proc.Id -Force
                exit 1
            }

            $walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker
            $node = $walker.GetParent($titleEl)
            for ($up = 0; $up -lt 10 -and $null -ne $node; $up++) {
                $btnCond = New-Object System.Windows.Automation.PropertyCondition(
                    [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                    [System.Windows.Automation.ControlType]::Button)
                foreach ($b in $node.FindAll([System.Windows.Automation.TreeScope]::Descendants, $btnCond)) {
                    if ($b.Current.Name -like '*Открыть проект*') { $target = $b; break }
                }
                if ($null -ne $target) { break }
                $node = $walker.GetParent($node)
            }
            if ($null -eq $target) {
                Write-Host "FAIL: found the title '$ProjectName' but no Open button in its card"
                Stop-Process -Id $proc.Id -Force
                exit 1
            }
        }
        else {
            $cond = New-Object System.Windows.Automation.PropertyCondition(
                [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                [System.Windows.Automation.ControlType]::Button)
            $openButtons = @($el.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond) |
                Where-Object { $_.Current.Name -like '*Открыть проект*' })
            if ($OpenIndex -ge $openButtons.Count) {
                Write-Host "FAIL: -OpenIndex $OpenIndex but only $($openButtons.Count) project cards exist"
                Stop-Process -Id $proc.Id -Force
                exit 1
            }
            $target = $openButtons[$OpenIndex]
        }

        Invoke-Element $target
        # Project loading is async (thumbnails, double BeginInvoke), so give it room.
        Start-Sleep -Seconds 7
    }

    $r = New-Object Win2+RECT
    [void][Win2]::GetWindowRect($proc.MainWindowHandle, [ref]$r)
    $w = $r.Right - $r.Left; $h = $r.Bottom - $r.Top

    if ($w -le 0 -or $h -le 0) {
        Write-Host "FAIL: window rect is ${w}x${h} - the app probably crashed while opening the project."
        Get-WinEvent -FilterHashtable @{ LogName = 'Application'; StartTime = (Get-Date).AddMinutes(-3) } -ErrorAction SilentlyContinue |
            Where-Object { $_.ProviderName -in 'Application Error', '.NET Runtime' } |
            Select-Object -First 2 | ForEach-Object { Write-Host $_.Message }
        Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
        exit 1
    }

    $bmp = New-Object System.Drawing.Bitmap $w, $h
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($r.Left, $r.Top, 0, 0, (New-Object System.Drawing.Size($w, $h)))

    $full = Join-Path $root $Out
    $dir = Split-Path -Parent $full
    if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
    $bmp.Save($full, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
    Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue

    Write-Host "saved $full (${w}x${h}) for tab '$Tab'"
    exit 0
}
finally {
    Pop-Location
}
