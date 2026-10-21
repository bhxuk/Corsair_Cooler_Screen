; Cooler Screen installer (Inno Setup 6)
; Built automatically by GitHub Actions. To build by hand:
;   iscc /DMyAppVersion=1.0.0 /DPublishDir=..\publish installer\CoolerScreen.iss

#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\publish"
#endif

#define MyAppName "Cooler Screen"
#define MyAppExe "CoolerScreen.exe"
#define MyAppPublisher "bhxuk"
#define MyAppURL "https://github.com/bhxuk/Corsair_Cooler_Screen"

[Setup]
AppId={{6B0E2C1F-3A9D-4F57-9C2E-8D41A7B5E913}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
AppUpdatesURL={#MyAppURL}/releases
DefaultDirName={localappdata}\Programs\Cooler Screen
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=..\dist
OutputBaseFilename=CoolerScreen-Setup-{#MyAppVersion}
SetupIconFile=..\src\CoolerScreen\Assets\coolerscreen.ico
UninstallDisplayIcon={app}\{#MyAppExe}
UninstallDisplayName={#MyAppName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
CloseApplications=no
VersionInfoVersion={#MyAppVersion}
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription={#MyAppName} Setup

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "startup"; Description: "Start Cooler Screen when I sign in to Windows"; GroupDescription: "Options:"
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Options:"; Flags: unchecked

[Files]
Source: "{#PublishDir}\{#MyAppExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDir}\*.dll"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#MyAppName}\{#MyAppName}"; Filename: "{app}\{#MyAppExe}"
Name: "{autoprograms}\{#MyAppName}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExe}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "CoolerScreen"; ValueData: """{app}\{#MyAppExe}"" --background"; Flags: uninsdeletevalue; Tasks: startup

[Run]
Filename: "{app}\{#MyAppExe}"; Description: "Open Cooler Screen now"; Flags: nowait postinstall skipifsilent
Filename: "{app}\{#MyAppExe}"; Parameters: "--background"; Flags: nowait; Check: WizardSilent

[UninstallRun]
Filename: "{app}\{#MyAppExe}"; Parameters: "--quit"; Flags: runhidden waituntilterminated; RunOnceId: "QuitCoolerScreen"
Filename: "{sys}\taskkill.exe"; Parameters: "/f /im {#MyAppExe}"; Flags: runhidden; RunOnceId: "StopCoolerScreen"

[Code]
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  { Ask a running copy to close (it hands the screen back to the cooler), then make sure it has. }
  if FileExists(ExpandConstant('{app}\{#MyAppExe}')) then
    Exec(ExpandConstant('{app}\{#MyAppExe}'), '--quit', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/f /im {#MyAppExe}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Sleep(300);
  Result := '';
end;
