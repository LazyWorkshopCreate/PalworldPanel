#requires -Version 7.0
[CmdletBinding()]
param([switch]$SkipDocker, [switch]$LinuxTestsAsRoot)
$ErrorActionPreference = 'Stop'
function Invoke-Checked { param([string]$Command, [string[]]$Arguments) & $Command @Arguments; if ($LASTEXITCODE) { throw "检查失败：$Command" } }
$repository = Split-Path -Parent $PSScriptRoot
Push-Location $repository
try {
    & (Join-Path $PSScriptRoot 'Test-Documentation.ps1')
    Invoke-Checked dotnet @('restore','PalworldPanel.sln','--locked-mode')
    if ($LinuxTestsAsRoot) {
        if (-not $IsLinux) { throw 'LinuxTestsAsRoot 只支持隔离 Linux 测试环境。' }
        Invoke-Checked sudo @('dotnet','test','PalworldPanel.sln','--no-restore','-c','Release')
    } else {
        Invoke-Checked dotnet @('test','PalworldPanel.sln','--no-restore','-c','Release')
    }
    Push-Location (Join-Path $repository 'src/PalworldPanel.AdminWeb')
    try {
        Invoke-Checked pnpm @('install','--frozen-lockfile')
        Invoke-Checked pnpm @('exec','prettier','--check','src','index.html','package.json','tsconfig.json','vite.config.ts')
        Invoke-Checked pnpm @('test')
        Invoke-Checked pnpm @('build')
    } finally { Pop-Location }
    if (-not $SkipDocker) {
        $engine = Invoke-Checked docker @('info','--format','{{.OSType}}')
        if ($engine -ne 'linux') { throw '本项目验证要求 Docker Desktop Linux containers。' }
        Invoke-Checked docker @('build','-f','deploy/Dockerfile.tests','-t','palworldpanel-tests:local','.')
        Invoke-Checked docker @('run','--rm','--label','com.palworldpanel.test=unit-tests','palworldpanel-tests:local')
        Invoke-Checked docker @('build','-f','deploy/Dockerfile','-t','palworldpanel:local','.')
    }
} finally { Pop-Location }
