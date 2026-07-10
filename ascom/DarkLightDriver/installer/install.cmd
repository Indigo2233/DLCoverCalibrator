@echo off
setlocal EnableExtensions EnableDelayedExpansion

set "QUIET=0"
if /I "%~1"=="/quiet" set "QUIET=1"
if /I "%~1"=="-quiet" set "QUIET=1"

set "DRIVER_ID=DarkLight.CoverCalibrator"
set "APP_NAME=DarkLight Cover Calibrator ASCOM Driver"
set "INSTALLDIR=%ProgramFiles(x86)%\DarkLight Cover Calibrator ASCOM Driver"
set "REGASM32=%SystemRoot%\Microsoft.NET\Framework\v4.0.30319\RegAsm.exe"
set "REGASM64=%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe"
set "UNINSTALL_KEY=HKLM\Software\Microsoft\Windows\CurrentVersion\Uninstall\DarkLightCoverCalibratorASCOM"
set "OLD_DRIVER_ID=ASCOM.DarkLight.CoverCalibrator"
set "OLD_DRIVER_CLSID={119826E4-D8DD-4E9C-92EC-65D82FF829EA}"
set "OLD_DRIVER_DIR=%ProgramFiles(x86)%\Common Files\ASCOM\CoverCalibrator"
set "OLD_DRIVER_DLL=%OLD_DRIVER_DIR%\ASCOM.DarkLight.CoverCalibrator.dll"

net session >nul 2>&1
if errorlevel 1 (
    echo Administrator privileges are required.
    echo Right-click install.cmd and choose Run as administrator.
    if "!QUIET!"=="0" pause
    exit /b 1
)

if not exist "!REGASM32!" (
    echo .NET Framework 4.x RegAsm.exe was not found.
    echo Please install .NET Framework 4.8.
    if "!QUIET!"=="0" pause
    exit /b 1
)

call :RemoveLegacyDriver

if not exist "!INSTALLDIR!" mkdir "!INSTALLDIR!"
if errorlevel 1 (
    echo Failed to create install directory:
    echo !INSTALLDIR!
    if "!QUIET!"=="0" pause
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
    if "!QUIET!"=="0" pause
    exit /b 1
)

if exist "!REGASM64!" (
    echo Registering 64-bit COM driver...
    "!REGASM64!" "!INSTALLDIR!\DarkLight.CoverCalibrator.dll" /codebase
    if errorlevel 1 (
        echo Failed to register 64-bit COM driver.
        if "!QUIET!"=="0" pause
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
if "!QUIET!"=="0" pause
exit /b 0

:RemoveLegacyDriver
echo Removing legacy ASCOM DarkLight driver if present...

if exist "!OLD_DRIVER_DLL!" (
    if exist "!REGASM32!" "!REGASM32!" "!OLD_DRIVER_DLL!" /unregister >nul 2>&1
    if exist "!REGASM64!" "!REGASM64!" "!OLD_DRIVER_DLL!" /unregister >nul 2>&1
)

reg delete "HKCR\!OLD_DRIVER_ID!" /f >nul 2>&1
reg delete "HKCR\Wow6432Node\!OLD_DRIVER_ID!" /f >nul 2>&1
reg delete "HKCR\CLSID\!OLD_DRIVER_CLSID!" /f >nul 2>&1
reg delete "HKCR\Wow6432Node\CLSID\!OLD_DRIVER_CLSID!" /f >nul 2>&1
reg delete "HKLM\SOFTWARE\ASCOM\CoverCalibrator Drivers\!OLD_DRIVER_ID!" /f >nul 2>&1
reg delete "HKLM\SOFTWARE\WOW6432Node\ASCOM\CoverCalibrator Drivers\!OLD_DRIVER_ID!" /f >nul 2>&1
reg delete "HKCU\SOFTWARE\ASCOM\CoverCalibrator Drivers\!OLD_DRIVER_ID!" /f >nul 2>&1
reg delete "HKCU\SOFTWARE\WOW6432Node\ASCOM\CoverCalibrator Drivers\!OLD_DRIVER_ID!" /f >nul 2>&1

del "!OLD_DRIVER_DLL!" /f /q >nul 2>&1
del "!OLD_DRIVER_DIR!\DLC_ReadMe.htm" /f /q >nul 2>&1

exit /b 0

:CopyFailed
echo Failed to copy driver files.
echo Close ASCOM Chooser, NINA, APT, SGP, and any process using this driver, then run install.cmd again.
if "!QUIET!"=="0" pause
exit /b 1
