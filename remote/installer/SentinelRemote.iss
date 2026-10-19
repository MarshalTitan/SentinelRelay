[Setup]
AppId={{90780B12-107E-4B91-B87E-9C6A9B6A2E41}
AppName=Sentinel Remote
AppVersion=0.1.2
AppPublisher=MarshalTitan
DefaultDirName={localappdata}\Programs\SentinelRemote
DefaultGroupName=Sentinel Remote
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\..\desktop-installer
OutputBaseFilename=SentinelRemote-0.1.2-Windows-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\SentinelRemote.exe
CloseApplications=yes

[Files]
Source: "..\..\desktop-candidate\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; Flags: unchecked

[Icons]
Name: "{group}\Sentinel Remote"; Filename: "{app}\SentinelRemote.exe"
Name: "{userdesktop}\Sentinel Remote"; Filename: "{app}\SentinelRemote.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\SentinelRemote.exe"; Description: "Open Sentinel Remote"; Flags: nowait postinstall skipifsilent runasoriginaluser

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueName: "SentinelRemote"; Flags: uninsdeletevalue
