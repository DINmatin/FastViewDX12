#define MyAppName "FastView"
#ifndef MyAppVersion
  #define MyAppVersion "1.1.0"
#endif
#define MyAppExeName "FastViewDX12.exe"
#define MyProviderComHost "FastView.ThumbnailProvider.comhost.dll"
#define GlbProviderClsid "{A2A86C88-5B89-4D5E-92B3-7A6CF4E0A1B7}"
#define GltfProviderClsid "{6B07920A-CFEC-42E6-B718-73A199156EFD}"
#define ThumbnailHandlerIid "{E357FCCD-A995-4576-B01F-234630154E96}"

[Setup]
AppId={{7C418CB6-29F2-4E4D-B54C-9ED558C52D37}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher=FastView
DefaultDirName={autopf}\FastView
DefaultGroupName=FastView
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
WizardStyle=modern
Compression=lzma2/max
SolidCompression=yes
OutputDir=Output
OutputBaseFilename=FastView_Setup_{#MyAppVersion}
SetupIconFile=Payload\Assets\Icons\GlbFile.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
ChangesAssociations=yes
CloseApplications=yes
RestartApplications=no
SetupLogging=yes
VersionInfoVersion={#MyAppVersion}.0
VersionInfoCompany=FastView
VersionInfoDescription=FastView 3D Viewer and GLB/glTF thumbnail provider
VersionInfoProductName=FastView
VersionInfoProductVersion={#MyAppVersion}

[Languages]
Name: "german"; MessagesFile: "compiler:Languages\German.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "restartexplorer"; Description: "Windows Explorer nach der Installation neu starten (empfohlen)"; Flags: checkedonce

[Files]
Source: "Payload\*"; Excludes: "\{#MyProviderComHost},*.pdb,manual-test*,*.error.txt"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

; The .NET COM host contains both provider classes. Inno Setup registers it
; after copying and unregisters it automatically during uninstallation.
Source: "Payload\{#MyProviderComHost}"; DestDir: "{app}"; Flags: ignoreversion regserver 64bit uninsrestartdelete

[Registry]
; Explorer thumbnail handlers
Root: HKLM64; Subkey: "Software\Classes\SystemFileAssociations\.glb\ShellEx\{{E357FCCD-A995-4576-B01F-234630154E96}"; ValueType: string; ValueName: ""; ValueData: "{{A2A86C88-5B89-4D5E-92B3-7A6CF4E0A1B7}"; Flags: uninsdeletevalue
Root: HKLM64; Subkey: "Software\Classes\SystemFileAssociations\.gltf\ShellEx\{{E357FCCD-A995-4576-B01F-234630154E96}"; ValueType: string; ValueName: ""; ValueData: "{{6B07920A-CFEC-42E6-B718-73A199156EFD}"; Flags: uninsdeletevalue

; glTF may reference adjacent .bin and texture files. The file-based handler
; therefore needs the original path instead of an isolated file stream.
Root: HKLM64; Subkey: "Software\Classes\CLSID\{{6B07920A-CFEC-42E6-B718-73A199156EFD}"; ValueType: dword; ValueName: "DisableProcessIsolation"; ValueData: "1"; Flags: uninsdeletevalue

; Large multi-resolution fallback icon (16 through 256 px)
Root: HKLM64; Subkey: "Software\Classes\SystemFileAssociations\.glb\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\Assets\Icons\GlbFile.ico,0"; Flags: uninsdeletevalue
Root: HKLM64; Subkey: "Software\Classes\SystemFileAssociations\.gltf\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\Assets\Icons\GlbFile.ico,0"; Flags: uninsdeletevalue

; Offer FastView in Open with, without replacing the user's default app.
Root: HKLM64; Subkey: "Software\Classes\Applications\{#MyAppExeName}"; ValueType: string; ValueName: "FriendlyAppName"; ValueData: "FastView"; Flags: uninsdeletekey
Root: HKLM64; Subkey: "Software\Classes\Applications\{#MyAppExeName}\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" ""%1"""
Root: HKLM64; Subkey: "Software\Classes\Applications\{#MyAppExeName}\SupportedTypes"; ValueType: string; ValueName: ".glb"; ValueData: ""
Root: HKLM64; Subkey: "Software\Classes\Applications\{#MyAppExeName}\SupportedTypes"; ValueType: string; ValueName: ".gltf"; ValueData: ""

Root: HKLM64; Subkey: "Software\Microsoft\Windows\CurrentVersion\App Paths\{#MyAppExeName}"; ValueType: string; ValueName: ""; ValueData: "{app}\{#MyAppExeName}"; Flags: uninsdeletekey
Root: HKLM64; Subkey: "Software\Microsoft\Windows\CurrentVersion\App Paths\{#MyAppExeName}"; ValueType: string; ValueName: "Path"; ValueData: "{app}"

[Icons]
Name: "{autoprograms}\FastView"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{autodesktop}\FastView"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{sys}\taskkill.exe"; Parameters: "/F /IM dllhost.exe"; Flags: runhidden waituntilterminated
Filename: "{sys}\taskkill.exe"; Parameters: "/F /IM explorer.exe"; Flags: runhidden waituntilterminated; Tasks: restartexplorer
Filename: "{win}\explorer.exe"; Flags: nowait runasoriginaluser; Tasks: restartexplorer
Filename: "{app}\{#MyAppExeName}"; Description: "FastView starten"; Flags: nowait postinstall skipifsilent runasoriginaluser

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/F /IM {#MyAppExeName}"; Flags: runhidden waituntilterminated; RunOnceId: "StopFastView"
Filename: "{sys}\taskkill.exe"; Parameters: "/F /IM dllhost.exe"; Flags: runhidden waituntilterminated; RunOnceId: "StopThumbnailSurrogate"

[Code]
const
  SHCNE_ASSOCCHANGED = $08000000;
  SHCNF_IDLIST = $0000;

function HasDotNet10Runtime: Boolean;
var
  RuntimeRoot: String;
  FindRec: TFindRec;
begin
  Result := False;
  RuntimeRoot := ExpandConstant(
    '{pf}\dotnet\shared\Microsoft.NETCore.App');

  if FindFirst(
    AddBackslash(RuntimeRoot) + '10.*',
    FindRec) then
  begin
    try
      repeat
        if ((FindRec.Attributes and $00000010) <> 0) and
           (FindRec.Name <> '.') and
           (FindRec.Name <> '..') then
        begin
          Result := True;
        end;
      until Result or not FindNext(FindRec);
    finally
      FindClose(FindRec);
    end;
  end;
end;

function InitializeSetup: Boolean;
begin
  Result := HasDotNet10Runtime;

  if not Result then
  begin
    MsgBox(
      'FastView itself is self-contained, but the Windows Explorer ' +
      'thumbnail provider requires the Microsoft .NET 10 Runtime (x64).' +
      Chr(13) + Chr(10) + Chr(13) + Chr(10) +
      'Install the x64 .NET 10 Runtime, then start this installer again.',
      mbError,
      MB_OK);
  end;
end;

procedure SHChangeNotify(
  wEventId: LongWord;
  uFlags: LongWord;
  dwItem1: LongInt;
  dwItem2: LongInt);
external 'SHChangeNotify@shell32.dll stdcall';

procedure NotifyShellOfAssociationChange;
begin
  SHChangeNotify(
    SHCNE_ASSOCCHANGED,
    SHCNF_IDLIST,
    0,
    0);
end;

procedure StopProcess(const ImageName: String);
var
  ResultCode: Integer;
begin
  Exec(
    ExpandConstant('{sys}\taskkill.exe'),
    '/F /IM ' + ImageName,
    '',
    SW_HIDE,
    ewWaitUntilTerminated,
    ResultCode);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  StopProcess('{#MyAppExeName}');
  StopProcess('dllhost.exe');
  Result := '';
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    NotifyShellOfAssociationChange;
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
  begin
    NotifyShellOfAssociationChange;
  end;
end;
