# ADR-002：root 与 Docker 管理权限、内网 IP 白名单入口

日期：2026-10-05。状态：root/Docker 权限与内网 IP 白名单策略已按用户指示采纳（D2）；容器打包已按用户最佳实践决策授权采纳，2026-10-06 本机隔离实现已验证，生产未部署。

## 决策

用户确定后端服务拥有 root 和 Docker 管理权限，访问者必须同时满足“内网 IP”与“显式白名单”。IP 白名单限制整个 Web 入口，不能只限制写 API；管理员登录、会话和 CSRF 校验继续保留。

采用在 example-host 独立容器内运行，进程 UID 0（root），挂载 Docker socket，Linux host 网络访问已有 `127.0.0.1:8212/8214`。Web 直接 HTTPS 绑定明确的内网地址 `<LAN_BIND_IP>:18080`，不绑定 `0.0.0.0`、`::` 或公网接口。18080 是默认端口，实际绑定地址、允许客户端 IP 和 TLS 材料实施前填写；不得根据环境快照自动允许整个 192.168.1.10/24。白名单为空/无效则启动拒绝，不以开放全网作为回退。

容器不需要 privileged 或完整宿主根目录挂载；仅挂面板状态、批准实例根、独立备份根、TLS/密钥材料和 Docker socket。容器 root 直接文件访问仍限挂载路径，但 Docker 管理能力允许取得宿主 root 控制权；这是用户已明确接受的权限边界。socket 的只读 bind 不会把 Docker API 变成只读，不能把容器描述为宿主安全隔离。[Docker 安全](https://docs.docker.com/engine/security/)。实例路径保持宿主同绝对路径，CLI/Compose 兼容性在 W01 验证。

宿主防火墙对该内网地址/端口按相同源 IP 白名单放行，应用在认证/静态文件之前再校验真实连接来源。默认直连 Kestrel，不信任 X-Forwarded-For/Forwarded；只有明确配置受信代理和原客户端来源校验后才允许代理模式。拒绝回环、未知来源或未命中白名单的来源，不因有正确密码而放行。详细地址规范化、代理和失败行为见[设计 §12](../design/2026-10-06-system-design-r4.md#12-管理安全与面板自身恢复req-01)。

## 替代

本机 systemd root 服务可作为打包不便时的备选，适用同一白名单/TLS/应用规则。此前非 root + socket、仅 SSH/VPN 入口和受限执行器是比较过的候选；用户本次明确 root/Docker 权限及内网白名单后，不再要求确认这些权限。SSH 转发会掩盖浏览器真实源 IP，默认不作为面板入口；不通过允许 127.0.0.1 绕过白名单。远程 SSH 执行仍延后。

游戏 REST/RCON 只发布回环；Docker daemon 不开放 TCP。已有外部维护脚本必须遵守同一实例锁或在写权限接管时停用；无法控制外部写入者则面板只读。部署不得自动迁移既有目录或给所有容器添加标签。

2026-10-06 平台扩展：用户授权 Windows 原生服务与 Inno 安装模式，见 [ADR-005](005-windows-service.md)。本 ADR 的 Linux root/host 网络仍适用于 Linux，Windows 使用对应的 LocalSystem/本机 Docker Linux pipe，白名单与管理权限边界不变。
