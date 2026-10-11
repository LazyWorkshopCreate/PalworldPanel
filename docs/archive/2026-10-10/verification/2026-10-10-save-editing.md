# 验证记录：存档编辑

文件名：`2026-10-10-save-editing.md`。

日期：2026-10-10。状态：Passed。关联：REQ-22、W26、ADR-010。

## 范围与环境

Windows 本机仓库。测试使用合成的 zlib 存档，不包含真实玩家文件。未启动面板，未连接 Docker 游戏实例，未读取生产存档。

## 检查与结果

| 检查 | 命令或操作 | 预期 | 实际 | 状态 |
|---|---|---|---|---|
| 后端测试 | `dotnet test tests/PalworldPanel.UnitTests/PalworldPanel.UnitTests.csproj -c Release --nologo` | 全部通过 | 177 通过，0 失败 | Passed |
| 容器往返 | `ZlibSaveRoundTripsAndDoesNotBecomeWritable` | 一次和两次 zlib 解压后与原文一致，且不可改写内容 | 通过 | Passed |
| 当前压缩与坏长度 | `CurrentCompressionAndBrokenLengthsStayClosed` | `PlM` 和错误长度不能解码 | 通过 | Passed |
| 玩家文件移动 | `PlayerFileMovesOnlyAfterBackupAndWorldBytesStay` | 备份回调先发生；玩家文件移出；世界文件摘要不变；非法编号拒绝 | 通过 | Passed |
| 前端 | `pnpm test` 与 `pnpm build` | 测试和 TypeScript 构建通过 | Vitest 18 个文件 103 项通过；Vite 构建完成 | Passed |
| 存档页 | `列出存档并只把可隔离的玩家编号交给确认` | 显示新角色风险，只提交玩家编号 | 通过 | Passed |
| 页签地址 | 路由测试包含 `saves` | 深链接可解析 | 通过 | Passed |

`pwsh -NoProfile -File scripts/Test-Documentation.ps1` 报告 119 篇文档通过。该检查不证明存档可以被游戏读取。

## 副作用与清理

测试在系统临时目录创建合成文件，结束时删除目录。没有游戏进程或真实存档被修改。

## 结论与限制

本记录证明合成 zlib 容器可以识别和往返，玩家文件只会在备份回调之后移动，世界文件字节保持不变，存档页能把玩家编号送入确认。它不能证明真实游戏读取 `PlM` 存档后的表现，也不能证明隔离任务在 Docker 中完成备份后游戏会如何生成新角色。运行中的实例拒绝编辑只存在于任务代码，这次没有启动容器来演练。
