#requires -Version 7.0
$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
$root = Join-Path ([IO.Path]::GetTempPath()) ('pp-defaults-test-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path (Join-Path $root 'private') -Force | Out-Null
$output = Join-Path $root 'defaults.txt'
$shell = Join-Path $env:WINDIR 'System32/WindowsPowerShell/v1.0/powershell.exe'
function Read-Defaults {
    & $shell -NoProfile -NonInteractive -ExecutionPolicy Bypass -File (Join-Path $repository 'deploy/windows/Get-InstallationDefaults.ps1') -OutputFile $output -DataRoot $root
    if ($LASTEXITCODE) { throw '默认值生成失败。' }
    return @(Get-Content -LiteralPath $output -Encoding UTF8)
}
try {
    $values = Read-Defaults
    $local = @(Get-NetIPAddress -AddressFamily IPv4 | Select-Object -ExpandProperty IPAddress)
    if ($values.Count -ne 3 -or $values[0] -notin $local -or $values[1] -ne '18080' -or $values[2] -ne $values[0]) { throw '首次安装默认值错误。' }
    $bind = $values[0]
    $config = @{bindIp=$bind;port=18181;allowedIps=@('10.23.45.67');secret='SYNTHETIC-NOT-A-REAL-SECRET'}
    [IO.File]::WriteAllText((Join-Path $root 'private/panel.json'),($config | ConvertTo-Json),[Text.UTF8Encoding]::new($false))
    $values = Read-Defaults
    if ($values[0] -ne $bind -or $values[1] -ne '18181' -or $values[2] -ne '10.23.45.67') { throw '现有网络配置未保留。' }
    if ((Get-Content $output -Raw) -match 'SYNTHETIC|secret') { throw '默认值输出包含非网络字段。' }
    $config.bindIp = '127.0.0.1'
    [IO.File]::WriteAllText((Join-Path $root 'private/panel.json'),($config | ConvertTo-Json),[Text.UTF8Encoding]::new($false))
    $values = Read-Defaults
    if ($values[0] -notin $local -or $values[0] -eq '127.0.0.1') { throw '失效监听地址未回退到本机内网。' }
    Write-Output 'PASS native Windows defaults, exact local whitelist, existing network values preserved, stale address fallback, no secret fields exported'
} finally {
    $resolved = [IO.Path]::GetFullPath($root)
    if (-not $resolved.StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()),[StringComparison]::OrdinalIgnoreCase) -or (Split-Path -Leaf $resolved) -notlike 'pp-defaults-test-*') { throw '拒绝清理越界目录。' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
