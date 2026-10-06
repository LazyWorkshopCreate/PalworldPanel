# 本机运行与恢复手册

日期：2026-10-06。适用：仓库当前 .NET 10 / React 单镜像实现、Docker Desktop Linux containers。只说明隔离本机测试；生产地址、证书、白名单、目录和维护窗口仍需另行输入与授权。实际结果见[验证记录](../verification/2026-10-06-desktop-implementation.md)。

## 构建和隔离启动

在仓库根目录运行 `pwsh -NoProfile -File scripts/Test-LocalBuild.ps1`。它检查文档、锁定依赖、后端测试、前端格式/测试/构建，构建并运行 Docker 后端测试镜像，最后构建 `palworldpanel:local`。不连接生产，也不创建游戏实例。

随后运行 `pwsh -NoProfile -File scripts/Start-DesktopValidation.ps1`，创建项目专用网络、卷、随机测试管理员、主密钥和自签证书。脚本使用 `desktop_wait.py` 有界等待实际 HTTPS 响应，成功后才报告就绪。重复初始化拒绝覆盖已有材料；失败时先检查资源再按清理流程处理。临时凭据只存系统临时目录及测试卷，不能粘贴进文档或仓库。测试入口直接绑定 Linux VM 网桥的 `172.30.88.1:18080`，只允许两个精确测试源 IP。测试客户端显式忽略自签信任，仅限此夹具。

`DesktopValidation=true` 是明确的测试配置：缩小内存预算、游戏 UDP 发布到 Windows 回环。它不能用于评估生产容量。常规配置要求至少 16 GiB 实例内存、4 GiB 系统预留，公网和整个内网均不会自动放行。测试专用浏览器 TCP 桥接只能证明页面可用，不能证明原浏览器来源的白名单校验。

故障/接管演练使用启动选项 `-EnableFaultFixtures`，额外挂载测试客户端的 Docker socket/CLI；`-IncludeComparisonGame` 建立独立对照游戏；`-StartBrowser` 开启 `https://127.0.0.1:18080` 测试桥接。管理员测试文件仅在系统临时目录 `palworldpanel-desktop-validation/panel/test-password`，不用后清理，不复制到仓库 trace、文档或公开日志。

面板要求 Linux、root、Docker socket/CLI/Compose、系统 SQLite、受限秘密文件及 HTTPS；源码里的 HTTP 开发启动配置不是运行入口。配置文件位置由 `PANEL_CONFIG_FILE` 明确指定。实例、状态、备份根目录必须绝对、独立、无链接。Docker 宿主与面板容器路径不同则配置 `DockerHostRoot` / `ContainerMountRoot` 映射，游戏目录依旧按实例隔离。生产配置不由测试脚本生成。

## 场景脚本

用户要求本机页面使用 HTTP 时，启动附加 `-Http -StartBrowser`，入口为 `http://127.0.0.1:18080/`。`DesktopAllowHttp` 仅允许专用 Desktop 测试绑定及两个精确测试源；本机桥接仍只发布到 Windows 回环，登录、HttpOnly/SameSite 和 Origin/CSRF 保留，测试 Cookie 不使用 Secure。证书仍用于会话密钥保护和面板灾备。生产配置拒绝此开关，默认测试及生产继续使用 HTTPS。

Python 场景通过白名单客户端执行，读取测试卷中的随机凭据，不打印响应秘密。`desktop_access.py`、`desktop_lifecycle.py`、`desktop_operations.py`、`desktop_configuration.py`、`desktop_recovery.py`、`desktop_mutations.py`、`desktop_export.py`、`desktop_executor_crash.py`、`desktop_adoption.py`、`desktop_clone.py` 位于 `tests/`。先建立全新实例，再按上述顺序演练；中断演练必须早于取消接管/重新登记，因为后者会更换面板身份。完整真实游戏测试需要下载游戏文件，并预留安装、完整恢复点和 staging 空间。

执行器中断与接管夹具额外使用测试客户端的 Docker CLI/socket，只操作精确命名的合成容器。`desktop_executor_crash.py` 在明确升级阶段终止测试面板，验证重启后进入 NeedsAttention、禁止自动重跑、其他实例不重启，再手动回退。它不能默认用于真实服务器。

启动脚本已把 `desktop_*.py` 复制至客户端 `/tmp`，例如 `docker exec palworldpanel-validation-files python /tmp/desktop_access.py`，随后 `desktop_lifecycle.py` 创建真实世界。增加的场景为 `desktop_creation_preview.py`（预检负例）、`desktop_save_failure.py`（保存超时）、`desktop_schedule.py`（调度）、`desktop_final_regression.py`（配置/小写 GUID）、`desktop_events_logs.py`（SSE/日志）、`desktop_final_purge.py`（合成到期清空）、`desktop_disaster_restore.py`（灾备实际启动）。清空克隆在调度/另服隔离之后，灾备在没有执行中任务时。`desktop_unknown_readonly.py` 仅适用于初始 VM 绑定失败夹具，不要求正常初始化制造此失败。重复整套场景需清理并重新初始化，不在未知现场连续新建。

拒绝源验证使用专用网络 .11 的独立临时 Python 客户端执行 `desktop_access.py denied`，标记 `com.palworldpanel.test=panel`；白名单客户端伪造头不能代替真实源。浏览器 trace/认证状态置于系统临时目录；公开截图仅含合成名称/指标。

已有容器默认只读。可写接管目前只支持已验证的单服务 JSON 形式 Compose、批准 digest、两个 `format: raw` env 文件、固定资源/端口/绑定挂载；普通 YAML、锚点、额外覆盖、未知命令或未核实资源保持只读。登记无需自定义标签。接管须由管理员确认外部备份、更新和重启任务停用；不自动扫描或修改外部 cron。写接管先保存、停止、独立备份，保留世界和未知安全配置键。

## 配置与任务

编辑只保存期望草稿；应用任务才写模板、保存/停服/备份并验证实际规则与 Docker 限额。页面分别展示期望、已应用和 REST 观测值。名称、描述、密码经 INI 内容转义后进入 raw env 文件，`$` 不被 Compose 展开。密码仅写入，轮换前保存仍使用已生效的管理凭据。

每实例只有一个未结束变更任务；持久队列、实例锁和单执行器锁协调重启。重复幂等请求返回原任务。Queued 可以取消，Running 不能强行取消。危险操作要求最近 5 分钟认证及绑定修订的单次预览令牌；认证过期需退出并重新登录。

新建先调用 `/api/v1/creation-previews` 显示实际目录、端口、资源和备份时段，不创建目录。确认 `/api/v1/instances` 带原 plan/token/hash 与幂等键；提交再次持锁检查预算、端口、克隆源修订，有变化 409 并重新预检。主机页 Linux 实际 CPU/内存和配置承诺分别展示，Docker Desktop 下实测对象为 VM。

保存失败或超时不自动停止；结果未知必须人工核实。显式强制停止跳过保存且可能丢失进度。实例停止后设置 restart=no；仅自动验证通过后才恢复正常重启策略。导入、恢复和升级的自动校验通过后仍为 PlayerVerificationPending，真实原玩家核验才可接受；本机合成测试不填“玩家已恢复”。

面板显示 Docker UDP 映射、游戏容器/宿主 Linux 套接字及采样时间。转发实现可能不创建宿主套接字，“未观测到”不等于映射失效。真实 LAN 客户端入服是独立证据；不提供公网网络诊断。

## 备份和世界回退

手动/每日调度执行保存、停止、完整离线快照，清单包括 SHA-256、世界、游戏 build、镜像和一致性等级。默认北京时间 05:00 / 05:15、14 天；错过时段当天补排一次，幂等键避免重复；失败不自动重试。最新可信恢复点、变更前恢复点和活动任务引用不参与普通轮转。

上传 ZIP 先验证大小、实际展开量、CRC、路径、链接、碰撞和世界结构；多世界明确选择，来源 Config/Logs/Crashes 与世界内嵌备份不导入。限制为 4 GiB 上传、20 GiB 展开、100,000 条目、8 GiB 单文件和 200:1 压缩比。备份和 staging 预检空间；不因上传成功就停服。未被活动任务引用的上传 24 小时后按小时清理。

导入/恢复使用同文件系统交换及 durable journal。中断后保留恢复点，停止自动执行。显式回退保留失败现场，恢复后保持停止，管理员重新启动核实。升级区分镜像与游戏：固定批准 digest，关闭日常自动更新；升级回退需要同一次恢复点中的旧安装、旧配置和旧 Saved，不能仅换镜像。游戏跨版本兼容仍须真实玩家验证。

“取消接管”只删除面板管理记录；“移除容器保留数据”保存、停服、备份后移除服务；“永久清理”仅限项目创建目录，输入完整实例名确认，先独立最终备份，再隔离至少 7 天。到期仍需明确清空任务，最终备份不随目录删除且继续保护。隔离期间可撤销；既有实例永久清理禁用。取消接管后恢复点仍可在主机页加密导出。

## 加密导出和面板灾备

世界恢复点与面板灾备分别导出。使用至少 16 字符的独立导出口令，流式加密并通过短期单次下载令牌传输，口令不放 URL。`Prepared` 审计只表示加密文件准备完成，不证明独立介质保存成功。

离线解密使用 `PANEL_EXPORT_PASSPHRASE` 环境变量以及 `PalworldPanel.Server --decrypt-export <加密输入绝对路径> <新 ZIP 输出绝对路径>`。CLI 不覆盖文件，验证全部帧及终止认证后才提交输出；错误口令、篡改或截断拒绝。在受限目录操作，不把解密 ZIP、密码或密钥放进仓库。完整安装包保留 Unix 执行权限，恢复后再次核实文件所有者与运行用户。

面板灾备包含 Online Backup API 的 SQLite 一致快照、管理员初始化文件、主密钥、会话密钥、证书/证书口令、选项和 journal；不包含完整游戏世界。恢复前停止面板，保留当前 DB/密钥/journal 备份，在隔离目录解密并检查清单、`PRAGMA integrity_check`、schema 和配套密钥。使用 CLI `--quarantine-pending-tasks <恢复数据库绝对路径>` 将旧 Queued/Running 变为 NeedsAttention，再启动执行器；不得自动重放旧破坏任务。游戏世界、配置和安装来自对应的独立世界恢复点，逐实例核对路径、GUID、build、SourceHash 和端口。先隔离演练，再考虑实际维护窗口。当前 schema=1，不支持直接打开更高版本数据库。

## 清理测试资源

先运行 `pwsh -NoProfile -File scripts/Stop-DesktopValidation.ps1 -WhatIf` 查看范围，再运行同脚本清理。脚本按测试标签、明确卷及受限挂载路径核实归属，移除合成测试容器、卷、网络和系统临时私有材料；不执行全局 prune，保留构建镜像和共享缓存。未识别的同名资源、重解析目录或占用卷会拒绝清理，不扩大范围。

本机真实实例和合成世界证据可以验证程序边界及恢复流程，不能替代生产防火墙、宿主掉电、生产峰值容量、真实玩家角色/背包/据点或 LAN UDP 入服验收。

## 完整参数设置与隔离验证（2026-10-06）

参数页可按中文名或原字段搜索，修改后先保存草稿，再显式应用；应用沿用停服备份与失败回退。扩展参数使实例使用面板维护的 INI，镜像不再启动时重生成该文件；升级前核对目标游戏版本对字段的支持，不能将可编辑当作已验证兼容。手工改动配置会触发漂移检查。恢复默认只修改选定草稿；未知默认不提供复位。密码不回读，REST/RCON 监听由面板管理。

`tests/desktop_all_settings.py` 提供 prepare / run / cleanup 隔离 API 场景：需要本机测试卷中已有一个合成实例，用 SQLite 在线备份复制测试 DB，再在独立根目录和 18082 端口运行面板；从私有测试文件读取认证材料，不输出凭据。验证类型/托管字段拒绝、扩展参数保存与回读、旧修订冲突及原实例记录不变，不启动游戏、不应用世界配置。临时面板停止并移除后再执行 cleanup，只清理 `/test/settings-integration`。此脚本不作为生产检查运行。
