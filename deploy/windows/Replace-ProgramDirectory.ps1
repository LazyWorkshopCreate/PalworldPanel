#requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('Validate','Activate','Rollback')][string]$Mode,
    [Parameter(Mandatory)][string]$InstallDirectory,
    [Parameter(Mandatory)][string]$StageDirectory,
    [Parameter(Mandatory)][string]$JournalFile,
    [string]$DataRoot = (Join-Path $env:ProgramData 'PalworldPanel'),
    [string]$InstallerErrorFile
)
$ErrorActionPreference = 'Stop'
trap {
    if ($InstallerErrorFile) { [IO.File]::WriteAllText($InstallerErrorFile,$_.Exception.Message,[Text.UTF8Encoding]::new($false)) }
    throw $_
}
$InstallDirectory = [IO.Path]::GetFullPath($InstallDirectory).TrimEnd('\')
$StageDirectory = [IO.Path]::GetFullPath($StageDirectory).TrimEnd('\')
$DataRoot = [IO.Path]::GetFullPath($DataRoot).TrimEnd('\')
$parent = Split-Path -Parent $InstallDirectory
$name = Split-Path -Leaf $InstallDirectory
if (-not $parent -or $InstallDirectory -eq [IO.Path]::GetPathRoot($InstallDirectory) -or
    (Split-Path -Parent $StageDirectory) -ne $parent -or
    (Split-Path -Leaf $StageDirectory) -notlike ($name + '.stage-*')) { throw '程序交换路径无效。' }
foreach ($target in @($InstallDirectory,$StageDirectory)) {
    if ($target -eq $DataRoot -or $target.StartsWith($DataRoot + '\',[StringComparison]::OrdinalIgnoreCase) -or
        $DataRoot.StartsWith($target + '\',[StringComparison]::OrdinalIgnoreCase)) { throw '程序和运行数据目录必须独立。' }
}
function Check-Tree([string]$Directory) {
    if (-not (Test-Path -LiteralPath $Directory)) { return }
    $current = $Directory
    while ($current) {
        if ((Get-Item -LiteralPath $current -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw '交换路径不能包含链接。' }
        $next = Split-Path -Parent $current
        if ($next -eq $current) { break }; $current = $next
    }
    if (@(Get-ChildItem -LiteralPath $Directory -Recurse -Force | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }).Count) { throw '程序目录不能包含链接。' }
    foreach ($runtime in @('private','state','instances','backups')) {
        if (Test-Path -LiteralPath (Join-Path $Directory $runtime)) { throw '程序目录包含运行数据，拒绝整体替换。' }
    }
}
Check-Tree $InstallDirectory
Check-Tree $StageDirectory
if ($Mode -eq 'Validate') { return }
if ($Mode -eq 'Activate') {
    if (Test-Path -LiteralPath $JournalFile) { throw '交换日志已存在，拒绝重复执行。' }
    foreach ($required in @('PalworldPanel.Server.exe','PalworldPanel.Server.dll','PalworldPanel.Server.runtimeconfig.json','PalworldPanel.Server.deps.json','hostpolicy.dll','hostfxr.dll','coreclr.dll')) {
        if (-not (Test-Path -LiteralPath (Join-Path $StageDirectory $required) -PathType Leaf)) { throw "新程序目录不完整，缺少 $required。" }
    }
    $hadOld = Test-Path -LiteralPath $InstallDirectory
    if ($hadOld -and -not (Test-Path -LiteralPath (Join-Path $InstallDirectory 'PalworldPanel.Server.exe') -PathType Leaf)) { throw '目标不是已识别的面板目录。' }
    $backup = Join-Path $parent ($name + '.previous-' + [Guid]::NewGuid().ToString('N'))
    if (Test-Path -LiteralPath $backup) { throw '旧程序归档已存在。' }
    $journal = @{install=$InstallDirectory;stage=$StageDirectory;backup=$backup;hadOld=$hadOld;phase='Prepared'}
    [IO.File]::WriteAllText($JournalFile,($journal | ConvertTo-Json),[Text.UTF8Encoding]::new($false))
    if ($hadOld) { [IO.Directory]::Move($InstallDirectory,$backup) }
    try {
        [IO.Directory]::Move($StageDirectory,$InstallDirectory)
        $journal.phase = 'Activated'
        [IO.File]::WriteAllText($JournalFile,($journal | ConvertTo-Json),[Text.UTF8Encoding]::new($false))
    }
    catch {
        if ((Test-Path -LiteralPath $InstallDirectory) -and -not (Test-Path -LiteralPath $StageDirectory)) {
            [IO.Directory]::Move($InstallDirectory,$StageDirectory)
        }
        if ($hadOld -and -not (Test-Path -LiteralPath $InstallDirectory)) { [IO.Directory]::Move($backup,$InstallDirectory) }
        throw
    }
    Write-Output '新程序目录已整体启用，旧目录保留用于回退。'
} else {
    $journal = Get-Content -LiteralPath $JournalFile -Raw | ConvertFrom-Json
    $backup = [IO.Path]::GetFullPath($journal.backup)
    if ($journal.install -ne $InstallDirectory -or $journal.stage -ne $StageDirectory -or
        (Split-Path -Parent $backup) -ne $parent -or (Split-Path -Leaf $backup) -notlike ($name + '.previous-*')) { throw '交换日志路径不匹配。' }
    if ($journal.phase -ne 'Activated') { throw '交换未完成，请人工核实日志。' }
    Check-Tree $backup
    if (Test-Path -LiteralPath $StageDirectory) { throw '回退暂存目录已占用。' }
    if ($journal.hadOld -and -not (Test-Path -LiteralPath $backup)) { throw '旧程序归档不存在。' }
    [IO.Directory]::Move($InstallDirectory,$StageDirectory)
    try { if ($journal.hadOld) { [IO.Directory]::Move($backup,$InstallDirectory) } }
    catch { [IO.Directory]::Move($StageDirectory,$InstallDirectory); throw }
    $journal.phase = 'RolledBack'
    [IO.File]::WriteAllText($JournalFile,($journal | ConvertTo-Json),[Text.UTF8Encoding]::new($false))
    Write-Output '程序目录已回退，服务保持停止，失败的新目录保留供检查。'
}
