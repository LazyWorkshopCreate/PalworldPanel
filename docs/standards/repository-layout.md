# 仓库目录规划

日期：2026-10-06。状态：文档、源码、单元测试、隔离场景脚本和单镜像构建目录已建立。目录围绕单服务器单体，不提前拆成多个服务或共享库。

## 已建立的目录

```text
PalworldPanel/
  README.md                       项目入口和阶段
  AGENTS.md                       自动化协作边界与阅读顺序
  CONTRIBUTING.md                  人工与自动化共同的变更流程
  .editorconfig                   编码、缩进、换行
  .gitattributes                  Git 文本换行规则
  .gitignore                      本地生成物及敏感材料排除
  docs/
    README.md                     唯一文档导航与决策摘要
    AGENTS.md                     文档维护补充规则
    current-environment.md        环境快照稳定索引
    research-brief.md             任务书稳定索引
    context/2026-10-05-current-environment.md 原始环境快照
    context/2026-10-05-research-brief.md      原任务书与后续范围说明
    requirements/2026-10-06-mvp-r6.md           业务目标、流程、REQ-01–15、验收
    research/2026-10-05-assessment.md        有日期与来源的调研和选型依据
    design/2026-10-06-system-design-r4.md       单体架构、UI、模型、API、任务与恢复
    design/2026-10-05-operational-defaults.md 操作策略及可配置初值
    design/2026-10-06-technology-stack-r2.md 技术组件与构建发布规范
    design/2026-10-05-ui-style.md   前端视觉、组件与交互规范
    adr/                          递增编号的决策记录
    planning/2026-10-06-implementation-plan-r5.md 阶段、W01–17、依赖与完成条件
    verification/2026-10-05-design-review.md 文档交付检查及尚缺工程证据
    standards/                    目录、文档、工程规则
    templates/                    ADR 与验证记录模板
    archive/README.md             归档台账，记录原因和替代关系
    archive/YYYY-MM-DD/<分类>/     被替代的原日期版本，冻结正文
  scripts/Test-Documentation.ps1   无网络、无服务器副作用的文档检查
```

不为每个文件建立 README。现行方案以导航链接为入口；主设计当前保留一个完整文件，避免目录迁移同时重写其契约。内容增长到难以维护时可按 API、任务、恢复拆分，同步导航与章节引用，不能只复制。

所有时效正文采用 `YYYY-MM-DD-主题.md`，例中日期为现有版本日，不应复制为未来文档的日期。实质替代版本移入 archive，目录日期取归档发生日，文件日期保留原版本日。两个稳定索引不存事实副本；归档规则见[文档维护规则](documentation-policy.md#日期前缀与归档)。

## 工程目录

| 路径 | 职责与约束 |
|---|---|
| src/PalworldPanel.Server/ | 单个 ASP.NET Core 项目；Api、Application、Domain、Infrastructure、Workers 等逻辑目录按需建立；模块不代表微服务 |
| src/PalworldPanel.Contracts/ | 前后端共用的游戏参数 JSON 元数据；不包含秘密，不拆分新服务 |
| src/PalworldPanel.AdminWeb/ | React/TS/Vite；组件、页面、API 客户端及前端测试；dist 是构建产物，不入 Git |
| tests/PalworldPanel.UnitTests/ | 业务约束、配置映射、任务状态、ZIP 校验 |
| tests/PalworldPanel.IntegrationTests/ | 文件事务、数据库恢复、镜像适配和隔离实例演练；默认禁止连接生产 |
| tests/desktop_*.py | 已建立的隔离 Docker Desktop 场景，不保存秘密或真实玩家数据；.NET 集成项目按需要再拆分 |
| tests/fixtures/ | 合成数据说明/生成脚本，不保存真实存档、秘密或玩家数据 |
| deploy/ | 经验证的 Dockerfile、Compose 和脱敏配置模板；无生产凭据和实际访问策略 |
| deploy/windows/ | Inno ISS、服务注册/升级/卸载及显式初始化脚本；不存 Windows 运行配置或秘密 |
| scripts/Build-WindowsInstaller.ps1 | 自包含 win-x64 发布及调用本机 Inno 编译器；不自动下载安装依赖 |
| scripts/Test-WindowsDeployment.ps1 | 当前用户的 Windows 原生与 Docker Linux 绑定隔离测试；不注册服务 |
| .github/workflows/ | GitHub Actions CI、tag 发行、tag Pages 与仅打包演练 |
| version.json | 发行版本唯一源 |
| docs/releases/ | 带日期且与 tag 一一对应的发行说明 |
| website/ | 公开官网，docs 用户文档、releases 版本日志、en 英文对应页、images 已审核截图；Pages 只发布此目录 |
| scripts/website-templates/、scripts/website-english.mjs | 中英首页源模板与英文用户文档/界面文本；生成 HTML 不手工编辑 |
| scripts/ | 有用途说明的构建、检查辅助；生产变更脚本显式区分，不在文档检查中调用 |
| docs/operations/ | 实施时建立的接管、部署、备份恢复、升级与手工维护手册；记录适用版本与演练证据 |
| docs/verification/YYYY-MM-DD-主题.md | 脱敏工程证据，含命令、适用版本、结果和未验证边界 |

当前已建立 solution、Directory.Build.props、global.json、NuGet/pnpm 锁文件及本机构建检查脚本；CI 已建立，覆盖 Linux/Windows 构建与测试、Docker 镜像和 Inno 安装包；公开官网发布见[交付设计](../design/2026-10-06-delivery-and-website-r2.md)。Linux 生产只有一个面板容器；Windows 使用原生服务，前端构建进入发布目录 wwwroot；不得把实例 data、SQLite、journal、备份或 TLS 密钥放在仓库/镜像可写目录中。运行路径仍以[技术设计](../design/2026-10-06-system-design-r4.md)为准。

## 本次迁移对照

| 旧路径 | 现行路径 |
|---|---|
| docs/requirements.md | docs/requirements/2026-10-06-mvp-r6.md |
| docs/research.md | docs/research/2026-10-05-assessment.md |
| docs/design.md | docs/design/2026-10-06-system-design-r4.md |
| docs/operational-defaults.md | docs/design/2026-10-05-operational-defaults.md |
| docs/plan.md | docs/planning/2026-10-06-implementation-plan-r5.md |
| docs/review.md | docs/verification/2026-10-05-design-review.md |

原始任务书内旧交付路径作为历史保留，以此表和导航为准；不保留重复全文或长期兼容占位页。环境快照中的实例事实不更新成推测的实时状态；用户要求清理的范围外项目来源与网络细节可以移除，并注明未复核运行状态。
