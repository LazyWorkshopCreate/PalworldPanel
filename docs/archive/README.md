# 文档归档索引

此目录保留已经被替代、撤销或结束适用的文档，归档内容仅供历史追踪，不能当作现行需求、计划或环境事实。规则见[文档维护规则](../standards/documentation-policy.md#日期前缀与归档)，现行文件见[文档导航](../README.md)。

路径格式：`docs/archive/YYYY-MM-DD/<原分类>/YYYY-MM-DD-主题.md`。目录日期是归档发生日，文件前缀保留原版本日期；同日多版使用 `-r2`、`-r3` 区分，不能覆盖旧文件。

## 归档台账

| 原版本日期 | 归档日期 | 归档文件 | 原因 | 替代文件或终止说明 |
|---|---|---|---|---|
| 2026-10-05 | 2026-10-06 | [设计阶段实现计划](2026-10-06/planning/2026-10-05-implementation-plan.md) | 用户授权实现及本机测试，原全部 Not Run 计划被当前交付/生产准入追踪替代 | [当前计划](2026-10-06/planning/2026-10-06-implementation-plan.md) |
| 2026-10-05 | 2026-10-06 | [原系统设计](2026-10-06/design/2026-10-05-system-design.md) | 实际 API、单服务器执行锁和人工恢复契约替代原草案 | [当前设计](2026-10-06/design/2026-10-06-system-design.md) |
| 2026-10-05 | 2026-10-06 | [原技术栈规范](2026-10-06/design/2026-10-05-technology-stack.md) | 已锁定实现与单执行器机制取代工程未实现版本 | [当前规范](2026-10-06/design/2026-10-06-technology-stack.md) |

| 2026-10-05 | 2026-10-06 | [原业务需求](2026-10-06/requirements/2026-10-05-mvp.md) | REQ-06 扩展为完整参数目录、中文字段展示与搜索 | [当前需求](2026-10-06/requirements/2026-10-06-mvp.md) |

| 2026-10-06 | 2026-10-06 | [mvp 原版](2026-10-06/requirements/2026-10-06-mvp.md) | 增加 Windows 原生服务与 Inno 安装交付范围 | [现行 r2](2026-10-06/requirements/2026-10-06-mvp-r2.md) |

| 2026-10-06 | 2026-10-06 | [implementation-plan 原版](2026-10-06/planning/2026-10-06-implementation-plan.md) | 增加 Windows 原生服务与 Inno 安装交付范围 | [现行 r2](2026-10-06/planning/2026-10-06-implementation-plan-r2.md) |

| 2026-10-06 | 2026-10-06 | [technology-stack 原版](2026-10-06/design/2026-10-06-technology-stack.md) | 增加 Windows 原生服务与 Inno 安装交付范围 | [现行 r2](../design/2026-10-06-technology-stack-r2.md) |

| 2026-10-06 | 2026-10-06 | [需求 r2](2026-10-06/requirements/2026-10-06-mvp-r2.md) | 增加白名单内未登录只读仪表盘，管理权限保持认证 | [现行 r3](2026-10-06/requirements/2026-10-06-mvp-r3.md) |

归档正文冻结，2026-10-06 迁移仅修复相对链接；替代关系维护在此台账和现行文档元信息，不给历史版本补写新的业务结论。

## 2026-10-06 HTTP 与部署依赖修订

- [原版 2026-10-06-windows-deployment.md](2026-10-06/design/2026-10-06-windows-deployment.md)：原版本日期与归档日期均为 2026-10-06；被 [现行修订](2026-10-06/design/2026-10-06-windows-deployment-r2.md) 替代，原因是 Windows 默认 HTTP 与安装依赖/端口检查。
- [原版 2026-10-06-windows-deployment.md](2026-10-06/operations/2026-10-06-windows-deployment.md)：原版本日期与归档日期均为 2026-10-06；被 [现行修订](2026-10-06/operations/2026-10-06-windows-deployment-r2.md) 替代，原因是 Windows 默认 HTTP 与安装依赖/端口检查。
- [原版 2026-10-06-mvp-r3.md](2026-10-06/requirements/2026-10-06-mvp-r3.md)：原版本日期与归档日期均为 2026-10-06；被 [现行修订](2026-10-06/requirements/2026-10-06-mvp-r4.md) 替代，原因是 Windows 默认 HTTP 与安装依赖/端口检查。
- [原版 2026-10-06-system-design.md](2026-10-06/design/2026-10-06-system-design.md)：原版本日期与归档日期均为 2026-10-06；被 [现行修订](2026-10-06/design/2026-10-06-system-design-r2.md) 替代，原因是 Windows 默认 HTTP 与安装依赖/端口检查。
- [原版 2026-10-06-implementation-plan-r2.md](2026-10-06/planning/2026-10-06-implementation-plan-r2.md)：原版本日期与归档日期均为 2026-10-06；被 [现行修订](2026-10-06/planning/2026-10-06-implementation-plan-r3.md) 替代，原因是 Windows 默认 HTTP 与安装依赖/端口检查。

本次归档仅修复历史正文中的失效文件链接，使链接指向对应的冻结归档；历史业务结论未更新。

## 2026-10-06 首次网站设置管理员

- [原版 2026-10-06-mvp-r4.md](2026-10-06/requirements/2026-10-06-mvp-r4.md)：版本及归档日期 2026-10-06；[现行版](2026-10-06/requirements/2026-10-06-mvp-r5.md) 改为 Windows 首次网站设置管理员密码，保留 REQ-15 / W16 / W17。
- [原版 2026-10-06-system-design-r2.md](2026-10-06/design/2026-10-06-system-design-r2.md)：版本及归档日期 2026-10-06；[现行版](2026-10-06/design/2026-10-06-system-design-r3.md) 改为 Windows 首次网站设置管理员密码，保留 REQ-15 / W16 / W17。
- [原版 2026-10-06-windows-deployment-r2.md](2026-10-06/design/2026-10-06-windows-deployment-r2.md)：版本及归档日期 2026-10-06；[现行版](2026-10-06/design/2026-10-06-windows-deployment-r3.md) 改为 Windows 首次网站设置管理员密码，保留 REQ-15 / W16 / W17。
- [原版 2026-10-06-windows-deployment-r2.md](2026-10-06/operations/2026-10-06-windows-deployment-r2.md)：版本及归档日期 2026-10-06；[现行版](2026-10-06/operations/2026-10-06-windows-deployment-r3.md) 改为 Windows 首次网站设置管理员密码，保留 REQ-15 / W16 / W17。
- [原版 2026-10-06-implementation-plan-r3.md](2026-10-06/planning/2026-10-06-implementation-plan-r3.md)：版本及归档日期 2026-10-06；[现行版](2026-10-06/planning/2026-10-06-implementation-plan-r4.md) 改为 Windows 首次网站设置管理员密码，保留 REQ-15 / W16 / W17。

此次历史正文仅修复迁移后的文件链接，业务结论保持冻结。

## 2026-10-06 安装模式和整目录升级

- [原版 2026-10-06-mvp-r5.md](2026-10-06/requirements/2026-10-06-mvp-r5.md)：版本和归档日期 2026-10-06；[现行版](../requirements/2026-10-06-mvp-r6.md) 增加双模式向导和整目录升级，保留 REQ-15 / W17。
- [原版 2026-10-06-system-design-r3.md](2026-10-06/design/2026-10-06-system-design-r3.md)：版本和归档日期 2026-10-06；[现行版](../design/2026-10-06-system-design-r4.md) 增加双模式向导和整目录升级，保留 REQ-15 / W17。
- [原版 2026-10-06-windows-deployment-r3.md](2026-10-06/design/2026-10-06-windows-deployment-r3.md)：版本和归档日期 2026-10-06；[现行版](../design/2026-10-06-windows-deployment-r4.md) 增加双模式向导和整目录升级，保留 REQ-15 / W17。
- [原版 2026-10-06-windows-deployment-r3.md](2026-10-06/operations/2026-10-06-windows-deployment-r3.md)：版本和归档日期 2026-10-06；[现行版](../operations/2026-10-06-windows-deployment-r4.md) 增加双模式向导和整目录升级，保留 REQ-15 / W17。
- [原版 2026-10-06-implementation-plan-r4.md](2026-10-06/planning/2026-10-06-implementation-plan-r4.md)：版本和归档日期 2026-10-06；[现行版](../planning/2026-10-06-implementation-plan-r5.md) 增加双模式向导和整目录升级，保留 REQ-15 / W17。

本轮历史正文仅修复归档文件链接，历史结论不变。

| 2026-10-06 | 2026-10-06 | [交付与官网 requirements 原版](2026-10-06/requirements/2026-10-06-delivery-and-website.md) | 单一推送构建/部署改为日常 CI、tag 发行和 tag Pages | [现行 r2](../requirements/2026-10-06-delivery-and-website-r2.md) |

| 2026-10-06 | 2026-10-06 | [交付与官网 design 原版](2026-10-06/design/2026-10-06-delivery-and-website.md) | 单一推送构建/部署改为日常 CI、tag 发行和 tag Pages | [现行 r2](../design/2026-10-06-delivery-and-website-r2.md) |

| 2026-10-06 | 2026-10-06 | [交付与官网 planning 原版](2026-10-06/planning/2026-10-06-delivery-and-website.md) | 单一推送构建/部署改为日常 CI、tag 发行和 tag Pages | [现行 r2](../planning/2026-10-06-delivery-and-website-r2.md) |
