#requires -Version 7.0
$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
$helper = Join-Path $repository 'deploy/windows/Replace-ProgramDirectory.ps1'
$root = Join-Path ([IO.Path]::GetTempPath()) ('pp-directory-swap-' + [Guid]::NewGuid().ToString('N'))
$app = Join-Path $root 'PalworldPanel'
$stage = Join-Path $root 'PalworldPanel.stage-test'
$data = Join-Path $root 'data'
$journal = Join-Path $root 'transaction.json'
New-Item -ItemType Directory -Path $app,$stage,$data | Out-Null
try {
    [IO.File]::WriteAllText((Join-Path $app 'PalworldPanel.Server.exe'),'old-synthetic')
    [IO.File]::WriteAllText((Join-Path $app 'obsolete.dll'),'old')
    [IO.File]::WriteAllText((Join-Path $stage 'PalworldPanel.Server.exe'),'new-synthetic')
    [IO.File]::WriteAllText((Join-Path $stage 'new.dll'),'new')
    foreach ($required in @('PalworldPanel.Server.dll','PalworldPanel.Server.runtimeconfig.json','PalworldPanel.Server.deps.json','hostpolicy.dll','hostfxr.dll','coreclr.dll')) {
        [IO.File]::WriteAllText((Join-Path $stage $required),'synthetic-runtime')
    }
    [IO.File]::WriteAllText((Join-Path $data 'state-proof'),'synthetic-data')
    $before = (Get-FileHash -LiteralPath (Join-Path $data 'state-proof')).Hash
    Remove-Item -LiteralPath (Join-Path $stage 'hostpolicy.dll')
    $rejected = $false
    try { & $helper -Mode Activate -InstallDirectory $app -StageDirectory $stage -JournalFile $journal -DataRoot $data } catch { $rejected = $true }
    if (-not $rejected -or -not (Test-Path (Join-Path $app 'obsolete.dll')) -or (Test-Path $journal)) { throw '缺失运行时未在交换前拒绝。' }
    [IO.File]::WriteAllText((Join-Path $stage 'hostpolicy.dll'),'synthetic-runtime')
    if ($IsWindows) {
        Add-Type @'
using System;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
public static class DirectorySwapLock {
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
}
'@
        $handle = [DirectorySwapLock]::CreateFile($app,[uint32]2147483648,3,[IntPtr]::Zero,3,0x02000000,[IntPtr]::Zero)
        if ($handle.IsInvalid) { throw '无法创建合成目录锁。' }
        try {
            $rejected = $false
            try { & $helper -Mode Activate -InstallDirectory $app -StageDirectory $stage -JournalFile $journal -DataRoot $data } catch { $rejected = $true }
            if ($rejected) {
                if (-not (Test-Path (Join-Path $app 'obsolete.dll')) -or (Test-Path (Join-Path $app 'new.dll')) -or -not (Test-Path (Join-Path $stage 'hostpolicy.dll'))) { throw '目录被锁时发生部分迁移。' }
            } else {
                $lockedJournal = Get-Content -LiteralPath $journal -Raw | ConvertFrom-Json
                if (-not (Test-Path (Join-Path $app 'hostpolicy.dll')) -or -not (Test-Path (Join-Path $lockedJournal.backup 'obsolete.dll'))) { throw '目录句柄允许重命名时也必须完整交换。' }
            }
        } finally { $handle.Dispose() }
        if (-not $rejected) { & $helper -Mode Rollback -InstallDirectory $app -StageDirectory $stage -JournalFile $journal -DataRoot $data }
        Remove-Item -LiteralPath $journal
    }
    & $helper -Mode Activate -InstallDirectory $app -StageDirectory $stage -JournalFile $journal -DataRoot $data
    if (Test-Path -LiteralPath (Join-Path $app 'obsolete.dll')) { throw '旧文件仍在活动目录。' }
    if (-not (Test-Path -LiteralPath (Join-Path $app 'new.dll'))) { throw '新目录未启用。' }
    $saved = Get-Content -LiteralPath $journal -Raw | ConvertFrom-Json
    if (-not (Test-Path -LiteralPath (Join-Path $saved.backup 'obsolete.dll'))) { throw '旧目录未保留。' }
    if ((Get-FileHash -LiteralPath (Join-Path $data 'state-proof')).Hash -ne $before) { throw '运行数据被修改。' }
    & $helper -Mode Rollback -InstallDirectory $app -StageDirectory $stage -JournalFile $journal -DataRoot $data
    if (-not (Test-Path -LiteralPath (Join-Path $app 'obsolete.dll')) -or (Test-Path -LiteralPath (Join-Path $app 'new.dll'))) { throw '目录回退失败。' }
    $rejected = $false
    try { & $helper -Mode Validate -InstallDirectory $app -StageDirectory (Join-Path $root 'outside/wrong') -JournalFile $journal -DataRoot $data } catch { $rejected = $true }
    if (-not $rejected) { throw '越界暂存路径未拒绝。' }
    New-Item -ItemType Directory -Path (Join-Path $app 'instances') | Out-Null
    $rejected = $false
    try { & $helper -Mode Validate -InstallDirectory $app -StageDirectory $stage -JournalFile $journal -DataRoot $data } catch { $rejected = $true }
    if (-not $rejected) { throw '程序目录内运行数据未拒绝。' }
    Write-Output 'PASS missing runtime rejected, open directory handle never causes partial moves, whole-directory replacement and rollback, unchanged data and path guards'
} finally {
    $resolved = [IO.Path]::GetFullPath($root)
    $boundary = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if (-not $resolved.StartsWith($boundary,[StringComparison]::OrdinalIgnoreCase) -or (Split-Path -Leaf $resolved) -notlike 'pp-directory-swap-*') { throw '测试目录清理边界无效。' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
