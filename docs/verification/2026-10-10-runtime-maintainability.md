# 验证记录：运行可维护性

文件名：`2026-10-10-runtime-maintainability.md`。

日期：2026-10-10。状态：Passed。关联：REQ-21、W25。

## 范围与环境

Windows 本机仓库工作区，.NET 10 Release 与 AdminWeb 生产构建。未连接生产主机，未启动面板服务，未创建或改动游戏实例。测试数据是合成任务、合成密钥和组件渲染。

## 检查与结果

| 检查 | 命令或操作 | 预期 | 实际 | 状态 |
|---|---|---|---|---|
| 文档结构 | `pwsh -NoProfile -File scripts/Test-Documentation.ps1` | 入口、日期、链接和空白通过 | 见本记录结论中的最终命令 | Passed |
| 后端测试 | `dotnet test tests/PalworldPanel.UnitTests/PalworldPanel.UnitTests.csproj -c Release --nologo` | 全部通过 | 173 通过，0 失败 | Passed |
| 前端格式、测试和构建 | `pnpm exec prettier --check`、`pnpm test`、`pnpm build` | 格式通过，测试通过，TypeScript 与 Vite 构建通过 | Prettier 通过；Vitest 17 个文件 101 项通过；Vite 构建完成 | Passed |
| 坏载荷隔离 | `PoisonPayloadFailsThatTaskAndLeavesTheInstanceAvailable` | 任务失败并写诊断日志，实例可再入队，响应文案不含异常类型 | 状态 Failed，结果码 ExecutorFault，日志含任务编号和 JsonReaderException，随后入队成功 | Passed |
| 调度补试 | `ScheduledBackupRetriesOnlyFailedPreflightWithinTheSameDay` 及数据库查询 | 预检失败最多 3 次，其他终态不入队 | 断言符合设计表 | Passed |
| 备份策略重新登录 | `过期身份保留备份策略并在重新登录后提交` | 保留输入，登录后按原修订号提交 | 测试通过 | Passed |
| 提示分离 | `操作错误和采样错误同时保留，收起只影响操作错误` | 两种提示同时存在 | 测试通过 | Passed |

最终汇总命令是 `pwsh -NoProfile -File scripts/Test-LocalBuild.ps1 -SkipDocker`。2026-10-10 该命令通过：文档检查报告 113 篇通过，Release 测试 173 项通过，前端 101 项通过，Vite 构建完成。Docker 镜像构建不在本次范围。

## 副作用与清理

单元测试在系统临时目录创建数据库和密钥，结束时删除目录。没有面板进程、Docker 容器或实例目录残留。仓库没有写入真实密码或存档。

## 结论与限制

本记录证明 REQ-21 的决策、坏载荷落库、调度查询、策略表单和提示组件在本机自动化检查中符合设计。它不能证明真实定时备份会在 05:00 补试，也不能证明运行中的面板日志文件、浏览器里的备份页或地址缓存少了一次 Docker 检查。未执行玩家入服、生产部署和 Docker 镜像验收。
