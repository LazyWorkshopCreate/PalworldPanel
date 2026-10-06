# ADR-003：原位接管、单写入者与恢复事务

日期：2026-10-05。状态：已按用户最佳实践决策授权采纳 D3/D4/D5；2026-10-06 本机隔离实现已验证，生产接管未执行。具体执行机制见 [ADR-004](004-local-executor.md)。

## 决策

既有实例保留原目录和 Compose 身份，用数据库登记绑定，不依赖自定义标签。登记默认只读；模板未知、目录共享、手工漂移或外部自动变更未消除时拒绝写管理。新实例一个目录和一个 Compose 项目，不共享可写安装文件或 Saved。

单实例所有变更共用文件锁和持久任务，每阶段执行前后记日志。定时备份只有一个所有者；接管初期外部备份保留，受其影响的恢复/升级不开放，直到维护窗口完成调度切换。完整恢复点采用 REST save、受控停容器、确认停止、离线 Saved 与配置快照；在线备份仅可标 best-effort。

Saved.zip 导入只替换选定世界与玩家文件，保留目标配置，专门修改 DedicatedServerName 指向源世界目录。禁止拼接旧新世界文件、只改 GUID 字符串、自动玩家身份转换。维护者恢复文档支持世界目录与该配置项联动，但玩家恢复还需真实账号验收。[维护者恢复指南](https://palworld-server-docker.loef.dev/guides/backup/restoring-backup)。

升级除 Saved 和配置，还保留旧安装文件和镜像 digest。旧镜像不会还原持久卷里被 SteamCMD 更新的游戏程序。失败后先保持停服，提供回到匹配安装/配置/存档组合的操作；不将新版写过的存档直接放进旧版本运行。[版本降级风险](https://palworld-server-docker.loef.dev/guides/pinning-game-version)。

每日错峰离线备份、保留/保护规则、显式升级、正常 unless-stopped 与事务 restart=no、清理隔离和容量初值以[运维策略与工程默认值](../design/2026-10-05-operational-defaults.md)为准。原来停止的实例备份后保持停止；面板 DB 使用 Online Backup API，在安全 checkpoint 配套保存 journal。

## 代价与结果

可信备份会短暂停服，占用比仅 ZIP 世界更大的存储；恢复使用同盘 staging/rename 和操作日志，但 SQLite 与文件系统无法单一原子提交，需要可恢复阶段。保留完整回退点优先于磁盘节省；低空间时拒绝变更，不删除其他实例或唯一可用备份。跨平台与合作主机身份迁移另立方案。
