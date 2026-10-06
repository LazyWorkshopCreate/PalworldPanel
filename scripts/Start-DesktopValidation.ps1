#requires -Version 7.0
[CmdletBinding()]
param([string]$Image = 'palworldpanel:local', [switch]$EnableFaultFixtures, [switch]$IncludeComparisonGame, [switch]$StartBrowser, [switch]$Http)
$ErrorActionPreference = 'Stop'
function Invoke-TestDocker { & docker @args; if ($LASTEXITCODE) { throw 'Docker 测试资源操作失败，检查已建立的隔离资源。' } }
# Isolated synthetic material remains outside the repository.
$testDirectory = Join-Path ([IO.Path]::GetTempPath()) 'palworldpanel-desktop-validation/panel'
if (Test-Path -LiteralPath $testDirectory) { throw '测试初始化目录已存在，请先检查现有测试资源。' }
New-Item -ItemType Directory -Path $testDirectory | Out-Null
$testPassword = [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(24))
$certificatePassword = [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(24))
$rsa = [Security.Cryptography.RSA]::Create(3072)
$certificateRequest = [Security.Cryptography.X509Certificates.CertificateRequest]::new('CN=PalworldPanel synthetic validation', $rsa,
    [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.RSASignaturePadding]::Pkcs1)
$san = [Security.Cryptography.X509Certificates.SubjectAlternativeNameBuilder]::new()
$san.AddIpAddress([Net.IPAddress]::Parse('172.30.88.1'))
$san.AddIpAddress([Net.IPAddress]::Loopback)
$certificateRequest.CertificateExtensions.Add($san.Build())
$certificate = $certificateRequest.CreateSelfSigned([DateTimeOffset]::UtcNow.AddMinutes(-5), [DateTimeOffset]::UtcNow.AddDays(7))
[IO.File]::WriteAllBytes((Join-Path $testDirectory 'tls.pfx'), $certificate.Export([Security.Cryptography.X509Certificates.X509ContentType]::Pfx, $certificatePassword))
[IO.File]::WriteAllText((Join-Path $testDirectory 'tls-password'), $certificatePassword)
[IO.File]::WriteAllText((Join-Path $testDirectory 'test-password'), $testPassword)
[IO.File]::WriteAllText((Join-Path $testDirectory 'initialize.env'), "PANEL_INITIAL_ADMIN_PASSWORD=$testPassword`n")
$options = @{
    bindIp='172.30.88.1'; port=18080; allowedIps=@('172.30.88.10','172.30.88.5')
    stateRoot='/test/state'; instanceRoots=@('/test/instances'); backupRoot='/test/backups'
    keyFile='/test/private/key'; administratorFile='/test/private/administrator.json'
    certificateFile='/test/private/tls.pfx'; certificatePasswordFile='/test/private/tls-password'
    defaultImage='ghcr.io/thijsvanloef/palworld-server-docker@sha256:a308e2eaa494f2df0448a59cb22f820ba679bd57729880528c48981a49c08870'
    allowedImages=@('ghcr.io/thijsvanloef/palworld-server-docker@sha256:a308e2eaa494f2df0448a59cb22f820ba679bd57729880528c48981a49c08870')
    minimumMemoryMiB=512; reservedMemoryMiB=512; containerMountRoot='/test'; desktopValidation=$true; desktopAllowHttp=[bool]$Http
}
Invoke-TestDocker network create --label com.palworldpanel.test=panel --subnet 172.30.88.0/24 palworldpanel-validation | Out-Null
if ($LASTEXITCODE) { throw '隔离网络初始化失败。' }
Invoke-TestDocker volume create --label com.palworldpanel.test=panel palworldpanel-panel-validation | Out-Null
$options.dockerHostRoot = Invoke-TestDocker volume inspect --format '{{.Mountpoint}}' palworldpanel-panel-validation
[IO.File]::WriteAllText((Join-Path $testDirectory 'panel.json'), ($options | ConvertTo-Json -Depth 8))
$clientArguments = @('run','-d','--name','palworldpanel-validation-files','--label','com.palworldpanel.test=panel','--network','palworldpanel-validation','--ip','172.30.88.10','-v','palworldpanel-panel-validation:/test')
if ($EnableFaultFixtures) { $clientArguments += @('-v','/var/run/docker.sock:/var/run/docker.sock') }
$clientArguments += @('python:3-alpine','sleep','86400')
Invoke-TestDocker @clientArguments | Out-Null
Invoke-TestDocker exec palworldpanel-validation-files mkdir -p /test/private /test/state /test/instances /test/backups | Out-Null
foreach ($name in @('tls.pfx','tls-password','test-password','panel.json')) {
    Invoke-TestDocker cp (Join-Path $testDirectory $name) "palworldpanel-validation-files:/test/private/$name"
    if ($LASTEXITCODE) { throw '测试材料复制失败。' }
}
Invoke-TestDocker exec palworldpanel-validation-files chmod 700 /test/private /test/state /test/instances /test/backups
Invoke-TestDocker exec palworldpanel-validation-files sh -c 'chmod 600 /test/private/*'
Invoke-TestDocker run --rm --label com.palworldpanel.test=panel -v palworldpanel-panel-validation:/test $Image --initialize-key /test/private/key
if ($LASTEXITCODE) { throw '测试密钥初始化失败。' }
Invoke-TestDocker run --rm --label com.palworldpanel.test=panel --env-file (Join-Path $testDirectory 'initialize.env') -v palworldpanel-panel-validation:/test $Image --initialize-administrator /test/private/administrator.json
if ($LASTEXITCODE) { throw '测试管理员初始化失败。' }
Invoke-TestDocker run -d --name palworldpanel-validation-panel --label com.palworldpanel.test=panel --network host -v palworldpanel-panel-validation:/test -v /var/run/docker.sock:/var/run/docker.sock -e PANEL_CONFIG_FILE=/test/private/panel.json $Image | Out-Null
if ($LASTEXITCODE) { throw '测试面板启动失败。' }
if ($EnableFaultFixtures) {
    $cli = Join-Path $testDirectory 'docker-test-cli'
    Invoke-TestDocker cp palworldpanel-validation-panel:/usr/local/bin/docker $cli
    Invoke-TestDocker cp $cli palworldpanel-validation-files:/usr/local/bin/docker
    Invoke-TestDocker exec palworldpanel-validation-files chmod 755 /usr/local/bin/docker
}
$repository = Split-Path -Parent $PSScriptRoot
foreach ($script in Get-ChildItem -LiteralPath (Join-Path $repository 'tests') -Filter 'desktop_*.py') {
    Invoke-TestDocker cp $script.FullName "palworldpanel-validation-files:/tmp/$($script.Name)"
}
Invoke-TestDocker exec palworldpanel-validation-files python /tmp/desktop_wait.py
if ($IncludeComparisonGame) {
    $gameAdministrator = [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(24))
    $gamePassword = [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(24))
    $environmentFile = Join-Path $testDirectory 'comparison-game.env'
    [IO.File]::WriteAllText($environmentFile, "ADMIN_PASSWORD=$gameAdministrator`nSERVER_PASSWORD=$gamePassword`nSERVER_NAME=panel comparison validation`nPLAYERS=4`nREST_API_ENABLED=true`nRCON_ENABLED=false`nUPDATE_ON_BOOT=false`nAUTO_UPDATE_ENABLED=false`nBACKUP_ENABLED=false`nAUTO_REBOOT_ENABLED=false`nPUID=1000`nPGID=1000`nTZ=Asia/Shanghai`n")
    Invoke-TestDocker volume create --label com.palworldpanel.test=real-game palworldpanel-real-game-validation | Out-Null
    Invoke-TestDocker run -d --name palworldpanel-real-game-validation --label com.palworldpanel.test=real-game --cpus 4 --memory 4096m --restart no --env-file $environmentFile -p 127.0.0.1:18512:8212/tcp -p 127.0.0.1:18511:8211/udp -v palworldpanel-real-game-validation:/palworld $options.defaultImage | Out-Null
}
if ($StartBrowser) {
    $bridge = Join-Path $repository 'tests/desktop_browser_bridge.py'
    Invoke-TestDocker run -d --name palworldpanel-validation-browser --label com.palworldpanel.test=panel --network palworldpanel-validation --ip 172.30.88.5 -p 127.0.0.1:18080:18080 -v "${bridge}:/bridge.py:ro" python:3-alpine python /bridge.py | Out-Null
}
Write-Output '隔离面板已启动；测试凭据仅保存在系统临时目录和受限测试卷。测试预算不代表生产容量。'
