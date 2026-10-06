#requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$BindIp,
    [Parameter(Mandatory)][string[]]$AllowedIps,
    [string]$CertificateFile,
    [Security.SecureString]$CertificatePassword,
    [switch]$UseHttps,
    [Parameter(Mandatory)][string]$DockerDesktopAccount,
    [int]$Port = 18080,
    [string]$InstallDirectory = (Join-Path $(if ($env:ProgramW6432) { $env:ProgramW6432 } else { $env:ProgramFiles }) 'PalworldPanel'),
    [string]$DataRoot = (Join-Path $env:ProgramData 'PalworldPanel'),
    [string]$DockerExecutable = (Join-Path $(if ($env:ProgramW6432) { $env:ProgramW6432 } else { $env:ProgramFiles }) 'Docker/Docker/resources/bin/docker.exe'),
    [string]$ComposePluginDirectory = (Join-Path $(if ($env:ProgramW6432) { $env:ProgramW6432 } else { $env:ProgramFiles }) 'Docker/Docker/resources/cli-plugins')
)
$ErrorActionPreference = 'Stop'
if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw '初始化与防火墙配置需要管理员权限。' }
$DataRoot = [IO.Path]::GetFullPath($DataRoot)
$private = Join-Path $DataRoot 'private'
$config = Join-Path $private 'panel.json'
if (Test-Path -LiteralPath $config) { throw '配置已存在，拒绝覆盖。升级请保留现有材料。' }
& (Join-Path $PSScriptRoot 'Check-Prerequisites.ps1') -BindIp $BindIp -Port $Port -AllowedIps $AllowedIps -DockerExecutable $DockerExecutable -ComposePluginDirectory $ComposePluginDirectory -DockerDesktopAccount $DockerDesktopAccount
if ($UseHttps -and (-not $CertificateFile -or -not (Test-Path -LiteralPath $CertificateFile) -or $null -eq $CertificatePassword)) { throw '启用 HTTPS 时必须提供 PFX 证书和密码。' }
& (Join-Path $PSScriptRoot 'Service.ps1') -Mode Register -InstallDirectory $InstallDirectory -DataRoot $DataRoot
$binary = Join-Path $InstallDirectory 'PalworldPanel.Server.exe'
$image = 'ghcr.io/thijsvanloef/palworld-server-docker@sha256:a308e2eaa494f2df0448a59cb22f820ba679bd57729880528c48981a49c08870'
function Reveal([Security.SecureString]$Secret) {
    $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($Secret)
    try { [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer) } finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer) }
}
# Never pass credentials as command-line arguments or print them.
$created = @()
try {
    foreach ($file in @('key','administrator.json','tls.pfx','tls-password')) {
        if (Test-Path -LiteralPath (Join-Path $private $file)) { throw '部分初始化材料已存在，请核实后人工恢复，拒绝覆盖。' }
    }
    if ($UseHttps) {
        Copy-Item -LiteralPath $CertificateFile -Destination (Join-Path $private 'tls.pfx'); $created += 'tls.pfx'
        [IO.File]::WriteAllText((Join-Path $private 'tls-password'),(Reveal $CertificatePassword)); $created += 'tls-password'
    }
    & $binary --initialize-key (Join-Path $private 'key'); if ($LASTEXITCODE) { throw '密钥初始化失败。' }; $created += 'key'
    & $binary --prepare-administrator (Join-Path $private 'administrator.json'); if ($LASTEXITCODE) { throw '首次访问设置入口准备失败。' }
    $created += 'administrator.json'
    # Docker Desktop file sharing runs under its interactive user's identity.
    $desktopSid = ([Security.Principal.NTAccount]::new($DockerDesktopAccount)).Translate([Security.Principal.SecurityIdentifier])
    $instanceDirectory = Join-Path $DataRoot 'instances'
    $instanceAcl = Get-Acl -LiteralPath $instanceDirectory
    $instanceAcl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($desktopSid,'Modify','ContainerInherit,ObjectInherit','None','Allow'))
    Set-Acl -LiteralPath $instanceDirectory -AclObject $instanceAcl
    [IO.File]::WriteAllText((Join-Path $DataRoot 'docker/config.json'),(@{ cliPluginsExtraDirs = @($ComposePluginDirectory) } | ConvertTo-Json),[Text.UTF8Encoding]::new($false))
    $options = @{
        bindIp=$BindIp; port=$Port; allowedIps=$AllowedIps; allowHttp=(-not $UseHttps); allowWebSetup=$true
        stateRoot=(Join-Path $DataRoot 'state'); instanceRoots=@((Join-Path $DataRoot 'instances')); backupRoot=(Join-Path $DataRoot 'backups')
        keyFile=(Join-Path $private 'key'); administratorFile=(Join-Path $private 'administrator.json')
        certificateFile=$(if ($UseHttps) { Join-Path $private 'tls.pfx' } else { '' }); certificatePasswordFile=$(if ($UseHttps) { Join-Path $private 'tls-password' } else { '' })
        defaultImage=$image; allowedImages=@($image)
        dockerExecutable=$DockerExecutable; dockerEndpoint='npipe:////./pipe/dockerDesktopLinuxEngine'; dockerConfigDirectory=(Join-Path $DataRoot 'docker')
    }
    [IO.File]::WriteAllText($config,($options | ConvertTo-Json -Depth 6),[Text.UTF8Encoding]::new($false))
    & $binary --validate-config $config; if ($LASTEXITCODE) { throw '绑定地址、白名单、预算或安全材料配置无效。' }
    New-NetFirewallRule -Name 'PalworldPanel-Management' -DisplayName 'PalworldPanel management' -Direction Inbound -Action Allow -Protocol TCP -LocalAddress $BindIp -LocalPort $Port -RemoteAddress $AllowedIps -Profile Private,Domain | Out-Null
    Write-Output '初始化完成，服务尚未启动。请按手册以服务账户验证 Docker Linux 引擎及绑定目录，然后启用服务。'
} catch {
    # Only remove material created by this failed, not-yet-started initialization.
    foreach ($name in $created) { Remove-Item -LiteralPath (Join-Path $private $name) -ErrorAction SilentlyContinue }
    Remove-Item -LiteralPath $config -ErrorAction SilentlyContinue
    throw
}
