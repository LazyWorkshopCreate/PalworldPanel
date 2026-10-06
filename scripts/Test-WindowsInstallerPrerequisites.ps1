#requires -Version 7.0
[CmdletBinding()]
param([string]$IsccPath = (Join-Path $env:ProgramFiles 'Inno Setup 7/ISCC.exe'))
$ErrorActionPreference='Stop'
$repository=Split-Path -Parent $PSScriptRoot
$output=Join-Path $repository 'artifacts/windows/prerequisite-probe'
New-Item -ItemType Directory -Path $output -Force | Out-Null
& $IsccPath /Q "/DOutputDirectory=$output" (Join-Path $repository 'tests/windows-prerequisites.iss')
if ($LASTEXITCODE) { throw 'Inno 依赖探针编译失败。' }
$report=Join-Path $output 'dependency-probe-result.txt'
if (Test-Path -LiteralPath $report) { Remove-Item -LiteralPath $report }
$process=Start-Process -FilePath (Join-Path $output 'dependency-probe.exe') -ArgumentList '/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART' -WindowStyle Hidden -Wait -PassThru
# InitializeSetup deliberately returns False after the shared check: never install or register a service.
if (-not (Test-Path -LiteralPath $report) -or (Get-Content -LiteralPath $report -Raw) -ne 'PASS') { throw '实际 Inno 调用链检查失败，见 artifacts/windows/prerequisite-probe/dependency-probe-result.txt。' }
Write-Output 'PASS actual Inno executable/native PowerShell/Docker/Compose, shared installer code; no installation or service changes'
