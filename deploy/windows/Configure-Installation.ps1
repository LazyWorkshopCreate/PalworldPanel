#requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('ValidateInput','Validate','Apply')][string]$Mode,
    [Parameter(Mandatory)][string]$SettingsFile,
    [Parameter(Mandatory)][string]$InstallDirectory,
    [string]$DataRoot = (Join-Path $env:ProgramData 'PalworldPanel'),
    [string]$InstallerErrorFile
)
$ErrorActionPreference = 'Stop'
trap {
    if ($InstallerErrorFile) { [IO.File]::WriteAllText($InstallerErrorFile,$_.Exception.Message,[Text.UTF8Encoding]::new($false)) }
    throw $_
}
$settings = Get-Content -LiteralPath $SettingsFile -Raw -Encoding UTF8 | ConvertFrom-Json
if ([string]::IsNullOrWhiteSpace([string]$settings.bindIp)) { throw '请填写监听 IP（本机内网 IPv4）。' }
$port = 0
if (-not [int]::TryParse([string]$settings.port,[ref]$port) -or $port -lt 1024 -or $port -gt 65535) { throw '端口须为 1024–65535。' }
if ([string]::IsNullOrWhiteSpace([string]$settings.account)) { throw '请填写 Docker Desktop 的 Windows 账户。' }
$allowed = @(([string]$settings.allowedIps -split '[,;\s]+' | Where-Object { $_ }))
if ($allowed.Count -eq 0) { throw '请填写至少一个允许访问的内网 IP。' }
$settings.bindIp = ([string]$settings.bindIp).Trim()
$settings.account = ([string]$settings.account).Trim()
& (Join-Path $PSScriptRoot 'Check-Prerequisites.ps1') -BindIp ([string]$settings.bindIp).Trim() -Port $port -AllowedIps $allowed -DockerDesktopAccount ([string]$settings.account).Trim() -SkipPortCheck:($Mode -eq 'ValidateInput')
if ($Mode -ne 'Apply') { return }
$config = Join-Path $DataRoot 'private/panel.json'
if (-not (Test-Path -LiteralPath $config)) {
    & (Join-Path $InstallDirectory 'deployment/Initialize.ps1') -BindIp $settings.bindIp -Port $port -AllowedIps $allowed -DockerDesktopAccount $settings.account -InstallDirectory $InstallDirectory -DataRoot $DataRoot
    return
}
$oldBytes = [IO.File]::ReadAllBytes($config)
$oldOptions = [Text.Encoding]::UTF8.GetString($oldBytes) | ConvertFrom-Json
$options = Get-Content -LiteralPath $config -Raw -Encoding UTF8 | ConvertFrom-Json
foreach ($entry in @{bindIp=$settings.bindIp;port=$port;allowedIps=$allowed}.GetEnumerator()) {
    $options | Add-Member -NotePropertyName $entry.Key -NotePropertyValue $entry.Value -Force
}
$temporary = Join-Path (Split-Path -Parent $config) ('panel.configure-' + [Guid]::NewGuid().ToString('N') + '.json')
$previousAcls = @{}
function Set-ManagementFirewall($Value) {
    Remove-NetFirewallRule -Name 'PalworldPanel-Management' -ErrorAction SilentlyContinue
    New-NetFirewallRule -Name 'PalworldPanel-Management' -DisplayName 'PalworldPanel management' -Direction Inbound -Action Allow -Protocol TCP -LocalAddress $Value.bindIp -LocalPort $Value.port -RemoteAddress $Value.allowedIps -Profile Private,Domain | Out-Null
}
try {
    [IO.File]::WriteAllText($temporary,($options | ConvertTo-Json -Depth 12),[Text.UTF8Encoding]::new($false))
    & (Join-Path $InstallDirectory 'PalworldPanel.Server.exe') --validate-config $temporary
    if ($LASTEXITCODE) { throw '重新配置校验失败。' }
    $sid = ([Security.Principal.NTAccount]::new([string]$settings.account)).Translate([Security.Principal.SecurityIdentifier])
    foreach ($directory in $options.instanceRoots) {
        $acl = Get-Acl -LiteralPath $directory
        $previousAcls[$directory] = $acl.GetSecurityDescriptorSddlForm('All')
        $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($sid,'Modify','ContainerInherit,ObjectInherit','None','Allow'))
        Set-Acl -LiteralPath $directory -AclObject $acl
    }
    Move-Item -LiteralPath $temporary -Destination $config -Force
    Set-ManagementFirewall $options
} catch {
    [IO.File]::WriteAllBytes($config,$oldBytes)
    foreach ($directory in $previousAcls.Keys) {
        try {
            $acl = Get-Acl -LiteralPath $directory
            $acl.SetSecurityDescriptorSddlForm($previousAcls[$directory])
            Set-Acl -LiteralPath $directory -AclObject $acl
        } catch { Write-Warning '原实例目录权限未恢复，请保持服务停止并人工核实。' }
    }
    try { Set-ManagementFirewall $oldOptions } catch { Write-Warning '原防火墙规则未恢复，请保持服务停止并人工核实。' }
    throw $_
} finally { Remove-Item -LiteralPath $temporary -ErrorAction SilentlyContinue }
Write-Output '网络配置已更新；管理员密码、主密钥、数据库、实例及备份已保留。'
