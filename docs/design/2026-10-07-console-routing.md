# 控制台 URL 路由设计

日期：2026-10-07（北京时间）。状态：已实现。对应 [REQ-18](../requirements/2026-10-07-console-routing.md)。

## 地址契约

| 地址 | 页面与行为 |
|---|---|
| `/`、`/index.html` | 根据首次设置及会话状态替换为具体入口 |
| `/dashboard` | 白名单内只读仪表盘；登录后也可查看 |
| `/login?returnTo=…` | 登录，成功后替换为安全的原目标；默认 `/instances` |
| `/setup` | 首次设置；已有管理员时转登录 |
| `/instances` | 实例列表 |
| `/instances/:id` | 实例概览；`/overview` 为兼容别名 |
| `/instances/:id/settings` | 参数与密码草稿 |
| `/instances/:id/logs` | 实例日志 |
| `/instances/:id/backups` | 定时备份与手动恢复 |
| `/instances/:id/tasks` | 实例任务与进度 |
| `/host` | 主机监控、备份、历史与操作记录 |

实例 ID 仅接受有界的字母、数字、下划线与连字符；尾部斜杠与概览别名通过 replaceState 规范化。未知路由显示不存在页，不发起实例写操作。

## 实现与状态

页面数量有限，采用原生 History API 与 React useSyncExternalStore，集中在 `src/PalworldPanel.AdminWeb/src/router.tsx`，无需新增运行依赖。pathname 是 page、instanceId、tab 的唯一来源，不再维护与地址独立的三份导航状态。RouteLink 保留真实 href，仅拦截无修饰键的普通左击；页签的键盘可访问行为保持不变。

普通导航 pushState；默认入口、登录回跳及地址规范化 replaceState；popstate 恢复页面。保存页面滚动容器的位置到历史条目，返回时恢复；标题根据页面、实例及页签更新。弹窗属于瞬时操作状态，不创建可执行命令的深链接。

导航关闭旧页面弹窗并清空旧日志、恢复点及搜索；实例读取使用 AbortController，异步结果在未取消时才写入。后台任务回执锁不因路由变化释放；每次进入设置由对应实例的持久草稿初始化。

## 认证与托管

完成首次设置/会话核验后再决定入口。管理路由匿名访问替换为登录地址，returnTo 只允许已知本地管理页面或仪表盘，剥离查询参数；拒绝外站、双斜杠、反斜杠和未知页面。退出清理已加载实例并进入仪表盘；会话失效重新受登录守卫保护。后端认证、来源白名单和 CSRF 保持独立校验。

现有 ASP.NET Core SPA 回退已支持直接请求深路径及 HEAD；所有入口继续使用动态 CSP nonce。API 404 与缺失静态资源保持 404，不能返回 SPA。无效前端页面通过 SPA 显示不存在提示，HTML 入口响应仍为 200。
