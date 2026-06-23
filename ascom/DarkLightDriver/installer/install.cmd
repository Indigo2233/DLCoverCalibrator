@echo off
setlocal EnableExtensions EnableDelayedExpansion

set "DRIVER_ID=DarkLight.CoverCalibrator"
set "APP_NAME=DarkLight Cover Calibrator ASCOM Driver"
set "INSTALLDIR=%ProgramFiles(x86)%\DarkLight Cover Calibrator ASCOM Driver"
set "REGASM32=%SystemRoot%\Microsoft.NET\Framework\v4.0.30319\RegAsm.exe"
set "REGASM64=%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe"
set "UNINSTALL_KEY=HKLM\Software\Microsoft\Windows\CurrentVersion\Uninstall\DarkLightCoverCalibratorASCOM"

net session >nul 2>&1
if errorlevel 1 (
    echo Administrator privileges are required.
    echo Right-click install.cmd and choose Run as administrator.
    pause
    exit /b 1
)

if not exist "!REGASM32!" (
    echo .NET Framework 4.x RegAsm.exe was not found.
    echo Please install .NET Framework 4.8.
    pause
    exit /b 1
)

if not exist "!INSTALLDIR!" mkdir "!INSTALLDIR!"
if errorlevel 1 (
    echo Failed to create install directory:
    echo !INSTALLDIR!
    pause
    exit /b 1
)

echo Installing !APP_NAME!...
copy "%~dp0DarkLight.CoverCalibrator.dll" "!INSTALLDIR!\" /Y >nul
if errorlevel 1 goto CopyFailed
copy "%~dp0ASCOM.Attributes.dll" "!INSTALLDIR!\" /Y >nul
if errorlevel 1 goto CopyFailed
copy "%~dp0ASCOM.DeviceInterfaces.dll" "!INSTALLDIR!\" /Y >nul
if errorlevel 1 goto CopyFailed
copy "%~dp0ASCOM.Exceptions.dll" "!INSTALLDIR!\" /Y >nul
if errorlevel 1 goto CopyFailed
copy "%~dp0ASCOM.Utilities.dll" "!INSTALLDIR!\" /Y >nul
if errorlevel 1 goto CopyFailed
copy "%~dp0uninstall.cmd" "!INSTALLDIR!\" /Y >nul
if errorlevel 1 goto CopyFailed

echo Registering 32-bit COM driver...
"!REGASM32!" "!INSTALLDIR!\DarkLight.CoverCalibrator.dll" /codebase
if errorlevel 1 (
    echo Failed to register 32-bit COM driver.
    pause
    exit /b 1
)

if exist "!REGASM64!" (
    echo Registering 64-bit COM driver...
    "!REGASM64!" "!INSTALLDIR!\DarkLight.CoverCalibrator.dll" /codebase
    if errorlevel 1 (
        echo Failed to register 64-bit COM driver.
        pause
        exit /b 1
    )
)

reg add "!UNINSTALL_KEY!" /v "DisplayName" /t REG_SZ /d "!APP_NAME!" /f >nul
reg add "!UNINSTALL_KEY!" /v "DisplayVersion" /t REG_SZ /d "1.0.1" /f >nul
reg add "!UNINSTALL_KEY!" /v "Publisher" /t REG_SZ /d "DarkLight Cover Calibrator" /f >nul
reg add "!UNINSTALL_KEY!" /v "InstallLocation" /t REG_SZ /d "!INSTALLDIR!" /f >nul
reg add "!UNINSTALL_KEY!" /v "UninstallString" /t REG_SZ /d "\"!INSTALLDIR!\uninstall.cmd\"" /f >nul
reg add "!UNINSTALL_KEY!" /v "DisplayIcon" /t REG_SZ /d "!INSTALLDIR!\DarkLight.CoverCalibrator.dll" /f >nul

echo.
echo Installation complete.
echo Driver ID: !DRIVER_ID!
echo The driver should appear in the ASCOM Chooser as:
echo DarkLight Cover Calibrator
echo.
pause
exit /b 0

:CopyFailed
echo Failed to copy driver files.
echo Close ASCOM Chooser, NINA, APT, SGP, and any process using this driver, then run install.cmd again.
pause
exit /b 1
