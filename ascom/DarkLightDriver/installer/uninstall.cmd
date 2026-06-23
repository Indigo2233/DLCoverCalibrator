@echo off
setlocal EnableExtensions EnableDelayedExpansion

set "APP_NAME=DarkLight Cover Calibrator ASCOM Driver"
set "INSTALLDIR=%ProgramFiles(x86)%\DarkLight Cover Calibrator ASCOM Driver"
set "DRIVER_DLL=%INSTALLDIR%\DarkLight.CoverCalibrator.dll"
set "REGASM32=%SystemRoot%\Microsoft.NET\Framework\v4.0.30319\RegAsm.exe"
set "REGASM64=%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe"
set "UNINSTALL_KEY=HKLM\Software\Microsoft\Windows\CurrentVersion\Uninstall\DarkLightCoverCalibratorASCOM"

net session >nul 2>&1
if errorlevel 1 (
    echo Administrator privileges are required.
    echo Right-click uninstall.cmd and choose Run as administrator.
    pause
    exit /b 1
)

echo Uninstalling !APP_NAME!...

if exist "!DRIVER_DLL!" (
    if exist "!REGASM32!" (
        echo Unregistering 32-bit COM driver...
        "!REGASM32!" "!DRIVER_DLL!" /unregister
    )

    if exist "!REGASM64!" (
        echo Unregistering 64-bit COM driver...
        "!REGASM64!" "!DRIVER_DLL!" /unregister
    )
)

reg delete "!UNINSTALL_KEY!" /f >nul 2>&1

del "!INSTALLDIR!\DarkLight.CoverCalibrator.dll" /f /q >nul 2>&1
del "!INSTALLDIR!\ASCOM.Attributes.dll" /f /q >nul 2>&1
del "!INSTALLDIR!\ASCOM.DeviceInterfaces.dll" /f /q >nul 2>&1
del "!INSTALLDIR!\ASCOM.Exceptions.dll" /f /q >nul 2>&1
del "!INSTALLDIR!\ASCOM.Utilities.dll" /f /q >nul 2>&1
del "!INSTALLDIR!\uninstall.cmd" /f /q >nul 2>&1
rd "!INSTALLDIR!" >nul 2>&1

echo.
echo Uninstall complete.
pause
exit /b 0
