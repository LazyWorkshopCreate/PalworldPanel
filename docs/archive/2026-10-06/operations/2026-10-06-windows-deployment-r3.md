# Windows 安装与服务运行手册

版本日期：2026-10-06。修订日期：2026-10-06。状态：原生发布和隔离测试通过，Inno 编译通过，SCM 演练待运行。适用范围：Windows x64 + Docker Desktop Linux containers。替代来源：[上一版](2026-10-06-windows-deployment.md)。设计见 [Windows 设计](../design/2026-10-06-windows-deployment-r3.md)，证据见 [Windows 验证](../../../verification/2026-10-06-windows-deployment.md)。

## 构建与安装

构建机需要 .NET 10 SDK、Node/pnpm 及 Inno Setup 6/7。目标机需要 Docker Desktop Linux 引擎与 Compose、管理员安装权限；默认 HTTP，无需证书。游戏容量使用 Docker VM 预算：生产每新实例至少 16 GiB，另保留 4 GiB；不足时面板拒绝创建。Windows 主机 RAM 足够不代表 Docker VM 配额足够。

```powershell
# 仓库根目录；无凭据
pwsh -NoProfile -File scripts/Build-WindowsInstaller.ps1 -Version 0.1.0 -IsccPath 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe'
# 没有 Inno 时先产生可审阅的自包含发布目录
pwsh -NoProfile -File scripts/Build-WindowsInstaller.ps1 -PublishOnly
pwsh -NoProfile -File scripts/Test-WindowsDeployment.ps1
```

产物为 artifacts/windows/publish 与 artifacts/windows/installer/PalworldPanel-0.1.0-win-x64-setup.exe。运行安装器将程序放入 Program Files/PalworldPanel，注册服务但不启动。安装包目前未签名，不宣称已签名发行；自动构建不下载/打包 Docker Desktop 或 Windows 用户凭据。

## 初始化

管理员 PowerShell 运行以下模板，替换 LAN/白名单/账户占位符；管理员密码在首次访问网站时填写；可选 PFX 密码仍通过 SecureString 交互读取，不写进脚本或仓库。HTTPS 是可选项，只有使用 `-UseHttps` 时才提供匹配监听 IP 的 PFX 证书和证书密码。

```powershell
& 'C:\Program Files\PalworldPanel\deployment\Initialize.ps1' `
  -BindIp '<LAN_BIND_IP>' -AllowedIps @('<ALLOWED_CLIENT_IP>') `
  -DockerDesktopAccount '<MACHINE\DOCKER_DESKTOP_USER>'
```

初始化建立受限 ProgramData、密钥、待设置管理员标记、独立 Docker 配置和精确管理防火墙规则，拒绝覆盖已有材料；失败只移除本次新建私有初始化文件，不清理实例。Docker CLI/Compose 安装位置不同时传 `-DockerExecutable` / `-ComposePluginDirectory`。初始化脚本采用 UTF-8 BOM 以兼容 Windows PowerShell 5.1 中文，生成 JSON 是 UTF-8 无 BOM。

## 服务启动和 Docker 验收

确认 Docker Desktop Linux 引擎已经运行。服务为 LocalSystem，不能以登录用户的 `docker ps` 当成服务账户权限验收。先手动 `Start-Service PalworldPanel`，从白名单客户端登录并验证主机、发现页面及独立测试实例；未验证前不启用既有实例写管理。Docker 不可达时核实 Linux pipe 权限与 Desktop 运行状态，禁止开放 2375/2376 或全网白名单绕过。

通过真实服务验证后，管理员设置延迟自动启动；Docker Desktop 登录/启动条件单独负责，面板自动启动不等于游戏引擎开机可用。

```powershell
Start-Service PalworldPanel
Get-Service PalworldPanel
# 服务账户 Docker 验收通过后再设置
sc.exe config PalworldPanel start= delayed-auto
```

默认访问 `http://<LAN_BIND_IP>:18080/`；`-Port` 可指定管理端口。可选 HTTPS 初始化增加 `-UseHttps -CertificateFile <TLS_PFX_PATH> -CertificatePassword <SECURE_STRING>`。HTTP 使用普通名称的 HttpOnly/SameSite 会话 Cookie，HTTPS 使用 Secure Cookie。浏览器来源仍必须匹配精确白名单；回环并未开放。帕鲁使用 Docker Linux 镜像和 LinuxServer 配置，游戏 REST/RCON 回环、公网 UDP 独立。

## 升级、卸载与回退

先等待/处理实例任务，备份世界恢复点，再运行新安装器。部署脚本拒绝排队/运行/待处理任务；停服务后将 DB/journal/密钥/配置成套备份至 ProgramData/PalworldPanel/upgrade-backups，再替换程序。升级结束保持停服，确认配置后显式启动；程序回退须匹配数据 schema，不能任意复制新旧 DB。

卸载同样先核实任务，删除面板服务、程序及 PalworldPanel-Management 防火墙规则；游戏容器、实例、秘密、面板数据库和备份保留，管理员后续按独立恢复手册处理。不会自动卸载 Docker Desktop 或全局 prune。SCM 实际安装/升级/卸载保留数据的验收本机尚未运行。

升级/卸载在停止前和停止后分别核实任务；若停止期间出现待处理任务，取消文件替换/服务删除并保持停止，管理员先处理再重试。

2026-10-06 协议与部署修订（REQ-15 / W16 / W17）：Windows 默认 HTTP、HTTPS 可选；安装前检查 Docker Linux 引擎/Compose，初始化前检查本机内网地址、精确白名单、账户和 TCP 端口；失败停止初始化。见 [ADR-007](../../../adr/007-http-and-prerequisites.md)。

安装器在替换程序和停止旧服务前检查 Docker Desktop CLI、Linux 引擎和 Compose v2。初始化脚本在注册服务或写入秘密前检查指定本机内网 IP、逐项内网 IP 白名单、Docker Desktop Windows 账户及 TCP 端口独占绑定，Docker 子进程超时为 15 秒。端口检测释放后仍可能被占用，服务启动时由 Kestrel 再次绑定；启动失败须处理占用后重试。安装器目前无自定义参数输入页，地址/端口等仍由初始化脚本提供；这些检查不证明 LocalSystem 权限或游戏容量足够。

可单独运行 `deployment/Check-Prerequisites.ps1 -BindIp <LAN_BIND_IP> -Port 18080 -AllowedIps @(<ALLOWED_CLIENT_IP>) -DockerDesktopAccount <WINDOWS_ACCOUNT>`；只检查时不注册服务或修改防火墙。自定义 Docker CLI/Compose 路径通过对应参数传入。

0.1.1 修复 32 位安装进程对 Docker Desktop 64 位路径的误判。旧 0.1.0 安装器若报告依赖检查失败但 Docker 正常，请使用 0.1.1 重试；不需要因此重装 Docker。检查脚本可在 32/64 位 Windows PowerShell 运行。

0.1.2 进一步修复 Sysnative 工作目录继承导致的 Docker 子进程启动错误。0.1.1 若仍失败，请使用 0.1.2；安装向导会显示捕获的具体错误和退出码。实际 Inno 依赖调用链可用 scripts/Test-WindowsInstallerPrerequisites.ps1 验证，探针仅检查依赖，不执行安装。

2026-10-06 首次访问设置修订（REQ-15 / W16 / W17）：本版实质替代 [上一版](2026-10-06-windows-deployment-r2.md)，Windows 初始化不再输入管理员密码；网站首次设置一次性创建 admin 密码哈希，设置后关闭入口，已有管理员和丢失文件均不能重设。配置明确开启 allowWebSetup，初始化脚本先生成待设置标记。见 [ADR-008](../../../adr/008-first-access-setup.md)。

0.1.3：完成网络/白名单/Docker 账户初始化并启动服务后，首次访问显示“设置管理员密码”，固定账号 admin，密码与确认密码须一致且为 16–128 字符。成功后进入登录页；不提供默认密码，不显示明文。重复初始化返回冲突；管理员文件丢失或损坏仍拒绝启动，不能通过删文件开启重设入口。已有完成初始化的账号升级后保留。
