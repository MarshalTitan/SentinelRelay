[Setup]
AppId={{C4740220-377A-4DD4-A61B-06A00FC34BE2}
AppName=Sentinel Remote Gamepad Check
AppVersion=0.1.0
AppPublisher=MarshalTitan
DefaultDirName={localappdata}\Programs\SentinelGamepadCheck
DefaultGroupName=Sentinel Remote
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\..\inputcheck-installer
OutputBaseFilename=SentinelRemote-Gamepad-Check-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\SentinelGamepadCheck.exe
CloseApplications=yes

[Files]
Source: "..\..\inputcheck-candidate\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Sentinel Remote Gamepad Check"; Filename: "{app}\SentinelGamepadCheck.exe"

[Run]
Filename: "{app}\SentinelGamepadCheck.exe"; Description: "Open Gamepad Check"; Flags: nowait postinstall skipifsilent runasoriginaluser

[UninstallDelete]
Type: filesandordirs; Name: "{localappdata}\Sentinel\GamepadCheck"
