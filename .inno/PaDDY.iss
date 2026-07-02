; ============================================================================
; PaDDY Inno Setup Script
; Copyright (c) NoID Softwork 2020-2026. All rights reserved.
;
; Do NOT edit AppVersion, SourceDir, OutputDir, or OutputName manually.
; These are injected at compile-time by push.ps1 via /D command-line defines.
;
; To compile manually (for testing):
;   iscc.exe .inno\PaDDY.iss /DAppVersion=1.0.0.0427 /DSourceDir=bin\artifacts\PaDDY-1.0.0.0427 /DOutputDir=bin\artifacts /DOutputName=PaDDY-1.0.0.0427-Setup
; ============================================================================

; ── Defaults (allow manual compilation without /D flags) ──────────────────
#ifndef AppVersion
  #define AppVersion "0.0.0.0000"
#endif
#ifndef SourceDir
  #define SourceDir "..\bin\artifacts\PaDDY-1.0.0.0427"
#endif
#ifndef OutputDir
  #define OutputDir "..\bin\artifacts"
#endif
#ifndef OutputName
  #define OutputName "PaDDY-Installer"
#endif

; ── Constants ──────────────────────────────────────────────────────────────
#define AppName        "PaDDY"
#define AppPublisher   "NoID Softwork"
#define AppExeName     "PaDDY.exe"
#define AppURL         "https://github.com/NoID1290/Paddy"

; ============================================================================
[Setup]
; IMPORTANT: AppId identifies this application for upgrade detection.
; Never change this GUID once the installer has been distributed,
; or existing installs will be treated as a separate product.
AppId={{8B3F2E4A-C591-47D8-9E3A-B6C4D5E1F70C}

AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
AppSupportURL={#AppURL}
AppUpdatesURL={#AppURL}
UninstallDisplayName={#AppName}
UninstallDisplayIcon={app}\{#AppExeName}

DefaultDirName={autopf}\NoID Softwork\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
DisableWelcomePage=no

LicenseFile=..\LICENSE
SetupIconFile=..\PaDDY.ico
WizardStyle=modern dark polar includetitlebar
WizardImageFile=wizard-sidebar.bmp
WizardSmallImageFile=wizard-small.bmp
WizardImageStretch=no

; Self-contained build — no .NET prerequisite needed
; Requires Windows 10 1809+ (minimum for .NET 8)
MinVersion=10.0.17763

ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

; Machine install only. Runtime writable data is stored per-user under LocalAppData.
PrivilegesRequired=admin

Compression=lzma2/ultra64
SolidCompression=yes

OutputDir={#OutputDir}
OutputBaseFilename={#OutputName}

VersionInfoVersion={#AppVersion}
VersionInfoCompany={#AppPublisher}
VersionInfoProductName={#AppName}
VersionInfoProductVersion={#AppVersion}
VersionInfoDescription={#AppName} Setup
VersionInfoCopyright=Copyright (c) NoID Softwork 2020-2026
VersionInfoTextVersion={#AppVersion}

; ============================================================================
[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

; ============================================================================
[Tasks]
Name: "desktopicon";    Description: "{cm:CreateDesktopIcon}";                                      GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "startmenuicon";  Description: "Create Start Menu shortcut";                                  GroupDescription: "{cm:AdditionalIcons}"; Flags: checkedonce
Name: "installstreamdeckplugin"; Description: "Install Elgato Stream Deck Plugin"; GroupDescription: "Integrations"; Flags: unchecked

; ============================================================================
[Files]
; All files from the self-contained publish directory
Source: "{#SourceDir}\*"; DestDir: "{app}"; Excludes: "*.dll.config"; Flags: ignoreversion recursesubdirs createallsubdirs

; ============================================================================
[Icons]
Name: "{group}\{#AppName}";                              Filename: "{app}\{#AppExeName}"; Tasks: startmenuicon
Name: "{group}\{cm:UninstallProgram,{#AppName}}";        Filename: "{uninstallexe}"; Tasks: startmenuicon
Name: "{commondesktop}\{#AppName}";                      Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

; ============================================================================
[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(AppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
Filename: "{app}\com.paddy.streamDeckPlugin"; Description: "Install Stream Deck Plugin"; Tasks: installstreamdeckplugin; Flags: shellexec waituntilidle skipifsilent

; ============================================================================
; Register .PADBACK file type so it always opens with PaDDY and shows its icon
[Registry]

; ── ProgID ─────────────────────────────────────────────────────────────────
; Friendly description shown in Explorer "Type" column and Open-With dialog
Root: HKCR; Subkey: "PaDDY.BackupFile";                            ValueType: string;  ValueName: "";       ValueData: "PaDDY Backup File";                    Flags: uninsdeletekey
Root: HKCR; Subkey: "PaDDY.BackupFile\DefaultIcon";               ValueType: string;  ValueName: "";       ValueData: "{app}\{#AppExeName},0"
Root: HKCR; Subkey: "PaDDY.BackupFile\shell\open";                ValueType: string;  ValueName: "FriendlyAppName"; ValueData: "{#AppName}"
Root: HKCR; Subkey: "PaDDY.BackupFile\shell\open\command";        ValueType: string;  ValueName: "";       ValueData: """{app}\{#AppExeName}"" ""%1"""

; ── Extension → ProgID mapping ─────────────────────────────────────────────
Root: HKCR; Subkey: ".PADBACK";                                    ValueType: string;  ValueName: "";       ValueData: "PaDDY.BackupFile";                     Flags: uninsdeletevalue
Root: HKCR; Subkey: ".PADBACK";                                    ValueType: string;  ValueName: "Content Type"; ValueData: "application/x-padback"
Root: HKCR; Subkey: ".PADBACK\OpenWithProgids";                    ValueType: string;  ValueName: "PaDDY.BackupFile"; ValueData: ""

; ── Notify shell of the new association ─────────────────────────────────────
; (SHChangeNotify is called from [Code] after install so Explorer refreshes)

; ============================================================================
[Code]

// Shell notification — tells Explorer to refresh file-type icons immediately
// dwItem1/dwItem2 are declared as Integer (not PAnsiChar) so we can pass 0
// when SHCNF_IDLIST is used and both pointers are unused.
procedure SHChangeNotify(wEventId: Integer; uFlags: Cardinal; dwItem1: Integer; dwItem2: Integer);
  external 'SHChangeNotify@shell32.dll stdcall';

procedure CurStepChanged(CurStep: TSetupStep);
begin
  // After all files and registry keys are written, notify the shell so
  // Explorer picks up the new .PADBACK icon/association straight away.
  if CurStep = ssDone then
    SHChangeNotify($08000000 {SHCNE_ASSOCCHANGED}, $0000 {SHCNF_IDLIST}, 0, 0);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  AppDataPath: string;
  Msg: string;
  Answer: Integer;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    // ── Remove .PADBACK file-type registry keys ──────────────────────────
    // The [Registry] Flags: uninsdeletekey / uninsdeletevalue entries handle
    // the ProgID and extension automatically, but belt-and-suspenders cleanup:
    RegDeleteKeyIncludingSubkeys(HKEY_CLASSES_ROOT, 'PaDDY.BackupFile');
    RegDeleteKeyIncludingSubkeys(HKEY_CLASSES_ROOT, '.PADBACK\OpenWithProgids');
    RegDeleteValue(HKEY_CLASSES_ROOT, '.PADBACK', '');
    RegDeleteValue(HKEY_CLASSES_ROOT, '.PADBACK', 'Content Type');

    // Notify shell that associations changed
    SHChangeNotify($08000000, $0000, 0, 0);

    // ── Offer to remove user data ────────────────────────────────────────
    AppDataPath := ExpandConstant('{localappdata}') + '\NoID Softwork\PaDDY';
    if DirExists(AppDataPath) then
    begin
      Msg := 'Do you want to remove your PaDDY recordings and settings?' + #13#10 +
             '' + #13#10 +
             'Folder: ' + AppDataPath + #13#10 +
             '' + #13#10 +
             'This will permanently delete all your saved recordings.' + #13#10 +
             'Choose No to keep your data for a future reinstall.';
      Answer := MsgBox(Msg, mbConfirmation, MB_YESNO or MB_DEFBUTTON2);
      if Answer = IDYES then
        DelTree(AppDataPath, True, True, True);
    end;
  end;
end;
