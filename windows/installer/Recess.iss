; Inno Setup script for the Recess for Windows installer.
;
; Builds a per-user installer (no admin rights needed) from a self-contained publish:
;   dotnet publish src/Recess.App/Recess.App.csproj -c Release -r win-x64 --self-contained -o publish/Recess
;   iscc /DAppVersion=1.2.3 installer/Recess.iss
; The release workflow does both.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

[Setup]
AppId={{90B01474-0149-4A65-9A91-DA0A6DAD31C0}
AppName=Recess
AppVersion={#AppVersion}
AppVerName=Recess {#AppVersion}
AppPublisher=Recess contributors
AppPublisherURL=https://recess.always17.com/
AppSupportURL=https://github.com/NeelkanthTandel/recess/issues
AppUpdatesURL=https://github.com/NeelkanthTandel/recess/releases
VersionInfoVersion={#AppVersion}
; Installs for the current user only, into %LOCALAPPDATA%\Programs\Recess.
PrivilegesRequired=lowest
DefaultDirName={autopf}\Recess
DisableProgramGroupPage=yes
DisableDirPage=auto
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
OutputDir=..\publish
OutputBaseFilename=Recess-Setup
SetupIconFile=..\src\Recess.App\Assets\Recess.ico
UninstallDisplayIcon={app}\Recess.exe
UninstallDisplayName=Recess
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
; Asks you to quit a running Recess (it holds this mutex) before files are replaced.
AppMutex=RecessAppRunning
CloseApplications=yes
RestartApplications=no

[Files]
Source: "..\publish\Recess\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[InstallDelete]
; Files from an older version that the new one no longer has.
Type: filesandordirs; Name: "{app}\Themes"

[Icons]
; The app ID lets Windows show Recess's notifications with its name and icon.
Name: "{autoprograms}\Recess"; Filename: "{app}\Recess.exe"; AppUserModelID: "Recess.BreakReminder"

[Registry]
; recess:// links.
Root: HKCU; Subkey: "Software\Classes\recess"; ValueType: string; ValueName: ""; ValueData: "URL:Recess"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\recess"; ValueType: string; ValueName: "URL Protocol"; ValueData: ""
Root: HKCU; Subkey: "Software\Classes\recess\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: """{app}\Recess.exe"",0"
Root: HKCU; Subkey: "Software\Classes\recess\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\Recess.exe"" ""%1"""
; Created by Recess itself; removed on uninstall.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "Recess"; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Classes\AppUserModelId\Recess.BreakReminder"; ValueType: none; Flags: uninsdeletekey

[Run]
Filename: "{app}\Recess.exe"; Description: "Open Recess"; Flags: nowait postinstall skipifsilent

[Messages]
; Settings, breaks and activity stay in %APPDATA%\Recess after uninstalling, in case you come back.
UninstalledAll=Recess was removed from your computer. Your settings and activity are still in %APPDATA%\Recess; delete that folder to remove them too.
