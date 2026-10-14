# 世界密码只读实例详情验证

版本日期：2026-10-14。修订日期：2026-10-14。状态：Passed（仓库实现与本机合成隔离验收）。适用范围：REQ-24 / W28。替代来源：无。

## 交付范围

新增世界 Id 与已应用游戏密码登录、独立访客会话、基本信息、安全游戏参数表、脱敏日志和同世界 .sav ZIP 下载；控制台实例页提供分享入口，未登录仪表盘提供世界访问入口。分享地址为 `/world-view/{32位世界GUID}`，通用入口 `/world-view`。保留内网精确白名单和全部管理员认证/CSRF 边界。

## 实际检查

| 检查 | 命令或方式 | 结果 |
|---|---|---|
| .NET 回归 | `dotnet test tests/PalworldPanel.UnitTests -c Release --no-restore --filter 'Category!=Desktop'` | Passed，219 项，包含本需求 18 项 |
| Docker Desktop HTTP 验收 | 设置 `PANEL_TEST_SERVER` 为本机测试发布 DLL、`PANEL_TEST_BIND` 为本机内网 IPv4，再运行 `dotnet test tests/PalworldPanel.UnitTests -c Release --no-restore --filter 'Category=Desktop'` | Passed，1 项；未提供环境变量时该项明确 Skip，不默认连接 Docker |
| 前端回归 | `pnpm --dir src/PalworldPanel.AdminWeb test` | Passed，149 项，包含本需求 6 项 |
| 前端格式与构建 | `pnpm --dir src/PalworldPanel.AdminWeb format:check` / `pnpm --dir src/PalworldPanel.AdminWeb build` | Passed，TypeScript 与 Vite 构建通过 |
| 后端测试发布 | `dotnet publish src/PalworldPanel.Server -c Release --no-restore -o artifacts/world-readonly/publish` | Passed；仅本机忽略的测试产物 |
| 文档与差异 | `pwsh -NoProfile -File scripts/Test-Documentation.ps1` / `git diff --check` | Passed；不代表游戏或生产验证 |

后端回归验证：只认已应用密码、草稿不生效、应用密码变化撤销会话、跨世界、世界变化、重启、退出与固定期限失效；空密码、未应用、未知、隔离、删除和歧义世界统一拒绝；来源限流及过期恢复；设置白名单和日志秘密/IP/路径脱敏；普通备份与含安装备份均只导出世界及玩家 .sav；损坏、错误世界/实例/路径、任务和 OS 锁冲突拒绝；响应流关闭删除临时文件。

前端回归验证：访客入口不调用任何管理员 API；密码仅在 POST 正文且成功后清空；错误允许重试；直接下载链接、无游戏写操作；退出取消读请求且迟到响应不能恢复详情；会话失效清空旧数据；切换世界清空并忽略旧响应。

## 隔离运行证据

使用 Windows 原生临时面板进程及 Docker Desktop Linux 引擎中的专用 `pp-world-view-test-<随机Id>` 合成容器（128 MiB / 0.5 CPU），只创建合成世界和玩家占位存档、合成密码及临时 SQLite。游戏 REST 使用未占用的临时端口，因此运行设置显示不可用，已应用基准正常显示。未使用真实存档或生产凭据。

实际 HTTP 核验：未登录读取 401、错误密码 401、跨源登录 403、正确登录 200；独立 cookie 有 HttpOnly / SameSite=Strict / 限定 Path；访客读取实例/任务/审计/管理设置及提交停止动作仍 401；跨世界 401；脱敏 Docker 日志 200；ZIP 200 且仅 3 个指定世界 .sav 文件；退出 204 后读取 401。没有建立任何游戏任务，临时导出目录为空。

Playwright CLI 在真实内网 HTTP 地址核验深链接、密码表单、登录、参数不可用提示、日志、备份跳转、浏览器 ZIP 下载、刷新恢复会话及退出。下载到本机的 ZIP 再次核验 3 个同世界 .sav 条目，ZIP CRC 通过。1440×1000 与 390×844 截图已人工检查；页面宽度与滚动宽度分别为 1440/1440、390/390，无整页横向溢出。视觉检查后修正基本信息网格，并将参数区域限高 480px、添加内容跳转入口。截图在忽略的 `output/playwright/world-readonly-desktop.png` 与 `output/playwright/world-readonly-mobile.png`，仅是本机验收附件，不作为仓库持续集成依赖。

浏览器初次请求产生预期的未认证 401；未观察到页面脚本异常。可选 `PANEL_TEST_BROWSER_READY` 只写本机 URL 与合成世界 Id，最多等待四分钟供浏览器复核；不写秘密。验收后临时进程、专用测试容器和临时数据均已清理；保留本机忽略的测试构建与截图用于审阅。

## 未验证边界

本次没有部署生产、提交、推送或发布。未运行真实帕鲁游戏客户端，未验证真实角色恢复、真实游戏 REST 设置成功观测、性能长稳、Linux HTTPS 端到端或外网访问；合成 .sav 占位文件不是可玩的世界。纯后端投影回归覆盖运行观测字段安全性，但不能替代真实游戏观测。新入口仍受白名单限制，不提供公网访问或公网 UDP 管理。

关联：[需求](../requirements/2026-10-14-world-readonly.md)、[设计](../design/2026-10-14-world-readonly.md)、[计划](../planning/2026-10-14-world-readonly.md)、[ADR-015](../adr/015-world-readonly.md)。

2026-10-14 后续补充：[真实游戏验收](2026-10-14-world-readonly-real-game.md)已核实真实服务端设置、日志及备份下载，并修复数组观测和 ANSI 残片。本文保留早期合成测试事实；真实游戏的当前证据与剩余边界以上述补充记录为准。
