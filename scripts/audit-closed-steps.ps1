# Re-checks every step the campaign already closed, with a command that can fail.
#
# Written after backlog item 6 turned out to be the other half of wave 1 step 1.3. That step
# was closed on the strength of the port types existing, and the half nobody looked at - the
# layer still opening files itself - sat unnoticed through the whole of wave 2. A step closed
# on "the type is there" cannot fail, which is the same as not having checked.
#
# Every check below is a fact about the repository as it is now. If a step really is done,
# the check passes. If it is not, the check says so in one line.
#
# Usage:  pwsh -File scripts\audit-closed-steps.ps1
# Exit 1 if any check fails.
#
# NOTE: this file contains Cyrillic literals and must stay UTF-8 **with BOM**.

[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$script:Pass = 0
$script:Fail = 0
$script:Failures = New-Object System.Collections.Generic.List[string]

function Check([string]$step, [string]$claim, [bool]$ok, [string]$detail = '') {
    if ($ok) {
        $script:Pass++
        Write-Host ("  PASS  {0,-6} {1}" -f $step, $claim)
    } else {
        $script:Fail++
        $script:Failures.Add("$step - $claim")
        Write-Host ("  FAIL  {0,-6} {1}" -f $step, $claim)
        if ($detail) { foreach ($d in ($detail -split "`n")) { Write-Host ("          $d") } }
    }
}

function Source([string]$pattern, [string[]]$paths) {
    $hits = @()
    foreach ($p in $paths) {
        $files = Get-ChildItem $p -Recurse -Filter *.cs -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -notmatch '\\(obj|bin)\\' }
        $hits += @($files | Select-String -Pattern $pattern)
    }
    return $hits
}

Write-Host ''
Write-Host '=== ВОЛНА 1 ================================================='
Write-Host ''

# --- 1.1 -----------------------------------------------------------------
$props = 'Directory.Build.props'
$hasProps = Test-Path $props
$warns = $false
if ($hasProps) {
    $warns = (Get-Content $props -Raw) -match 'TreatWarningsAsErrors'
}
$editor = Test-Path '.editorconfig'
Check '1.1' 'Directory.Build.props + TreatWarningsAsErrors' ($hasProps -and $warns) `
    ("Directory.Build.props: {0}, TreatWarningsAsErrors: {1}" -f $hasProps, $warns)
Check '1.1' '.editorconfig на месте' $editor ''

# --- 1.2 -----------------------------------------------------------------
$coreProj = 'src\PhotoBook.Core\PhotoBook.Core.csproj'
$core = Get-Content $coreProj -Raw
$tfm = if ($core -match '<TargetFramework>([^<]+)<') { $Matches[1] } else { '?' }
$pkgs = ([regex]::Matches($core, '<PackageReference')).Count
$refs = ([regex]::Matches($core, '<ProjectReference')).Count
Check '1.2' 'PhotoBook.Core: net8.0' ($tfm -eq 'net8.0') ("TargetFramework: $tfm")
Check '1.2' 'PhotoBook.Core: ноль пакетов и ссылок' ($pkgs -eq 0 -and $refs -eq 0) `
    ("PackageReference: $pkgs, ProjectReference: $refs")

# --- 1.3 -----------------------------------------------------------------
# The step that was closed on "the ports exist". The half that was missed.
$appFiles = @('src\PhotoBook.Application')
$disk = @()
foreach ($f in (Get-ChildItem src\PhotoBook.Application -Filter *.cs -File)) {
    # Comment lines are excluded on purpose. ThumbnailStore explains in prose that
    # File.Exists used to live there, and that sentence is worth more than a green tick;
    # a check that read it as code would push the next person to delete the explanation.
    foreach ($hit in (Select-String -Path $f.FullName -Pattern '\b(Directory|File|FileInfo|DirectoryInfo)\.[A-Z]')) {
        if ($hit.Line.TrimStart().StartsWith('//')) { continue }
        $disk += $hit
    }
}
$ioUsings = @(Select-String -Path src\PhotoBook.Application\*.cs -Pattern '^using System\.IO;')
$offenders = @($disk | ForEach-Object { (Split-Path -Leaf $_.Path) } | Sort-Object -Unique)
Check '1.3' 'слой Application не обращается к диску' ($disk.Count -eq 0) `
    ("`n          осталось обращений: $($disk.Count) в файлах: " + ($offenders -join ', '))
Check '1.3' 'в Application нет using System.IO' ($ioUsings.Count -eq 0) `
    ("`n          файлов: " + ((@($ioUsings | ForEach-Object { Split-Path -Leaf $_.Path })) -join ', '))

# --- 1.4 -----------------------------------------------------------------
$ui = @(Get-ChildItem src\PhotoBook.Infrastructure, src\PhotoBook.Application -Recurse -Filter *.cs |
    Where-Object { $_.FullName -notmatch '\\(obj|bin)\\' } |
    Select-String -Pattern 'System\.Windows|WindowsBase|System\.Drawing\.Common')
Check '1.4' 'Application и Infrastructure без WPF' ($ui.Count -eq 0) `
    ("`n          ссылок: $($ui.Count)" + $(if ($ui.Count) { "`n          " + (($ui | ForEach-Object { "$($_.Filename):$($_.LineNumber)" }) -join ', ') } else { '' }))

# --- 1.5 -----------------------------------------------------------------
$shellProj = 'src\PhotoBook.Desktop.Wpf\PhotoBook.Desktop.Wpf.csproj'
$shellOk = Test-Path $shellProj
$asm = if ($shellOk) { if ((Get-Content $shellProj -Raw) -match '<AssemblyName>([^<]+)<') { $Matches[1] } else { '?' } } else { '?' }
Check '1.5' 'оболочка отдельным проектом' $shellOk ''
Check '1.5' 'имя сборки PhotoBookRenamer (знают установщик и ярлыки)' ($asm -eq 'PhotoBookRenamer') `
    ("AssemblyName: $asm")
$locator = @(Get-ChildItem src -Recurse -Filter *.cs |
    Where-Object { $_.FullName -notmatch '\\(obj|bin)\\' -and $_.FullName -notmatch 'App\.xaml\.cs$' } |
    Select-String -Pattern 'ServiceLocator|GetService<')
Check '1.5' 'сервис-локатор убран (вне точки сборки)' ($locator.Count -eq 0) `
    ("`n          осталось: $($locator.Count)" + $(if ($locator.Count) { "`n          " + (($locator | ForEach-Object { "$($_.Filename):$($_.LineNumber)" }) -join ', ') } else { '' }))

# --- 1.6 -----------------------------------------------------------------
$impl = @(Get-ChildItem src\PhotoBook.Infrastructure -Filter *.cs -File |
    Where-Object { (Get-Content $_.FullName -Raw) -match 'class\s+\w+\s*:\s*I(File|Image|Thumbnail|Update|Logging|Project)\w*Service' -or (Get-Content $_.FullName -Raw) -match 'class\s+\w+\s*:\s*I\w+(Repository|Store|PickFiles|UpdateFeed)' })
$publicImpl = @($impl | Where-Object { (Get-Content $_.FullName -Raw) -notmatch 'internal\s+class' })
Check '1.6' 'реализации портов internal' ($publicImpl.Count -eq 0) `
    ("`n          публичных: " + ((@($publicImpl | ForEach-Object { $_.Name })) -join ', '))
$vis = @(Get-ChildItem src -Recurse -Filter AssemblyInfo.cs |
    Where-Object { $_.FullName -notmatch '\\(obj|bin)\\' } |
    Select-String -Pattern 'InternalsVisibleTo')
$badVis = @($vis | Where-Object { $_.Line -notmatch 'Tests' })
Check '1.6' 'InternalsVisibleTo только для тестов' ($badVis.Count -eq 0) `
    ("`n          строк: $($vis.Count), не про тесты: $($badVis.Count)")

Write-Host ''
Write-Host '=== ВОЛНА 2 ================================================='
Write-Host ''

# --- 2.1 -----------------------------------------------------------------
# One sink. A null-object is not a second sink - Journal.NullJournal writes nowhere and
# exists so a caller with no journal installed does not have to null-check - so it is
# excluded explicitly rather than by counting types.
$journalImpl = @(Get-ChildItem src -Recurse -Filter *.cs |
    Where-Object { $_.FullName -notmatch '\\(obj|bin)\\' } |
    Select-String -Pattern 'class\s+(\w*Journal\w*)\s*:\s*IJournal')
$realSinks = @($journalImpl | Where-Object { $_.Line -notmatch 'NullJournal' })
$nulls = @($journalImpl | Where-Object { $_.Line -match 'NullJournal' })
Check '2.1' 'у журнала один настоящий сток' ($realSinks.Count -eq 1) `
    ("`n          стоков: $($realSinks.Count)" + $(if ($realSinks.Count) { "`n          " + (($realSinks | ForEach-Object { "$($_.Filename):$($_.LineNumber)" }) -join ', ') } else { '' }))
Check '2.1' 'пустой объект вложен и ничего не пишет' (@($nulls | Where-Object { $_.Line -notmatch 'private' }).Count -eq 0) `
    ("`n          NullJournal вне класса: " + (@($nulls | Where-Object { $_.Line -notmatch 'private' }).Count))
$consoleErr = @(Get-ChildItem src -Recurse -Filter *.cs |
    Where-Object { $_.FullName -notmatch '\\(obj|bin)\\' } |
    Select-String -Pattern 'Console\.(Write(Error)?|Out)\(')
Check '2.1' 'ошибки не пишутся в консоль (у ярлыка её нет)' ($consoleErr.Count -eq 0) `
    ("`n          вызовов Console: $($consoleErr.Count)" + $(if ($consoleErr.Count) { "`n          " + (($consoleErr | ForEach-Object { "$($_.Filename):$($_.LineNumber)" }) -join ', ') } else { '' }))

# --- 2.2 -----------------------------------------------------------------
# The property is "no dead code in the dialogs", not a fixed list of names. A list rots:
# the first version of this check named MultiFolderBrowserDialog as dead, and it is alive -
# UniqueFoldersViewModel.Folders.cs opens it on the main path. So the check asks the real
# question instead: is there a dialog class that nothing in the solution refers to?
$allSource = @(Get-ChildItem src -Recurse -File -Include *.cs,*.xaml |
    Where-Object { $_.FullName -notmatch '\\(obj|bin)\\' })
$dialogClasses = @()
foreach ($f in (Get-ChildItem src -Recurse -Filter *.cs -File |
    Where-Object { $_.FullName -notmatch '\\(obj|bin)\\' -and $_.DirectoryName -match 'Dialogs' })) {
    foreach ($m in [regex]::Matches([System.IO.File]::ReadAllText($f.FullName), 'class\s+(\w+Dialog)\s*:\s*Window')) {
        $dialogClasses += $m.Groups[1].Value
    }
}
$unused = @()
foreach ($d in $dialogClasses) {
    $refs = @($allSource | Select-String -Pattern "\b$d\b").Count
    # Three references is the floor for a live dialog: the class declaration, the
    # x:Class in its own markup, and the place that opens it.
    if ($refs -lt 3) { $unused += "$d ($refs)" }
}
Check '2.2' 'нет диалога, на который никто не ссылается' ($unused.Count -eq 0) `
    ("`n          диалогов всего: $($dialogClasses.Count), без ссылок: " + ($unused -join ', '))

# --- 2.4 -----------------------------------------------------------------
$big = @(Get-ChildItem src\PhotoBook.Desktop.Wpf\Presentation\ViewModels -Filter *.cs -File |
    Where-Object { $_.FullName -notmatch '\\(obj|bin)\\' -and ([System.IO.File]::ReadAllLines($_.FullName).Count -gt 500) })
$largest = Get-ChildItem src\PhotoBook.Desktop.Wpf\Presentation\ViewModels -Filter *.cs -File |
    Where-Object { $_.FullName -notmatch '\\(obj|bin)\\' } |
    Sort-Object { [System.IO.File]::ReadAllLines($_.FullName).Count } -Descending |
    Select-Object -First 1
$largestLines = [System.IO.File]::ReadAllLines($largest.FullName).Count
Check '2.4' 'нет файла модели представления крупнее 500 строк' ($big.Count -eq 0) `
    ("`n          крупнейший: $($largest.Name) = $largestLines строк")

# --- 2.6 -----------------------------------------------------------------
$convDir = 'src\PhotoBook.Desktop.Wpf\Presentation\Converters'
$multi = @()
foreach ($f in (Get-ChildItem $convDir -Filter *.cs -File)) {
    $text = Get-Content $f.FullName -Raw
    $classes = ([regex]::Matches($text, 'public\s+class\s+\w+')).Count
    if ($classes -gt 1) { $multi += "$($f.Name) ($classes)" }
}
Check '2.6' 'один класс конвертера на файл' ($multi.Count -eq 0) `
    ("`n          файлов с несколькими классами: " + ($multi -join ', '))

# --- 2.8 -----------------------------------------------------------------
$bare = @(Get-ChildItem src\PhotoBook.Application, src\PhotoBook.Infrastructure -Recurse -Filter *.cs |
    Where-Object { $_.FullName -notmatch '\\(obj|bin)\\' } |
    Select-String -Pattern '^\s*Task<\s*bool\s*>|^\s*Task\s+bool\b')
Check '2.8' 'нет метода, возвращающего Task<bool> вместо Result' ($bare.Count -eq 0) `
    ("`n          найдено: $($bare.Count)" + $(if ($bare.Count) { "`n          " + (($bare | ForEach-Object { "$($_.Filename):$($_.LineNumber)" }) -join ', ') } else { '' }))

# --- 2.9 (скрипты проверки) ------------------------------------------------
$hardcoded = @(Select-String -Path scripts\*.ps1 -Pattern 'src\\PhotoBook\.Desktop\.Wpf\\bin' |
    Where-Object { $_.Line -notmatch 'Get-AppExe' -and $_.Filename -ne 'common.ps1' })
Check '2.9' 'скрипты не зашивают путь к сборке' ($hardcoded.Count -le 1) `
    ("`n          осталось: $($hardcoded.Count) (один запасной в verify-startup допустим)")

# --- проверки ресурсов, которые молчали -----------------------------------
# A view may declare styles of its own, and it does: nineteen of them across the folders
# dialog and the combined mode. Collect the application-wide keys and the file-local ones
# separately, exactly as scripts\check-resource-refs.ps1 does, or every local style reads as
# a missing one.
$styleDir = Join-Path $root 'src\PhotoBook.Desktop.Wpf\Styles'
$global = New-Object System.Collections.Generic.HashSet[string]
foreach ($f in (Get-ChildItem $styleDir -Filter *.xaml -File)) {
    foreach ($m in [regex]::Matches([System.IO.File]::ReadAllText($f.FullName), 'x:Key\s*=\s*"([^"]+)"')) {
        [void]$global.Add($m.Groups[1].Value)
    }
}
$unresolved = @()
foreach ($f in (Get-ChildItem src -Recurse -Filter *.xaml -File | Where-Object { $_.FullName -notmatch '\\(obj|bin)\\' })) {
    $text = [System.IO.File]::ReadAllText($f.FullName)
    $local = New-Object System.Collections.Generic.HashSet[string]
    foreach ($m in [regex]::Matches($text, 'x:Key\s*=\s*"([^"]+)"')) { [void]$local.Add($m.Groups[1].Value) }
    foreach ($m in [regex]::Matches($text, '\{StaticResource\s+([A-Za-z0-9_.]+)\s*\}')) {
        $k = $m.Groups[1].Value
        if ($global.Contains($k) -or $local.Contains($k)) { continue }
        $unresolved += "$($f.Name) -> $k"
    }
}
Check 'пров.' 'каждая ссылка на ресурс разрешается' ($unresolved.Count -eq 0) `
    ("`n          не разрешаются: " + (($unresolved | Select-Object -Unique) -join ', '))

Write-Host ''
Write-Host '============================================================='
Write-Host ("ИТОГО: пройдено {0}, провалено {1}" -f $script:Pass, $script:Fail)
if ($script:Fail -gt 0) {
    Write-Host ''
    Write-Host 'Что на самом деле не закрыто:'
    $script:Failures | ForEach-Object { Write-Host ("  - " + $_) }
    exit 1
}
Write-Host 'Каждый закрытый шаг подтверждается командой, а не наличием типа.'
exit 0