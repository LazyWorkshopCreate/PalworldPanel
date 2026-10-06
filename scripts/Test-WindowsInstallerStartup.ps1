#requires -Version 7.0
[CmdletBinding()]
param([string]$IsccPath = (Join-Path $env:ProgramFiles 'Inno Setup 7/ISCC.exe'))
$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
$publish = Join-Path $repository 'artifacts/windows/publish'
$output = Join-Path $repository 'artifacts/windows/wizard-startup-probe'
New-Item -ItemType Directory -Path $output -Force | Out-Null
& $IsccPath /Q /DWizardStartupProbe "/DPublishDirectory=$publish" "/DOutputDirectory=$output" (Join-Path $repository 'deploy/windows/PalworldPanel.iss')
if ($LASTEXITCODE) { throw '只读安装向导探针编译失败。' }
$report = Join-Path $output 'wizard-startup-result.txt'
if (Test-Path -LiteralPath $report) { Remove-Item -LiteralPath $report }
$process = Start-Process -FilePath (Join-Path $output 'wizard-startup-probe.exe') -ArgumentList '/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART' -WindowStyle Hidden -PassThru
try {
    if (-not $process.WaitForExit(30000)) { $process.Kill(); throw '向导启动探针超时。' }
    if (-not (Test-Path -LiteralPath $report) -or (Get-Content -LiteralPath $report -Raw) -ne 'PASS') { throw '安装向导未正常初始化。' }
} finally { $process.Dispose() }
Write-Output 'PASS actual Inno wizard defaults, required fields, invalid bind address rejected with exact Chinese UTF-8 message; aborted before installation/service changes'
