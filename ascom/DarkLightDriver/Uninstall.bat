@echo off
REM ======================================================
REM  DarkLight Cover Calibrator ASCOM Driver Uninstaller
REM ======================================================

echo.
echo Uninstalling DarkLight Cover Calibrator ASCOM Driver...
echo.

REM Check for admin rights
net session >nul 2>&1
if %errorlevel% neq 0 (
    echo [ERROR] Please run this script as Administrator.
    pause
    exit /b 1
)

set "DRIVER_DLL=%~dp0DarkLight.CoverCalibrator.dll"
if not exist "%DRIVER_DLL%" (
    set "DRIVER_DLL=%~dp0bin\Release\net48\DarkLight.CoverCalibrator.dll"
)

if not exist "%DRIVER_DLL%" (
    echo [ERROR] DarkLight.CoverCalibrator.dll not found.
    pause
    exit /b 1
)

set "REGASM32=%SystemRoot%\Microsoft.NET\Framework\v4.0.30319\RegAsm.exe"
set "REGASM64=%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe"

echo Unregistering 32-bit COM driver...
"%REGASM32%" "%DRIVER_DLL%" /unregister

if %errorlevel% neq 0 (
    echo [WARNING] 32-bit unregister returned error. Driver may already be unregistered.
)

if exist "%REGASM64%" (
    echo Unregistering 64-bit COM driver...
    "%REGASM64%" "%DRIVER_DLL%" /unregister
    if %errorlevel% neq 0 (
        echo [WARNING] 64-bit unregister returned error. Driver may already be unregistered.
    )
)

echo Done.
pause
