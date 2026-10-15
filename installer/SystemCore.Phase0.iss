; Dedicated test installer. Never use this AppId for the production product.
#define ProbeName "SystemCore Phase 0"
#define ProbeVersion "0.0.1"
#define RepoRoot AddBackslash(SourcePath) + ".."

[Setup]
AppId={{89809483-871F-4A39-8B52-214560EFC764}
AppName={#ProbeName}
AppVersion={#ProbeVersion}
AppComments=Development compatibility probe. Windows 11 23H2+ x64 only.
DefaultDirName={autopf}\SystemCore-Phase0
DisableDirPage=yes
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
SetupArchitecture=x64
MinVersion=10.0.22631
WizardStyle=modern
OutputDir={#RepoRoot}\artifacts\installer
OutputBaseFilename=SystemCore-Phase0-0.0.1-win-x64-setup
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

[Files]
Source: "{#RepoRoot}\artifacts\publish\App\*"; DestDir: "{app}\App"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#RepoRoot}\artifacts\publish\ElevatedHost\*"; DestDir: "{app}\ElevatedHost"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#RepoRoot}\THIRD-PARTY-NOTICES"; DestDir: "{app}\Notices"; Flags: ignoreversion
Source: "{#RepoRoot}\docs\licenses\*"; DestDir: "{app}\Notices\licenses"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{commonprograms}\{#ProbeName}"; Filename: "{app}\App\F1.exe"; WorkingDir: "{app}\App"

; No post-install elevated launch. Start from the standard user's Start menu.
; No UninstallDelete wildcard: uninstall only the files recorded by this installer.
[Code]
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
    MsgBox('This probe requires Windows 11 Home, Pro, Enterprise or Education, version 23H2 or later, x64.', mbError, MB_OK);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  if CompareText(ExpandConstant('{app}'), ExpandConstant('{autopf}\SystemCore-Phase0')) <> 0 then
    Result := 'This test fixture must be installed in its fixed Program Files location.';
end;
