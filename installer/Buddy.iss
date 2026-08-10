#define MyAppName "Buddy"
#ifndef MyAppVersion
  #define MyAppVersion "0.1.0"
#endif

[Setup]
AppId={{7D20D3D8-57D0-49D0-9B2A-FEC0A823C0D2}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher=flcl42
AppPublisherURL=https://flcl42.github.io/buddy/
AppSupportURL=https://github.com/flcl42/buddy/issues
AppUpdatesURL=https://github.com/flcl42/buddy/releases
VersionInfoVersion={#MyAppVersion}.0
VersionInfoCompany=flcl42
VersionInfoDescription=Buddy installer
VersionInfoProductName={#MyAppName}
VersionInfoProductVersion={#MyAppVersion}
DefaultDirName={sd}\Programs
UsePreviousAppDir=no
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
InfoBeforeFile=INSTALL-NOTES.txt
OutputDir=..\artifacts\release
OutputBaseFilename=Buddy-Setup
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=commandline
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
CloseApplications=no
RestartApplications=no
UninstallDisplayIcon={app}\Buddy.exe
SetupLogging=yes
ShowLanguageDialog=auto

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "german"; MessagesFile: "compiler:Languages\German.isl"
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked
Name: "launch"; Description: "Launch Buddy after setup"; GroupDescription: "After installation:"; Flags: checkedonce

[Files]
Source: "..\artifacts\release\Buddy.exe"; DestDir: "{app}"; DestName: "Buddy.exe"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\Buddy"; Filename: "{app}\Buddy.exe"; WorkingDir: "{app}"; Comment: "Buddy speech recorder, trainer, and AI dialog"
Name: "{autodesktop}\Buddy"; Filename: "{app}\Buddy.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\Buddy.exe"; Description: "Launch Buddy"; Flags: nowait postinstall skipifsilent; Tasks: launch

[Code]
function IsBuddyRunning: Boolean;
var
  ResultCode: Integer;
  PowerShellPath: String;
  Parameters: String;
begin
  PowerShellPath := ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe');
  Parameters := '-NoLogo -NoProfile -NonInteractive -WindowStyle Hidden -Command ' +
    '"if (Get-Process -Name Buddy -ErrorAction SilentlyContinue) { exit 23 } else { exit 0 }"';
  if not Exec(PowerShellPath, Parameters, '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
  begin
    Result := True;
    exit;
  end;

  Result := ResultCode = 23;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  if IsBuddyRunning then
    Result :=
      'Buddy is still running in the notification area.' + #13#10 + #13#10 +
      'Finish and save any active recording or dialog, choose Exit Buddy from the tray menu, then run setup again.';
end;
