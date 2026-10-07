# 流水线与版本发行手册

日期：2026-10-06。状态：现行流程。正式发行仅由版本 tag 触发。首次发行结果见[首个版本验证](../verification/2026-10-07-first-release.md)。

## 日常校验

main 推送、PR、手动 CI 和 Release 复用都会校验 Linux/Windows 后端、前端、文档、版本/资产契约和 Docker 镜像。日常 CI 不生成安装包，不创建 Release，不部署官网。

## 准备版本

1. 更新 version.json；正式版本如 0.1.21，预发布如 0.1.21-rc.1。
2. 新建 docs/releases/YYYY-MM-DD-vVERSION.md，写变化、下载升级与实际验证边界。必须唯一且非空。
3. 运行 node scripts/release-metadata.mjs vVERSION、node --test scripts/release-metadata.test.mjs scripts/release-assets.test.mjs、node scripts/Test-Website.mjs 和文档检查。
4. 在 Actions 的 Release 中手动运行，tag 输入与当前分支 version.json 相同。此入口只生成演练资产，publish 跳过，不创建 tag 或 Release。
5. 经明确版本发行安排后，在已提交源码上创建并推送对应 tag。tag 推送分别启动 Release 和 Pages；二者都要检查。

## 正式发行

Release 校验 tag 与说明 → 复用 CI → Linux Docker 镜像包 / Windows ZIP 和 Inno EXE → 精确检查三个文件 → SHA256SUMS → 发布 Release。预发布后缀自动标为 prerelease。只有 publish 作业有仓库写权限。发行包在组织仓库公开发布，可直接从 GitHub Releases 下载；流水线不安装服务或游戏实例。

GitHub Release 正文直接读取带日期的版本说明。相同 tag 重跑会更新说明并覆盖同名资产；不要移动已公布 tag，用户须用新的 SHA256SUMS 核对。Actions 中间资产保留 7 天，不作为长期下载入口。

Pages 在 v* tag 推送时从 tag 源码检查并发布 website；不等待 Release 完成，也不发布内部 docs。构建任务需要 contents:read/pages:read，部署任务需要 pages:write/id-token:write。确认仓库 Pages 源为 GitHub Actions，github-pages 环境允许 v* tag 部署。private 源码不使公开 Pages 内容私有。

网站只允许版本 tag 部署，github-pages 环境只放行 v* tag，不允许 main 或手动分支部署。若发行后需要修复工作流，在 main 提交修复，通过后续版本 tag 发布；不要移动已有发行 tag。

## 下载与校验

Releases 提供四个文件：Linux Docker 镜像归档、Windows 程序 ZIP、Windows 安装包、SHA256SUMS。Linux 可在下载目录执行 sha256sum -c SHA256SUMS；如只下载部分资产，请只校验对应行。Windows 使用 Get-FileHash -Algorithm SHA256 与清单对应项核对。

Docker 镜像可 docker load --input 文件名.docker.tar.gz 导入；仍需配置持久目录、内网白名单与管理权限。Windows 安装选择“仅升级程序”完整交换目录并保留数据，手工 ZIP 不等于已注册服务。

构建环境使用 Node 24、global.json 的 .NET SDK、项目 pnpm 版本及经过校验的 Inno Setup 7.1.0。Windows 安装器未签名，未进行真实目标机或玩家验收。实际结果见[发行流程验证](../verification/2026-10-06-release-workflow.md)。
