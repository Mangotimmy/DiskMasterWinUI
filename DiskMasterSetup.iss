; Script generated for Inno Setup 6
; DiskMaster Pro - Advanced Windows Disk & Deployment Manager Standard Installer
; Designed for full upgrade detection, duplicate installation prevention, and multilingual wizard

#ifndef MyAppVersion
#define MyAppVersion "1.4.6"
#endif

#define MyAppName "DiskMaster Pro"
#define MyAppPublisher "DiskMaster Team"
#define MyAppURL "https://github.com/Mangotimmy/DiskMasterWinUI"
#define MyAppExeName "DiskMasterWinUI.exe"
#define MyAppId "{5C17D77F-5A8E-47C2-9118-E8DE79B278AA}"

[Setup]
AppId={{#MyAppId}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
; Support both per-user and per-machine installation modes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
OutputDir=installer_output
OutputBaseFilename=DiskMaster_Setup
SetupIconFile=Assets\AppIcon.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/ultra64
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
WizardStyle=modern
DisableProgramGroupPage=yes
UsePreviousAppDir=yes
CloseApplications=yes
RestartApplications=no
AppMutex=Global\DiskMasterWinUI_SingleInstanceMutex
SetupMutex=Global\DiskMaster_Setup_Mutex
ShowLanguageDialog=auto

[Languages]
Name: "chinesetraditional"; MessagesFile: "tools\InnoLanguages\ChineseTraditional.isl"
Name: "chinesesimplified"; MessagesFile: "tools\InnoLanguages\ChineseSimplified.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "japanese"; MessagesFile: "compiler:Languages\Japanese.isl"

[CustomMessages]
english.RunAsAdminTask=Run application as Administrator (recommended for disk management)
chinesetraditional.RunAsAdminTask=以系統管理員身分執行應用程式 (磁碟管理推薦)
chinesesimplified.RunAsAdminTask=以管理员身份运行应用程序 (磁盘管理推荐)
japanese.RunAsAdminTask=管理者としてアプリケーションを実行 (ディスク管理に推奨)

english.AppAlreadyInstalled=DiskMaster Pro version %1 is already installed on your computer at:%n%n%2%n%nDo you want to reinstall or repair the current installation?
chinesetraditional.AppAlreadyInstalled=DiskMaster Pro 版本 %1 已安裝於您的電腦：%n%n%2%n%n您是否要重新安裝或修復現有安裝？
chinesesimplified.AppAlreadyInstalled=DiskMaster Pro 版本 %1 已安装在您的计算机上：%n%n%2%n%n您是否要重新安装或修复当前安装？
japanese.AppAlreadyInstalled=DiskMaster Pro バージョン %1 は既にインストールされています：%n%n%2%n%n現在のインストールを再インストールまたは修復しますか？

english.AppUpgradePrompt=An existing version of DiskMaster Pro (%1) was found at:%n%n%2%n%nSetup will upgrade your installation to version %3.%n%nDo you wish to continue?
chinesetraditional.AppUpgradePrompt=偵測到已安裝的舊版 DiskMaster Pro (%1)：%n%n%2%n%n安裝精靈將為您升級至版本 %3。%n%n是否繼續？
chinesesimplified.AppUpgradePrompt=检测到已安装的旧版 DiskMaster Pro (%1)：%n%n%2%n%n安装向导将为您升级至版本 %3。%n%n是否继续？
japanese.AppUpgradePrompt=既存の DiskMaster Pro (%1) が見つかりました：%n%n%2%n%nセットアップによりバージョン %3 にアップグレードされます。%n%n続行しますか？

english.AppDowngradeWarning=A newer version of DiskMaster Pro (%1) is already installed at:%n%n%2%n%nDowngrading to version %3 is not recommended.%n%nAre you sure you want to proceed?
chinesetraditional.AppDowngradeWarning=您的系統已安裝較新版本的 DiskMaster Pro (%1)：%n%n%2%n%n不建議降級安裝至版本 %3。%n%n您確定要繼續嗎？
chinesesimplified.AppDowngradeWarning=您的系统已安装较新版本的 DiskMaster Pro (%1)：%n%n%2%n%n不建议降级安装至版本 %3。%n%n您确定要继续吗？
japanese.AppDowngradeWarning=より新しいバージョンの DiskMaster Pro (%1) が既にインストールされています：%n%n%2%n%nバージョン %3 へのダウングレードは推奨されません。%n%n続行してもよろしいですか？

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "runasadmin"; Description: "{cm:RunAsAdminTask}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\Assets\AppIcon.ico"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\Assets\AppIcon.ico"; Tasks: desktopicon

[Registry]
Root: HKA; Subkey: "Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers"; ValueType: string; ValueName: "{app}\{#MyAppExeName}"; ValueData: "~ RUNASADMIN"; Tasks: runasadmin; Flags: uninsdeletevalue

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[Code]
var
  G_ExistingVer: String;
  G_ExistingPath: String;
  G_ExistingRootKey: Integer;
  G_HasExisting: Boolean;

function GetVersionPart(var S: String): Integer;
var
  P: Integer;
begin
  P := Pos('.', S);
  if P > 0 then
  begin
    Result := StrToIntDef(Copy(S, 1, P - 1), 0);
    Delete(S, 1, P);
  end
  else
  begin
    Result := StrToIntDef(S, 0);
    S := '';
  end;
end;

function CompareVersion(V1, V2: String): Integer;
var
  N1, N2: Integer;
begin
  Result := 0;
  while (V1 <> '') or (V2 <> '') do
  begin
    N1 := GetVersionPart(V1);
    N2 := GetVersionPart(V2);
    if N1 > N2 then
    begin
      Result := 1;
      Exit;
    end
    else if N1 < N2 then
    begin
      Result := -1;
      Exit;
    end;
  end;
end;

function FindExistingInstallation(var InstalledVer, InstalledPath: String; var InstalledRootKey: Integer): Boolean;
var
  SubKey: String;
  ExeFile: String;
begin
  Result := False;
  InstalledVer := '';
  InstalledPath := '';

  // 1. Check Inno Setup registry in HKLM
  SubKey := 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{#MyAppId}_is1';
  if RegQueryStringValue(HKLM, SubKey, 'DisplayVersion', InstalledVer) then
  begin
    RegQueryStringValue(HKLM, SubKey, 'InstallLocation', InstalledPath);
    InstalledRootKey := HKLM;
    Result := True;
    Exit;
  end;

  // 2. Check Inno Setup registry in HKCU
  if RegQueryStringValue(HKCU, SubKey, 'DisplayVersion', InstalledVer) then
  begin
    RegQueryStringValue(HKCU, SubKey, 'InstallLocation', InstalledPath);
    InstalledRootKey := HKCU;
    Result := True;
    Exit;
  end;

  // 3. Check Native C# installer registry in HKLM
  SubKey := 'Software\Microsoft\Windows\CurrentVersion\Uninstall\DiskMasterPro';
  if RegQueryStringValue(HKLM, SubKey, 'DisplayVersion', InstalledVer) then
  begin
    RegQueryStringValue(HKLM, SubKey, 'InstallLocation', InstalledPath);
    InstalledRootKey := HKLM;
    Result := True;
    Exit;
  end;

  // 4. Check Native C# installer registry in HKCU
  if RegQueryStringValue(HKCU, SubKey, 'DisplayVersion', InstalledVer) then
  begin
    RegQueryStringValue(HKCU, SubKey, 'InstallLocation', InstalledPath);
    InstalledRootKey := HKCU;
    Result := True;
    Exit;
  end;

  // 5. Fallback check for Program Files
  ExeFile := ExpandConstant('{commonpf}\{#MyAppName}\{#MyAppExeName}');
  if FileExists(ExeFile) then
  begin
    InstalledPath := ExpandConstant('{commonpf}\{#MyAppName}');
    GetVersionNumbersString(ExeFile, InstalledVer);
    InstalledRootKey := HKLM;
    Result := True;
    Exit;
  end;

  // 6. Fallback check for LocalAppData Programs
  ExeFile := ExpandConstant('{localappdata}\Programs\{#MyAppName}\{#MyAppExeName}');
  if FileExists(ExeFile) then
  begin
    InstalledPath := ExpandConstant('{localappdata}\Programs\{#MyAppName}');
    GetVersionNumbersString(ExeFile, InstalledVer);
    InstalledRootKey := HKCU;
    Result := True;
    Exit;
  end;
end;

function InitializeSetup(): Boolean;
var
  Cmp: Integer;
  PromptMsg: String;
begin
  Result := True;
  G_HasExisting := FindExistingInstallation(G_ExistingVer, G_ExistingPath, G_ExistingRootKey);

  if G_HasExisting then
  begin
    Cmp := CompareVersion(G_ExistingVer, '{#MyAppVersion}');
    if Cmp = 0 then
    begin
      // Exactly same version is already installed -> prevent accidental repeated installation
      PromptMsg := FmtMessage(CustomMessage('AppAlreadyInstalled'), [G_ExistingVer, G_ExistingPath]);
      if MsgBox(PromptMsg, mbConfirmation, MB_YESNO or MB_DEFBUTTON2) <> IDYES then
      begin
        Result := False;
        Exit;
      end;
    end
    else if Cmp < 0 then
    begin
      // Older version is installed -> confirm upgrade
      PromptMsg := FmtMessage(CustomMessage('AppUpgradePrompt'), [G_ExistingVer, G_ExistingPath, '{#MyAppVersion}']);
      if MsgBox(PromptMsg, mbInformation, MB_YESNO) <> IDYES then
      begin
        Result := False;
        Exit;
      end;
    end
    else
    begin
      // Newer version is installed -> warn against downgrade
      PromptMsg := FmtMessage(CustomMessage('AppDowngradeWarning'), [G_ExistingVer, G_ExistingPath, '{#MyAppVersion}']);
      if MsgBox(PromptMsg, mbError, MB_YESNO or MB_DEFBUTTON2) <> IDYES then
      begin
        Result := False;
        Exit;
      end;
    end;
  end;
end;

procedure InitializeWizard();
begin
  if G_HasExisting and (G_ExistingPath <> '') and DirExists(G_ExistingPath) then
  begin
    WizardForm.DirEdit.Text := G_ExistingPath;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  LegacyKey: String;
begin
  if CurStep = ssInstall then
  begin
    // Clean up legacy native C# installer ARP entries to avoid duplicates in Add/Remove Programs
    LegacyKey := 'Software\Microsoft\Windows\CurrentVersion\Uninstall\DiskMasterPro';
    RegDeleteKeyIncludingSubkeys(HKLM, LegacyKey);
    RegDeleteKeyIncludingSubkeys(HKCU, LegacyKey);
  end;
end;
