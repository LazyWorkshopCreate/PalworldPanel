# Windows 部署验证记录

版本日期：2026-10-06。修订日期：2026-10-06。状态：原生运行与 Inno 编译 Passed；管理员 SCM 演练 Not Run。适用范围：本机隔离 Windows 进程与 Docker Desktop Linux 引擎。替代来源：无。关联 REQ-15 / W16 / W17。

## 已运行

- `scripts/Build-WindowsInstaller.ps1 -PublishOnly`：self-contained win-x64、静态前端产物生成；不要求目标机装 .NET/Node。
- Windows 后端 72 项 Passed；Docker Linux 后端 72 项 Passed。新增 drive/mount 映射、未知/UNC/穿越/ADS 拒绝、Windows 物理内存、非本机 Linux endpoint 拒绝测试。
- `scripts/Test-WindowsDeployment.ps1` Passed：独立 LAN HTTPS 18090、静态资源、当前用户登录、显式 Linux named pipe、Windows 实际 CPU/内存与 Docker VM 容量、发现实际 NTFS bind、Linux UID1000 写 Windows 文件。程序从非安装工作目录运行，验证 ContentRoot 不依赖当前目录。
- 原生测试自签证书/随机凭据仅存在受限临时目录；API 不输出凭据。临时 Python Linux 容器只执行合成文件写入，未启动游戏、未创建世界或操作当前面板实例。容器/私有目录已清理，现有演示服务保留。
- Windows 部署 PowerShell 脚本语法检查与前端生产构建 Passed；文档结构检查范围以脚本输出为准。

首次原生夹具使用非 digest 测试镜像作为默认配置，被批准镜像校验拒绝；改为现有批准 digest 后隔离场景通过。辅助 Python 容器不被声明为可写游戏模板，不放宽生产镜像约束。

## 历史阻塞与尚未运行项目

本机未检测到 Inno 编译器；下载并静默安装官方签名编译器的命令被自动审批拒绝，返回 `blocked by policy`。未改用其他方式绕过。因此 `.iss` 尚未实际编译，无 setup.exe 交付，安装器静态审阅不能当成编译成功。

当前会话非管理员；没有注册、启动、停止或卸载本机 Windows 服务，没有修改主机管理防火墙。LocalSystem 的 Docker pipe/文件共享、真实 SCM 服务生命周期、UAC 安装、升级快照与卸载保留数据均 Not Run。当前用户原生进程成功不证明 LocalSystem 成功。

未执行原生 Windows 面板的真实游戏创建、备份/恢复、升级/回退、玩家加入与 NTFS 长期 IO 压力；既有 Linux 游戏演练不自动覆盖这些。Docker Desktop 无用户登录/冷启动、跨 build 游戏兼容、正式证书和生产防火墙亦 Not Run。

后续滚动布局构建发现 Windows RID 发布会改写基础 NuGet 锁，导致 Linux locked-mode 构建拒绝；恢复基础锁后重建通过。Windows 发布改用 artifacts/windows/packages.win-x64.lock.json，实际重复发布前后基础锁 hash 不变，Linux 演示镜像与最新版 UI 已更新。

## 2026-10-06 默认 HTTP 与前置检查

关联 REQ-15 / W16 / W17 / ADR-007。本次已运行：后端 75 项 Passed；Windows 无 TLS 文件的原生发布及 HTTP 静态资源/登录 Passed，HttpOnly 非 Secure Cookie 验证 Passed；Linux pipe、宿主/VM 监控与 UID1000 NTFS 写盘 Passed。无证书时不加载 TLS 文件，Windows 使用本机 DPAPI 保护会话密钥。

`scripts/Test-WindowsPrerequisites.ps1` Passed：实际 Docker Desktop Linux 引擎/Compose、当前 Windows 账户、空闲 TCP 端口；真实占用端口、缺少 Docker CLI、非法白名单均拒绝。PowerShell 语法检查 Passed。测试仅临时原生进程和合成 Python 容器，没有注册服务、修改防火墙或运行游戏。

Inno 增加替换文件前的依赖检查，初始化默认 HTTP 并增加地址/账户/端口检查；Inno 编译、UAC 向导、管理员初始化、LocalSystem 权限仍 Not Run。当前检查未覆盖引擎超时、缺失 Compose 和 Docker Windows 容器模式的实际故障注入；源码有对应拒绝分支，未报告这些场景已测试。管理端口检查只对当时有效，实际启动仍由 Kestrel 独占绑定。当前 Docker 演示面板镜像未因 Windows 部署变更重新替换。

无证书 HTTP 的单元验证额外覆盖加密面板灾备导出与解密，确认只包含管理员/主密钥而不读取 TLS 文件。该测试发现 Windows 数据库快照目标的连接池持有文件，导致临时目录无法清理；快照目标改为禁用连接池，关闭后立即释放文件。修复后 75 项通过。

## 2026-10-06 安装包编译

用户已安装 Inno Setup 7.1.0，实际路径 `C:/Program Files/Inno Setup 7/ISCC.exe`。此前编译器缺失的阻塞已解除，下载/自动安装被拒绝的历史记录仍保留。

`scripts/Build-WindowsInstaller.ps1 -IsccPath <ISCC_PATH>` Passed：前端构建、自包含 win-x64 发布、ISS/Pascal Script 编译和 LZMA2 安装包生成通过。产物 `artifacts/windows/installer/PalworldPanel-0.1.0-win-x64-setup.exe`；安装包未签名。构建脚本增加本地 .tools 和 Program Files 下 Inno 7/6 的自动发现。

未运行安装器，不以编译通过代替 UAC、管理员初始化、SCM 服务账户权限、升级/卸载或游戏验收。安装器运行时的依赖检查仍由实际目标机执行。

## 2026-10-06 安装依赖检查路径修复（0.1.1）

用户本机 0.1.0 安装器报依赖检查失败。只读复核显示 Docker Linux 引擎可达，Compose 5.3.1 正常；32 位 Windows PowerShell 执行原脚本重现“未找到 Docker Desktop CLI”，而 64 位执行通过。原默认路径依赖 ProgramFiles 环境变量，32 位进程将其指向 Program Files (x86)，错误定位 64 位 Docker Desktop。

Check-Prerequisites/Initialize 的安装路径默认改用 ProgramW6432（缺失时回退 ProgramFiles）。安装器运行检查与服务脚本时优先选择 Sysnative 下的原生 PowerShell（不可访问时使用 System32），并向依赖检查传入安装模式对应的绝对 Docker/Compose 路径。

修复后实际 32 位及 64 位 Windows PowerShell 检查均 Passed，端口占用/缺失 CLI/非法白名单回归 Passed。安装包重新编译为 0.1.1；实际 UAC 向导继续安装、SCM 注册与 LocalSystem 权限尚未在本次执行，不把脚本成功当成服务安装成功。

## 2026-10-06 安装调用链工作目录修复（0.1.2）

用户反馈 0.1.1 仍失败。实际编译并运行仅检查依赖的 Inno 探针，捕获到 PowerShell 在启动 Docker 子进程时返回“目录名称无效”。Sysnative 别名可供 32 位安装进程访问，但作为 64 位子进程继承的工作目录不可用；单独从仓库运行脚本不能覆盖该问题。0.1.1 只做脚本检查和编译，不足以证明安装调用链通过。

0.1.2 使用 Inno 原生系统目录执行函数，检查启动目录显式设为安装器临时目录，Docker 子进程工作目录显式设为其检查临时目录；服务脚本启动目录明确为实际安装目录。捕获检查 stderr 与退出码并在安装向导显示具体错误。

新增共享 InstallerPrerequisites.iss，正式安装器和 tests/windows-prerequisites.iss 调用同一函数。scripts/Test-WindowsInstallerPrerequisites.ps1 实际编译并执行 Inno 探针 Passed：Inno → 原生 PowerShell → Docker Linux 引擎/Compose，InitializeSetup 检查后返回 False，绝不安装程序或注册服务。该探针以当前用户运行，不证明管理员身份或 LocalSystem 的服务权限；完整安装/SCM 验收仍待运行。0.1.2 安装包编译 Passed。

## 2026-10-06 首次网站设置管理员（0.1.3）

关联 REQ-15 / W16 / W17 / ADR-008。Windows 初始化不再收取管理员密码，生成明确的待设置标记；缺失管理员文件仍拒绝启动，不触发重置。

后端 76 项 Passed：待设置标记必须显式开启、错误令牌/短密码/重复不一致拒绝、首次完成后持久化哈希、不包含明文、重复覆盖拒绝、重启可登录、管理员文件丢失拒绝。前端 12 项 Passed，新增确认密码不一致不发请求、设置成功清空密码并完成流程。TypeScript/Vite 构建 Passed。

scripts/Test-WindowsDeployment.ps1 原生隔离 Passed：无证书 HTTP 启动待设置状态，网页 API 设置管理员，错误 Origin 403、错误令牌 403、首次设置 204、重复设置 409、入口关闭，再登录并采样宿主/VM与验证 UID1000 NTFS 挂载。随机测试凭据不输出，临时进程/目录/合成容器已清理，无游戏世界创建。

安装包重新编译为 0.1.3；尚未运行该新版完整 UAC 安装/管理员网络初始化/SCM。当前已安装的程序不会因仓库发布目录更新而自动变更；请安装新版并使用其网络初始化脚本，再启动服务访问网站。已有完成初始化的管理员密码保留。

## 2026-10-06 双模式与整目录更新（0.1.4）

REQ-15 / W17 / ADR-009：向导新增重新配置/仅升级程序；重新配置收集四项非秘密输入，既有管理员与数据保留。仅升级不运行配置脚本、不写配置/防火墙、不要求 Docker 引擎运行。文件先完整写入唯一同级暂存目录，服务停服和配套数据备份后进行目录交换；卸载器与交换日志置于 ProgramData installer 目录。

scripts/Test-WindowsProgramReplacement.ps1 Passed：实际合成目录整目录交换，活动目录无 obsolete.dll，新目录正确；旧目录完整保留；程序回退成功；独立数据哈希未变；越界暂存与程序内运行数据均拒绝。未运行任何生产/实际安装目录移动。部署脚本语法检查 Passed；Inno 向导与脚本编译 Passed。

重新配置 Validate 已通过 Windows PowerShell 5.1 实测：实际本机内网 IPv4、空闲端口、逐项白名单、当前 Windows 账户和 Docker Linux/Compose 检查成功；仅临时非秘密设置 JSON，不修改现有配置、防火墙或服务。

尚未执行管理员重新配置 Apply 的真实配置/ACL/防火墙与失败回退、仅升级实际 SCM、两模式 UAC 完整安装、卸载及中断恢复演练。目录测试 Passed 不等于这些项目通过。服务结束保持停止，原程序目录作为回退材料保留。未修改本机已安装面板或其运行数据。

## 2026-10-06 向导初始化时机修复（0.1.5）

用户反馈 0.1.4 启动时报 app 常量尚未初始化。InitializeWizard 过早读取安装目录；移除该事件中的 app 展开，暂存目录/日志/旧程序识别改为目标路径可用后按需计算，使用 WizardDirValue。

新增 scripts/Test-WindowsInstallerStartup.ps1：编译同一份正式 ISS 的只读测试变体，实际执行 InitializeWizard，确认两个选项及配置字段初始化成功，然后 Abort；额外在 PrepareToInstall 拒绝所有安装操作。探针 Passed，无服务、配置、文件交换或防火墙变更。发布构建不定义测试标志。向导启动验证不等同于完整管理员安装/SCM 通过。

## 2026-10-06 必填配置与停服前预检修复（0.1.6）

用户反馈安装 Apply 阶段 BindIp 为空。已复现旧 Validate 模式接受空监听地址与空白名单：通用依赖脚本允许省略地址执行仅 Docker 检查，但安装配置入口未要求必填。现安装页拦截空 IP、白名单、账户及非法端口；Configure-Installation 三种模式统一拒绝空 IP 和空白名单，规范化输入空白。

新增 ValidateInput 模式，在停服和目录替换之前验证输入、本机地址、白名单、账户和 Docker 依赖。实际管理端口独占绑定检查仍在旧服务停止后执行，避免旧服务自身监听造成误判。仅升级模式保持原有配置，不调用重新配置脚本。

scripts/Test-WindowsConfigurationValidation.ps1 Passed：使用 Windows PowerShell 5.1，ValidateInput/Validate/Apply 均拒绝空地址和仅分隔符白名单，不创建运行数据；占用端口在输入预检阶段跳过、完整预检阶段拒绝，释放后完整预检通过。scripts/Test-WindowsInstallerStartup.ps1 实际 Inno 探针 Passed，确认空配置被拒绝、完整字段通过，然后在安装前中止。

只读检查本机旧程序版本为 0.1.2.0、服务停止，与截图所述失败回退一致。未执行新版完整 UAC 安装、真实 Apply 配置/防火墙/ACL 或 SCM 验证，未更改已安装程序与数据。

0.1.6 自包含 Windows 构建与正式 Inno 安装包编译 Passed；整目录替换/回退回归 Passed，文档结构检查 61 篇 Passed。

## 2026-10-06 参数默认值与中文错误修复（0.1.7）

关联 REQ-15 / W17。用户截图为监听地址不属于本机内网网卡的校验错误，同时控制台编码导致中文乱码；保留校验，提示明确限制。新增 Get-InstallationDefaults.ps1，为首次安装检测有默认网关的内网接口，默认精确本机白名单和 18080；重新配置只提取既有网络三字段，失效地址回退检测。账户保留当前 Windows 用户初值。部署脚本错误通过 UTF-8 文件传递至 Inno，显式解码，不依赖 Windows 控制台代码页。

Test-WindowsInstallationDefaults.ps1 Passed：Windows PowerShell 5.1 实际检测本机 IPv4、默认精确白名单、合成现有网络字段保留、失效地址回退、非网络字段不导出。Test-WindowsConfigurationValidation.ps1 Passed：空值、占用端口和依赖回归。Test-WindowsInstallerStartup.ps1 实际 Inno 探针 Passed：默认值完整、必填拦截、非法监听被拒绝，中文提示逐字一致；在安装前中止。上述检查未修改现有程序、配置、防火墙或服务；完整 UAC 安装、SCM 与实际 Apply 仍未运行。

0.1.7 Windows 自包含构建和正式 Inno 编译 Passed；共享 Inno 依赖探针 Passed，整目录交换与回退回归 Passed，文档结构检查 61 篇 Passed。

## 2026-10-06 默认回环监听（0.1.8）

REQ-15 / W17：Windows 服务同时绑定配置 LAN IPv4 与 127.0.0.1 的相同管理端口。Windows 应用来源策略固定允许精确 127.0.0.1（及其 IPv4 映射），不允许其他回环地址、不扩展 LAN 白名单；Linux 策略不变。两个入口使用相同协议和认证/同源/CSRF 策略。安装预检同时独占探测两个绑定。

后端 77 项 Passed，新增精确回环允许、映射归一化、非白名单 LAN/公网/其他回环拒绝和空白名单仍拒绝。Test-WindowsConfigurationValidation.ps1 Passed，回环占用即使 LAN 空闲也会拒绝，释放后通过。Test-WindowsDeployment.ps1 原生隔离 Passed：LAN 和 127.0.0.1 均返回静态页面，回环首次设置读取和登录成功，匿名管理 401、跨源写入 403；Docker Linux pipe、指标、UID1000 NTFS 挂载回归通过。仅创建合成测试容器/临时目录，已清理，未创建游戏世界。win-x64 自包含构建 Passed。

本机实际管理员安装 Passed：使用 0.1.8 正式 Inno 的默认仅升级模式，安装退出码 0，已安装文件版本 0.1.8.0，恢复原先运行的 PalworldPanel 服务。实时验证同一进程监听 127.0.0.1:18080 与配置内网 IP:18080，两个首页均 HTTP 200，回环匿名管理 API HTTP 401；已请求内置浏览器打开回环首页。此证据覆盖本机仅升级/服务恢复，不覆盖重新配置 Apply、防火墙变更或玩家游戏验收。

## 2026-10-06 1053 与不完整目录恢复（0.1.10）

用户反馈服务 1053。Application .NET Runtime 事件确认 hostpolicy.dll 缺失；只读实测活动 0.1.8 目录有 268 文件、缺 hostpolicy/hostfxr/coreclr；同次 previous 目录有 158 文件、含运行时但缺主程序集，失败 stage 目录有完整 426 文件。说明原整目录交换留下分裂的旧程序，服务尚未进入应用逻辑即退出。

按用户明确恢复要求，以已验证 0.1.9 发布目录建立同级 repair 暂存，逐文件比对哈希，校验现有配置与任务空闲；使用 Directory.Move 隔离损坏目录并启用完整程序，启动服务成功。配置、数据库、管理员与游戏数据未改动。安装版本 0.1.9.0，原损坏目录保留 PalworldPanel.broken-*；LAN 和回环首页均 HTTP 200，两个监听为同一服务进程。

修复正式安装脚本为 Directory.Move，补七项关键运行文件检查，并在 SCM 停止后等待原进程退出。Test-WindowsProgramReplacement.ps1 Passed：缺失 hostpolicy 在交换前拒绝；合成目录句柄阻止重命名时原目录/暂存内容完整，解除后整目录交换、回退和数据哈希验证通过。

0.1.10 正式 Inno 本机仅升级 Passed：退出码 0，安装版本 0.1.10.0，服务 Running，hostpolicy/hostfxr/coreclr 完整，LAN 和回环首页均 HTTP 200。活动目录全部 426 个发布文件逐项 SHA256 与构建目录一致。完成正式安装、目录交换和服务启动验收；未覆盖重新配置 Apply、卸载或生产/玩家验收。

## 2026-10-06 内置浏览器标注样式兼容（0.1.11）

用户反馈只在本项目的普通页面启用标注时光标消失且无反馈，其他网站正常。源码无隐藏鼠标规则；只读客户端实现检查确认标注模式主动隐藏原光标并注入替代光标样式，应用 CSP 样式仅允许 self/nonce，而注入样式没有应用 nonce。缺少实际浏览器 Console 记录和标注层复现，不把此分析当成完整根因确认。

针对该兼容问题，仅 Windows 精确回环来源增加样式元素的内联许可，脚本 CSP 和 LAN/Linux 的 nonce 样式规则保持原策略。后端 85 项 Passed，新增七种平台/来源边界测试，包括 IPv4 映射与非精确回环拒绝；实际浏览器光标/框选反馈仍需用户重试验收。浏览器自动化曾因 URL 安全策略拒绝，未尝试绕过，也未修改 Codex 客户端文件。

0.1.11 win-x64 自包含构建与原生隔离验证 Passed：回环响应允许标注样式元素，LAN 响应仍使用 nonce 且没有 unsafe-inline，脚本 self 策略保持；双入口、首次网站设置、认证/同源门禁、Docker Linux pipe、指标与 UID1000 挂载回归通过，未创建游戏世界。

0.1.11 正式 Inno 本机仅升级 Passed：安装版本 0.1.11.0、服务 Running，127.0.0.1 和配置 LAN 首页均 HTTP 200；实时响应确认仅回环含 style-src-elem self/unsafe-inline，LAN 没有该许可，两个入口脚本均限定 self。已请求用户强制刷新并重新进入标注；实际光标、高亮与点击标注反馈仍 Pending，不能用响应头验证代替交互验收。

用户在 0.1.11 安装后按上述流程重试，并明确确认已恢复正常：本机回环页面 Annotate 光标、高亮和点击标注反馈用户交互验收 Passed。该结果替代上段 Pending，仅覆盖用户本机已安装版本与回环页面，不外推其他客户端或入口。
