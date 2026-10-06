#ifndef PublishDirectory
  #error PublishDirectory must point to the win-x64 publish folder
#endif
#ifndef OutputDirectory
  #define OutputDirectory "..\..\artifacts\windows\installer"
#endif
#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef NumericVersion
  #define NumericVersion AppVersion
#endif
[Setup]
AppId={{0312AD13-9701-4CB7-9250-78B23B4590F6}
AppName=PalworldPanel
AppVersion={#AppVersion}
VersionInfoVersion={#NumericVersion}
DefaultDirName={autopf}\PalworldPanel
DisableDirPage=yes
DefaultGroupName=PalworldPanel
#ifdef WizardStartupProbe
PrivilegesRequired=lowest
#else
PrivilegesRequired=admin
#endif
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19045
OutputDir={#OutputDirectory}
#ifdef WizardStartupProbe
OutputBaseFilename=wizard-startup-probe
#else
OutputBaseFilename=PalworldPanel-{#AppVersion}-win-x64-setup
#endif
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=no
RestartApplications=no
UninstallDisplayIcon={app}\PalworldPanel.Server.exe
UninstallFilesDir={commonappdata}\PalworldPanel\installer

[Files]
Source: "Check-Prerequisites.ps1"; Flags: dontcopy
Source: "Configure-Installation.ps1"; Flags: dontcopy
Source: "Replace-ProgramDirectory.ps1"; Flags: dontcopy
Source: "Service.ps1"; Flags: dontcopy
Source: "Get-InstallationDefaults.ps1"; Flags: dontcopy
Source: "{#PublishDirectory}\*"; DestDir: "{code:GetStageDirectory}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\部署说明"; Filename: "{app}\docs\operations\2026-10-06-windows-deployment-r4.md"

[UninstallDelete]
Type: filesandordirs; Name: "{app}"

[Code]
#include "InstallerPrerequisites.iss"
var
  ModePage: TInputOptionWizardPage;
  SettingsPage: TInputQueryWizardPage;
  StageDirectory, SettingsFile, JournalFile, LastScriptError: String;
  HadProgram: Boolean;

function GetStageDirectory(Param: String): String;
var AppDirectory: String;
begin
  if StageDirectory = '' then begin
    AppDirectory := WizardDirValue;
    if AppDirectory = '' then RaiseException('程序目录尚未初始化。');
    StageDirectory := AppDirectory + '.stage-' + GetDateTimeString('yyyymmddhhnnss', '-', ':') + '-' + ExtractFileName(ExpandConstant('{tmp}'));
    JournalFile := ExpandConstant('{commonappdata}\PalworldPanel\installer\') + ExtractFileName(StageDirectory) + '.json';
    HadProgram := FileExists(AddBackslash(AppDirectory) + 'PalworldPanel.Server.exe');
  end;
  Result := StageDirectory;
end;

function JsonEscape(Value: String): String;
begin
  Result := Value;
  StringChangeEx(Result, '\', '\\', True);
  StringChangeEx(Result, '"', '\"', True);
  StringChangeEx(Result, #13, '', True);
  StringChangeEx(Result, #10, '', True);
end;

function RunScript(Path, Arguments, WorkingDirectory: String): Boolean;
var ExitCode: Integer; Output: TExecOutput; ErrorFile: String; ErrorText: AnsiString;
begin
  LastScriptError := '';
  ErrorFile := ExpandConstant('{tmp}\installer-script-error.txt');
  DeleteFile(ErrorFile);
  Result := ExecAndCaptureOutputWithNativeSysDir(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
    '-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' + Path + '" ' + Arguments + ' -InstallerErrorFile "' + ErrorFile + '"',
    WorkingDirectory, SW_HIDE, ewWaitUntilTerminated, ExitCode, Output);
  if not Result then LastScriptError := SysErrorMessage(ExitCode)
  else if ExitCode <> 0 then begin
    Result := False;
    if LoadStringFromFile(ErrorFile, ErrorText) then LastScriptError := UTF8Decode(ErrorText);
    if LastScriptError = '' then LastScriptError := '部署脚本执行失败（退出码 ' + IntToStr(ExitCode) + '）。';
  end;
end;

function SettingsValidationError(): String;
var Port: Integer;
begin
  Result := '';
  if (Trim(SettingsPage.Values[0]) = '') or (Trim(SettingsPage.Values[2]) = '') or (Trim(SettingsPage.Values[3]) = '') then begin
    Result := '监听 IP、访问白名单和 Docker Desktop 账户均为必填项。'; Exit;
  end;
  Port := StrToIntDef(Trim(SettingsPage.Values[1]), 0);
  if (Port < 1024) or (Port > 65535) then Result := '管理端口须为 1024–65535。';
end;

procedure InitializeWizard();
var Identity, DefaultsFile: String; Defaults: TArrayOfString; ExitCode: Integer;
begin
  SettingsFile := ExpandConstant('{tmp}\installation-settings.json');
  ModePage := CreateInputOptionPage(wpWelcome, '安装方式', '请选择此次安装的操作', '重新配置会修改网络参数；仅升级程序保留全部现有配置和管理员密码。两种方式均保留实例与存档。', True, False);
  ModePage.Add('重新配置');
  ModePage.Add('仅升级程序');
  if FileExists(ExpandConstant('{commonappdata}\PalworldPanel\private\panel.json')) then ModePage.SelectedValueIndex := 1
  else ModePage.SelectedValueIndex := 0;
  SettingsPage := CreateInputQueryPage(ModePage.ID, '重新配置', '设置管理网站访问参数', '填写本机内网 IPv4 和精确访问白名单。管理员密码在首次访问网站时设置；已有密码保留。');
  SettingsPage.Add('监听 IP：', False);
  SettingsPage.Add('管理端口：', False);
  SettingsPage.Add('允许访问的 IP（逗号分隔）：', False);
  SettingsPage.Add('Docker Desktop Windows 账户（计算机或域\用户名）：', False);
  SettingsPage.Values[1] := '18080';
  Identity := GetEnv('USERDOMAIN') + '\' + GetUserNameString;
  SettingsPage.Values[3] := Identity;
  DefaultsFile := ExpandConstant('{tmp}\installation-defaults.txt');
  ExtractTemporaryFile('Get-InstallationDefaults.ps1');
  if ExecWithNativeSysDir(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
    '-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' + ExpandConstant('{tmp}\Get-InstallationDefaults.ps1') + '" -OutputFile "' + DefaultsFile + '"',
    ExpandConstant('{tmp}'), SW_HIDE, ewWaitUntilTerminated, ExitCode) then begin
    if (ExitCode = 0) and LoadStringsFromFile(DefaultsFile, Defaults) then begin
      if GetArrayLength(Defaults) = 3 then begin
        SettingsPage.Values[0] := Defaults[0];
        SettingsPage.Values[1] := Defaults[1];
        SettingsPage.Values[2] := Defaults[2];
      end;
    end;
  end;
#ifdef WizardStartupProbe
  if ModePage.CheckListBox.Items.Count <> 2 then RaiseException('安装方式数量错误。');
  SaveStringToFile(ExpandConstant('{src}\wizard-startup-result.txt'), UTF8Encode('默认参数检查：' + SettingsValidationError()), False);
  if SettingsValidationError() <> '' then RaiseException('默认参数不完整。');
  SettingsPage.Values[0] := '';
  if SettingsValidationError() = '' then RaiseException('空配置未被拦截。');
  SettingsPage.Values[0] := '10.0.0.10';
  SettingsPage.Values[2] := '10.0.0.11';
  if SettingsValidationError() <> '' then RaiseException('完整输入被错误拦截。');
  ExtractTemporaryFile('Configure-Installation.ps1');
  ExtractTemporaryFile('Check-Prerequisites.ps1');
  SaveStringToFile(SettingsFile, '{"bindIp":"127.0.0.1","port":"18080","allowedIps":"10.0.0.11","account":"test"}', False);
  if RunScript(ExpandConstant('{tmp}\Configure-Installation.ps1'),
    '-Mode ValidateInput -SettingsFile "' + SettingsFile + '" -InstallDirectory "' + ExpandConstant('{tmp}') + '"', ExpandConstant('{tmp}')) then RaiseException('非法监听地址未被拦截。');
  SaveStringToFile(ExpandConstant('{src}\wizard-startup-result.txt'), UTF8Encode('中文传输检查：' + LastScriptError), False);
  if LastScriptError <> '监听地址必须是本机网卡上的内网 IPv4，请填写 IP 地址而非计算机名、远端地址或 127.0.0.1。' then RaiseException('中文错误传输失败。');
  SaveStringToFile(ExpandConstant('{src}\wizard-startup-result.txt'), 'PASS', False);
  Abort;
#endif
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := (PageID = SettingsPage.ID) and (ModePage.SelectedValueIndex = 1);
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var ValidationError: String;
begin
  Result := True;
  if (CurPageID = ModePage.ID) and (ModePage.SelectedValueIndex = 1) and
    not FileExists(ExpandConstant('{commonappdata}\PalworldPanel\private\panel.json')) then begin
    MsgBox('尚无现有配置，请选择重新配置。', mbError, MB_OK); Result := False; Exit;
  end;
  if CurPageID = SettingsPage.ID then begin
    ValidationError := SettingsValidationError();
    if ValidationError <> '' then begin
      MsgBox(ValidationError, mbError, MB_OK); Result := False;
    end;
  end;
end;

function ServiceScript(Mode: String): Boolean;
begin
  Result := RunScript(ExpandConstant('{app}\deployment\Service.ps1'),
    '-Mode ' + Mode + ' -InstallDirectory "' + ExpandConstant('{app}') + '"', ExpandConstant('{app}'));
end;

function DirectoryArguments(Mode: String): String;
begin
  Result := '-Mode ' + Mode + ' -InstallDirectory "' + ExpandConstant('{app}') + '" -StageDirectory "' + StageDirectory + '" -JournalFile "' + JournalFile + '"';
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var SettingsJson: String;
begin
  Result := '';
#ifdef WizardStartupProbe
  Result := '只读向导检查，禁止安装。'; Exit;
#endif
  StageDirectory := GetStageDirectory('');
  if (ModePage.SelectedValueIndex = 1) and not FileExists(ExpandConstant('{commonappdata}\PalworldPanel\private\panel.json')) then begin
    Result := '尚无现有配置，请选择重新配置。'; Exit;
  end;
  ExtractTemporaryFile('Check-Prerequisites.ps1');
  ExtractTemporaryFile('Configure-Installation.ps1');
  ExtractTemporaryFile('Replace-ProgramDirectory.ps1');
  ExtractTemporaryFile('Service.ps1');
  if DirExists(StageDirectory) then begin Result := '暂存目录已存在，请退出并重新运行安装器。'; Exit; end;
  if not RunScript(ExpandConstant('{tmp}\Replace-ProgramDirectory.ps1'), DirectoryArguments('Validate'), ExpandConstant('{tmp}')) then begin
    Result := LastScriptError; Exit;
  end;
  if ModePage.SelectedValueIndex = 0 then begin
    SettingsJson := '{"bindIp":"' + JsonEscape(Trim(SettingsPage.Values[0])) + '","port":"' + JsonEscape(Trim(SettingsPage.Values[1])) + '","allowedIps":"' + JsonEscape(Trim(SettingsPage.Values[2])) + '","account":"' + JsonEscape(Trim(SettingsPage.Values[3])) + '"}';
    if not SaveStringToFile(SettingsFile, UTF8Encode(SettingsJson), False) then begin Result := '无法保存安装设置。'; Exit; end;
    if not RunScript(ExpandConstant('{tmp}\Configure-Installation.ps1'),
      '-Mode ValidateInput -SettingsFile "' + SettingsFile + '" -InstallDirectory "' + ExpandConstant('{app}') + '"', ExpandConstant('{tmp}')) then begin Result := LastScriptError; Exit; end;
  end;
  if HadProgram then
    if not RunScript(ExpandConstant('{tmp}\Service.ps1'), '-Mode PrepareUpgrade -InstallDirectory "' + ExpandConstant('{app}') + '"', ExpandConstant('{tmp}')) then begin Result := LastScriptError; Exit; end;
  if ModePage.SelectedValueIndex = 0 then begin
    if not RunScript(ExpandConstant('{tmp}\Configure-Installation.ps1'),
      '-Mode Validate -SettingsFile "' + SettingsFile + '" -InstallDirectory "' + ExpandConstant('{app}') + '"', ExpandConstant('{tmp}')) then begin Result := LastScriptError; Exit; end;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var Failure: String;
begin
  if CurStep = ssPostInstall then begin
    ForceDirectories(ExtractFileDir(JournalFile));
    if not RunScript(ExpandConstant('{tmp}\Replace-ProgramDirectory.ps1'), DirectoryArguments('Activate'), ExpandConstant('{tmp}')) then
      RaiseException('程序目录替换失败：' + LastScriptError);
    try
      if not ServiceScript('Register') then RaiseException(LastScriptError);
      if ModePage.SelectedValueIndex = 0 then
        if not RunScript(ExpandConstant('{tmp}\Configure-Installation.ps1'),
          '-Mode Apply -SettingsFile "' + SettingsFile + '" -InstallDirectory "' + ExpandConstant('{app}') + '"', ExpandConstant('{tmp}')) then RaiseException(LastScriptError);
    except
      Failure := GetExceptionMessage;
      if not HadProgram then ServiceScript('Unregister');
      if not RunScript(ExpandConstant('{tmp}\Replace-ProgramDirectory.ps1'), DirectoryArguments('Rollback'), ExpandConstant('{tmp}')) then
        Failure := Failure + #13#10 + '程序回退失败，请按交换日志人工恢复：' + JournalFile;
      RaiseException('安装未完成，服务保持停止。' + #13#10 + Failure);
    end;
  end;
end;

function InitializeUninstall(): Boolean;
begin
  StageDirectory := ExpandConstant('{app}') + '.stage-uninstall-validation';
  JournalFile := ExpandConstant('{commonappdata}\PalworldPanel\installer\unused-uninstall.json');
  Result := RunScript(ExpandConstant('{app}\deployment\Replace-ProgramDirectory.ps1'), DirectoryArguments('Validate'), ExpandConstant('{app}'));
  if Result then Result := ServiceScript('Unregister');
  if not Result then MsgBox('无法安全卸载：' + LastScriptError, mbError, MB_OK);
end;
