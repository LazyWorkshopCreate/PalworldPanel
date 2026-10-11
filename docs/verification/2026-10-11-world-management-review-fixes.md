# 验证记录：世界管理审查修复

版本日期：2026-10-11。修订日期：2026-10-11。状态：Passed（本机自动化检查）。适用范围：REQ-23 / W27.4。替代来源：无；补充[历史世界管理验收](2026-10-10-world-management.md)，不替代其中的真实游戏未验证边界。

## 范围与修复

rc.2 后提交审查发现五处问题：连接控制未持有实例锁、切换实例沿用旧玩家状态、存档错误遮蔽玩家列表、游戏 API 连接异常未降级、世界页不自动更新。本次在 Windows 本机修复，不连接生产、真实游戏 REST 或玩家存档，不创建或修改游戏实例。

## 检查与结果

| 检查 | 命令或场景 | 实际 | 状态 |
|---|---|---|---|
| 连接控制互斥 | `WorldManagementTests`：持有 OS 锁或存在恢复任务时直接调用真实 moderation 处理器 | 两条路径拒绝 TaskConflict；未触发 Docker；冲突结束后校验正常且锁已释放 | Passed |
| 游戏接口降级 | 合成连接拒绝、超时、业务错误和请求取消 | 三类接口失败保留历史及封禁状态；请求取消继续抛出 | Passed |
| 实例切换 | `WorldAccess.test.tsx`：A 玩家确认弹窗切换至 B，B 读取失败；A 响应迟到 | 清除旧玩家和弹窗；取消旧请求；不向 B 提交 A 玩家操作；忽略迟到响应 | Passed |
| 独立读取 | 存档请求失败而玩家请求成功 | 仍显示玩家并能提交封禁；单独提示存档失败 | Passed |
| 存档容器损坏 | 合成损坏的世界与玩家 PlZ 存档 | 库存解压失败，但玩家文件名枚举正常 | Passed |
| 周期采样 | 假时钟推进五秒、模拟上线、读取失败和恢复 | 更新在线状态；失败显示过期并禁用操作；后续恢复 | Passed |
| 完整本机构建 | `pwsh -NoProfile -File scripts/Test-LocalBuild.ps1 -SkipDocker` | 文档 132 篇、Release 后端 186 项、前端 18 个文件 107 项通过；冻结依赖、Prettier、TypeScript 和 Vite 生产构建通过 | Passed |
| 后端最终检查 | `dotnet test tests/PalworldPanel.UnitTests/PalworldPanel.UnitTests.csproj --no-restore -c Release` | 补充存档文件名枚举与损坏容器测试后，187 项通过，0 失败 | Passed |
| 差异空白检查 | `git diff --check` | 通过 | Passed |

## 限制与副作用

后端使用系统临时目录、合成密钥、任务和玩家记录，结束清理。互斥测试指定不存在的 Docker 可执行文件，保证拒绝路径不会访问真实引擎。前端为组件测试，不证明真实浏览器端到端流程。未重新构建 Docker 镜像、未部署或执行真实踢出/封禁与玩家入服验收。
