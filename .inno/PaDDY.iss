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
WizardStyle=modern
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
Name: "installvad";     Description: "Install Virtual Audio Driver (virtual speaker and microphone)"; GroupDescription: "Optional components:"; Flags: unchecked

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
; pnputil alone cannot create a root-enumerated device node — install.ps1 does
; both: stages the driver package AND creates ROOT\VirtualAudioDriver via SetupAPI
; (equivalent to "Add Legacy Hardware" in Device Manager).
Filename: "powershell.exe"; Parameters: "-ExecutionPolicy Bypass -NonInteractive -WindowStyle Hidden -File ""{app}\vad\install.ps1"" -InfPath ""{app}\vad\VirtualAudioDriver.inf"""; StatusMsg: "Installing Virtual Audio Driver…"; Tasks: installvad; Flags: waituntilterminated

; ============================================================================
[Code]

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  AppDataPath: string;
  Msg: string;
  Answer: Integer;
begin
  if CurUninstallStep = usPostUninstall then
  begin
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
