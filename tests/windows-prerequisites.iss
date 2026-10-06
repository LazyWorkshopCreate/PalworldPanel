[Setup]
AppName=PalworldPanel dependency probe
AppVersion=0.1
DefaultDirName={tmp}\unused-panel-probe
PrivilegesRequired=lowest
OutputDir={#OutputDirectory}
OutputBaseFilename=dependency-probe
Uninstallable=no
[Files]
Source: "..\deploy\windows\Check-Prerequisites.ps1"; Flags: dontcopy
[Code]
#include "..\deploy\windows\InstallerPrerequisites.iss"
function InitializeSetup(): Boolean;
var Report: String;
begin
  Report := CheckInstallerPrerequisites();
  if Report = '' then Report := 'PASS';
  SaveStringToFile(ExpandConstant('{src}\dependency-probe-result.txt'), Report, False);
  Result := False;
end;
