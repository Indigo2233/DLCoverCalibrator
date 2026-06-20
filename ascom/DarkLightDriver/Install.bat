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

REM Locate .NET Framework directory
set "FRAMEWORK=%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319"
if not exist "%FRAMEWORK%\RegAsm.exe" (
    set "FRAMEWORK=%SystemRoot%\Microsoft.NET\Framework\v4.0.30319"
)

if not exist "%FRAMEWORK%\RegAsm.exe" (
    echo [ERROR] .NET Framework 4.8 RegAsm.exe not found.
    echo Please install .NET Framework 4.8.
    pause
    exit /b 1
)

REM Register the driver DLL
echo [1/2] Registering driver with COM...
"%FRAMEWORK%\RegAsm.exe" "%~dp0DarkLight.CoverCalibrator.dll" /codebase /tlb

if %errorlevel% neq 0 (
    echo [ERROR] Failed to register driver DLL.
    pause
    exit /b 1
)

echo [2/2] Setup complete. The driver should now appear in the ASCOM Chooser.
echo.
echo You can access the setup dialog from the ASCOM Chooser Properties button
echo to configure COM port, baud rate, and servo open/close angles.
echo.
echo To uninstall, run: Uninstall.bat
echo.
pause
