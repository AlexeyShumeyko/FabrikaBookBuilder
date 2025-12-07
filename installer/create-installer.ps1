# PowerShell script for creating installer
param(
    [string]$Version = "1.0.0",
    [string]$OutputPath = ".\installer",
    [string]$PublishPath = ".\publish"
)

Write-Host "Creating installer for version $Version" -ForegroundColor Green

# Create installer directory
if (Test-Path $OutputPath) {
    Remove-Item $OutputPath -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $OutputPath | Out-Null

# Copy published files
Write-Host "Copying published files..." -ForegroundColor Yellow
Copy-Item -Path "$PublishPath\*" -Destination $OutputPath -Recurse -Force

# Create installer script
$installerScript = @"
@echo off
setlocal enabledelayedexpansion

set INSTALL_DIR=%ProgramFiles%\Fabrika BookBuilder Studio
set APP_NAME=Fabrika BookBuilder Studio
set APP_EXE=PhotoBookRenamer.exe

echo ========================================
echo Installing %APP_NAME% v$Version
echo ========================================
echo.

REM Check for admin rights
net session >nul 2>&1
if %errorLevel% neq 0 (
    echo This installer requires administrator privileges.
    echo Please run as administrator.
    pause
    exit /b 1
)

echo Creating installation directory...
if not exist "%INSTALL_DIR%" mkdir "%INSTALL_DIR%"

echo Copying files...
xcopy /E /I /Y /Q "%~dp0*" "%INSTALL_DIR%\" >nul

echo Creating Start Menu shortcut...
set START_MENU=%APPDATA%\Microsoft\Windows\Start Menu\Programs
powershell -NoProfile -ExecutionPolicy Bypass -Command "$WshShell = New-Object -ComObject WScript.Shell; $Shortcut = $WshShell.CreateShortcut('%START_MENU%\%APP_NAME%.lnk'); $Shortcut.TargetPath = '%INSTALL_DIR%\%APP_EXE%'; $Shortcut.WorkingDirectory = '%INSTALL_DIR%'; $Shortcut.IconLocation = '%INSTALL_DIR%\icon.ico'; $Shortcut.Description = '%APP_NAME%'; $Shortcut.Save()" 2>nul

echo Creating Desktop shortcut...
set DESKTOP=%USERPROFILE%\Desktop
powershell -NoProfile -ExecutionPolicy Bypass -Command "$WshShell = New-Object -ComObject WScript.Shell; $Shortcut = $WshShell.CreateShortcut('%DESKTOP%\%APP_NAME%.lnk'); $Shortcut.TargetPath = '%INSTALL_DIR%\%APP_EXE%'; $Shortcut.WorkingDirectory = '%INSTALL_DIR%'; $Shortcut.IconLocation = '%INSTALL_DIR%\icon.ico'; $Shortcut.Description = '%APP_NAME%'; $Shortcut.Save()" 2>nul

echo.
echo ========================================
echo Installation completed successfully!
echo ========================================
echo.
echo %APP_NAME% has been installed to: %INSTALL_DIR%
echo.
pause
"@

$installerScript | Out-File -FilePath "$OutputPath\install.bat" -Encoding ASCII

# Create uninstaller script
$uninstallerScript = @"
@echo off
setlocal

set INSTALL_DIR=%ProgramFiles%\Fabrika BookBuilder Studio
set APP_NAME=Fabrika BookBuilder Studio

echo ========================================
echo Uninstalling %APP_NAME%
echo ========================================
echo.

REM Check for admin rights
net session >nul 2>&1
if %errorLevel% neq 0 (
    echo This uninstaller requires administrator privileges.
    echo Please run as administrator.
    pause
    exit /b 1
)

echo Removing shortcuts...
set START_MENU=%APPDATA%\Microsoft\Windows\Start Menu\Programs
if exist "%START_MENU%\%APP_NAME%.lnk" del "%START_MENU%\%APP_NAME%.lnk"

set DESKTOP=%USERPROFILE%\Desktop
if exist "%DESKTOP%\%APP_NAME%.lnk" del "%DESKTOP%\%APP_NAME%.lnk"

echo Removing installation directory...
if exist "%INSTALL_DIR%" (
    rd /s /q "%INSTALL_DIR%"
)

echo.
echo ========================================
echo Uninstallation completed!
echo ========================================
echo.
pause
"@

$uninstallerScript | Out-File -FilePath "$OutputPath\uninstall.bat" -Encoding ASCII

Write-Host "Installer created successfully!" -ForegroundColor Green
Write-Host "Location: $OutputPath" -ForegroundColor Cyan


