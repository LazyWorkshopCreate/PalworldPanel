# 首个版本发行验证

历史记录：本文验证的是原开发仓库，不代表组织公开仓库此次发布。原始运行链接及校验值保留历史来源；当前公开发布约定见 [公开仓库与发布约定](../operations/2026-10-07-public-release.md)。

日期：2026-10-07。版本：v0.1.0-rc.1。范围：Git tag、GitHub Actions、发行资产与公开网站。

## 版本与源码

- 首个 annotated tag：v0.1.0-rc.1，已推送 origin。
- 发行源码：de9e5cf6d84de1b1ba0c93eebc25704023630dce。tag 未移动。
- [源码 CI](https://github.com/DeronQi/PalworldPanel/actions/runs/37490397927)：成功。
- [权限修复 CI](https://github.com/DeronQi/PalworldPanel/actions/runs/37493983788)：成功。
- [发行流水线](https://github.com/DeronQi/PalworldPanel/actions/runs/37493771138)：最终结果与下载验证见下文。

## 网站发布与环境规则

首次 tag 触发的 Pages 构建在读取站点配置时失败，原因是缺少 pages:read。修复提交 8ba3359 增加构建任务的 pages:read，并临时提供指定 tag 的恢复入口。

[恢复部署](https://github.com/DeronQi/PalworldPanel/actions/runs/37493986879)第一次执行被环境的 tag 规则拦截；临时允许 main 后，重跑成功。部署读取的是 v0.1.0-rc.1 的 website 源码，未改动 tag。

用户明确要求网站只允许 tag 部署后，立即移除临时 main 环境规则与手动恢复入口。最终 github-pages 环境仅有一条规则：type=tag，name=v*。工作流只由 v* tag 推送触发，main 不部署网站；后续修复通过新版本 tag 发布。

线上检查：[公开官网](https://deronqi.github.io/PalworldPanel/)的 12 个中文/英文 HTML 页面均返回 HTTP 200，内容逐页与发行源码一致；8 张控制台文档截图均返回 HTTP 200。覆盖首页、文档列表、功能说明、使用指南、发布列表与 v0.1.0-rc.1 详情的两种语言。

## 发行产物

[发行流水线](https://github.com/DeronQi/PalworldPanel/actions/runs/37493771138)全部成功：版本与说明校验、Windows/Linux CI、容器测试与镜像构建、两平台打包、资产校验和 GitHub Release 发布均通过。

[v0.1.0-rc.1](https://github.com/DeronQi/PalworldPanel/releases/tag/v0.1.0-rc.1)已发布为 prerelease，非 draft；私有仓库下载仍需访问权限。四个资产已上传：

| 文件 | 字节数 | SHA-256（GitHub 资产摘要） |
|---|---:|---|
| PalworldPanel-0.1.0-rc.1-win-x64-setup.exe | 36843692 | f2f208238b7745ad5fcfb0ce15706d156394e8cee30ce91a174a30e9d194c57f |
| PalworldPanel-linux-x64-0.1.0-rc.1.docker.tar.gz | 126620560 | ab45d81b3608d310164030f1838ccd7decb0598e9a2ce7612297ba99f395a0ee |
| PalworldPanel-win-x64-0.1.0-rc.1.zip | 51224030 | d17ccfb5dd440619e6d4a725561079ee83ddccae43fcf56937138cbfa5ee349a |
| SHA256SUMS | 327 | 46069ce52d99ac0931aa9c54b5b329b795c84eef242930131018f9679d3ad037 |

三个发行包全部下载完成，逐项 Get-FileHash SHA256 与 SHA256SUMS 一致。初次 gh 下载因 unexpected EOF 中断，重试后使用带超时和重试的下载完成校验；下载文件存于被忽略的 artifacts/release-verification/2026-10-07-v0.1.0-rc.1，不纳入源码。

Windows ZIP 包含 PalworldPanel.Server.exe 与 wwwroot/index.html；Linux 镜像归档 manifest.json 的 RepoTags 为 palworldpanel:0.1.0-rc.1。文档检查 79 篇通过，公开网站源检查 12 页通过。

## 验证边界

云端构建与文件校验不代表本次已在目标 Windows 主机安装新包，也不代表真实玩家连接验收。本次不安装程序、不重启本机游戏实例、不修改运行数据。
