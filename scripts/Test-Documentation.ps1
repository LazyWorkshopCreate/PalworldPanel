#requires -Version 7.0
[CmdletBinding()]
param(
    [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($RepositoryRoot)
$issues = [Collections.Generic.List[string]]::new()
$required = @(
    'README.md', 'AGENTS.md', 'CONTRIBUTING.md',
    'docs/README.md', 'docs/AGENTS.md',
    'docs/current-environment.md', 'docs/research-brief.md',
    'docs/archive/README.md',
    'docs/standards/repository-layout.md',
    'docs/standards/documentation-policy.md', 'docs/standards/engineering-rules.md'
)
foreach ($relative in $required) {
    if (-not (Test-Path -LiteralPath (Join-Path $root $relative) -PathType Leaf)) {
        $issues.Add("缺少入口：$relative")
    }
}

# Select one current dated version per established topic; never guess the latest date.
$topics = @{
    'mvp' = 'requirements'
    'assessment' = 'research'
    'system-design' = 'design'
    'technology-stack' = 'design'
    'ui-style' = 'design'
    'operational-defaults' = 'design'
    'implementation-plan' = 'planning'
    'design-review' = 'verification'
    'current-environment' = 'context'
    'research-brief' = 'context'
}
$currentPaths = @{}
$navigationPath = Join-Path $root 'docs/README.md'
$navigation = if (Test-Path -LiteralPath $navigationPath) {
    [IO.File]::ReadAllText($navigationPath)
} else { '' }
foreach ($topic in $topics.Keys) {
    $directory = Join-Path $root ('docs/' + $topics[$topic])
    $candidates = if (Test-Path -LiteralPath $directory) {
        @(Get-ChildItem -LiteralPath $directory -File -Filter '*.md' |
            Where-Object { $_.Name -match ('^\d{4}-\d{2}-\d{2}-' +
                [regex]::Escape($topic) + '(?:-r(?:[2-9]|[1-9]\d+))?\.md$') })
    } else { @() }
    if (@($candidates).Count -ne 1) {
        $issues.Add("主题必须只有一个现行日期版本：$topic")
        continue
    }
    $currentPaths[$topic] = $candidates[0].FullName
    $navTarget = $topics[$topic] + '/' + $candidates[0].Name
    if (-not $navigation.Contains('](' + $navTarget)) {
        $issues.Add("现行版本未被导航引用：$navTarget")
    }
}

$files = @(Get-ChildItem -LiteralPath $root -File -Filter '*.md')
$docsPath = Join-Path $root 'docs'
if (Test-Path -LiteralPath $docsPath -PathType Container) {
    $files += @(Get-ChildItem -LiteralPath $docsPath -Recurse -File -Filter '*.md')
}
foreach ($file in $files) {
    $relative = [IO.Path]::GetRelativePath($root, $file.FullName)
    $portableRelative = $relative.Replace('\', '/')
    if ($portableRelative -match '^docs/(context|requirements|research|design|planning|verification|operations|archive)/' -and
        $file.Name -notin @('README.md', 'AGENTS.md')) {
        $dateMatch = [regex]::Match($file.Name, '^(\d{4}-\d{2}-\d{2})-.+\.md$')
        $dateValue = [datetime]::MinValue
        if (-not $dateMatch.Success -or -not [datetime]::TryParseExact(
            $dateMatch.Groups[1].Value, 'yyyy-MM-dd',
            [Globalization.CultureInfo]::InvariantCulture,
            [Globalization.DateTimeStyles]::None, [ref]$dateValue)) {
            $issues.Add("时效文档缺少有效年月日前缀：$relative")
        }
        if ($portableRelative.StartsWith('docs/archive/')) {
            if ($portableRelative -notmatch '^docs/archive/\d{4}-\d{2}-\d{2}/[^/]+/[^/]+\.md$') {
                $issues.Add("归档路径不符合日期/分类结构：$relative")
            }
            $archiveIndex = Join-Path $root 'docs/archive/README.md'
            $archiveTarget = $portableRelative.Substring('docs/archive/'.Length)
            if (-not (Test-Path -LiteralPath $archiveIndex) -or
                -not ([IO.File]::ReadAllText($archiveIndex)).Contains('](' + $archiveTarget + ')')) {
                $issues.Add("归档未登记索引链接：$relative")
            }
        }
    }
    $body = [IO.File]::ReadAllText($file.FullName)
    if ($body -notmatch '(?m)^# [^\r\n]+') {
        $issues.Add("缺少一级标题：$relative")
    }
    if ($body -match '(?m)[ \t]+\r?$') {
        $issues.Add("行尾空白：$relative")
    }
    if (-not $body.EndsWith("`n")) {
        $issues.Add("缺少末尾换行：$relative")
    }
    # Strip fenced code before examining links. Report unmatched fences separately.
    $inFence = $false
    $fenceMarker = ''
    $visibleLines = [Collections.Generic.List[string]]::new()
    foreach ($line in ($body -split '\r?\n')) {
        $fence = [regex]::Match($line, '^\s{0,3}(`{3,}|~{3,})(.*)$')
        if ($fence.Success) {
            $marker = $fence.Groups[1].Value
            if (-not $inFence) {
                $inFence = $true
                $fenceMarker = $marker
            } elseif ($marker[0] -eq $fenceMarker[0] -and
                $marker.Length -ge $fenceMarker.Length -and
                [string]::IsNullOrWhiteSpace($fence.Groups[2].Value)) {
                $inFence = $false
            }
            continue
        }
        if (-not $inFence) { $visibleLines.Add($line) }
    }
    if ($inFence) { $issues.Add("代码围栏未闭合：$relative") }
    foreach ($match in [regex]::Matches(($visibleLines -join "`n"), '\]\(([^)]+)\)')) {
        $target = $match.Groups[1].Value
        if ($target -match '^(?:[a-zA-Z][a-zA-Z0-9+.-]*:|#)') { continue }
        $pathPart = ($target -split '#', 2)[0]
        if (-not $pathPart) { continue }
        try {
            $resolved = [IO.Path]::GetFullPath((Join-Path $file.DirectoryName $pathPart))
            if (-not (Test-Path -LiteralPath $resolved)) {
                $issues.Add("失效文件链接：$relative -> $target")
            }
        } catch {
            $issues.Add("无法解析文件链接：$relative -> $target")
        }
    }
}

$requirementsPath = $currentPaths['mvp']
$planPath = $currentPaths['implementation-plan']
if ($requirementsPath -and $planPath) {
    $requirements = [IO.File]::ReadAllText($requirementsPath)
    $plan = [IO.File]::ReadAllText($planPath)
    $ids = @([regex]::Matches($requirements, '(?m)^\| (REQ-\d{2})\s*\|') |
        ForEach-Object { $_.Groups[1].Value })
    if ($ids.Count -eq 0) { $issues.Add('需求表没有 REQ 编号') }
    if (($ids | Sort-Object -Unique).Count -ne $ids.Count) { $issues.Add('需求表编号重复') }
    foreach ($id in $ids) {
        if ($plan -notmatch ('\b' + [regex]::Escape($id) + '\b')) {
            $issues.Add("计划缺少需求映射：$id")
        }
    }
    $workIds = @([regex]::Matches($plan, '(?m)^\| (W\d{2})\b') |
        ForEach-Object { $_.Groups[1].Value })
    if ($workIds.Count -eq 0) { $issues.Add('计划表没有 W 编号') }
    if (($workIds | Sort-Object -Unique).Count -ne $workIds.Count) { $issues.Add('工作项编号重复') }
}

if ($issues.Count -gt 0) {
    $issues | ForEach-Object { Write-Output "FAIL: $_" }
    exit 1
}
Write-Output "PASS: $($files.Count) 篇文档的入口、日期命名、现行版本、归档登记、文件链接、围栏、空白与需求映射检查通过。"
Write-Output '不覆盖外部链接、标题锚点、设计语义、凭据扫描或工程运行验证。'
exit 0
