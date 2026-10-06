# PalworldPanel 文档导航

日期：2026-10-06（北京时间）。状态：本机实现与 Docker Desktop 隔离验证交付完成；生产部署与既有实例写接管未授权。实际结果见[实现与本机验证](verification/2026-10-06-desktop-implementation.md)。

## 推荐结论

本项目采用单服务器、单管理员的自建轻量单体：.NET 10 LTS / ASP.NET Core Minimal API + React/TypeScript/Vite + pnpm + SQLite（Microsoft.Data.Sqlite）+ 持久化后台任务。前端独立源码项目，构建为静态资源后由同一后端托管，Linux 生产发布一个面板容器；Windows 发布原生服务与 Inno 安装包，两种模式均无需 Node.js。后端具有 root 和 Docker 管理权限，仅允许显式白名单中的内网 IP 访问；Linux 默认 HTTPS、Windows 默认 HTTP（HTTPS 可选），绑定指定 LAN 接口，防火墙和应用双层校验，白名单内可未登录只读查看仪表盘，管理数据与操作仍要求管理员登录。Docker/Compose 管生命周期，游戏 REST/RCON 保持回环、不得公开。RCON 官方已弃用，不作为 MVP 依赖。出处与比较见[调研](research/2026-10-05-assessment.md)中的技术栈比较，具体采用要求以项目设计规范为准。

优先只读接管既有两个实例，保留原目录、Compose 项目、端口与世界。开放写操作前验证实例身份、备份回退、后台任务互斥及镜像兼容性。Saved.zip 默认只导入世界和玩家数据，目标密码、端口和游戏规则保留。面板验收 API 健康、世界加载、原玩家角色恢复与本机端口；公网 UDP 转发、诊断和连通性验收在面板范围之外。

面板提供本地实例 UDP 地址/端口，不集成外部网络设施的管理 API 或状态。两个实例内存上限合计 36 GiB 超过快照中的 29 GiB 物理内存，不能据此认定能稳定同时运行；后续需测量峰值并设宿主预算。

## 阅读路径

时效正文采用年月日前缀，此导航明确选择现行版本。被替代内容见[归档索引](archive/README.md)，归档不能作为现行约束；具体流程见[日期与归档规则](standards/documentation-policy.md#日期前缀与归档)。

新会话先读环境快照与任务书，再读需求、现行设计和实现计划。维护文档时先看[目录规划](standards/repository-layout.md)、[文档规则](standards/documentation-policy.md)；实施时另读[工程规则](standards/engineering-rules.md)。入口顺序及协作边界见根 [AGENTS.md](../AGENTS.md)。

| 文档 | 用途 |
|---|---|
| [官网双语设计](design/2026-10-06-website-languages.md) | 中英内容来源、对应页切换与翻译维护 |
| [官网双语验证](verification/2026-10-06-website-languages.md) | 12 页静态检查、48 组响应式检查及发布边界 |
| [公开文档与日志设计](design/2026-10-06-public-documentation.md) | 独立列表/详情、公开内容生成与站内导航 |
| [公开文档与日志验证](verification/2026-10-06-public-documentation.md) | 六页面、项目路径及移动端验证 |
| [流水线与发行手册](operations/2026-10-06-release-workflow.md) | 日常 CI、tag 发行、只打包演练和 Pages |
| [发行流程验证](verification/2026-10-06-release-workflow.md) | 本次流程调整的实际验证，未创建 Release |
| [首个版本发行验证](verification/2026-10-07-first-release.md) | v0.1.0-rc.1 的 tag、流水线、下载校验与网站部署证据 |
| [v0.1.0-rc.1 版本说明](releases/2026-10-06-v0.1.0-rc.1.md) | 首个候选版本的功能、交付与使用边界 |
| [交付与官网需求](requirements/2026-10-06-delivery-and-website-r2.md) | REQ-16 / REQ-17：流水线与公开 GitHub Pages 官网 |
| [交付与官网设计](design/2026-10-06-delivery-and-website-r2.md) | 最小权限 Actions、安装包及静态网站发布边界 |
| [交付与官网计划](planning/2026-10-06-delivery-and-website-r2.md) | W18 / W19 交付与验收 |
| [交付与官网验证](verification/2026-10-06-delivery-and-website.md) | 本地检查、云端运行与线上发布实际结果 |
| [Windows 部署设计](design/2026-10-06-windows-deployment-r4.md) | Windows 服务、Docker Desktop Linux 引擎、路径/权限和安装契约 |
| [Windows 安装手册](operations/2026-10-06-windows-deployment-r4.md) | 发布、Inno、初始化、SCM 与保留数据的升级卸载 |
| [Windows 验证](verification/2026-10-06-windows-deployment.md) | 原生隔离结果及尚未编译/未运行的安装器与管理员服务验收 |
| [ADR-006 未登录只读仪表盘](adr/006-readonly-dashboard.md) | 白名单内匿名状态展示与管理认证边界 |
| [ADR-009 安装与目录交换](adr/009-directory-upgrade.md) | 重新配置/仅升级程序、完整目录交换、回退与卸载器隔离 |
| [ADR-008 首次网站设置](adr/008-first-access-setup.md) | Windows 首次访问设置 admin 密码，一次性入口，保留已有账号 |
| [ADR-007 HTTP 与部署检查](adr/007-http-and-prerequisites.md) | Windows 默认 HTTP、可选 TLS、安装依赖与初始化端口检查 |
| [ADR-005 Windows 服务](adr/005-windows-service.md) | 扩展 Windows 部署平台，Linux 模式保留 |
| [本机运行与恢复手册](operations/2026-10-06-local-run-and-recovery.md) | 当前实现的构建、隔离测试、受控接管、世界/面板灾备及测试清理 |
| [实现与本机验证](verification/2026-10-06-desktop-implementation.md) | 实际构建、真实游戏场景、失败修复和未验证边界 |
| [现有环境](context/2026-10-05-current-environment.md) | 用户提供的 2026-10-05 快照，非实时状态 |
| [任务书](context/2026-10-05-research-brief.md) | 原始问题与交付范围 |
| [调研](research/2026-10-05-assessment.md) | 原始来源、能力矩阵、现成面板与技术栈比较、验证边界 |
| [运维策略与工程默认值](design/2026-10-05-operational-defaults.md) | 已采纳的备份、更新、容量、清理策略与可配置初值 |
| [业务需求](requirements/2026-10-06-mvp-r6.md) | 用户目标、流程、MVP、可验收条件 |
| [技术设计](design/2026-10-06-system-design-r4.md) | 架构、页面线框、模型、接口、任务、安全与失败处理 |
| [技术栈设计规范](design/2026-10-06-technology-stack-r2.md) | 本项目组件、依赖、测试工具与同源单镜像发布契约 |
| [前端 UI 设计规范](design/2026-10-05-ui-style.md) | 本项目布局、颜色、字体、组件、响应式与交互验收 |
| [实现计划](planning/2026-10-06-implementation-plan-r5.md) | 分阶段工作项、依赖、交付物、完成条件 |
| [ADR-001 技术栈](adr/001-stack.md) | 已确定的本项目技术栈与取舍 |
| [ADR-002 部署与权限](adr/002-deployment.md) | 已确定 root/Docker 权限、内网 IP 白名单与已采纳容器打包 |
| [ADR-003 存档与操作安全](adr/003-instance-and-recovery.md) | 原位接管、单写入者、恢复事务、升级回退 |
| [ADR-004 执行器与模板](adr/004-local-executor.md) | 实际 OS 锁/队列、新建预检和已支持写模板边界 |
| [交付检查](verification/2026-10-05-design-review.md) | 任务书覆盖、文档一致性与工程验证清单 |
| [目录规划](standards/repository-layout.md) | 已建立文档结构、未来工程目录与迁移对照 |
| [文档维护规则](standards/documentation-policy.md) | 内容归属、状态、ADR 维护和检查要求 |
| [工程与运行安全规则](standards/engineering-rules.md) | 实施时的模块、数据、任务、权限与质量门槛 |
| [协作流程](../CONTRIBUTING.md) | 修改、检查与交付步骤 |
| [ADR 模板](templates/adr.md) / [验证模板](templates/verification-record.md) | 新决策和脱敏工程证据记录 |
| [时效文档模板](templates/dated-document.md) | 日期版本、范围、修订与替代来源 |
| [归档索引](archive/README.md) | 历史版本、归档原因与替代关系 |

[登录控制台截图验证](verification/2026-10-06-authenticated-console-screenshots.md)：管理员重置与功能说明的八张截图已完成；[首次截图记录](verification/2026-10-06-console-screenshots.md)保留早期登录依赖与浏览器视口复核证据。

## 决策状态

| 编号 | 已确定策略 | 实施边界 |
|---|---|---|
| D1（已确定） | 本项目采用：.NET 10 Minimal API + React/TypeScript/Vite/pnpm + SQLite | 用户已确定技术栈方向；依赖版本与镜像适配在 W01 验证，不需再次确认方向 |
| D2（已确定） | 后端具有 root 和 Docker 管理权限；只允许白名单中的内网 IP 访问 | 保留登录与 CSRF；Linux 单容器默认 HTTPS、Windows 原生服务默认 HTTP、双层 IP 校验；实际地址/白名单/TLS 部署参数尚未提供，不自动放行整个内网 |
| D3（已确定） | 定时备份采用保存后停服的完整快照；先保留现有外部备份 | 05:00/05:15 错峰、保留 14 天；停服耗时待演练；切换后每实例仅一个调度所有者 |
| D4（已确定） | 管理写权限接管时关闭镜像启动更新与自动变更，升级显式执行 | 普通重启不再隐式升级；本阶段未修改现有设置 |
| D5（已确定） | Linux 专用服务器同平台 ZIP 导入；跨平台身份转换延后 | 不承诺单机/合作主机或跨平台账号自动迁移 |
| D6（已确定） | 公网 UDP 在面板范围之外 | 面板不配置/监控/诊断公网隧道，不设置外网探针或验收；只管理本地游戏端口和实例状态 |
| D7（已确定） | 宿主预算约束、重 IO 串行、清理先隔离、可配置保护初值 | 见[运维策略](design/2026-10-05-operational-defaults.md)；不自动改变现有实例资源或清理数据 |

原任务书是初始范围，以用户后续指示及 D1–D7 为准。用户已授权本机实现与隔离测试；真实构建、合成游戏和灾备证据见验证记录。未登录生产服务器、未改动原实例、未复制真实秘密或玩家存档；本机交付不代替生产变更授权和真实玩家验收。

[本机实例生命周期验收](verification/2026-10-07-lifecycle-acceptance.md)：API、浏览器与 Windows/Docker 隔离实例的操作和修复证据。

- [公开仓库与发布约定](operations/2026-10-07-public-release.md)
