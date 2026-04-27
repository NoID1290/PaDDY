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

LicenseFile=..\LICENSE
SetupIconFile=..\PaDDY.ico

; Self-contained build — no .NET prerequisite needed
; Requires Windows 10 1809+ (minimum for .NET 8)
MinVersion=10.0.17763

ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

; Machine install only. Runtime writable data is stored per-user under LocalAppData.
PrivilegesRequired=admin

Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern

; Branded wizard images — regenerate with .inno\gen-images.ps1
WizardImageFile=wizard-sidebar.bmp
WizardSmallImageFile=wizard-small.bmp
WizardImageStretch=no

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
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

; ============================================================================
[Files]
; All files from the self-contained publish directory
Source: "{#SourceDir}\*"; DestDir: "{app}"; Excludes: "*.dll.config"; Flags: ignoreversion recursesubdirs createallsubdirs

; ============================================================================
[Icons]
Name: "{group}\{#AppName}";                              Filename: "{app}\{#AppExeName}"
Name: "{group}\{cm:UninstallProgram,{#AppName}}";        Filename: "{uninstallexe}"
Name: "{commondesktop}\{#AppName}";                      Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

; ============================================================================
[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(AppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

; ============================================================================
[Code]

{ ── Wizard form theming: match PaDDY's dark-navy / green-accent palette ──── }
const
  clWindowBg    = $140D0D;   { #0D0D14  window background  }
  clPanelBg     = $281A1A;   { #1A1A28  card / panel bg    }
  clPrimaryText = $F4E8E8;   { #E8E8F4  primary text       }
  clSecondText  = $CCB0B0;   { #B0B0CC  secondary text     }
  clSubtleText  = $A07070;   { #7070A0  subtle / muted     }
  clAccentGreen = $50AF4C;   { #4CAF50  green accent       }
  clSeparator   = $402828;   { #282840  separator bevel    }

procedure InitializeWizard();
begin
  { ── Form & notebook backgrounds ─────────────────────────────────────── }
  WizardForm.Color := clWindowBg;
  WizardForm.Font.Color := clPrimaryText;
  WizardForm.MainPanel.Color := clWindowBg;

  { ── Notebook / page container backgrounds (fixes the white page canvas) }
  WizardForm.InnerNotebook.Color := clWindowBg;
  WizardForm.OuterNotebook.Color := clWindowBg;
  WizardForm.WelcomePage.Color   := clWindowBg;
  WizardForm.FinishedPage.Color  := clWindowBg;
  WizardForm.InnerPage.Color     := clWindowBg;
  var I: Integer;
  for I := 0 to WizardForm.InnerNotebook.PageCount - 1 do
    WizardForm.InnerNotebook.Pages[I].Color := clWindowBg;
  for I := 0 to WizardForm.OuterNotebook.PageCount - 1 do
    WizardForm.OuterNotebook.Pages[I].Color := clWindowBg;

  { ── Bevel separator lines ────────────────────────────────────────────── }
  WizardForm.Bevel.Visible  := False;
  WizardForm.Bevel1.Visible := False;

  { ── Progress bar ─────────────────────────────────────────────────────── }
  WizardForm.ProgressGauge.BackColor := clPanelBg;

  { ── Image backgrounds (no white halo around sidebar / small bitmaps) ─── }
  WizardForm.WizardBitmapImage.BackColor      := clWindowBg;
  WizardForm.WizardSmallBitmapImage.BackColor := clWindowBg;
  WizardForm.WizardBitmapImage2.BackColor     := clWindowBg;

  { ── Page header labels (shown on inner pages) ───────────────────────── }
  WizardForm.PageNameLabel.Font.Color := clPrimaryText;
  WizardForm.PageDescriptionLabel.Font.Color := clSecondText;

  { ── Welcome page ────────────────────────────────────────────────────── }
  WizardForm.WelcomeLabel1.Font.Color := clAccentGreen;
  WizardForm.WelcomeLabel2.Font.Color := clSecondText;

  { ── Finished page ───────────────────────────────────────────────────── }
  WizardForm.FinishedHeadingLabel.Font.Color := clAccentGreen;
  WizardForm.FinishedLabel.Font.Color := clSecondText;

  { ── License page ────────────────────────────────────────────────────── }
  WizardForm.LicenseLabel1.Font.Color := clSecondText;
  WizardForm.LicenseMemo.Color := clPanelBg;
  WizardForm.LicenseMemo.Font.Color := clSecondText;
  WizardForm.LicenseAcceptedRadio.Font.Color := clPrimaryText;
  WizardForm.LicenseNotAcceptedRadio.Font.Color := clSecondText;

  { ── Select directory page ───────────────────────────────────────────── }
  WizardForm.SelectDirLabel.Font.Color := clSecondText;
  WizardForm.SelectDirBrowseLabel.Font.Color := clSubtleText;
  WizardForm.DirEdit.Color := clPanelBg;
  WizardForm.DirEdit.Font.Color := clPrimaryText;
  WizardForm.DirBrowseButton.Font.Color := clPrimaryText;

  { ── Select tasks page ───────────────────────────────────────────────── }
  WizardForm.SelectTasksLabel.Font.Color := clSecondText;
  WizardForm.TasksList.Color := clPanelBg;
  WizardForm.TasksList.Font.Color := clPrimaryText;

  { ── Ready to install page ───────────────────────────────────────────── }
  WizardForm.ReadyLabel.Font.Color := clSecondText;
  WizardForm.ReadyMemo.Color := clPanelBg;
  WizardForm.ReadyMemo.Font.Color := clSecondText;

  { ── Installing page ─────────────────────────────────────────────────── }
  WizardForm.FilenameLabel.Font.Color := clSubtleText;
  WizardForm.StatusLabel.Font.Color := clSecondText;

  { ── Buttons (font only — OS visual styles control button backgrounds) ── }
  WizardForm.NextButton.Font.Color := clPrimaryText;
  WizardForm.BackButton.Font.Color := clSecondText;
  WizardForm.CancelButton.Font.Color := clSecondText;
end;

procedure CurPageChanged(CurPageID: Integer);
var I: Integer;
begin
  { Re-apply container backgrounds on every page transition so no white flash }
  WizardForm.InnerNotebook.Color := clWindowBg;
  WizardForm.InnerPage.Color     := clWindowBg;
  for I := 0 to WizardForm.InnerNotebook.PageCount - 1 do
    WizardForm.InnerNotebook.Pages[I].Color := clWindowBg;
end;

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
