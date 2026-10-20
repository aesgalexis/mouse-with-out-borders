#define AppName "Mouse, with out borders"
#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif

[Setup]
AppId={{BAA10551-2B65-4E83-B102-1F184AE6C673}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=Mouse, with out borders
AppPublisherURL=https://github.com/aesgalexis/mouse-with-out-borders
AppSupportURL=https://github.com/aesgalexis/mouse-with-out-borders/issues
DefaultDirName={localappdata}\Programs\MouseWithOutBorders
DefaultGroupName={#AppName}
UninstallDisplayIcon={app}\MouseWithOutBorders.exe
PrivilegesRequired=lowest
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
MinVersion=10.0
OutputDir=..\dist\installer
OutputBaseFilename=MouseWithOutBorders-{#AppVersion}-Setup
SetupIconFile=..\MouseWithOutBorders\assets\app.ico
WizardStyle=modern
Compression=lzma2
SolidCompression=yes
DisableProgramGroupPage=yes
CloseApplications=yes
RestartApplications=no
AppMutex=Local\MouseWithOutBorders-79DBA1BD
UsePreviousAppDir=yes
SetupLogging=yes

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "..\dist\app\MouseWithOutBorders.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\MouseWithOutBorders.exe"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\MouseWithOutBorders.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\MouseWithOutBorders.exe"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent
