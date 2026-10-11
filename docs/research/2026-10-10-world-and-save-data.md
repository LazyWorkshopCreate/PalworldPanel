# 世界管理调研

版本日期：2026-10-10。修订日期：2026-10-11。状态：现行调研。适用范围：谁可以连接。替代来源：无。数值编辑和玩家存档隔离已撤销，见 [ADR-013](../adr/013-withdraw-player-isolation.md)。压缩层次仍以[先前存档调研](../archive/2026-10-10/research/2026-10-10-save-editing.md)为历史说明。

## 谁可以连接

2026-10-10 核对的官方 REST 文档版本为 1.0.4：

- 在线玩家：<https://docs.palworldgame.com/api/rest-api/players/>。响应含名称、账号名、玩家编号、用户编号、IP、坐标、等级和建筑数。
- 踢出：<https://docs.palworldgame.com/api/rest-api/kick/>。请求体为 `userid`，可选 `message`。
- 封禁：<https://docs.palworldgame.com/api/rest-api/ban/>。字段与踢出相同。
- 解除封禁：<https://docs.palworldgame.com/api/rest-api/unban/>。请求体只有 `userid`。

本实例自己的封禁记录在 `Pal/Saved/SaveGames/banlist.txt`，一行一个用户编号，例如 `steam_` 后接数字。它和设置项 `BanListURL` 指向的公共封禁地址不是同一份名单。公共地址不能由面板改写成“允许谁连接”。

踢出只断开当前连接。封禁会拒绝之后的连接。把玩家 `.sav` 移出世界目录不会改封禁名单，也不会改 `Level.sav`；对方下次仍可能以新角色进入。所以隔离存档属于世界管理里的成员处理，不是数值编辑。

## 数值编辑

2026-10-10 撤销数值编辑。当前专用服务器存档使用不能随面板分发的压缩，面板不能解开后安全改写正文。世界管理不因这项撤销而改变。
