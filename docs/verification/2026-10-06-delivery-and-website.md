# 流水线与官网验证记录

历史记录：本文验证的是原开发仓库，不代表组织公开仓库此次发布。原始运行链接及校验值保留历史来源；当前公开发布约定见 [公开仓库与发布约定](../operations/2026-10-07-public-release.md)。

日期：2026-10-06（北京时间）。关联 REQ-16 / REQ-17、W18 / W19。

## 云端流水线

[首次完整成功运行](https://github.com/DeronQi/PalworldPanel/actions/runs/37473572803) 对应 c14d45f：Linux/Windows verify、Docker 镜像构建、Windows 安装包及 Pages 部署全部 Success。两平台后端 94 项、前端 53 项测试通过；Docker 内后端测试与应用镜像构建通过。Windows Runner 校验并安装官方 Inno Setup 7.1.0 后编译安装包，产物与 SHA-256 文件上传成功，保留 14 天。

首次 Linux 非 root 运行暴露游戏文件归属测试失败，已用显式 LinuxTestsAsRoot 在隔离 Runner 内修复。首次 Inno 6 编译缺少 ExecAndCaptureOutputWithNativeSysDir，已改用固定版本、固定校验值的 Inno 7。构建失败时 Pages 作业跳过，未绕过发布门槛。

## 官网与本地检查

GitHub 已启用 build_type=workflow、HTTPS，源码仓库仍 private=true。官网公开地址：https://deronqi.github.io/PalworldPanel/ 。仅 website 四个静态文件进入 Pages，内部 docs、配置、数据和管理控制台不发布。构建产物受私有仓库权限限制。

本地 Test-Website.mjs：部署文件白名单、相对路径、资源、锚点和静态内容检查 Passed。Prettier 与 65 篇文档结构检查 Passed。

Playwright 线上页面加载成功，标题、CSS 与导航存在，控制台无错误。1440px 桌面 scrollWidth=1440。390px 手机第一次检查发现代码块撑宽 Grid 卡片至 460px，已补 min-width:0；临时预览修正后 320px/390px 均无横向溢出。正式修正版本 [3714132 云端运行](https://github.com/DeronQi/PalworldPanel/actions/runs/37474648859) 五个作业全部 Success，Pages 部署成功。全新浏览器会话实际加载样式后，320px、390px、1440px 的 scrollWidth 均等于视口宽度，卡片 min-width=0px；部署导航跳转至 #deployment，标题未被导航遮挡，控制台无错误。

截图位于忽略的 output/playwright/，不含宿主运行数据。流水线只验证构建、单元测试与网站，不证明真实玩家连通、游戏集成恢复或生产部署；未改动本机服务及游戏实例。

线上 index.html、styles.css、favicon.svg 均 HTTP 200，SHA-256 与本地发布源码一致。发布后再次确认 GitHub 仓库 isPrivate=true。W18 / W19 交付及验收完成。
