#requires -Version 7.0
[CmdletBinding()]
param([string]$PublishDirectory = (Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts/windows/publish'))
$ErrorActionPreference = 'Stop'
$binary = Join-Path $PublishDirectory 'PalworldPanel.Server.exe'
if (-not (Test-Path -LiteralPath $binary)) { throw '请先运行 Windows publish。' }
$dockerExecutable = (Get-Command docker.exe).Source
$bind = Get-NetIPAddress -AddressFamily IPv4 | Where-Object { $_.IPAddress -match '^192\.168\.' -and $_.AddressState -eq 'Preferred' } | Select-Object -First 1 -ExpandProperty IPAddress
if (-not $bind) { throw '隔离测试需要本机 RFC1918 LAN IPv4。' }
$root = Join-Path ([IO.Path]::GetTempPath()) ('palworldpanel-windows-test-' + [Guid]::NewGuid().ToString('N'))
$container = 'pp-windows-bind-' + [Guid]::NewGuid().ToString('N')
$port = 18090
$process = $null
$containerCreated = $false
New-Item -ItemType Directory -Path $root | Out-Null
# Test secrets readable only by this test user, SYSTEM and administrators.
$acl = [Security.AccessControl.DirectorySecurity]::new()
$acl.SetAccessRuleProtection($true,$false)
foreach ($identity in @([Security.Principal.WindowsIdentity]::GetCurrent().User.Value,'S-1-5-18','S-1-5-32-544')) {
    $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new([Security.Principal.SecurityIdentifier]::new($identity),'FullControl','ContainerInherit,ObjectInherit','None','Allow'))
}
Set-Acl -LiteralPath $root -AclObject $acl
try {
    foreach ($name in @('private','state','instances','backups','docker','instances/fixture/data')) { New-Item -ItemType Directory -Path (Join-Path $root $name) -Force | Out-Null }
    $password = [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(24))
    & $binary --initialize-key (Join-Path $root 'private/key'); if ($LASTEXITCODE) { throw '测试密钥初始化失败' }
    & $binary --prepare-administrator (Join-Path $root 'private/admin.json'); if ($LASTEXITCODE) { throw '首次设置材料准备失败' }
    $options = @{
        bindIp=$bind; port=$port; allowedIps=@($bind); desktopValidation=$true; allowHttp=$true; allowWebSetup=$true
        stateRoot=(Join-Path $root 'state'); instanceRoots=@((Join-Path $root 'instances')); backupRoot=(Join-Path $root 'backups')
        keyFile=(Join-Path $root 'private/key'); administratorFile=(Join-Path $root 'private/admin.json')
        certificateFile=''; certificatePasswordFile=''
        defaultImage='ghcr.io/thijsvanloef/palworld-server-docker@sha256:a308e2eaa494f2df0448a59cb22f820ba679bd57729880528c48981a49c08870'; allowedImages=@('ghcr.io/thijsvanloef/palworld-server-docker@sha256:a308e2eaa494f2df0448a59cb22f820ba679bd57729880528c48981a49c08870'); minimumMemoryMiB=512; reservedMemoryMiB=512
        dockerExecutable=$dockerExecutable; dockerEndpoint='npipe:////./pipe/dockerDesktopLinuxEngine'; dockerConfigDirectory=(Join-Path $root 'docker')
    }
    $plugin = Join-Path $env:ProgramFiles 'Docker/Docker/resources/cli-plugins'
    [IO.File]::WriteAllText((Join-Path $root 'docker/config.json'),(@{cliPluginsExtraDirs=@($plugin)} | ConvertTo-Json))
    [IO.File]::WriteAllText((Join-Path $root 'private/panel.json'),($options | ConvertTo-Json -Depth 6))
    & $binary --validate-config (Join-Path $root 'private/panel.json'); if ($LASTEXITCODE) { throw '测试配置无效' }
    & $dockerExecutable --host npipe:////./pipe/dockerDesktopLinuxEngine info --format '{{.OSType}}' | Out-String | ForEach-Object { if ($_.Trim() -ne 'linux') { throw '测试必须使用 Linux Docker 引擎' } }
    if ($LASTEXITCODE) { throw 'Docker 引擎不可达' }
    & $dockerExecutable run -d --name $container --label com.palworldpanel.test=windows-native --user 1000:1000 --cpus 1 --memory 512m --mount "type=bind,source=$(Join-Path $root 'instances/fixture/data'),target=/palworld" python:3-alpine python -c 'import pathlib,time; pathlib.Path("/palworld/bind-proof").write_text("synthetic"); time.sleep(600)' | Out-Null
    if ($LASTEXITCODE) { throw 'Windows bind mount 测试容器启动失败' }; $containerCreated=$true
    $start = [Diagnostics.ProcessStartInfo]::new($binary)
    $start.UseShellExecute=$false; $start.CreateNoWindow=$true; $start.WorkingDirectory=$env:SystemRoot
    $start.RedirectStandardOutput=$true; $start.RedirectStandardError=$true
    $start.ArgumentList.Add('--config'); $start.ArgumentList.Add((Join-Path $root 'private/panel.json'))
    $process=[Diagnostics.Process]::Start($start)
    $stdout=$process.StandardOutput.ReadToEndAsync(); $stderr=$process.StandardError.ReadToEndAsync()
    $base="http://${bind}:$port"
    $ready=$false
    for ($attempt=0;$attempt -lt 30;$attempt++) {
        if ($process.HasExited) { throw 'Windows 原生面板启动失败，未回显私有日志。' }
        try { $response=Invoke-WebRequest $base -SkipCertificateCheck -TimeoutSec 2; if ($response.StatusCode -eq 200) { $ready=$true; break } } catch { Start-Sleep -Milliseconds 500 }
    }
    if (-not $ready) { throw 'Windows HTTP 入口未就绪' }
    if ($response.Content -notmatch '/assets/index-') { throw '原生发布静态资源缺失' }
    $localBase = "http://127.0.0.1:$port"
    $localPage = Invoke-WebRequest $localBase -TimeoutSec 3
    if ($localPage.StatusCode -ne 200 -or $localPage.Content -notmatch '/assets/index-') { throw '回环入口未监听或静态资源缺失' }
    if ($localPage.Headers['Content-Security-Policy'] -notmatch "style-src-elem 'self' 'unsafe-inline'") { throw '本机标注样式兼容策略缺失' }
    if ($response.Headers['Content-Security-Policy'] -match 'unsafe-inline') { throw '内网入口不应放宽样式策略' }
    if ($localPage.Headers['Content-Security-Policy'] -notmatch "script-src 'self';") { throw '标注兼容不应放宽脚本策略' }
    $localSetup = Invoke-RestMethod "$localBase/api/v1/setup"
    if (-not $localSetup.required) { throw '回环来源被错误拒绝' }
    $denied = Invoke-WebRequest "$localBase/api/v1/host" -SkipHttpErrorCheck
    if ($denied.StatusCode -ne 401) { throw '回环访问不应绕过登录' }
    $session=[Microsoft.PowerShell.Commands.WebRequestSession]::new()
    $headers=@{Origin=$base}
    $setup=Invoke-RestMethod "$base/api/v1/setup"
    if (-not $setup.required -or -not $setup.token) { throw '首次设置入口未开启' }
    $payload=@{token=$setup.token;password=$password;confirmation=$password} | ConvertTo-Json
    $denied=Invoke-WebRequest "$base/api/v1/setup" -Method Post -ContentType application/json -Body $payload -Headers @{Origin='http://invalid.example'} -SkipHttpErrorCheck
    if ($denied.StatusCode -ne 403) { throw '跨源首次设置未拒绝' }
    $denied=Invoke-WebRequest "$base/api/v1/setup" -Method Post -ContentType application/json -Body (@{token='wrong';password=$password;confirmation=$password} | ConvertTo-Json) -Headers $headers -SkipHttpErrorCheck
    if ($denied.StatusCode -ne 403) { throw '无效设置令牌未拒绝' }
    $result=Invoke-WebRequest "$base/api/v1/setup" -Method Post -ContentType application/json -Body $payload -Headers $headers
    if ($result.StatusCode -ne 204) { throw '首次设置失败' }
    $denied=Invoke-WebRequest "$base/api/v1/setup" -Method Post -ContentType application/json -Body $payload -Headers $headers -SkipHttpErrorCheck
    if ($denied.StatusCode -ne 409) { throw '重复初始化未拒绝' }
    $setup=Invoke-RestMethod "$base/api/v1/setup"
    if ($setup.required -or $null -ne $setup.token) { throw '设置入口未关闭' }
    $null=Invoke-WebRequest "$base/api/v1/session" -Method Post -ContentType application/json -Body (@{userName='admin';password=$password} | ConvertTo-Json) -Headers $headers -WebSession $session -SkipCertificateCheck
    $hostState=Invoke-RestMethod "$base/api/v1/host" -WebSession $session -SkipCertificateCheck
    $localSession = [Microsoft.PowerShell.Commands.WebRequestSession]::new()
    $loginBody = @{userName='admin';password=$password} | ConvertTo-Json
    $denied = Invoke-WebRequest "$localBase/api/v1/session" -Method Post -ContentType application/json -Body $loginBody -Headers @{Origin=$base} -SkipHttpErrorCheck
    if ($denied.StatusCode -ne 403) { throw '回环入口未拒绝跨源写入' }
    $null = Invoke-WebRequest "$localBase/api/v1/session" -Method Post -ContentType application/json -Body $loginBody -Headers @{Origin=$localBase} -WebSession $localSession
    $localHost = Invoke-RestMethod "$localBase/api/v1/host" -WebSession $localSession
    if (-not ($localHost.systemMemoryBytes -gt 0)) { throw '回环登录后无法读取主机指标' }
    if (-not ($hostState.systemMemoryBytes -gt 0 -and $hostState.usedMemoryBytes -gt 0 -and $hostState.memoryMiB -gt 0)) { throw 'Windows/Docker 分离采样失败' }
    $discovery=Invoke-RestMethod "$base/api/v1/discovery" -WebSession $session -SkipCertificateCheck
    $candidate=$discovery | Where-Object name -eq $container
    if ($candidate.root -ne (Join-Path $root 'instances/fixture')) { throw 'Docker Desktop 挂载路径未正确映射到本机目录' }
    if (-not (Test-Path -LiteralPath (Join-Path $root 'instances/fixture/data/bind-proof'))) { throw 'UID 1000 无法写入 Windows 游戏绑定目录' }
    Start-Sleep -Seconds 6
    $hostState=Invoke-RestMethod "$base/api/v1/host" -WebSession $session -SkipCertificateCheck
    if ($null -eq $hostState.cpuPercent) { throw 'Windows CPU 增量采样失败' }
    $cookies=$session.Cookies.GetCookies([Uri]$base)
    if (-not @($cookies | Where-Object { $_.Name -eq 'PalworldPanel' -and $_.HttpOnly -and -not $_.Secure }).Count) { throw 'HTTP 会话 cookie 策略错误' }
    Write-Output 'PASS Windows LAN and 127.0.0.1 listeners, loopback login and same-origin/auth gates, HTTP/one-time website setup, Linux pipe, host/VM metrics and UID1000 bind write; no game world created'
} finally {
    if ($process) { if (-not $process.HasExited) { $process.Kill($true); $process.WaitForExit() }; $process.Dispose() }
    if ($containerCreated) { & $dockerExecutable rm -f $container | Out-Null }
    # Verify exact temporary target before project-scoped recursive cleanup.
    $temporaryRoot=[IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    if (-not $root.StartsWith($temporaryRoot,[StringComparison]::OrdinalIgnoreCase) -or (Split-Path $root -Leaf) -notlike 'palworldpanel-windows-test-*') { throw '测试目录清理边界不匹配' }
    Remove-Item -LiteralPath $root -Recurse -Force
}
