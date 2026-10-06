# ADR-007：Windows 默认 HTTP 与部署前置检查

日期：2026-10-06。状态：已采纳。关联 REQ-15、W16、W17；修订 ADR-005 的 Windows 初始化协议要求，Linux 默认 HTTPS 保留。

用户明确要求不强制 HTTPS。Windows 初始化默认 `allowHttp=true`，证书和证书密码不再必填；指定 `-UseHttps` 才要求 TLS 文件。精确内网 IP 白名单、管理员认证、CSRF、近期认证和实例隔离保持。HTTP Cookie 使用普通名称与 HttpOnly/SameSite，HTTPS 保留 Secure 与 Host 前缀。

安装器替换程序前检查已安装的 Docker CLI、Linux named pipe 引擎和 Compose v2；初始化在写配置、注册服务或开放防火墙前检查本机内网 IP、端口独占绑定、逐项白名单与 Windows 账户，并再次检查 Docker。子进程有 15 秒超时；不自动安装依赖、不切换引擎、不结束占用端口的程序。检测后释放端口，实际服务仍必须成功绑定。

Windows 无 TLS 文件时使用 DPAPI 保护会话密钥；灾备包含存在的证书材料，无证书时不要求 TLS 文件。HTTP 不提供传输加密；白名单并不改变这一性质。当前用户检查成功不证明 LocalSystem 可访问 Docker，SCM 权限和真实服务测试另行验收。

实际运行证据见 [Windows 验证](../verification/2026-10-06-windows-deployment.md)，流程见 [安装手册](../operations/2026-10-06-windows-deployment-r4.md)。

2026-10-06 Windows 本机入口补充（REQ-15 / W17）：默认增加 127.0.0.1 同端口监听与精确回环来源允许，LAN 白名单仍独立匹配；回环访问仍执行管理登录、同源与 CSRF 校验。端口预检覆盖 LAN 与回环两个绑定。
