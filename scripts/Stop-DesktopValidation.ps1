#requires -Version 7.0
[CmdletBinding(SupportsShouldProcess)]
param([switch]$KeepPrivateMaterials)
$ErrorActionPreference = 'Stop'
function Invoke-TestDocker { & docker @args; if ($LASTEXITCODE) { throw 'Docker 测试资源清理失败，请检查指定资源。' } }
$volumeName = 'palworldpanel-panel-validation'
$volumeJson = & docker volume inspect $volumeName 2>$null
$volumeRoot = $null
if ($LASTEXITCODE -eq 0) {
    $volume = ($volumeJson | ConvertFrom-Json)[0]
    if ($volume.Labels.'com.palworldpanel.test' -ne 'panel') { throw '同名卷缺少测试归属标签，拒绝清理。' }
    $volumeRoot = $volume.Mountpoint.TrimEnd('/') + '/instances/'
}
$containerIds = Invoke-TestDocker ps -aq
foreach ($containerId in $containerIds) {
    $container = ((Invoke-TestDocker inspect $containerId) | ConvertFrom-Json)[0]
    $testLabel = $container.Config.Labels.'com.palworldpanel.test'
    $fixtureInstance = $volumeRoot -and ($container.Mounts | Where-Object {
        $_.Type -eq 'bind' -and $_.Source.StartsWith($volumeRoot, [StringComparison]::Ordinal)
    }) -and $container.Config.Labels.'com.docker.compose.project' -match '^pp-[a-f0-9]{32}$'
    if ($testLabel -in @('panel','real-game','unit-tests') -or $fixtureInstance) {
        if ($PSCmdlet.ShouldProcess($container.Name, '停止并移除本项目合成测试容器')) {
            Invoke-TestDocker stop --time 45 $containerId | Out-Null
            Invoke-TestDocker rm $containerId | Out-Null
        }
    }
}
foreach ($name in @('palworldpanel-panel-validation','palworldpanel-real-game-validation')) {
    $json = & docker volume inspect $name 2>$null
    if ($LASTEXITCODE -ne 0) { continue }
    $entry = ($json | ConvertFrom-Json)[0]
    if ($entry.Labels.'com.palworldpanel.test' -notin @('panel','real-game')) { throw '测试卷归属标签无效，拒绝清理。' }
    if ($PSCmdlet.ShouldProcess($name, '移除本项目合成测试卷')) { Invoke-TestDocker volume rm $name | Out-Null }
}
$networkJson = & docker network inspect palworldpanel-validation 2>$null
if ($LASTEXITCODE -eq 0) {
    $network = ($networkJson | ConvertFrom-Json)[0]
    if ($network.Labels.'com.palworldpanel.test' -ne 'panel') { throw '测试网络归属标签无效。' }
    if ($PSCmdlet.ShouldProcess('palworldpanel-validation','移除隔离测试网络')) { Invoke-TestDocker network rm palworldpanel-validation | Out-Null }
}
if (-not $KeepPrivateMaterials) {
    $temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\','/')
    $target = [IO.Path]::GetFullPath((Join-Path $temporaryRoot 'palworldpanel-desktop-validation'))
    if (-not $target.StartsWith($temporaryRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetFileName($target) -ne 'palworldpanel-desktop-validation') { throw '临时目录路径校验失败。' }
    if (Test-Path -LiteralPath $target) {
        $entries = @(Get-Item -LiteralPath $target) + @(Get-ChildItem -LiteralPath $target -Recurse -Force)
        if ($entries | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }) { throw '临时测试目录含重解析点，拒绝递归清理。' }
        if ($PSCmdlet.ShouldProcess($target,'移除合成测试私有材料')) { Remove-Item -LiteralPath $target -Recurse -Force }
    }
}
Write-Output '已按测试归属处理资源；未执行全局 prune，构建镜像与共享缓存保留。'
