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
TargetName=D:\Unity\DLCoverCalibrator\ascom\DarkLightDriver\dist\DarkLight_CoverCalibrator_ASCOM_Setup.exe
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
SourceFiles0=D:\Unity\DLCoverCalibrator\ascom\DarkLightDriver\installer\
SourceFiles1=D:\Unity\DLCoverCalibrator\ascom\DarkLightDriver\bin\Release\net48\

[SourceFiles0]
%FILE0%=
%FILE1%=

[SourceFiles1]
%FILE2%=
%FILE3%=
%FILE4%=
%FILE5%=
%FILE6%=
