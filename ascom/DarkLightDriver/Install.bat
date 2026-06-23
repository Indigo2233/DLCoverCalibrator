@echo off
REM ======================================================
REM  DarkLight Cover Calibrator ASCOM Driver Installer
REM  
REM  Prerequisites:
REM  1. ASCOM Platform 7 (or newer) installed
REM  2. .NET Framework 4.8 installed
REM  3. Run as Administrator
REM ======================================================

echo.
echo DarkLight Cover Calibrator ASCOM Driver Setup
echo ==============================================
echo.

REM Check for admin rights
net session >nul 2>&1
if %errorlevel% neq 0 (
    echo [ERROR] Please run this script as Administrator.
    echo Right-click the file and select "Run as administrator".
    pause
    exit /b 1
)

REM Locate the compiled driver DLL
set "DRIVER_DLL=%~dp0DarkLight.CoverCalibrator.dll"
if not exist "%DRIVER_DLL%" (
    set "DRIVER_DLL=%~dp0bin\Release\net48\DarkLight.CoverCalibrator.dll"
)

if not exist "%DRIVER_DLL%" (
    echo [ERROR] DarkLight.CoverCalibrator.dll not found.
    echo Build the driver first: dotnet build -c Release
    pause
    exit /b 1
)

set "REGASM32=%SystemRoot%\Microsoft.NET\Framework\v4.0.30319\RegAsm.exe"
set "REGASM64=%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe"

if not exist "%REGASM32%" (
    echo [ERROR] .NET Framework 4.8 RegAsm.exe not found.
    echo Please install .NET Framework 4.8.
    pause
    exit /b 1
)

REM Register the driver DLL
echo [1/3] Registering 32-bit COM driver...
"%REGASM32%" "%DRIVER_DLL%" /codebase

if %errorlevel% neq 0 (
    echo [ERROR] Failed to register 32-bit driver DLL.
    pause
    exit /b 1
)

echo [2/3] Registering 64-bit COM driver...
if exist "%REGASM64%" (
    "%REGASM64%" "%DRIVER_DLL%" /codebase
    if %errorlevel% neq 0 (
        echo [ERROR] Failed to register 64-bit driver DLL.
        pause
        exit /b 1
    )
) else (
    echo [WARNING] 64-bit RegAsm.exe not found; skipped 64-bit registration.
)

echo [3/3] Setup complete. The driver should now appear in the ASCOM Chooser.
echo.
echo You can access the setup dialog from the ASCOM Chooser Properties button
echo to configure COM port, baud rate, and servo open/close angles.
echo.
echo To uninstall, run: Uninstall.bat
echo.
pause
