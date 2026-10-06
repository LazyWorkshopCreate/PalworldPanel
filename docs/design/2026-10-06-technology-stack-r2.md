# PalworldPanel 技术栈设计规范

版本日期：2026-10-06。修订日期：2026-10-06。状态：已采纳，本机工程已实现，生产未部署。适用范围：单服务器单管理员。替代来源：[原规范（历史）](../archive/2026-10-06/design/2026-10-05-technology-stack.md)。精确版本与实证见[本机记录](../verification/2026-10-06-desktop-implementation.md)。

本版增加 Windows 部署，替代[同日原版（历史）](../archive/2026-10-06/design/2026-10-06-technology-stack.md)；原 Linux 模式继续支持。

## 技术组件

| 层面 | 本项目规范 | 实施要求 |
|---|---|---|
| 后端 | C#、.NET 10、ASP.NET Core Minimal API | Web SDK、Nullable、warnings as errors；接口 `/api/v1`；异步 I/O、取消令牌与有界响应 |
| 前端 | React、TypeScript、Vite、pnpm | 独立 AdminWeb 源码项目、TypeScript strict；生产为静态 SPA，由后端同源托管 |
| UI | Radix Dialog、Lucide、自有 CSS | 视觉及交互以[UI 规范](2026-10-05-ui-style.md)为准；不默认引入大型组件框架或 Tailwind |
| 数据库 | Microsoft.Data.Sqlite、SQLite/WAL、参数化直接 SQL | 不引入 ORM 或外部数据库；外键、短事务、busy timeout、版本迁移与 Online Backup API |
| Linux 原生库 | Microsoft.Data.Sqlite.Core 与适配系统 SQLite 的 provider | 运行镜像安装 libsqlite3-0；具体 provider 与目标平台加载、备份行为在 W01 验证；避免同时混用不同原生初始化方案 |
| 后台任务 | BackgroundService、单持久执行器 | SQLite 保存任务、条件领取/取消；执行器全生命周期 OS 独占锁、实例锁、文件 journal、人工中断恢复；不依赖内存队列保证持久性 |
| 日志与审计 | Serilog、File Sink；SQLite 审计独立存储 | 结构化脱敏、有界输出与保留策略；任务证据不被普通轮转提前删除 |
| 测试 | xUnit；Vitest、Testing Library、jsdom | 后端单元/集成测试、前端语义和交互测试；不默认连接生产实例 |
| 格式 | EditorConfig、Prettier | UTF-8、LF；锁定工具版本并建立实际检查，不能报告尚不存在的构建通过 |
| 部署 | Linux 单面板容器 / Windows 自包含服务 | .NET 运行时、SQLite 原生库、Docker CLI/Compose 和受控归档工具；root/Docker 权限、Linux host 网络与默认 HTTPS 入口；Windows 默认 HTTP、可选 HTTPS；两者均为 LAN 白名单入口 |

## 工程结构与发布

项目与命名空间使用 PalworldPanel。Server 内按应用、领域、基础设施、API、Workers 划分逻辑模块，先保留一个后端项目；前端测试贴近 AdminWeb。完整目录和建立时机见[目录规划](../standards/repository-layout.md)。不引入微服务、消息集群、租户平台或其他项目的程序、数据库与运行配置。

Node 24 为开发/构建基线；pnpm、React、TypeScript、Vite、NuGet 包及 SQLite provider 的精确版本由 W01 核定兼容性、许可和包源后锁定。不从其他仓库复制依赖范围、锁文件或未核实版本号，本次具体锁定版本见验证记录；字体采用系统回退，不额外打包字体。

构建顺序为冻结依赖安装 → TypeScript 检查与前端测试 → Vite build → dotnet build/test/publish → 前端 dist 复制到 Server/wwwroot → 多阶段单镜像打包。前端只构建一次，后端发布复制结果，不重复执行前端构建。运行镜像不启动 Node、pnpm 或 Vite 服务。Windows 发布使用同一后端和静态前端，自包含 win-x64；Microsoft.Extensions.Hosting.WindowsServices 10.0.12 接 SCM，Inno Setup 6/7 打包，不要求目标机安装 Node 或 .NET。部署细节见 [Windows 设计](2026-10-06-windows-deployment-r4.md)。

开发时 Vite 仅代理到回环后端。生产 API 不命中返回 JSON 404，SPA 深链接刷新返回入口页；API 不能被页面 fallback 吞掉。带内容哈希的 JS/CSS 可长缓存，入口 HTML 需重新验证或短缓存，发布时检查资源引用与升级缓存失效。所有入口先验证来源白名单，所有管理数据再认证，写操作再校验 CSRF。

## 验证与变更

W01 核定构建平台、依赖锁、许可与镜像适配；W03 验证任务/文件事务；W04 验证单镜像静态资源、路由、来源与认证；后续工作项覆盖升级、备份恢复和真实玩家验收。选择技术栈不是工程通过证据。实际命令、版本和结果写入带日期的 verification 文档。

组件职责变化需同步系统设计、ADR 和计划；精确版本更新要有构建/适配验证及回退路径。运维数值以[操作策略](2026-10-05-operational-defaults.md)为准，不在本文重复维护。
