; Inno Setup script - builds QuickFind-Setup-<version>.exe
; Build: ISCC.exe /DAppVersion=0.1.0 installer\QuickFind.iss   (after `cargo build --release` in app\)
#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif

[Setup]
AppId={{8F3B6C1E-5A47-4D52-9B0E-2C7A41D6E9F3}
AppName=QuickFind
AppVersion={#AppVersion}
AppPublisher=lakmina456
AppPublisherURL=https://github.com/lakmina456/QuickFind
DefaultDirName={autopf}\QuickFind
DefaultGroupName=QuickFind
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\QuickFind.exe
SetupIconFile=..\app\assets\QuickFind.ico
OutputDir=..\dist
OutputBaseFilename=QuickFind-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
CloseApplications=yes

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked
Name: "startup"; Description: "Start QuickFind when Windows starts"; GroupDescription: "Options:"; Flags: unchecked

[Files]
Source: "..\app\target\release\QuickFind.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\QuickFind"; Filename: "{app}\QuickFind.exe"
Name: "{group}\Uninstall QuickFind"; Filename: "{uninstallexe}"
Name: "{autodesktop}\QuickFind"; Filename: "{app}\QuickFind.exe"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "QuickFind"; ValueData: """{app}\QuickFind.exe"""; Flags: uninsdeletevalue; Tasks: startup

[Run]
Filename: "{app}\QuickFind.exe"; Description: "Launch QuickFind"; Flags: nowait postinstall skipifsilent
