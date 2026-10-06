#requires -Version 7.0
$ErrorActionPreference='Stop'
$check=Join-Path (Split-Path -Parent $PSScriptRoot) 'deploy/windows/Check-Prerequisites.ps1'
# The setup executable can invoke a 32-bit process with ProgramFiles pointing at x86.
& "$env:SystemRoot/SysWOW64/WindowsPowerShell/v1.0/powershell.exe" -NoProfile -NonInteractive -ExecutionPolicy Bypass -File $check
if ($LASTEXITCODE) { throw '32 位 PowerShell 依赖检查失败。' }
& "$env:SystemRoot/System32/WindowsPowerShell/v1.0/powershell.exe" -NoProfile -NonInteractive -ExecutionPolicy Bypass -File $check
if ($LASTEXITCODE) { throw '64 位 PowerShell 依赖检查失败。' }
$bind=Get-NetIPAddress -AddressFamily IPv4 | Where-Object { $_.IPAddress -match '^192\.168\.' -and $_.AddressState -eq 'Preferred' } | Select-Object -First 1 -ExpandProperty IPAddress
if (-not $bind) { throw '测试需要本机内网 IPv4。' }
$listener=[Net.Sockets.TcpListener]::new([Net.IPAddress]::Parse($bind),0)
$listener.Server.ExclusiveAddressUse=$true
$listener.Start()
$port=$listener.LocalEndpoint.Port
try {
    $rejected=$false
    try { & $check -BindIp $bind -Port $port -AllowedIps @($bind) } catch { if ($_.Exception.Message -notmatch '不可用') { throw }; $rejected=$true }
    if (-not $rejected) { throw '占用端口未拒绝。' }
} finally { $listener.Stop() }
& $check -BindIp $bind -Port $port -AllowedIps @($bind) -DockerDesktopAccount ([Security.Principal.WindowsIdentity]::GetCurrent().Name)
$rejected=$false
try { & $check -DockerExecutable 'C:/nonexistent-palworldpanel-test/docker.exe' } catch { if ($_.Exception.Message -notmatch 'CLI') { throw }; $rejected=$true }
if (-not $rejected) { throw '缺失 Docker CLI 未拒绝。' }
$rejected=$false
try { & $check -BindIp $bind -AllowedIps @('0.0.0.0/0') } catch { if ($_.Exception.Message -notmatch '白名单') { throw }; $rejected=$true }
if (-not $rejected) { throw '非法白名单未拒绝。' }
Write-Output 'PASS prerequisites: Linux engine/Compose/account/free port; occupied port, missing CLI and invalid allowlist rejected; no service/firewall changes'
