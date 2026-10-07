// Public website translations. Keep sections aligned with the Chinese source.
export const documents = [
  {
    slug: "features",
    title: "Features",
    category: "Features",
    date: "2026-10-06",
    summary:
      "Explore instance management, configuration drafts, backups, recovery and access control.",
    sections: [
      [
        "Independent worlds and instances",
        [
          "Each instance has its own save directory, container, passwords, ports, backups and task lock. Create a new world or take over an existing instance through a controlled workflow.",
          "The instance list shows runtime status, game version, resource allocation and the game connection address. Instance details include an overview, settings, logs, backups and recovery, and tasks.",
        ],
      ],
      [
        "Dashboard and runtime status",
        [
          "Visitors on the IP allowlist can view the dashboard without signing in. Instance management requires authentication.",
          "After creating an instance, follow initialization stages and task progress. Logs show the latest entries first. Host metrics, historical records and operation records have separate views.",
        ],
      ],
      [
        "Configuration and password drafts",
        [
          "Settings display their Chinese names and parameter fields and support search. Compare current, default and draft values; changed drafts are highlighted until applied. The management console currently uses Chinese.",
          "Passwords can be entered manually or generated randomly. Only their configured or modified status is shown; passwords are never displayed in the settings table. Saving a draft does not change the game. After confirmation, applying a draft updates configuration and restarts the instance, with the apply button locked during the operation.",
        ],
      ],
      [
        "Backups and recovery",
        [
          "Scheduled backups and manual recovery have separate sections. Browse recovery points and export them with encryption. Use a separate export passphrase and store it securely after downloading.",
          "Recovery and world ZIP import use controlled workflows, backing up and stopping the target instance when required. Restore only the selected world to avoid affecting other instances.",
        ],
      ],
      [
        "Maintenance and tasks",
        [
          "Start, stop, offline backup and actions in the More menu require confirmation. Tasks belong to each instance and record execution stages and outcomes.",
          "Read confirmations carefully before upgrading, removing a container while keeping its data, or quarantining files. A healthy management API does not establish that a real game client can connect.",
        ],
      ],
      [
        "Platforms and access scope",
        [
          "On Windows, the panel runs as a system service and Palworld instances run through the Docker Desktop Linux engine. On Linux, both the panel and game run through Docker.",
          "The panel has local Docker administration privileges and accepts only explicitly allowlisted private-network addresses. Public UDP forwarding is outside its management scope.",
        ],
      ],
    ],
  },
  {
    slug: "guide",
    title: "User guide",
    category: "Guide",
    date: "2026-10-06",
    summary:
      "Install the panel, set up access, create a world, change settings and manage backups.",
    sections: [
      [
        "1. Prepare the host",
        [
          "Windows: install and start Docker Desktop, select the Linux engine and verify that Compose v2 is available.",
          "Linux: prepare Docker Engine and Compose v2.",
          "Prepare a dedicated data directory, sufficient disk space and memory, and available panel and game ports. The installer checks dependencies, addresses and ports.",
        ],
      ],
      [
        "2. Install and configure access",
        [
          "Download the installer and SHA256SUMS from GitHub Releases in the LazyWorkshopCreate organization and verify the checksum.",
          "Download address: `https://github.com/LazyWorkshopCreate/PalworldPanel/releases`. You can also follow the project README to build from source.",
          "For a first Windows installation, choose Reconfigure (重新配置), then enter the listening address, port and exact IP allowlist. Windows defaults to HTTP and supports local loopback access.",
          "Open the console address shown after installation and set the panel administrator password on first access. The anonymous dashboard is read-only; sign in before managing instances.",
        ],
      ],
      [
        "3. Create or take over an instance",
        [
          "Select Create new world (创建新世界). Enter the name, player limit, resource allocation and game rules in the grouped form. Preflight the directory and ports, then confirm creation.",
          "Follow initialization stages and task results. Once running, copy the IP and UDP port from the instance list and connect through the Palworld game client. The game port is not a website address.",
          "Before taking over an existing instance, check its directory and container. Perform maintenance only after confirming write-management permissions.",
        ],
      ],
      [
        "4. Change game settings",
        [
          "Open Settings (设置) in instance details. Search by a parameter's Chinese name or field, then select Modify (修改) to edit it in a dialog.",
          "Change passwords in Access passwords (访问密码), either manually or with random generation. Check the draft status after saving. Leaving a password blank preserves its current value; explicitly clearing the game password requires the corresponding dialog option.",
          "Save the draft, select Apply and restart (应用并重启), review the maintenance preview and confirm. Wait for the task to finish, then check settings and instance status. Do not submit the same operation again while it runs.",
        ],
      ],
      [
        "5. Read status and logs",
        [
          "Overview (概览) shows basic instance information. Logs (日志) show the latest entries first. Tasks (任务) report initialization, configuration application and maintenance stages.",
          "If an error occurs, inspect the failed task and logs, check Docker and instance status, and resolve the specific reported error.",
        ],
      ],
      [
        "6. Schedule backups and restore manually",
        [
          "In Backups and recovery (备份与恢复), set the daily backup time in Beijing time (UTC+8) and save the policy. For an immediate snapshot, select offline backup and confirm the maintenance impact.",
          "Select a snapshot from the recovery-point list, review the recovery preview and confirm. ZIP import is for supported world data and should not contain unknown files.",
          "Set and securely store a separate passphrase for encrypted exports. Regularly practice decrypting and restoring backups.",
        ],
      ],
      [
        "7. Upgrade and maintain",
        [
          "For a Windows upgrade, choose Upgrade program only (仅升级程序) to preserve configuration, accounts and instance data. The installer swaps the complete program directory to avoid leaving obsolete files behind.",
          "Actions in the instance toolbar and More (更多) menu require confirmation. Before upgrading, force-stopping or quarantining files, verify the target instance and its backups. Check task results after completion.",
        ],
      ],
    ],
  },
];

export const labels = {
  跳到正文: "Skip to content",
  "独立世界 · 受控维护": "Independent worlds · Controlled maintenance",
  主导航: "Main navigation",
  首页: "Home",
  "了解功能，按步骤完成部署与日常维护。":
    "Explore the features and follow the steps for deployment and daily maintenance.",
  "阅读文档 →": "Read documentation →",
  面包屑: "Breadcrumbs",
  本页目录: "On this page",
  "更新：": "Updated: ",
  "← 返回文档列表": "← Back to documentation",
  "按版本查看功能变化和升级说明。发行状态与下载以 GitHub Releases 为准。":
    "Browse changes and upgrade instructions by version. Refer to GitHub Releases for release availability and downloads.",
  "查看版本详情 →": "View release notes →",
  "查看发行与下载 ↗": "Releases and downloads ↗",
  "← 返回发布日志列表": "← Back to release notes",
  "PalworldPanel · 单服务器多实例管理":
    "PalworldPanel · Multiple instances on one server",
  "非官方社区工具，与游戏开发商无隶属关系。":
    "An unofficial community tool, unaffiliated with the game developer.",
  发布日志: "Release notes",
  文档: "Documentation",
};
