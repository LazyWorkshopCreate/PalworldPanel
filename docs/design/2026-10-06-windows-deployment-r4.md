# Windows 部署设计

版本日期：2026-10-06。修订日期：2026-10-06。状态：实现及当前用户隔离验证完成；Inno 编译通过，SCM/安装器完整演练待运行。适用范围：Windows x64、Docker Desktop Linux 引擎、单管理员。替代来源：[上一版](../archive/2026-10-06/design/2026-10-06-windows-deployment.md)；是现行系统设计的 Windows 部署扩展。决策见 [ADR-005](../adr/005-windows-service.md)，对应 REQ-15 / W16 / W17。

## 程序、权限和配置

后端与同源静态前端发布为 self-contained win-x64，目标机不需要 .NET/Node。SCM 的服务名为 PalworldPanel；安装器注册 LocalSystem 手动启动服务，命令明确使用程序绝对路径和 `--config`，ContentRoot 为程序目录，不使用 System32 工作目录寻找静态资源。服务停止/取消沿用现有持久执行器；不能把强制终止视为任务已回退。

Program Files 只放程序和部署脚本；ProgramData/PalworldPanel 下 private、state、instances、backups、docker、upgrade-backups 分离。初始化使用受保护 ACL，仅 SYSTEM/管理员访问；为指定 Docker Desktop 用户单独授予 instances 修改权限，供 Linux UID1000 文件共享。该用户不获得面板 private/state/backups 访问权限。游戏密钥不进入安装包，初始化密码通过 SecureString 和短生命周期子进程环境传递，不进入命令行/日志。

CLI 使用存在的绝对路径；DockerEndpoint 限本机 Linux pipe `npipe:////./pipe/dockerDesktopLinuxEngine` 或 Linux socket，拒绝 TCP/其他引擎；独立 DOCKER_CONFIG 仅声明安装目录下 Compose 插件，不复制交互用户凭据。Docker context/TLS 环境不覆盖显式 endpoint。Windows 服务账户的管道权限必须在真实服务下验收，不能用当前用户 CLI 成功代替。

## 游戏、路径和指标

游戏继续使用批准的 Linux 镜像，实例数据采用本机 NTFS drive bind，容器目标 `/palworld` 和 LinuxServer 配置路径不变。Docker Desktop inspect 的 `/run/desktop/mnt/host/<盘符>`、`/host_mnt/<盘符>` 以及 Windows drive 路径统一映射，再验证批准根目录；拒绝 UNC、路径穿越、ADS、Linux volume 以及未知路径。无法核实的实例继续只读，不自动迁移 Linux 命名卷。

Windows TCP/UDP listeners 参与端口预检；容器 UDP 检查仍读取容器 Linux socket。Windows 宿主用 GetSystemTimes / GlobalMemoryStatusEx 获取实际 CPU/物理内存，Docker VM 的 MemTotal/NCPU 仅用于游戏容量预算，两者不混同。初次 CPU 尚无增量时仍未知。NTFS 无 chmod/chown，文件共享权限由 Windows ACL 和 Desktop 用户控制；不能以 Windows 原生测试通过推定 Linux 文件模式或跨 build 恢复兼容。

## 安装、升级、卸载

Inno 安装需管理员；注册失败显示失败，不启动游戏或删除数据。首次安装白名单默认仅填本机绑定内网 IP，管理员可编辑加入精确客户端 IP，不自动放行整个内网。默认 HTTP，HTTPS 证书可选，配置完成后先验证服务账户 Docker 及批准目录，再启用服务。防火墙仅对管理 TCP、指定绑定 LAN、精确来源 IP、Private/Domain 生效；游戏 REST/RCON 保持回环，公网 UDP 不在范围。

升级拒绝活跃任务和同名不同路径服务；停止确认后复制 private/state/docker 到受限 upgrade-backups，程序替换后服务保留停止，管理员核对再启动。卸载同样拒绝活跃任务，只删除面板服务、程序及面板规则，容器与 ProgramData 保留；游戏持续运行与否由原容器策略决定，卸载不擅自停止它们。备份不包含游戏世界，世界仍用原恢复点流程。

Docker Desktop WSL2 引擎通常依赖 Desktop 登录/启动，不与 com.docker.service 或面板服务混同。本设计不承诺 Windows 无人登录时的整机游戏自动恢复；要求该行为时需另行验证受支持引擎运维方案。

升级/卸载在停止前和停止后分别核实任务；若停止期间出现待处理任务，取消文件替换/服务删除并保持停止，管理员先处理再重试。

2026-10-06 协议与部署修订（REQ-15 / W16 / W17）：Windows 默认 HTTP、HTTPS 可选；安装前检查 Docker Linux 引擎/Compose，初始化前检查本机内网地址、精确白名单、账户和 TCP 端口；失败停止初始化。见 [ADR-007](../adr/007-http-and-prerequisites.md)。

HTTP 配置使用 `allowHttp=true`，TLS 文件可为空；不使用放宽容量的 desktopValidation 开关。Windows 无证书时会话保护密钥使用本机 DPAPI，仍持久化到受限状态目录；面板灾备不读取不存在的 TLS 文件，已有证书材料仍导出。跨机恢复不保证保留原会话，管理员重新登录。

2026-10-06 首次访问设置修订（REQ-15 / W16 / W17）：本版实质替代 [上一版](../archive/2026-10-06/design/2026-10-06-windows-deployment-r2.md)，Windows 初始化不再输入管理员密码；网站首次设置一次性创建 admin 密码哈希，设置后关闭入口，已有管理员和丢失文件均不能重设。配置明确开启 allowWebSetup，初始化脚本先生成待设置标记。见 [ADR-008](../adr/008-first-access-setup.md)。

2026-10-06 安装升级修订（REQ-15 / W17）：本版实质替代 [上一版](../archive/2026-10-06/design/2026-10-06-windows-deployment-r3.md)，安装向导提供重新配置和仅升级程序；完整程序先暂存，再整体交换目录。重新配置保留管理员密码、密钥、数据库和实例；仅升级不写配置。见 [ADR-009](../adr/009-directory-upgrade.md)。

2026-10-06 参数初值补充（REQ-15 / W17，0.1.7）：默认监听本机有效内网 IPv4，优先有默认网关的接口；端口默认 18080，白名单默认该单个本机 IP，账户默认当前 Windows 用户。重新配置只读取现有网络三字段作为初值，失效监听地址重新检测；不导出秘密。全部可编辑，检测不到内网地址时仍拒绝空值，不能回退到公开监听。中文部署错误用 UTF-8 文件传递，安装器显式解码，避免依赖控制台代码页。

2026-10-06 本机回环入口补充（REQ-15 / W17，0.1.8）：Windows 原生服务默认同时监听配置 LAN IPv4 和 127.0.0.1 的相同管理端口，两个入口使用相同 HTTP/HTTPS 策略。回环来源 127.0.0.1 固定允许，不要求写入内网白名单；局域网来源仍逐项匹配白名单。回环不豁免登录、同源写入、CSRF 或危险操作认证，不监听任意地址。安装预检同时检查 LAN 与回环端口占用。Linux 容器监听策略保持现行设计。

2026-10-06 目录交换可靠性修复（REQ-15 / W17，0.1.10）：Windows 程序目录交换使用 System.IO.Directory.Move 的整目录重命名，禁止使用可能降级逐项迁移的 PowerShell Move-Item；失败保留完整原目录。交换前验证 self-contained 的主程序集、配置/依赖清单与 hostpolicy/hostfxr/coreclr，SCM 停止后额外等待原服务进程退出。回退旧目录失败时将新目录移回活动位置，保留日志，不做半套逐文件恢复。

2026-10-06 本机浏览器标注兼容（REQ-15 / W17，0.1.11）：仅 Windows 精确 127.0.0.1 来源的响应增加 style-src-elem self/unsafe-inline，允许桌面标注工具注入没有应用 nonce 的样式块。其他样式与脚本规则继续按原 CSP，LAN/Linux 继续限制样式 nonce；认证、同源写入和精确来源策略继续执行，不通过允许任意脚本或解除 CSP 来兼容标注。
