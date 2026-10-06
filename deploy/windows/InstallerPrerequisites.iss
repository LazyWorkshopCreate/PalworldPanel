function CheckInstallerPrerequisites(): String;
var ExitCode: Integer; Started: Boolean; Params, ErrorFile: String; ErrorText: AnsiString; Output: TExecOutput;
begin
  Result := '';
  ExtractTemporaryFile('Check-Prerequisites.ps1');
  ErrorFile := ExpandConstant('{tmp}\dependency-error.txt');
  DeleteFile(ErrorFile);
  Params := '-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' + ExpandConstant('{tmp}\Check-Prerequisites.ps1') + '" -DockerExecutable "' + ExpandConstant('{commonpf64}\Docker\Docker\resources\bin\docker.exe') + '" -ComposePluginDirectory "' + ExpandConstant('{commonpf64}\Docker\Docker\resources\cli-plugins') + '" -InstallerErrorFile "' + ErrorFile + '"';
  Started := ExecAndCaptureOutputWithNativeSysDir(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
    Params, ExpandConstant('{tmp}'), SW_HIDE, ewWaitUntilTerminated, ExitCode, Output);
  if not Started then begin
    Result := '无法启动依赖检查：' + SysErrorMessage(ExitCode);
    Exit;
  end;
  if ExitCode <> 0 then begin
    Result := '依赖检查失败（退出码 ' + IntToStr(ExitCode) + '）：';
    if LoadStringFromFile(ErrorFile, ErrorText) then Result := Result + #13#10 + UTF8Decode(ErrorText)
    else
      Result := Result + #13#10 + '请检查 Docker Desktop Linux 引擎及 Compose。';
  end;
end;
