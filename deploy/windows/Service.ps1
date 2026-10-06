#requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('Register','PrepareUpgrade','Unregister')][string]$Mode,
    [Parameter(Mandatory)][string]$InstallDirectory,
    [string]$DataRoot = (Join-Path $env:ProgramData 'PalworldPanel'),
    [string]$InstallerErrorFile
)
$ErrorActionPreference = 'Stop'
trap {
    if ($InstallerErrorFile) { [IO.File]::WriteAllText($InstallerErrorFile,$_.Exception.Message,[Text.UTF8Encoding]::new($false)) }
    throw $_
}
if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Windows 服务管理需要管理员权限。'
}
$InstallDirectory = [IO.Path]::GetFullPath($InstallDirectory)
$DataRoot = [IO.Path]::GetFullPath($DataRoot)
$binary = Join-Path $InstallDirectory 'PalworldPanel.Server.exe'
$command = '"' + $binary + '" --config "' + (Join-Path $DataRoot 'private/panel.json') + '"'
$service = Get-CimInstance Win32_Service -Filter "Name='PalworldPanel'"
if ($service -and $service.PathName -ne $command) { throw '同名服务路径不属于此安装，拒绝接管。' }
function Protect-Directory([string]$Path) {
    New-Item -ItemType Directory -Force -Path $Path | Out-Null
    $acl = New-Object Security.AccessControl.DirectorySecurity
    $acl.SetAccessRuleProtection($true,$false)
    foreach ($sid in @('S-1-5-18','S-1-5-32-544')) {
        $identity = New-Object Security.Principal.SecurityIdentifier($sid)
        $rule = New-Object Security.AccessControl.FileSystemAccessRule($identity,'FullControl','ContainerInherit,ObjectInherit','None','Allow')
        $acl.AddAccessRule($rule)
    }
    Set-Acl -LiteralPath $Path -AclObject $acl
}
if ($Mode -eq 'Register') {
    if (-not (Test-Path -LiteralPath $binary)) { throw '发布程序缺失。' }
    if (-not (Test-Path -LiteralPath (Join-Path $DataRoot 'private/panel.json'))) {
        Protect-Directory $DataRoot
        foreach ($name in @('private','state','instances','backups','docker','upgrade-backups')) { Protect-Directory (Join-Path $DataRoot $name) }
    }
    if (-not $service) { New-Service -Name PalworldPanel -DisplayName 'PalworldPanel' -BinaryPathName $command -StartupType Manual -Description '帕鲁实例管理面板；游戏通过 Docker Desktop Linux 引擎运行。' | Out-Null }
    Write-Output '服务已注册，保持停止；完成配置和 Docker 权限核验后再启用。'
    exit 0
}
$database = Join-Path $DataRoot 'state/panel.db'
if (($Mode -eq 'PrepareUpgrade' -or $service) -and (Test-Path -LiteralPath $database)) {
    & $binary --check-idle $database; if ($LASTEXITCODE) { throw '任务未空闲，拒绝停止服务。' }
}
if ($service) {
    $oldProcess = if ($service.ProcessId) { Get-Process -Id $service.ProcessId -ErrorAction SilentlyContinue } else { $null }
    Stop-Service PalworldPanel
    (Get-Service PalworldPanel).WaitForStatus('Stopped',[TimeSpan]::FromSeconds(60))
    if ($oldProcess -and -not $oldProcess.WaitForExit(30000)) { throw '服务进程尚未退出，拒绝替换程序目录。' }
    if (Test-Path -LiteralPath $database) { & $binary --check-idle $database; if ($LASTEXITCODE) { throw '停止期间出现待处理任务，保持停止并取消升级/卸载。' } }
    if ($Mode -eq 'Unregister') {
        $result = Invoke-CimMethod -InputObject $service -MethodName Delete
        if ($result.ReturnValue -ne 0) { throw '服务删除失败。' }
        Remove-NetFirewallRule -Name 'PalworldPanel-Management' -ErrorAction SilentlyContinue
    }
}
if ($Mode -eq 'PrepareUpgrade' -and (Test-Path -LiteralPath (Join-Path $DataRoot 'private/panel.json'))) {
    $snapshot = Join-Path $DataRoot ('upgrade-backups/' + (Get-Date -Format 'yyyyMMdd-HHmmss-ffff'))
    Protect-Directory $snapshot
    foreach ($name in @('private','state','docker')) { Copy-Item -LiteralPath (Join-Path $DataRoot $name) -Destination (Join-Path $snapshot $name) -Recurse }
    Write-Output '面板状态及配套密钥已备份；游戏目录未改动。'
}
Write-Output '服务操作完成；实例、存档、秘密和备份保留。'
