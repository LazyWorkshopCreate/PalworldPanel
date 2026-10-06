# ADR-005：Windows 原生服务与 Inno 安装包

日期：2026-10-06。状态：已采纳；扩展 ADR-002 的部署平台，Linux 容器模式继续有效。Windows 发布及当前用户隔离测试已完成，安装器编译已完成，管理员 SCM 演练尚未完成。关联 REQ-15、W16、W17。

## 决策

Windows x64 上面板以 .NET 自包含程序运行，通过 Microsoft.Extensions.Hosting.WindowsServices 接入 SCM，Inno Setup 6/7 提供管理员安装与服务注册。游戏继续使用 Docker Desktop Linux 容器，不提供 Windows 原生游戏进程或远程 Docker。Windows 对应 Linux root 的服务账户为 LocalSystem，Docker 管理权限仍等价于高权限主机管理，来源白名单、认证及 CSRF 不变。

程序位于 Program Files，运行配置、密钥、SQLite、实例和备份位于 ProgramData。安装只注册手动启动服务，不生成未知 LAN 地址/允许客户端的生产配置；初始化脚本由管理员明确提供地址、精确白名单、可选 TLS 及 Docker Desktop 用户，管理员密码改由首次网站访问设置（ADR-008）。发布显式设置 CLI、Linux named pipe 和独立 Docker 配置，避免依赖交互用户 context；Docker API 不开放 TCP。

升级先拒绝排队/运行/待处理任务，停服务后备份面板状态和配套密钥，再替换程序；升级后保留停止状态。卸载只移除面板程序、服务和面板命名防火墙规则，保留游戏容器、实例目录、世界及备份。安装失败不自动清理世界。Docker Desktop 的登录用户/引擎生命周期与面板 SCM 独立，不能承诺无人登录时游戏自动启动。

## 取舍与证据

采用原生 Windows 服务复用现有后端，避免增加 NSSM/WinSW 包装进程及另一个控制平面。NTFS 和 Docker Desktop 文件共享需要实例目录明确授权给指定 Desktop 用户；该用户可写实例文件，但不授予面板密钥/数据库目录访问。网络或版本异常仍按原实例任务/人工恢复机制处理。

来源：[微软 Windows 服务托管](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/windows-service?view=aspnetcore-10.0)、[Docker Windows 权限](https://docs.docker.com/desktop/setup/install/windows-permission-requirements/)、[Inno 安装/卸载执行](https://jrsoftware.org/ishelp/topic_runsection.htm)。实施范围见 [Windows 设计](../design/2026-10-06-windows-deployment-r4.md)，实际结果见 [Windows 验证](../verification/2026-10-06-windows-deployment.md)。

升级/卸载在停止前和停止后分别核实任务；若停止期间出现待处理任务，取消文件替换/服务删除并保持停止，管理员先处理再重试。

初始化的强制 TLS 要求由 [ADR-007](007-http-and-prerequisites.md) 修订为 Windows 默认 HTTP，安装/初始化增加依赖与端口检查。
