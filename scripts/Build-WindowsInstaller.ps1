#requires -Version 7.0
[CmdletBinding()]
param([string]$Version, [string]$IsccPath, [switch]$PublishOnly)
$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
if (-not $Version) { $Version = (Get-Content -LiteralPath (Join-Path $repository 'version.json') -Raw | ConvertFrom-Json).version }
if ($Version -notmatch '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(-[0-9A-Za-z-]+(\.[0-9A-Za-z-]+)*)?$') { throw '版本应为语义版本，例如 1.2.3 或 1.2.3-rc.1。' }
$numericVersion = ($Version -split '-')[0]
if (@($numericVersion.Split('.') | Where-Object { [long]$_ -gt 65535 }).Count -gt 0) { throw '安装器数字版本超出范围。' }

$output = Join-Path $repository 'artifacts/windows'
$publish = Join-Path $output 'publish'
$publish = [IO.Path]::GetFullPath($publish)
$outputBoundary = [IO.Path]::GetFullPath($output).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if (-not $publish.StartsWith($outputBoundary,[StringComparison]::OrdinalIgnoreCase) -or (Split-Path $publish -Leaf) -ne 'publish') { throw '发布目录超出预期构建范围。' }
if (Test-Path -LiteralPath $publish) {
    if ((Get-Item -LiteralPath $publish).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw '发布目录不能是链接。' }
    Remove-Item -LiteralPath $publish -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $publish | Out-Null
& pnpm --dir (Join-Path $repository 'src/PalworldPanel.AdminWeb') build
if ($LASTEXITCODE) { throw '前端构建失败。' }
& dotnet publish (Join-Path $repository 'src/PalworldPanel.Server') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:Version=$Version "-p:NuGetLockFilePath=$(Join-Path $output 'packages.win-x64.lock.json')" -o $publish
if ($LASTEXITCODE) { throw 'Windows 发布失败。' }
New-Item -ItemType Directory -Force -Path (Join-Path $publish 'wwwroot') | Out-Null
Copy-Item -Path (Join-Path $repository 'src/PalworldPanel.AdminWeb/dist/*') -Destination (Join-Path $publish 'wwwroot') -Recurse -Force
New-Item -ItemType Directory -Force -Path (Join-Path $publish 'deployment'),(Join-Path $publish 'docs') | Out-Null
Copy-Item -Path (Join-Path $repository 'deploy/windows/*.ps1') -Destination (Join-Path $publish 'deployment') -Force
Copy-Item -Path (Join-Path $repository 'docs/*') -Destination (Join-Path $publish 'docs') -Recurse -Force
Copy-Item -LiteralPath (Join-Path $repository 'AGENTS.md'),(Join-Path $repository 'CONTRIBUTING.md'),(Join-Path $repository 'README.md'),(Join-Path $repository 'version.json') -Destination $publish -Force
Write-Output "Windows win-x64 发布目录：$publish"
if ($PublishOnly) { return }
if (-not $IsccPath) {
    $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($command) { $IsccPath = $command.Source }
    else {
        $candidates = @(
            (Join-Path $repository '.tools/inno-setup/ISCC.exe'),
            (Join-Path $env:ProgramFiles 'Inno Setup 7/ISCC.exe'),
            (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 7/ISCC.exe')
        )
        $IsccPath = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    }
}
if (-not $IsccPath -or -not (Test-Path -LiteralPath $IsccPath)) { throw '缺少 Inno Setup 7 编译器（项目使用原生系统目录执行函数）。发布目录已经生成；安装编译器后用 -IsccPath 指定 ISCC.exe。' }
& $IsccPath "/DPublishDirectory=$publish" "/DOutputDirectory=$(Join-Path $output 'installer')" "/DAppVersion=$Version" "/DNumericVersion=$numericVersion" (Join-Path $repository 'deploy/windows/PalworldPanel.iss')
if ($LASTEXITCODE) { throw 'Inno Setup 编译失败。' }
