# Windows 安装与服务运行手册

版本日期：2026-10-06。修订日期：2026-10-06。状态：原生发布和隔离测试通过，Inno 编译/SCM 演练待运行。适用范围：Windows x64 + Docker Desktop Linux containers。替代来源：无。设计见 [Windows 设计](../design/2026-10-06-windows-deployment.md)，证据见 [Windows 验证](../../../verification/2026-10-06-windows-deployment.md)。

## 构建与安装

构建机需要 .NET 10 SDK、Node/pnpm 及 Inno Setup 6/7。目标机需要 Docker Desktop Linux 引擎与 Compose、管理员安装权限及合适 LAN TLS 证书。游戏容量使用 Docker VM 预算：生产每新实例至少 16 GiB，另保留 4 GiB；不足时面板拒绝创建。Windows 主机 RAM 足够不代表 Docker VM 配额足够。

```powershell
# 仓库根目录；无凭据
pwsh -NoProfile -File scripts/Build-WindowsInstaller.ps1 -Version 0.1.0 -IsccPath 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe'
# 没有 Inno 时先产生可审阅的自包含发布目录
pwsh -NoProfile -File scripts/Build-WindowsInstaller.ps1 -PublishOnly
pwsh -NoProfile -File scripts/Test-WindowsDeployment.ps1
```

产物为 artifacts/windows/publish 与 artifacts/windows/installer/PalworldPanel-0.1.0-win-x64-setup.exe。运行安装器将程序放入 Program Files/PalworldPanel，注册服务但不启动。安装包目前未签名，不宣称已签名发行；自动构建不下载/打包 Docker Desktop 或 Windows 用户凭据。

## 初始化

管理员 PowerShell 运行以下模板，替换 LAN/白名单/证书路径/账户占位符；密码交互读取，不写进脚本或仓库。证书须匹配绑定 IP 并被客户端信任，不使用开发证书做生产 TLS。

```powershell
$adminSecret = Read-Host '设置面板管理员密码' -AsSecureString
$tlsSecret = Read-Host '输入 PFX 密码' -AsSecureString
& 'C:\Program Files\PalworldPanel\deployment\Initialize.ps1' `
  -BindIp '<LAN_BIND_IP>' -AllowedIps @('<ALLOWED_CLIENT_IP>') `
  -CertificateFile '<TLS_PFX_PATH>' -CertificatePassword $tlsSecret `
  -AdministratorPassword $adminSecret -DockerDesktopAccount '<MACHINE\DOCKER_DESKTOP_USER>'
```

初始化建立受限 ProgramData、密钥、管理员、独立 Docker 配置和精确管理防火墙规则，拒绝覆盖已有材料；失败只移除本次新建私有初始化文件，不清理实例。Docker CLI/Compose 安装位置不同时传 `-DockerExecutable` / `-ComposePluginDirectory`。初始化脚本采用 UTF-8 BOM 以兼容 Windows PowerShell 5.1 中文，生成 JSON 是 UTF-8 无 BOM。

## 服务启动和 Docker 验收

确认 Docker Desktop Linux 引擎已经运行。服务为 LocalSystem，不能以登录用户的 `docker ps` 当成服务账户权限验收。先手动 `Start-Service PalworldPanel`，从白名单客户端登录并验证主机、发现页面及独立测试实例；未验证前不启用既有实例写管理。Docker 不可达时核实 Linux pipe 权限与 Desktop 运行状态，禁止开放 2375/2376 或全网白名单绕过。

通过真实服务验证后，管理员设置延迟自动启动；Docker Desktop 登录/启动条件单独负责，面板自动启动不等于游戏引擎开机可用。

```powershell
Start-Service PalworldPanel
Get-Service PalworldPanel
# 服务账户 Docker 验收通过后再设置
sc.exe config PalworldPanel start= delayed-auto
```

访问 `https://<LAN_BIND_IP>:18080/`，Windows 生产不沿用 Docker 隔离演示 HTTP 开关。浏览器来源仍必须匹配精确白名单；回环并未开放。帕鲁使用 Docker Linux 镜像和 LinuxServer 配置，游戏 REST/RCON 回环、公网 UDP 独立。

## 升级、卸载与回退

先等待/处理实例任务，备份世界恢复点，再运行新安装器。部署脚本拒绝排队/运行/待处理任务；停服务后将 DB/journal/密钥/配置成套备份至 ProgramData/PalworldPanel/upgrade-backups，再替换程序。升级结束保持停服，确认配置后显式启动；程序回退须匹配数据 schema，不能任意复制新旧 DB。

卸载同样先核实任务，删除面板服务、程序及 PalworldPanel-Management 防火墙规则；游戏容器、实例、秘密、面板数据库和备份保留，管理员后续按独立恢复手册处理。不会自动卸载 Docker Desktop 或全局 prune。SCM 实际安装/升级/卸载保留数据的验收本机尚未运行。

升级/卸载在停止前和停止后分别核实任务；若停止期间出现待处理任务，取消文件替换/服务删除并保持停止，管理员先处理再重试。
