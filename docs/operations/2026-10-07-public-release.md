# 公开仓库与发布约定

日期：2026-10-07。状态：现行约定。范围：公开源码、发行资产与官网。

公开仓库为 https://github.com/LazyWorkshopCreate/PalworldPanel ，官网为 https://lazyworkshopcreate.github.io/PalworldPanel/ 。采用 MIT 许可证。

## 版本历史

每个版本 tag 对应一个汇总提交。首个版本 v0.1.0-rc.1 汇总已验收的源码快照；后续版本在前一个公开快照之上提交该 tag 的全部差异，不导入开发过程中的历史提交。导出仅使用已跟踪的 tag 文件，不复制构建产物、运行数据或私有凭据。公开前核查源码、文档和截图，具体环境信息使用示例。

首个公开快照源代码依据的提交为 96b95160ffee04c18a511346dbe2cfce5f901659；公开快照另包含组织仓库链接、MIT 许可证和环境脱敏调整。

## 发布契约

版本继续以 version.json 为唯一来源。仅推送 v* tag 触发 Release 和 Pages，main 提交运行 CI。Pages 使用 GitHub Actions 构建，github-pages 环境仅允许版本标签 v* 部署。发布前完成文档、网站与版本契约检查；发布后核对 CI、Release、Pages 的目标提交、发行资产 SHA256SUMS 及在线网站。

运行数据、管理员凭据、实例存档和本地构建产物不在公开仓库或 Pages 中。现有本机服务不随仓库发布而升级。
