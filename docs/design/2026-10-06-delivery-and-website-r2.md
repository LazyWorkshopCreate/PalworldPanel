# 流水线、发行与官网设计

版本日期：2026-10-06。状态：已采纳。关联 REQ-16 / REQ-17。替代原版单一流水线设计。

## 工作流职责

| 工作流 | 触发 | 职责 | 写入范围 |
|---|---|---|---|
| ci.yml | main、PR、workflow_call、手动 | 现有锁定依赖、文档、前后端、版本与资产契约测试、Docker 单元测试和镜像 | 无正式发布 |
| release.yml | v* tag；手动只打包演练 | 校验版本与说明，复用 CI，分别生成 Linux/Windows 包，校验并汇总资产 | 仅 tag 下 publish 获 contents:write |
| pages.yml | v* tag | 校验版本、说明和静态站点，上传官网并部署 | build 获 pages:read；仅 deploy 获 pages:write/id-token:write |

Actions 继续固定完整 commit SHA；Node 24、global.json 和 pnpm packageManager 作为工具契约，依赖按锁安装。checkout 不保留凭据，仅使用内置 GITHUB_TOKEN。手动 Release 演练的 tag 输入只是校验标签，不创建 Git tag，publish 被跳过。Pages 与 Release 独立，Pages 成功不代表发行资产成功。

## 版本与资产

version.json 为发行版本唯一源，Windows 默认构建读取此文件；tag 校验要求完全相同。语义版本允许 prerelease，不接收构建元数据、路径、前导零数字或超过 65535 的数值。Inno 显示/文件名使用完整版本，PE VersionInfoVersion 使用去掉后缀的三段数值；.NET 使用完整语义版本，Docker 镜像标签和 OCI 标签同步版本。

| 文件 | 内容 |
|---|---|
| PalworldPanel-linux-x64-VERSION.docker.tar.gz | linux/amd64 面板 Docker 镜像，docker load 可导入 |
| PalworldPanel-win-x64-VERSION.zip | 自包含程序、前端、Windows 脚本和文档；手工部署 |
| PalworldPanel-VERSION-win-x64-setup.exe | Inno 安装器；重新配置或完整程序目录升级 |
| SHA256SUMS | 精确三个发行文件的 SHA-256 清单 |

Linux 保留容器部署，不新增未经验证的原生 systemd/macOS 交付。Windows Runner 下载固定 Inno Setup 7.1.0 x64 并验证官方 SHA-256。安装包未签名。

说明放 docs/releases/YYYY-MM-DD-vVERSION.md，脚本按 tag 查找且要求唯一，不按日期最大猜测。重复发行会覆盖同名资产，消费者必须重新下载 SHA256SUMS 后核对；不得移动已公布的 tag。

## 官网与项目介绍

website 四个基础静态文件与经过审核的 images 截图目录保持部署白名单、相对路径、锚点检查与响应式布局；截图必须逐项列入白名单，不发布本地运行数据或未审核图片。发布只包含公开内容。README 和官网引用正式 Releases，不用短期 Actions Artifact 作为用户下载入口。main 上源码介绍可更新，但官网只在下一次版本 tag 时部署。

用户文档与版本日志的独立列表/详情、公开内容生成与页面同步见[公开文档补充设计](2026-10-06-public-documentation.md)。

网站部署只允许 v* tag；github-pages 环境只放行 tag，不允许 main 或手动分支部署。工作流修复通过后续版本 tag 发布，既有发行 tag 保持不变。首次发行与规则恢复证据见[首个版本验证](../verification/2026-10-07-first-release.md)。
