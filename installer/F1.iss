; F1 — the installable product build.
;
; This is the spec that ships. SystemCore.Phase0.iss is a different thing: the isolated
; phase-0 validation fixture whose AppId ADR-0001 deliberately keeps separate from the
; production identity, and whose install location is pinned and cannot be overridden.
; The two must not be confused, which is why this file has its own AppId
; ({{81C8CF82-BB21-4324-A51C-22C7C9F14222}) and does not enforce a fixed path.
;
; Layout contract (do not "simplify" this): the app goes to {app}\App and the elevated
; helper must end up inside the app directory at {app}\App\ElevatedHost. HelperLaunchLease
; resolves it as <app directory>\ElevatedHost\SystemCore.ElevatedHost.exe from
; AppContext.BaseDirectory, SecurityTests and CompatibilityTests pin that relationship, and
; docs/CLEANER-PROTOCOL.md documents it. The recursive App\* entry below is what places it.
#define ProductName "F1"
#define ProductVersion "0.9.0-dev"
#define RepoRoot AddBackslash(SourcePath) + ".."

[Setup]
AppId={{81C8CF82-BB21-4324-A51C-22C7C9F14222}
AppName={#ProductName}
AppVersion={#ProductVersion}
AppVerName={#ProductName} {#ProductVersion}
AppPublisher=Falcone
AppComments=Cleanup and application removal for Windows 11 x64.
DefaultDirName={autopf}\F1
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
SetupArchitecture=x64
; Windows 11 23H2. Mirrors CompatibilityRules.MinimumBuild = 22631 in the application.
MinVersion=10.0.22631
WizardStyle=modern
SetupIconFile={#RepoRoot}\src\SystemCore.App\Assets\Brand\F1.ico
OutputDir={#RepoRoot}\artifacts\installer
OutputBaseFilename=F1-{#ProductVersion}-win-x64-setup
Compression=lzma2
SolidCompression=yes
UninstallDisplayIcon={app}\App\F1.exe
UninstallFilesDir={app}\Uninstall
CloseApplications=no
RestartApplications=no
SetupLogging=yes
#ifdef EnableSigning
SignTool=systemcore
SignedUninstaller=yes
#endif

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#RepoRoot}\artifacts\publish\App\*"; DestDir: "{app}\App"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#RepoRoot}\THIRD-PARTY-NOTICES"; DestDir: "{app}\Notices"; Flags: ignoreversion
Source: "{#RepoRoot}\docs\licenses\*"; DestDir: "{app}\Notices\licenses"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#ProductName}"; Filename: "{app}\App\F1.exe"; WorkingDir: "{app}\App"
Name: "{autodesktop}\{#ProductName}"; Filename: "{app}\App\F1.exe"; WorkingDir: "{app}\App"; Tasks: desktopicon

[Run]
; No post-install elevated launch: the app is asInvoker and runs as the caller.
Filename: "{app}\App\F1.exe"; Description: "{cm:LaunchProgram,F1}"; Flags: nowait postinstall skipifsilent

[Code]
// Inside [Code] the comment marker is "//", not ";". A ";" here terminates a Pascal
// statement, which is why the phase-0 fixture carries no comments in this section.
// Same client-edition allowlist as CompatibilityRules.IsSupportedEdition. Server and other
// non-client editions are refused because the application itself does not support them.
function IsAllowedEdition: Boolean;
var
  Edition, InstallationType: String;
begin
  Result := False;
  if not RegQueryStringValue(HKLM64, 'SOFTWARE\Microsoft\Windows NT\CurrentVersion', 'InstallationType', InstallationType) then Exit;
  if InstallationType <> 'Client' then Exit;
  if not RegQueryStringValue(HKLM64, 'SOFTWARE\Microsoft\Windows NT\CurrentVersion', 'EditionID', Edition) then Exit;
  Result := (Edition = 'Core') or (Edition = 'CoreN') or
    (Edition = 'CoreSingleLanguage') or (Edition = 'CoreCountrySpecific') or
    (Edition = 'Professional') or (Edition = 'ProfessionalN') or
    (Edition = 'Enterprise') or (Edition = 'EnterpriseN') or
    (Edition = 'Education') or (Edition = 'EducationN');
end;

function InitializeSetup: Boolean;
begin
  Result := IsAllowedEdition;
  if not Result then
    MsgBox('F1 requires Windows 11 Home, Pro, Enterprise or Education, version 23H2 or later, x64.', mbError, MB_OK);
end;
