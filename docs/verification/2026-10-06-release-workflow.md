# 发行流程调整验证

历史记录：本文验证的是原开发仓库，不代表组织公开仓库此次发布。原始运行链接及校验值保留历史来源；当前公开发布约定见 [公开仓库与发布约定](../operations/2026-10-07-public-release.md)。

日期：2026-10-06。关联 REQ-16 / REQ-17、W18 / W19 / W20。用户明确选择只调整流程，暂不发行。

验证源码提交：0226474。已经拆分日常 CI、tag Release、tag Pages；版本由 version.json 统一，版本说明带日期，README 按使用者入口重写。

| 检查 | 结果 | 证据与范围 |
|---|---|---|
| 文档 | Passed | 71 篇文档的入口、日期命名、现行版本、归档登记、链接与需求映射检查 |
| 版本与资产契约 | Passed | 3 项 Node 测试覆盖无效版本、版本不匹配、缺失/空/重复说明、缺失/多余/空资产、校验值与重跑 |
| 官网静态内容 | Passed | 四文件白名单、相对资源、锚点和静态内容检查 |
| Windows 构建脚本 | Passed | PowerShell 解析无错误；云端真实编译 Inno 7.1.0 安装包 |
| 日常 CI | Passed | [运行 37477399904](https://github.com/DeronQi/PalworldPanel/actions/runs/37477399904)，Windows/Linux 后端各 94 项、前端各 53 项；容器后端 94 项及应用镜像构建通过 |
| 只打包演练 | Passed | [运行 37477401310](https://github.com/DeronQi/PalworldPanel/actions/runs/37477401310)，复用 CI、Linux 镜像归档、Windows ZIP/EXE、精确三文件及 SHA256SUMS 汇总均通过；publish 为 skipped |
| Pages 环境规则 | Passed | 实际 github-pages 环境仅允许 v* 类型 tag，替代旧 main 分支规则 |
| 发布状态 | Passed | 演练后仓库仍为 private；GitHub tags 和 Releases 均为 0；本机服务与实例未变更 |
| 演练资产本机复核 | Passed | release-bundle 延迟后下载完成；三个 SHA256 匹配；ZIP CRC、程序、wwwroot/index.html、version.json 齐全；EXE FileVersion/ProductVersion 为 0.1.20；Docker 归档为 linux/amd64，镜像 tag 与 OCI version 均为 0.1.20 |
| tag 正式发布 | Not Run | 未推送版本 tag，未执行 Release 发布分支 |
| tag 官网部署 | Not Run | 未触发 Pages；已上线官网保持先前版本，待后续版本 tag 更新 |

发行资产完整性和 SHA256SUMS 在云端 finalize 作业及本机独立下载复核中均验证通过。演练资产仅保存在被忽略的 artifacts/release-validation/bundle，不提交仓库。云端打包成功不能代替真实安装、服务升级、玩家连接或恢复验收；安装包未签名。

## 后续版本命名调整

2026-10-06 按用户要求，将当前待发行版本从 0.1.20 调整为 0.1.0-rc.1。同步 version.json、前端包版本、Release 演练默认输入、README、说明文件和官网日志。原打包演练及本机安装结果仍记录实际 0.1.20，不改写历史证据。

新版本元数据检查 Passed：prerelease=true，安装器数字版本为 0.1.0；3 项发行契约测试、官网页面生成/链接检查和 74 篇文档检查 Passed。0.1.0-rc.1 尚未打包、安装、推送 tag 或正式发行，不能沿用旧演练表示新安装包已验证。
