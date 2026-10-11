# 验证记录：世界管理

文件名：`2026-10-10-world-management.md`。

日期：2026-10-10。修订日期：2026-10-11。状态：Passed。关联：REQ-23、W27、ADR-012。

## 范围与环境

Windows 本机仓库。数值编辑代码和“存档”页已经删除。封禁列表使用合成编号。未启动面板，未连接游戏 REST，未读取真实玩家存档。

## 检查与结果

| 检查 | 命令或操作 | 预期 | 实际 | 状态 |
|---|---|---|---|---|
| 后端测试 | `dotnet test tests/PalworldPanel.UnitTests/PalworldPanel.UnitTests.csproj -c Release --nologo` | 全部通过 | 178 通过，0 失败 | Passed |
| 封禁编号 | `BanListRejectsUnsafeIds` | 合法编号可往返；路径和重复行被拒绝 | 通过 | Passed |
| 前端 | 在 `src/PalworldPanel.AdminWeb` 运行 `pnpm test` | 测试通过 | Vitest 18 个文件 103 项通过 | Passed |
| 世界页 | `封禁使用玩家编号，隔离说明新角色风险` | 封禁提交编号；隔离说明新角色 | 通过 | Passed |
| 已删除页签 | 路由测试访问 `/instances/synthetic/saves` | 不再是实例页签 | 解析为不存在页面 | Passed |

## 副作用与清理

测试使用系统临时目录，结束时删除。没有游戏进程或真实封禁列表被修改。

## 2026-10-11 玩家列表

| 检查 | 命令或操作 | 预期 | 实际 | 状态 |
|---|---|---|---|---|
| 后端测试 | 同上 Release 全量 | 全部通过 | 179 通过，0 失败 | Passed |
| 在线投影 | `OnlineProjectionKeepsSaveIdAndDropsAddress` | 保留存档编号，去掉 IP 和账号名 | 通过 | Passed |
| 前端 | 在 `src/PalworldPanel.AdminWeb` 运行 `pnpm test` | 测试通过 | Vitest 18 个文件 104 项通过 | Passed |
| 世界列表 | `按封禁状态显示操作，展开后只显示对应存档` | 未封禁显示封禁和踢出；已封禁显示解除；展开不含其他玩家存档 | 通过 | Passed |
| 停止后的隔离 | `停止时隔离对应存档，并且不显示踢出` | 停止时不显示踢出；隔离提交对应存档编号 | 通过 | Passed |
| 文档 | `scripts/Test-Documentation.ps1` | 结构与链接通过 | 130 份通过 | Passed |

运行中的隔离面板没有登记实例，世界页无法在浏览器里打开。本次没有重新构建镜像。

## 结论与限制

本记录证明世界页按封禁状态给出操作，并把存档收在对应玩家的子表里。它不能证明运行中的踢出或封禁接口在当前版本返回成功，也不能证明隔离任务在 Docker 中完成。停止时无法把用户编号对应到存档文件名，这类存档单独成行并显示无法判断。数值编辑不再属于本产品。
