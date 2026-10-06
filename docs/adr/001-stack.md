# ADR-001：采用 .NET、React 与 SQLite 单体

日期：2026-10-05。状态：已采纳；2026-10-06 本机实现及 W01 锁定构建已验证，生产未部署。

## 背景

单管理员、单服务器面板需要管理多个独立世界，主要工程难点是实例归属、配置与文件安全、任务恢复和操作反馈。需要明确类型边界、可测试的容器/文件适配和独立前端交互，不需要集群、多租户或多个发布服务。

## 决策

采用 .NET 10 LTS / ASP.NET Core Minimal API、React/TypeScript/Vite/pnpm、Microsoft.Data.Sqlite、BackgroundService 和 Serilog。前端独立源码、同源静态托管；生产发布一个面板容器，运行时不需要 Node 服务。SQLite 存任务与元数据，游戏文件独立管理。

组件职责、依赖锁与发布契约以[技术栈规范](../design/2026-10-06-technology-stack-r2.md)为准，视觉与交互以[UI 规范](../design/2026-10-05-ui-style.md)为准。所有管理读取要求认证，写操作校验 CSRF；公网网络服务不属于面板依赖。

选择原因是 C# 的类型约束、异步 I/O 和任务/文件/API 编排适合后台管理，React 可承载状态、配置差异及恢复向导；前后端同源发布限制运维复杂度。具体 LTS 与依赖适配在 W01 核定，不以技术选型替代构建或性能验证。

## 备选与代价

服务端页面可减少前端构建步骤，但本项目选择独立 TypeScript 前端承载持续更新与操作流程，接受额外开发工具。Node/TypeScript 或 Python 后端也可实现相同安全模型，当前不作为默认方向；技术栈比较见[调研 §4](../research/2026-10-05-assessment.md#4-技术栈与部署比较)。

SQLite 只运行一个执行进程，保持短事务。Channel 仅通知，不保存任务；BackgroundService 本身不能保证断电恢复，需要持久任务、实例锁及文件 journal。YAML 解析库、SQLite provider、镜像基线和许可在 W01/W03 验证。改变组件职责或发布模式需记录后续 ADR，并同步需求、设计、计划和规范。
