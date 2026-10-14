# 世界密码只读实例详情设计

版本日期：2026-10-14。修订日期：2026-10-14。状态：已采纳。适用范围：REQ-24 / W28。替代来源：无，扩展现行单体与认证边界。

## 访问与会话

SPA 地址 /world-view 和 /world-view/{worldId}，同源后端 /api/v1/world-view。保留全局精确 IP 白名单，不新增公网入口。POST /session 接收 worldId/password，校验 Origin、输入长度和来源失败次数。每来源 15 分钟最多 5 次失败，来源表有上限并清理过期项，容量满拒绝新增来源。世界 GUID 规范化，唯一匹配未隔离/未删除且已应用的实例；固定时间比较密码摘要，空密码禁止。使用已应用秘密（旧版本缺省回退 GameCipher）。

生成 256 位随机不透明令牌，服务端内存保存实例、世界、已应用密文和 30 分钟期限，最多 1024 会话。独立 HttpOnly / SameSite=Strict cookie，HTTPS 时 Secure，Path=/api/v1/world-view；不会形成管理员 ClaimsPrincipal。每次读取重新核对世界、实例和已应用密文，变化拒绝。DELETE /session 校验 Origin 并撤销；GET /{worldId}、GET /{worldId}/logs、GET /{worldId}/backups/{backupId} 每次验证会话归属。全局 CSRF 中仅这一独立路由组采用自己的来源检查，其余管理认证不改。

## 数据投影与失败

基本信息和状态用明确 DTO，只返回名称、GUID、版本、UDP 游戏端口、采样状态及时间。已应用游戏设置采用目录中非秘密且非面板托管字段白名单，CPU/内存预算和管理员草稿不公开；运行设置复用安全投影并二次筛选；目录中声明为 list 的数组转换为括号列表，空数组为 `()`，嵌套对象/null 元素及非 list 数组不公开。运行读取失败返回不可用提示，取消继续传播。日志复用 200 行有界 Docker 读取、倒序及现有密码/IP/玩家标识脱敏，先完整清除 ANSI 颜色序列，再脱敏并额外清除内部路径，输出不超过 64 Ki 字符。

## 存档导出

导出取得实例 OS 锁并拒绝未结束任务，经重 IO gate 控制资源。备份 ID 必须由当前实例恢复点查询得到，WorldGuid 匹配会话世界，备份路径必须等于 BackupRoot/instanceId/backupId；验证 manifest 摘要及全部文件摘要。根据 ContainsInstallation 选择 installation/Pal/Saved 或 Saved，通过 WorldFiles.Find 定位世界。仅枚举该世界 .sav 文件，ZIP 路径固定 Saved/SaveGames/0/{worldId}/相对路径，拒绝链接，限制 100000 文件、20 GiB，检查临时空间。清单必须覆盖导出文件，不打包未知文件。临时 ZIP 使用私有文件和 DeleteOnClose，异常清理，响应结束删除；不使用访客口令加密完整运维备份。

未建立只读会话时，与管理登录共用 LoginLayout：独立居中登录卡片、产品图标、标题、说明、等距表单、卡片内错误和底部返回链接。建立会话后基本信息常驻且不展示游戏 UDP 端口，游戏参数表、备份表和日志卡片通过页签切换，提供刷新和退出；管理员实例页显示分享链接。所有秘密只在登录输入内存短暂存在，成功清空。读取 AbortController 防止世界切换和退出的迟到响应。下载以浏览器同源链接流式传输，避免把大 ZIP 装入浏览器内存。

依据：[需求](../requirements/2026-10-14-world-readonly.md)、[ADR](../adr/015-world-readonly.md)。实际证据见[验证](../verification/2026-10-14-world-readonly.md)。

2026-10-14 本机标注兼容：延续 Windows 精确回环的无 nonce 样式元素兼容，并仅为显式 DesktopValidation 模式、真实来源为 `172.30.88.5` 的专用浏览器桥接增加同等 `style-src-elem` 例外；不根据 Host/转发头扩大权限，普通 Linux/LAN 来源继续保持 nonce，脚本策略保持 self。证据见[登录与标注验证](../verification/2026-10-14-world-readonly-login-style.md)。

实例列表的“查看实例”和“只读查看”并列使用现有 actions 容器。后者为真实 RouteLink，指向 `/world-view/{worldGuid}`，保留浏览器新标签页行为；无世界 Id 时显示禁用按钮。管理员会话不会自动成为世界访客会话。

2026-10-14 页签与搜索（W28.8）：使用现有 tabs 样式、tablist/tabpanel 语义及方向键/Home/End 导航。真实链接 `/world-view/{worldId}/{settings|backups|logs}` 以 URL 为状态源，原世界链接默认设置页；组件只按世界 Id 重建，切换页签保留当前搜索与已读快照，不重新建立访客会话。中文名及字段名经过小写和首尾空格归一后匹配；无结果明确提示。现有 API 的 gamePort 字段保持兼容，前端不使用也不展示该字段。本次只替换本机预览静态资源，不重启服务或更改游戏设置。

2026-10-14 参数列（W28.9）：移除已应用基准展示，将运行观测列命名为当前值，增加默认值列。前端复用 Contracts/game-settings.json 的非秘密字段定义，按 key 查找默认值；数值默认值统一数值格式、空字符串显示空、实例名称显示创建时填写、未知字段显示未知。当前值为空观测时显示未知，不回退到 applied/default；接口的 applied 字段保持兼容但不渲染。说明文字标明当前值与默认值来源。
