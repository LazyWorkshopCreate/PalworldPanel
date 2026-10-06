# 现有部署背景

2026-10-05 操作快照，非实时监控数据。

## 服务器

example-host：Ubuntu 24.04.4 LTS，x86_64，16 逻辑 CPU，29 GiB 内存，Docker 29.2.1、Compose v5.0.2。最近检查可用内存约 25 GiB，磁盘约 677 GB。使用 Docker 镜像 ghcr.io/thijsvanloef/palworld-server-docker:latest，游戏版本 v1.0.5.102999。

## 两个现有实例

| 项目 | 实例 1 | 实例 2 |
|---|---|---|
| 容器名 | palworld-server | palworld-server-2 |
| 部署目录 | /home/example/palworld | /home/example/palworld-2 |
| 游戏连接 | 192.168.1.10:8211/UDP | 192.168.1.10:8213/UDP |
| 主机管理 API | 127.0.0.1:8212 | 127.0.0.1:8214 |
| 存档目录 | data/Pal/Saved | data/Pal/Saved |
| CPU/内存上限 | 8 CPU / 20 GiB | 8 CPU / 16 GiB |
| 玩家上限 | 16 | 16 |
| 自动备份 | 北京时间 05:00，保留 14 天 | 北京时间 05:15，保留 14 天 |

共同设置：死亡不掉落、离线惩罚关闭、据点外建筑自然衰减关闭，受攻击伤害倍率仍为 1。随 Docker 自动启动，启动时检查游戏更新。

实例 2 已导入 Linux 专用服务器的 Saved.zip，加载第 976 天、4 个据点的世界；含两名玩家的存档以及一个 _dps.sav 文件。迁移只复制世界与玩家数据，保留目标实例端口、密码、规则，并修改 GameUserSettings.ini 中 DedicatedServerName。导入前完整 Saved 目录与压缩备份保留在 restorepoints 下。

## 当前维护方式与待解决问题

本地运维文件位于 D:\example\servers\palworld 和 palworld-2，含 Compose、Python API 验证、存档导入脚本及说明文档。这些目录包含明文游戏密码，应先脱敏后引用，不整体复制到本仓库。

创建实例曾复用原实例安装文件，用 rsync 排除 /Pal/Saved/ 和 /backups/，随后生成新密码与新世界。配置通过 Compose 环境变量生成 PalWorldSettings.ini；保存世界通过 REST API /v1/api/save，重建容器前保存与备份，启动后用 info/metrics/settings 验证。

公网转发与外部网络设施不在本仓库文档范围，本次按用户清理要求移除相关服务及通道细节，不代表网络现状已经复核或修复。面板保留本地 UDP 实例信息；不得把“防火墙开放 TCP”展示成“帕鲁可连接”。

## 研究起点

- 游戏官方指南：https://docs.palworldgame.com/
- 当前镜像维护者文档：https://palworld-server-docker.loef.dev/
- Docker Engine 与 Compose 官方文档：https://docs.docker.com/

不要将快照信息当成未经核实的当前状态。
