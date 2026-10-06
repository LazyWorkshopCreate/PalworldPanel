#requires -Version 7.0
$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
$root = Join-Path ([IO.Path]::GetTempPath()) ('pp-settings-validation-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root | Out-Null
$file = Join-Path $root 'settings.json'
$data = Join-Path $root 'data'
$script = Join-Path $repository 'deploy/windows/Configure-Installation.ps1'
$shell = Join-Path $env:WINDIR 'System32/WindowsPowerShell/v1.0/powershell.exe'
$ip = Get-NetIPAddress -AddressFamily IPv4 | Where-Object { $_.IPAddress -like '192.168.*' -and $_.AddressState -eq 'Preferred' } | Select-Object -First 1 -ExpandProperty IPAddress
if (-not $ip) { throw '测试需要本机 192.168.* 内网地址。' }
$settings = @{bindIp=$ip;port='18080';allowedIps=$ip;account=[Security.Principal.WindowsIdentity]::GetCurrent().Name}
function Check([string]$Mode,[bool]$Success,[string]$ErrorPattern='') {
    [IO.File]::WriteAllText($file,($settings | ConvertTo-Json),[Text.UTF8Encoding]::new($false))
    $output = & $shell -NoProfile -NonInteractive -ExecutionPolicy Bypass -File $script -Mode $Mode -SettingsFile $file -InstallDirectory (Join-Path $root 'app') -DataRoot $data 2>&1
    $code = $LASTEXITCODE
    if ($Success -and $code -ne 0) { throw "预期成功但失败：$output" }
    if (-not $Success -and ($code -eq 0 -or ($output -join "`n") -notmatch $ErrorPattern)) { throw "未按预期拒绝 $Mode 输入：$output" }
    if (Test-Path -LiteralPath $data) { throw '输入检查不应创建运行数据。' }
}
$listener = $null
try {
    foreach ($mode in @('ValidateInput','Validate','Apply')) {
        $settings.bindIp = ' '
        Check $mode $false '请填写监听'
        $settings.bindIp = $ip
        $settings.allowedIps = ' , ; '
        Check $mode $false '请填写至少一个'
        $settings.allowedIps = $ip
    }
    $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Parse($ip),0)
    $listener.Server.ExclusiveAddressUse = $true
    $listener.Start()
    $settings.port = [string]$listener.LocalEndpoint.Port
    Check 'ValidateInput' $true
    Check 'Validate' $false '不可用'
    $listener.Stop()
    Check 'Validate' $true
    $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,0)
    $listener.Server.ExclusiveAddressUse = $true
    $listener.Start()
    $settings.port = [string]$listener.LocalEndpoint.Port
    Check 'Validate' $false '127.0.0.1 不可用'
    $listener.Stop()
    Check 'Validate' $true
    Write-Output 'PASS empty IP/allowlist rejected in all modes before side effects; occupied port deferred then rejected; free port and Docker dependencies validated'
} finally {
    if ($listener) { $listener.Stop() }
    $resolved = [IO.Path]::GetFullPath($root)
    $temp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    if (-not $resolved.StartsWith($temp,[StringComparison]::OrdinalIgnoreCase) -or (Split-Path -Leaf $resolved) -notlike 'pp-settings-validation-*') { throw '拒绝清理越界测试目录。' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
