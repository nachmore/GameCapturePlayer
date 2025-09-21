; Inno Setup Script for Game Capture Player
; Requirements:
; 1) Install Inno Setup (https://jrsoftware.org/isinfo.php)
; 2) Run build_installer.ps1 from ../scripts/


#define AppName "Game Capture Player"
#define AppExe "GameCapturePlayer.exe"
#define AppVersion "0.1.0"

[Setup]
AppId={{6A0D3A9C-6D70-4F41-9CFE-9F6C5E6E7E21}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=""
AppPublisherURL=""
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableDirPage=yes
DisableProgramGroupPage=yes
OutputDir=dist
OutputBaseFilename=GameCapturePlayerSetup
Compression=lzma
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
WizardStyle=modern
SetupIconFile=..\src\img\logo.ico
UninstallDisplayIcon={app}\{#AppExe}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
; Copy everything from the publish folder
Source: "publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
; Start Menu shortcut only (no desktop icon)
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"

[Run]
; Offer to run after install
Filename: "{app}\{#AppExe}"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Ensure leftover logs/temp (if any) in app folder can be cleaned up
Type: filesandordirs; Name: "{app}\logs"
