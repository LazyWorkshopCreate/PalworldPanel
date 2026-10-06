# 调研：单服务器多帕鲁实例面板

调研日期：2026-10-05。目标是个人维护的最小方案。这里将来源事实、方案判断与工程待验证事项分开；现有环境以[快照](../context/2026-10-05-current-environment.md)为准，本次未连接 example-host。

## 1. 官方接口与容器能力

官方指南当前页面标记 1.0.4，快照游戏为 v1.0.5.102999；文档版本号不等于运行构建兼容证明。REST 文档简介仍含旧版本标记，不能直接用示例推定实际响应。[官方指南](https://docs.palworldgame.com/)、[REST 简介](https://docs.palworldgame.com/api/rest-api/palwold-rest-api/)。

| 操作 | 官方 REST | RCON | Docker/Compose / 文件 | 设计结论 |
|---|---|---|---|---|
| 名称、版本、世界 GUID | info | 旧式命令，结构弱 | inspect 只能看容器 | REST 归一化后展示 |
| 玩家、FPS、天数、据点数 | players、metrics | 玩家列表等有限命令 | stats 给 CPU/内存 | 指标来源分开，不以容器健康代替游戏健康 |
| 读取游戏设置 | GET settings | 非完整设置管理 | 环境变量、生成 INI | 无官方设置写接口，修改配置源后重建 |
| 保存、公告、踢人、封禁、解封 | 有对应端点 | 部分命令 | 不需要重建 | MVP 只要求保存与维护公告，玩家操作可后加 |
| 关服 | shutdown、stop | Shutdown / DoExit 等 | stop 控制容器及重启策略 | API 返回成功不等于容器已停 |
| 启动、重启、创建、删除、资源限制 | 无 | 无停服后启动能力 | Compose / Engine | 必须受控宿主权限 |
| 升级镜像、游戏文件 | 无 | 无安装管理 | pull、SteamCMD、持久卷 | 分开记录镜像 digest 和游戏 build |
| ZIP 导入、备份、恢复、切换世界 | 无 | Save 不是备份恢复 | 离线文件操作 | 面板自己编排备份和恢复 |
| 本机游戏端口 | 无 | 无 | UDP 发布、容器/宿主监听 | 面板管理本地端口；公网转发不在面板范围 |

REST 使用 HTTP Basic Auth，需启用 RESTAPIEnabled。官方明确不宜直接公开到互联网。设计采用固定 `admin` 用户及每实例 AdminPassword，用户名和完整前缀 `/v1/api` 要在隔离样机验证；浏览器不直连游戏 API。[认证及访问范围](https://docs.palworldgame.com/api/rest-api/palwold-rest-api/)。

官方端点文档写相对路径：GET [info](https://docs.palworldgame.com/api/rest-api/info/)、[players](https://docs.palworldgame.com/api/rest-api/players/)、[settings](https://docs.palworldgame.com/api/rest-api/settings/)、[metrics](https://docs.palworldgame.com/api/rest-api/metrics/)，POST [save](https://docs.palworldgame.com/api/rest-api/save/)、[shutdown](https://docs.palworldgame.com/api/rest-api/shutdown/) 等。metrics 列出 serverfps、currentplayernum、serverframetime、maxplayernum、uptime、basecampnum、days；info 含 worldguid。没有把 settings 当成 PATCH/PUT 端点的依据。

官方已将 RCON 标为弃用并说明未来更新会停止，且多字节名字有已知问题。MVP 不启用 RCON、不依赖任意控制台命令；兼容旧构建时可独立适配，但仍只能走私网。[官方 RCON](https://docs.palworldgame.com/api/rcon/)。

新官方 [game-data](https://docs.palworldgame.com/api/rest-api/game-data/) 提供演员快照，需 `-enable-gamedata-api`。MVP 的天数与据点数直接来自 metrics；不为这些指标额外启用重型快照、不引入存档解析器。缺字段显示“不支持/未知”，不可填 0 假装无据点。

## 2. 现有镜像、Compose 与文件

维护者快速示例发布游戏 8211/UDP、查询 27015/UDP，并提示不要转发 REST 8212/TCP。[镜像快速指南](https://palworld-server-docker.loef.dev/)。同一宿主必须分别分配游戏和查询 UDP 端口；容器内部同端口可复用，宿主映射不可冲突。

维护者配置表包含 PORT、QUERY_PORT、REST_API_PORT、RCON_PORT、UPDATE_ON_BOOT、BACKUP_ENABLED、BACKUP_CRON_EXPRESSION、AUTO_UPDATE_ENABLED 等。快照开启启动更新，普通重启可能隐式升级；接管写权限前需要明确变更调度所有者。[服务器参数](https://palworld-server-docker.loef.dev/getting-started/configuration/server-settings)。

主分支 [start.sh](https://raw.githubusercontent.com/thijsvanloef/palworld-server-docker/main/scripts/start.sh) 可见：`DISABLE_GENERATE_SETTINGS=true` 绕过环境变量生成；否则执行 compile-settings；QUERY_PORT 被转为启动参数。这是维护者当前源码事实，未证明 example-host 已拉取相同脚本。接管应读取实际镜像 digest 和该开关；无法匹配时只读，不套用最新参数。

环境变量游戏规则应显式映射，不能通用猜测拼写；例如死亡掉落枚举四种值由维护者表列出。[游戏配置映射](https://palworld-server-docker.loef.dev/getting-started/configuration/game-settings)。名称/密码/规则来自 Compose 环境，INI 是生成结果；禁用生成的实例则另走 INI 模式，不同时写两种源。

`docker compose config --quiet` 校验 Compose；`up -d` 配置或镜像变化时会重建且保留挂载卷，`restart` 不应用环境变量变更。命令必须限定已登记项目和服务，禁止全宿主 prune、down -v、remove-orphans。[config](https://docs.docker.com/reference/cli/docker/compose/config/)、[up](https://docs.docker.com/reference/cli/docker/compose/up/)、[restart](https://docs.docker.com/reference/cli/docker/compose/restart/)。

Docker daemon 控制权可创建宿主挂载并修改宿主文件，近似 root；非 root 面板进程加 socket 组并不是宿主权限隔离。远程 Docker 可以走 SSH，但仍需保护密钥和参数。[Docker 安全](https://docs.docker.com/engine/security/)。

`always` 与 `unless-stopped` 的手工停止和 daemon 重启行为不同；恢复事务采用显式 `no` hold，并在 Compose 重建时用目标服务 override 保持，不仅修改旧容器 runtime。[重启策略](https://docs.docker.com/engine/containers/start-containers-automatically/)。

维护者 backup 命令写入 `/palworld/backups`，文档说启用 RCON 才先 save；自动备份由 TZ 和 cron 控制。该旧说明与 RCON 弃用不能视为无缝兼容保证。因此采用面板显式 REST save + 离线快照，既有镜像备份只读登记。[创建备份](https://palworld-server-docker.loef.dev/guides/backup/creating-backup)、[自动备份](https://palworld-server-docker.loef.dev/guides/backup/automated-backup)。

维护者手动恢复要求停服，切换 SaveGames 世界目录，并同步 GameUserSettings.ini 的 DedicatedServerName；这支持世界目录切换方案，但不证明所有玩家身份迁移成功。[恢复指南](https://palworld-server-docker.loef.dev/guides/backup/restoring-backup)。

镜像挂载 `/palworld` 包含安装和数据。维护者允许 TARGET_MANIFEST_ID 锁游戏版本，但警示降级存档风险并要求拥有游戏的 Steam 账号，Steam Guard 不支持。MVP 不保存 Steam 账号；升级必须保留旧安装文件副本，单换旧镜像不能撤销持久卷中的游戏文件更新。[目录](https://palworld-server-docker.loef.dev/advanced/palworld-directory)、[版本固定说明](https://palworld-server-docker.loef.dev/guides/pinning-game-version)。

## 3. 现成方案比较

能力为维护者声明及文档核实，均未安装实测。零软件购买费用不等于零维护成本；本次不推荐商业套餐，也不估算未经核实价格。

| 方案 | 核实能力 / 维护证据 | 许可和费用 | 对既有多实例的适用性与不足 |
|---|---|---|---|
| Palhelm | 单镜像单进程，监控/配置/备份恢复；README 明说不能启动停服服务器，一键配置应用关闭 | Apache-2.0；源码自托管，无强制购买说明 | 与现有镜像配置模式匹配；示例绑定一个目标，统一多实例生命周期未证明。适合 UI/恢复交互参考 |
| Palworld A.D.M.I.N.（Lukium） | 安装/启动/RCON/备份；release 页可见 v0.10.4，其说明针对 0.2.2 兼容；多实例说明主要为 Windows 目录隔离 | 本次页面未检索到可确认的完整许可证，不能认定可复制源码；自托管发布包可见 | 不证明适配当前 Linux Docker 多实例或 1.0 存档。不是默认实现基础 |
| Pterodactyl | 多游戏、多容器；有 Panel/Wings、资源分配及通用运维框架；release 页当前显示 v1.15.1 | Panel MIT，免费开源；托管服务另计 | 多实例生命周期适合，但迁入 Wings 管理需要新的目录/模板体系，不是现有 Compose 的透明接管 |
| 自建轻量面板 | 可以严格限制到本机既有 Compose + REST + 文件 | 无购买第三方面板许可费用；本仓库未来许可另定 | 开发与维护成本自担；最契合无迁移接管、ZIP 保留目标配置和简单单管理员需求 |

Palhelm 当前 README 标出 v0.9.0 与后续未发布工作，但 [Releases 页](https://github.com/8tp/palhelm/releases)为空，README 还称预构建镜像未发布。不能将 README 版本文字当成已验证可下载稳定版。[Palhelm 仓库](https://github.com/8tp/palhelm)。

Lukium 的维护状态只能确认可见发布线较旧，没有当前构建兼容证据；不直接断言“已停止维护”。其许可待查，决定采用前必须确认 LICENSE 和依赖许可。[仓库](https://github.com/Lukium/palworld-admin)、[发布页](https://github.com/Lukium/palworld-admin/releases)。

Pterodactyl 有持续发布证据；文档依赖 PHP、MySQL/MariaDB、Redis、Web 服务和队列。官方指南导航与发布页显示的版本不同，实施时需锁配套 Panel/Wings 版本。[仓库及许可](https://github.com/pterodactyl/panel)、[安装依赖](https://pterodactyl.io/panel/1.0/getting_started.html)、[发布](https://github.com/pterodactyl/panel/releases)。Palworld 社区 egg 当前在 [games-steamcmd](https://github.com/pelican-eggs/games-steamcmd/tree/main/palworld)，其中部分 README 仍引用 2024 的配置；旧 [eggs 仓库](https://github.com/pelican-eggs/eggs/tree/master/game_eggs/steamcmd_servers/palworld) 已归档。模板存在不等于当前版本导入和恢复已验证。

推荐自建，以隔离样机门槛控制风险。若取消“原位接管 + ZIP 安全导入”并希望立即使用通用面板，重新评估 Pterodactyl；若只需监控单实例，可评估 Palhelm，不强行补它的宿主生命周期能力。

## 4. 技术栈与部署比较

以下维护成本为本项目判断，非性能基准。三者均可用 SQLite 和容器，均需持久任务而不能只开一个内存后台线程。

| 栈 | 最小组合 | 优点 | 代价 | 判断 |
|---|---|---|---|---|
| .NET/C# | ASP.NET Core Minimal API、React/TypeScript/Vite、pnpm、HttpClient、BackgroundService、Microsoft.Data.Sqlite | 后端类型明确、适合进程与文件运维编排；独立前端源码但单服务同源发布；类型明确 | 增加前端构建工具；YAML 保留格式、压缩安全、Docker 命令需自己封装验证 | 已按用户指示确定 .NET 10 + React 方向 |
| Node/TypeScript | 受支持 LTS Node、服务端页面或小前端、SQLite | Web 生态丰富、前后端统一语言 | 依赖链和构建更广；子进程及同步文件任务需控制事件循环阻塞 | 次选，已有 TS 熟练度时合理 |
| Python | FastAPI、模板、SQLite、单 worker 任务循环 | 文件运维脚本易写，开发反馈快 | 多 worker 下锁和调度容易重复，类型约束及打包需维护 | 若以脚本复用为首要目标可选 |

.NET 官方支持表显示 10 为 LTS、支持至 2028-11-14；8/9 已接近 2026-11 支持结束。用 10，不选预发布 11。[支持政策](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core)。框架提供 [Hosted Services](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/host/hosted-services?view=aspnetcore-10.0)，但任务持久化/断电恢复由应用实现。Node 版本实施时按 [官方周期](https://nodejs.org/en/about/previous-releases)重新核定；Python 参考 [FastAPI 部署](https://fastapi.tiangolo.com/deployment/)，不引入 Celery/Redis。

本项目采用 .NET 10 Minimal API、React/TypeScript/Vite/pnpm、SQLite、Serilog 和 xUnit/Vitest。前端产物由后端同源托管，运行时无需 Node 服务；Node/TypeScript 和 Python 行是后端替代方案，不排斥选中栈的 TypeScript 前端。

### 4.1 本项目技术与界面规范

组件职责、版本核定、构建链和发布要求以[技术栈设计规范](../design/2026-10-06-technology-stack-r2.md)为准；颜色、布局、组件、交互与响应式以[前端 UI 规范](../design/2026-10-05-ui-style.md)为准。两份文档是本项目现行契约。具体依赖版本、许可、SQLite provider、构建与适配行为在 W01 核定，目前没有业务工程或已安装依赖的证明。

### 4.2 部署位置比较

| 位置 | 好处 | 额外问题 | 推荐 |
|---|---|---|---|
| example-host 本机 systemd | 直接访问回环 API 和文件，路径最简单 | 安装运行时/工具，仍需 Docker 管理权限 | 备选 |
| example-host 独立容器 | 版本打包和回滚简单，保留游戏容器布局 | root/socket 权限等同宿主控制；回环 API 需 host 网络或改网络 | 推荐 host 网络、root 进程、指定 LAN HTTPS 接口及显式内网 IP 白名单，挂载限定实例根 |
| 其他主机经 SSH | 浏览器服务可离开游戏主机 | SSH 密钥、断线、文件传输和远程任务恢复复杂 | 单服务器初期延后 |

用户进一步确定后端 root 和 Docker 管理权限，且仅允许白名单内网 IP 访问。替换此前非 root/回环 SSH 入口候选，保留管理员认证。默认直连来源校验，不信任任意转发头；host 网络共享宿主网络，HTTP 服务应明确绑定内网接口并核实防火墙。白名单具体地址未提供，不能推定当前子网整体获准。[ASP.NET Core 来源与受信代理](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/proxy-load-balancer?view=aspnetcore-10.0)、[Docker host 网络](https://docs.docker.com/engine/network/drivers/host/)。这些是设计和资料核实，未修改宿主规则。

## 5. UDP 职责边界

用户已明确公网 UDP 在面板范围之外，面板不承担选型、转发配置、外网诊断、隧道监控或连通性验收。面板仅管理各实例本地 UDP 地址/端口、容器发布与监听状态；不集成外部网络设施的管理 API、凭据或公网健康状态。

历史公网通道的状态不作为面板待办或验收阻塞，本次没有检查或修改外部网络设施。

面板仍准确区分游戏 UDP 与管理 TCP：TCP connect/API 成功不能当作游戏客户端成功，本机监听也不能代替 LAN 入服与玩家恢复。公网 UDP 不作为面板交付门槛，管理入口内网白名单与管理 REST/RCON 禁止公开的边界仍保留。

## 6. 验证台账

| 编号 | 本次证据 | 状态 / 后续 |
|---|---|---|
| V1 | 官方 REST、metrics、settings、save、RCON 弃用页面 | 资料已核实；实际构建认证、前缀、字段和超时待样机验证 |
| V2 | 镜像配置文档和 main/start.sh | 资料已核实；实际 digest、生成开关、停止信号、挂载路径待核实 |
| V3 | Compose config/up/restart、安全文档 | 资料已核实；本机版本、CLI 参数、目标服务隔离待验证 |
| V4 | 三类面板仓库、依赖和发布页 | 资料已核实；软件安装、负载、漏洞审计未做，Lukium 许可未确认 |
| V5 | 双实例、原世界迁入、备份和网络问题 | 用户环境快照；不是本次现状复测 |
| V6 | ZIP/GUID/玩家/回退方案 | 设计，未运行；需合成包安全测试、私有样档演练和真实原账号入服 |
| V7 | 宿主资源、本机 UDP 映射与监听 | 未测试；内存上限之和大于物理内存；公网 UDP 不在本项目范围，不在面板验证范围 |
| V8 | 本项目技术栈、UI 和同源构建规范 | 设计已确定；依赖版本、许可、构建和界面行为待工程验证 |

实施前还要验证停止后不被 restart policy 或外部脚本拉起、UID/GID 权限、同文件系统 rename/fsync、面板崩溃恢复、磁盘峰值和备份保留。以上均进入[计划](../planning/2026-10-06-implementation-plan-r5.md)，不以文档完整度替代工程通过。
