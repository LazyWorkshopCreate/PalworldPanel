# 验证记录：世界管理与存档数值

文件名：`2026-10-10-world-and-save-data.md`。

日期：2026-10-10。状态：已结束。关联：REQ-23、REQ-24、W27、W28、ADR-011。归档说明：数值编辑撤销后，本记录不再作为现行验收。正文保留撤销前的检查结果。

## 范围与环境

Windows 本机仓库。封禁列表和存档数值使用合成数据。未启动面板，未连接游戏 REST，未读取真实玩家存档。

## 检查与结果

| 检查 | 命令或操作 | 预期 | 实际 | 状态 |
|---|---|---|---|---|
| 后端测试 | `dotnet test tests/PalworldPanel.UnitTests/PalworldPanel.UnitTests.csproj -c Release --nologo` | 全部通过 | 179 通过，0 失败 | Passed |
| 科技点替换 | `TechnologyPointChangesOnlyItsOwnBytes` | 只改变该整数所在字节，其他字节还原后一致 | 通过 | Passed |
| 专有压缩与封禁编号 | `CurrentCompressionCannotBeEditedAndBanListRejectsUnsafeIds` | `PlM` 不可编辑；`../` 和重复编号被拒绝 | 通过 | Passed |
| 前端 | `pnpm test` 与 `pnpm build` | 测试和构建通过 | Vitest 19 个文件 105 项通过；Vite 构建完成 | Passed |
| 世界页 | `封禁使用玩家编号，隔离说明新角色风险` | 封禁提交编号；隔离说明新角色 | 通过 | Passed |
| 存档页 | `只提交选中文件里允许修改的数值` | 按路径提交科技点，并说明帕鲁区不猜写 | 通过 | Passed |

## 副作用与清理

测试使用系统临时目录，结束时删除。没有游戏进程或真实封禁列表被修改。

## 结论与限制

本记录证明撤销前连接控制和存档数值已经分开：封禁编号有校验，科技点替换不改动其余正文，两个页面各自提交自己的操作。它不能证明真实游戏会接受修改后的科技点，也不能证明运行中的踢出或封禁接口在当前版本返回成功。隔离任务和数值写入任务没有在 Docker 里演练。
