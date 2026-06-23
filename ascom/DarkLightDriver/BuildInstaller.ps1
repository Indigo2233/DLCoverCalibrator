param(
    [string]$Configuration = "Release",
    [switch]$BuildExe
)

$ErrorActionPreference = "Stop"

$driverRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectPath = Join-Path $driverRoot "DarkLightDriver.csproj"
$installerDir = Join-Path $driverRoot "installer"
$outputDir = Join-Path $driverRoot "bin\$Configuration\net48"
$distDir = Join-Path $driverRoot "dist"
$exePath = Join-Path $distDir "DarkLight_CoverCalibrator_ASCOM_Setup.exe"
$zipPath = Join-Path $distDir "DarkLight_CoverCalibrator_ASCOM_ManualInstall.zip"

dotnet build $projectPath -c $Configuration

New-Item -ItemType Directory -Force -Path $distDir | Out-Null
Remove-Item -LiteralPath $zipPath -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $exePath -Force -ErrorAction SilentlyContinue
foreach ($staleName in @(
    "DarkLight.CoverCalibrator.dll",
    "ASCOM.Attributes.dll",
    "ASCOM.DeviceInterfaces.dll",
    "ASCOM.Exceptions.dll",
    "ASCOM.Utilities.dll",
    "install.cmd",
    "uninstall.cmd",
    "README.txt"
)) {
    Remove-Item -LiteralPath (Join-Path $distDir $staleName) -Force -ErrorAction SilentlyContinue
}

$manualStage = Join-Path $env:TEMP ("DarkLightDriverManual_" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $manualStage | Out-Null
try {
    Copy-Item -LiteralPath (Join-Path $installerDir "install.cmd") -Destination $manualStage
    Copy-Item -LiteralPath (Join-Path $installerDir "uninstall.cmd") -Destination $manualStage
    Copy-Item -LiteralPath (Join-Path $outputDir "DarkLight.CoverCalibrator.dll") -Destination $manualStage
    Copy-Item -LiteralPath (Join-Path $outputDir "ASCOM.Attributes.dll") -Destination $manualStage
    Copy-Item -LiteralPath (Join-Path $outputDir "ASCOM.DeviceInterfaces.dll") -Destination $manualStage
    Copy-Item -LiteralPath (Join-Path $outputDir "ASCOM.Exceptions.dll") -Destination $manualStage
    Copy-Item -LiteralPath (Join-Path $outputDir "ASCOM.Utilities.dll") -Destination $manualStage

    $readme = @"
DarkLight Cover Calibrator ASCOM Driver Manual Install

1. Extract this ZIP to a local folder.
2. Right-click install.cmd and choose Run as administrator.
3. Open ASCOM Chooser and select DarkLight Cover Calibrator.

If company security software blocks registration, ask IT to run install.cmd
with local administrator privileges. The script uses RegAsm to register the
.NET Framework COM driver.
"@
    Set-Content -Path (Join-Path $manualStage "README.txt") -Value $readme -Encoding ASCII

    Compress-Archive -Path (Join-Path $manualStage "*") -DestinationPath $zipPath -Force
}
finally {
    Remove-Item -LiteralPath $manualStage -Recurse -Force -ErrorAction SilentlyContinue
}

$outputs = @((Get-Item -LiteralPath $zipPath))

if ($BuildExe) {
    $iexpress = (Get-Command "iexpress.exe" -ErrorAction Stop).Source
    $sedPath = Join-Path $env:TEMP ("DarkLightDriver_" + [Guid]::NewGuid().ToString("N") + ".sed")

    $sed = @"
[Version]
Class=IEXPRESS
SEDVersion=3

[Options]
PackagePurpose=InstallApp
ShowInstallProgramWindow=1
HideExtractAnimation=0
UseLongFileName=1
InsideCompressed=0
CAB_FixedSize=0
CAB_ResvCodeSigning=0
RebootMode=N
InstallPrompt=%InstallPrompt%
DisplayLicense=%DisplayLicense%
FinishMessage=%FinishMessage%
TargetName=%TargetName%
FriendlyName=%FriendlyName%
AppLaunched=%AppLaunched%
PostInstallCmd=%PostInstallCmd%
AdminQuietInstCmd=%AdminQuietInstCmd%
UserQuietInstCmd=%UserQuietInstCmd%
SourceFiles=SourceFiles

[Strings]
InstallPrompt=
DisplayLicense=
FinishMessage=
TargetName=$exePath
FriendlyName=DarkLight Cover Calibrator ASCOM Driver Setup
AppLaunched=install.cmd
PostInstallCmd=<None>
AdminQuietInstCmd=
UserQuietInstCmd=
FILE0=install.cmd
FILE1=uninstall.cmd
FILE2=DarkLight.CoverCalibrator.dll
FILE3=ASCOM.Attributes.dll
FILE4=ASCOM.DeviceInterfaces.dll
FILE5=ASCOM.Exceptions.dll
FILE6=ASCOM.Utilities.dll

[SourceFiles]
SourceFiles0=$installerDir\
SourceFiles1=$outputDir\

[SourceFiles0]
%FILE0%=
%FILE1%=

[SourceFiles1]
%FILE2%=
%FILE3%=
%FILE4%=
%FILE5%=
%FILE6%=
"@

    try {
        Set-Content -Path $sedPath -Value $sed -Encoding ASCII
        $process = Start-Process -FilePath $iexpress -ArgumentList @("/N", "/Q", $sedPath) -Wait -PassThru
        if ($process.ExitCode -ne 0) {
            throw "IExpress failed with exit code $($process.ExitCode)"
        }

        $deadline = (Get-Date).AddSeconds(20)
        while (-not (Test-Path -LiteralPath $exePath) -and (Get-Date) -lt $deadline) {
            Start-Sleep -Milliseconds 250
        }

        if (-not (Test-Path -LiteralPath $exePath)) {
            throw "Installer was not generated: $exePath"
        }

        $outputs += Get-Item -LiteralPath $exePath
    }
    finally {
        Remove-Item -LiteralPath $sedPath -Force -ErrorAction SilentlyContinue
    }
}

$outputs
