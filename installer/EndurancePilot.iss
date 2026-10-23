
#ifndef AppVersion
  #error AppVersion is required
#endif
#ifndef Payload
  #error Payload is required
#endif
#ifndef OutputPath
  #error OutputPath is required
#endif
#define ProductId "{F9478DF4-3530-49E0-A31D-82F226493703}"

[Setup]
AppId={{F9478DF4-3530-49E0-A31D-82F226493703}
AppName=Endurance Pilot
AppVersion={#AppVersion}
AppPublisher=Endurance Pilot
AppPublisherURL=https://github.com/StwEndurancePilot/StwEndurancePilot
AppUpdatesURL=https://github.com/StwEndurancePilot/StwEndurancePilot/releases/latest
DefaultDirName={localappdata}\Programs\Endurance Pilot
DefaultGroupName=Endurance Pilot
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
OutputDir={#OutputPath}
OutputBaseFilename=endurance-pilot-{#AppVersion}-setup
SetupIconFile=..\EndurancePilot.App\Branding\Pilot.ico
UninstallDisplayIcon={app}\EndurancePilot.exe
UninstallDisplayName=Endurance Pilot
SetupMutex=Local\EndurancePilot.Setup
CloseApplications=yes
CloseApplicationsFilter=EndurancePilot.exe
RestartApplications=no
WizardStyle=modern
Compression=lzma2/normal
SolidCompression=yes
DisableWelcomePage=no
UsePreviousAppDir=yes
UsePreviousTasks=yes
VersionInfoVersion={#AppVersion}
VersionInfoCompany=Endurance Pilot
VersionInfoDescription=Endurance Pilot Setup

[Tasks]
Name: desktopicon; Description: "Create a desktop shortcut"; Flags: unchecked

[Files]
Source: "{#Payload}"; DestDir: "{app}"; DestName: "EndurancePilot.exe"; Flags: ignoreversion

[Icons]
Name: "{userprograms}\Endurance Pilot"; Filename: "{app}\EndurancePilot.exe"; WorkingDir: "{app}"; AppUserModelID: "EndurancePilot.App"
Name: "{userdesktop}\Endurance Pilot"; Filename: "{app}\EndurancePilot.exe"; WorkingDir: "{app}"; Tasks: desktopicon; AppUserModelID: "EndurancePilot.App"

[Run]
Filename: "{app}\EndurancePilot.exe"; Description: "Open Endurance Pilot"; Flags: nowait postinstall skipifsilent

[Code]
const
  UninstallKey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{#ProductId}_is1';
  DesktopRuntimeKey = 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App';
  RuntimeDownload = 'https://dotnet.microsoft.com/en-us/download/dotnet/10.0';

function HasDesktopRuntimeAt(Root: Integer): Boolean;
var
  Versions: TArrayOfString;
  I: Integer;
  Parsed: Int64;
begin
  Result := False;
  if RegGetValueNames(Root, DesktopRuntimeKey, Versions) then
    for I := 0 to GetArrayLength(Versions) - 1 do
      if (Copy(Versions[I], 1, 5) = '10.0.') and StrToVersion(Versions[I], Parsed) then
      begin
        Result := True;
        Exit;
      end;
end;

function InitializeSetup: Boolean;
var
  Existing, InstallLocation: String;
  OldVersion, NewVersion: Int64;
  HasOldVersion: Boolean;
  ErrorCode: Integer;
begin
  Result := False;
  HasOldVersion := False;
  if RegQueryStringValue(HKCU64, UninstallKey, 'InstallLocation', InstallLocation) then
    HasOldVersion := GetPackedVersion(AddBackslash(InstallLocation) + 'EndurancePilot.exe', OldVersion);
  if not HasOldVersion and RegQueryStringValue(HKCU64, UninstallKey, 'DisplayVersion', Existing) then
    HasOldVersion := StrToVersion(Existing, OldVersion);
  if HasOldVersion and StrToVersion('{#AppVersion}', NewVersion) then
    if ComparePackedVersion(OldVersion, NewVersion) > 0 then
    begin
      SuppressibleMsgBox('A newer version of Endurance Pilot is already installed. Setup will not downgrade it.', mbError, MB_OK, IDOK);
      Exit;
    end;
  if not (HasDesktopRuntimeAt(HKLM32) or HasDesktopRuntimeAt(HKLM64)) then
  begin
    if not WizardSilent then
      if MsgBox('Endurance Pilot requires the .NET 10 Windows Desktop Runtime (x64).' + #13#10#13#10 +
        'Open Microsoft''s download page? Install the Windows Desktop Runtime for x64, then run this setup again.',
        mbInformation, MB_YESNO) = IDYES then
        ShellExec('open', RuntimeDownload, '', '', SW_SHOWNORMAL, ewNoWait, ErrorCode);
    Log('Installation cancelled: .NET 10 Windows Desktop Runtime (x64) is required.');
    Exit;
  end;
  Result := True;
end;

// No settings, logs, credentials or game files are packaged, modified or removed.
// In particular, never add an UninstallDelete rule for LocalAppData\EndurancePilot.
