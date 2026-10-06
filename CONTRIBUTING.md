# 仓库维护说明

用户已授权实现和本机 Docker Desktop 隔离测试；生产部署及原实例变更须按后续明确要求开展。开始工作先读 [AGENTS.md](AGENTS.md)、[环境快照](docs/context/2026-10-05-current-environment.md)、[任务书](docs/context/2026-10-05-research-brief.md)和[文档导航](docs/README.md)。

## 变更流程

1. 确定修改的是业务目标、技术方案、决策、计划还是证据，按[目录规划](docs/standards/repository-layout.md)放置。
2. 按[文档规则](docs/standards/documentation-policy.md)更新唯一现行内容，保留来源、日期、状态和需求/工作项引用；已采纳决策改变时记录替代 ADR。

   需求、调研、设计、计划及验证等时效正文加 `YYYY-MM-DD-` 前缀；实质替代新建日期版本，将旧版移入 `docs/archive/<归档日>/<分类>/` 并登记台账。小修订保留文件版本日期，更新修订记录。
3. 若开始实现，遵守[工程规则](docs/standards/engineering-rules.md)，并同步行为契约、任务失败路径及验收条件。
4. 运行 `pwsh -NoProfile -File scripts/Test-Documentation.ps1` 与 `git diff --check`；人工核对决策一致性、敏感材料和验证边界。
5. 交付说明写清改变、理由、实际检查和未验证项。仅按明确要求提交或发布；提交信息使用 `docs:`、`chore:`、`feat:`、`fix:`、`test:` 等简明前缀即可。

单独维护文档需要 PowerShell 7，无需 .NET、Node 或 Docker。代码变更按 `scripts/Test-LocalBuild.ps1` 检查锁定依赖、后端、前端及 Docker 镜像；实际游戏验证范围见[验证记录](docs/verification/2026-10-06-desktop-implementation.md)。本机通过不能代表生产或真实玩家验收，不添加空 CI 或虚假的通过标记。

生产存档、凭据、原始 inspect/Compose 输出及玩家个人资料不入仓库。需要测试样档时使用隔离私有目录；仓库只保留合成 fixture 生成器和脱敏结果。忽略规则是辅助，不能代替提交前检查。
