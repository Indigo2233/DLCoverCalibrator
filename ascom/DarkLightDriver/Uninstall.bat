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

set "FRAMEWORK=%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319"
if not exist "%FRAMEWORK%\RegAsm.exe" (
    set "FRAMEWORK=%SystemRoot%\Microsoft.NET\Framework\v4.0.30319"
)

echo Unregistering driver from COM...
"%FRAMEWORK%\RegAsm.exe" "%~dp0DarkLight.CoverCalibrator.dll" /unregister

if %errorlevel% neq 0 (
    echo [WARNING] Unregister returned error. Driver may already be unregistered.
)

echo Done.
pause
