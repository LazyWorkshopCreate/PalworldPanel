# 流水线与官网设计

版本日期：2026-10-06。状态：已采纳。关联 REQ-16 / REQ-17。

## 构建与发布

.github/workflows/pipeline.yml 使用 GitHub 托管 ubuntu-24.04 与 windows-2025。verify 矩阵运行现有 Test-LocalBuild.ps1 -SkipDocker 及 Test-Website.mjs；之后并行运行 Docker 单元测试镜像、应用镜像构建与 Windows Inno 安装包构建。官网部署依赖全部任务成功，仅 main 推送或手动触发可部署，PR 不部署。

版本从现有 global.json、packageManager、锁文件与安装构建脚本读取。Actions 使用经核对的完整 commit SHA；默认 contents:read、checkout 不保留凭据，仅 Pages 作业获得 pages:write 与 id-token:write。使用短期 OIDC，不新增 PAT。并发推送取消旧流水线。Windows Runner 从官方 GitHub Release 下载固定 Inno Setup 7.1.0 x64，核对固定 SHA-256 后安装，使用 Inno 7 编译器；宿主默认 Inno 6 不支持项目使用的原生系统目录执行函数。安装包仅保存 exe 与 sha256，不上传发布目录中的内部文档。

当前流水线不发布 Release 或容器 Registry，不自动增加生产服务，不使用自托管高权限 Runner。

## 官网

website 使用无第三方运行依赖的 HTML/CSS，资源全部相对路径，可直接部署到项目子路径。页面包含介绍、功能、Windows/Linux 部署和四步入门，产品示意标识为示意。支持键盘焦点、跳过导航和 reduced-motion。

Test-Website.mjs 校验部署文件白名单、资源与锚点，阻止脚本、表单和本机路径进入网站。只上传 website 四个文件；内部 docs 不进入 Pages。GitHub 私有源码仓库不使 Pages 内容自动私有；本项目官网明确公开，构建产物仍受仓库权限限制。

位置：.github/workflows/ 放流水线；website/ 放公开官网；scripts/Test-Website.mjs 放无依赖检查。

Linux 游戏配置文件需要设置 UID/GID 1000，verify 的后端测试在隔离 GitHub Runner 上通过 sudo 执行；前端、文档和网站检查仍由 Runner 普通账户运行。Windows 继续原有测试方式。LinuxTestsAsRoot 为显式开关，本机默认构建行为不变。

仅修改 docs/verification 验证记录的 main 推送不重复构建；PR 仍检查文档。官网变更和其他源文件变更都会触发完整门槛。
