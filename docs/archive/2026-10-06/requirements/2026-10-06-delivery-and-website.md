# 流水线与项目官网需求

版本日期：2026-10-06。状态：已采纳，实施与验收中。补充现行 MVP，不替代实例管理需求。

## REQ-16 自动构建与验证

推送 main、提交面向 main 的 PR 或手动触发时，自动检查文档、锁定依赖、后端测试、前端格式、测试与构建。覆盖 Linux 与 Windows，并验证 Docker 镜像及 Windows Inno 安装包生成。安装包与 SHA-256 校验文件作为私有仓库 Actions 构建产物保留 14 天。任一门槛失败，不能发布官网。

流水线不安装本机服务、不运行真实游戏实例，不执行生产部署；构建通过不代表玩家连通或恢复验收。

## REQ-17 GitHub Pages 官网

提供中文项目介绍、功能、部署方式与入门指南，沿用项目蓝白视觉规范，支持桌面和手机。托管在 GitHub Pages，源码仓库保持私有，官网是公开静态内容。只发布 website 目录，不发布内部文档、运行配置、用户数据或管理控制台。

验收：网站 HTTPS 可访问；锚点及资源在 /PalworldPanel/ 项目子路径下有效；无横向页面溢出；下载入口明确要求仓库权限；不将示意界面描述为真实状态。

参考：[GitHub Pages 自定义工作流](https://docs.github.com/en/pages/getting-started-with-github-pages/using-custom-workflows-with-github-pages)。
