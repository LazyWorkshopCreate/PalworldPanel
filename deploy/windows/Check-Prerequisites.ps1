#requires -Version 5.1
[CmdletBinding()]
param(
    [string]$BindIp,
    [ValidateRange(1024,65535)][int]$Port = 18080,
    [string[]]$AllowedIps,
    [string]$DockerDesktopAccount,
    [switch]$SkipPortCheck,
    [string]$InstallerErrorFile,
    [string]$DockerExecutable = (Join-Path $(if ($env:ProgramW6432) { $env:ProgramW6432 } else { $env:ProgramFiles }) 'Docker/Docker/resources/bin/docker.exe'),
    [string]$ComposePluginDirectory = (Join-Path $(if ($env:ProgramW6432) { $env:ProgramW6432 } else { $env:ProgramFiles }) 'Docker/Docker/resources/cli-plugins')
)
$ErrorActionPreference = 'Stop'
trap {
    if ($InstallerErrorFile) { [IO.File]::WriteAllText($InstallerErrorFile,$_.Exception.Message,[Text.UTF8Encoding]::new($false)) }
    throw $_
}
if (-not [Environment]::Is64BitOperatingSystem) { throw '需要 64 位 Windows。' }
if (-not [IO.Path]::IsPathRooted($DockerExecutable) -or -not (Test-Path -LiteralPath $DockerExecutable -PathType Leaf)) { throw '未找到 Docker Desktop CLI，请安装 Docker Desktop。' }
if (-not (Test-Path -LiteralPath (Join-Path $ComposePluginDirectory 'docker-compose.exe') -PathType Leaf)) { throw '未找到 Docker Compose 插件。' }
function Is-PrivateIPv4([string]$Value) {
    $parsed = $null
    if (-not [Net.IPAddress]::TryParse($Value,[ref]$parsed) -or $parsed.AddressFamily -ne [Net.Sockets.AddressFamily]::InterNetwork) { return $false }
    $b=$parsed.GetAddressBytes()
    return ($b[0] -eq 10 -or ($b[0] -eq 172 -and $b[1] -ge 16 -and $b[1] -le 31) -or ($b[0] -eq 192 -and $b[1] -eq 168))
}
if ($BindIp) {
    if (-not (Is-PrivateIPv4 $BindIp) -or -not (Get-NetIPAddress -AddressFamily IPv4 | Where-Object IPAddress -eq $BindIp)) { throw '监听地址必须是本机网卡上的内网 IPv4，请填写 IP 地址而非计算机名、远端地址或 127.0.0.1。' }
    if (-not $AllowedIps -or @($AllowedIps | Where-Object { -not (Is-PrivateIPv4 $_) }).Count) { throw '白名单必须逐项填写内网 IPv4，不能使用网段或通配符。' }
    if (-not $SkipPortCheck) {
        foreach ($address in @($BindIp,'127.0.0.1')) {
            $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Parse($address),$Port)
            $listener.Server.ExclusiveAddressUse=$true
            try { $listener.Start() } catch { throw "管理端口 $Port 在 $address 不可用，请更换端口或处理占用程序。" } finally { $listener.Stop() }
        }
    }
}
if ($DockerDesktopAccount) {
    try { $null=([Security.Principal.NTAccount]::new($DockerDesktopAccount)).Translate([Security.Principal.SecurityIdentifier]) } catch { throw 'Docker Desktop Windows 账户不存在或无法解析。' }
}
$temporary = Join-Path ([IO.Path]::GetTempPath()) ('pp-dependency-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temporary | Out-Null
function Docker-Check([string]$Arguments) {
    $start=[Diagnostics.ProcessStartInfo]::new()
    $start.FileName=$DockerExecutable
    $start.WorkingDirectory=$temporary
    $start.Arguments='--config "' + $temporary + '" --host npipe:////./pipe/dockerDesktopLinuxEngine ' + $Arguments
    $start.UseShellExecute=$false; $start.CreateNoWindow=$true
    $start.RedirectStandardOutput=$true; $start.RedirectStandardError=$true
    foreach ($name in @('DOCKER_CONTEXT','DOCKER_HOST','DOCKER_TLS_VERIFY','DOCKER_CERT_PATH','DOCKER_CONFIG')) { $start.EnvironmentVariables.Remove($name) }
    $process=[Diagnostics.Process]::Start($start)
    try {
        $output=$process.StandardOutput.ReadToEndAsync(); $errorOutput=$process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit(15000)) { $process.Kill(); $process.WaitForExit(); throw 'Docker 检查超时，请确认 Docker Desktop 已启动。' }
        if ($process.ExitCode -ne 0) { throw 'Docker 依赖检查失败，请确认 Docker Desktop Linux 引擎正常运行。' }
        return $output.GetAwaiter().GetResult().Trim()
    } finally { $process.Dispose() }
}
try {
    [IO.File]::WriteAllText((Join-Path $temporary 'config.json'),(@{cliPluginsExtraDirs=@($ComposePluginDirectory)} | ConvertTo-Json),[Text.UTF8Encoding]::new($false))
    if ((Docker-Check 'info --format "{{.OSType}}"') -ne 'linux') { throw '请切换 Docker Desktop 到 Linux containers。' }
    $version=Docker-Check 'compose version --short'
    if ($version -notmatch '^v?[2-9]\.') { throw '需要 Docker Compose v2 或更新版本。' }
    if ($SkipPortCheck) { Write-Output '输入和依赖检查通过；管理端口将在停服后单独检查。' }
    else { Write-Output '依赖检查通过：Docker Linux 引擎、Compose；已提供的地址、白名单、账户和管理端口检查通过。' }
} finally {
    Remove-Item -LiteralPath (Join-Path $temporary 'config.json') -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $temporary -ErrorAction SilentlyContinue
}
