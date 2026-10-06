import { readFile, readdir, mkdir, writeFile } from "node:fs/promises";
import { fileURLToPath } from "node:url";
import path from "node:path";
import assert from "node:assert/strict";
import { documents as englishDocuments, labels } from "./website-english.mjs";
import { featureScreenshots } from "./website-screenshots.mjs";
import { parseVersion } from "./release-metadata.mjs";

const root = fileURLToPath(new URL("../", import.meta.url));
const site = path.join(root, "website");
const imageSizes = new Map();
for (const file of new Set(
  Object.values(featureScreenshots)
    .flat()
    .map((image) => image.file),
)) {
  const bytes = await readFile(path.join(site, "images", file));
  assert.ok(
    bytes.subarray(0, 8).equals(Buffer.from([137, 80, 78, 71, 13, 10, 26, 10])),
    `Invalid screenshot: ${file}`,
  );
  imageSizes.set(file, {
    width: bytes.readUInt32BE(16),
    height: bytes.readUInt32BE(20),
  });
}
const escape = (value) =>
  String(value).replace(
    /[&<>"']/g,
    (c) =>
      ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[
        c
      ],
  );
const inline = (value) =>
  escape(value)
    .replace(/`([^`]+)`/g, "<code>$1</code>")
    .replace(/\*\*([^*]+)\*\*/g, "<strong>$1</strong>");

// This public content is separate from internal requirements, environment and operations records.
const chineseDocuments = [
  {
    slug: "features",
    title: "功能说明",
    category: "功能",
    date: "2026-10-06",
    summary: "了解实例管理、参数草稿、备份恢复与访问权限。",
    sections: [
      [
        "独立世界与实例管理",
        [
          "每个实例使用独立的存档目录、容器、密码、端口、备份和任务锁。可创建新世界，也可受控接管已有实例。",
          "实例列表显示运行状态、游戏版本、资源配置和游戏连接地址。进入详情查看基本信息、设置、日志、备份与恢复和任务。",
        ],
      ],
      [
        "仪表盘与运行状态",
        [
          "白名单中的访问者可在未登录时只读查看仪表盘。登录后才能执行实例管理操作。",
          "创建实例后可查看初始化阶段与任务进度；日志按最新记录优先显示。主机监控指标、历史记录和操作记录分别展示。",
        ],
      ],
      [
        "参数与密码草稿",
        [
          "参数以中文名称和字段显示，支持搜索；列表对照当前值、默认值和草稿值。修改未应用时，高亮对应草稿。",
          "密码支持手动输入与随机生成，仅显示设置或修改状态，不回显密码。保存草稿不会应用到游戏；确认应用后，面板执行配置更新与重启，期间锁定应用按钮。",
        ],
      ],
      [
        "备份与恢复",
        [
          "定时备份与手动恢复分区展示，可查看恢复点并进行加密导出。导出使用独立口令，下载后请妥善保存。",
          "恢复与世界 ZIP 导入按受控流程执行，在需要时先备份并停止目标实例。只恢复对应世界，避免影响其他实例。",
        ],
      ],
      [
        "维护与任务",
        [
          "启动、停止、离线备份及更多菜单中的操作都有二次确认。任务位于实例详情中，记录执行阶段与结果。",
          "升级、移除容器保留数据、隔离清理等操作需仔细阅读确认内容。管理接口健康并不能代替真实游戏客户端连接验证。",
        ],
      ],
      [
        "支持的平台与访问范围",
        [
          "Windows 面板注册为系统服务，帕鲁实例通过 Docker Desktop Linux 引擎运行；Linux 面板与游戏通过 Docker 运行。",
          "面板具有本机 Docker 管理权限，仅允许明确白名单中的内网地址访问。公网 UDP 转发不属于面板管理范围。",
        ],
      ],
    ],
  },
  {
    slug: "guide",
    title: "使用指南",
    category: "指南",
    date: "2026-10-06",
    summary: "从安装和首次登录，到创建世界、修改配置与备份恢复。",
    sections: [
      [
        "1. 准备运行环境",
        [
          "Windows：安装并启动 Docker Desktop，使用 Linux 引擎并确认 Compose v2 可用。",
          "Linux：准备 Docker Engine 与 Compose v2。",
          "准备独立的数据目录、足够的磁盘和内存，以及可用的面板与游戏端口。安装器会进行依赖、地址和端口检查。",
        ],
      ],
      [
        "2. 安装并建立访问权限",
        [
          "从 LazyWorkshopCreate 组织仓库的 GitHub Releases 获取安装包和 SHA256SUMS，并核对文件校验值。",
          "下载地址：`https://github.com/LazyWorkshopCreate/PalworldPanel/releases`。也可按项目 README 从源码构建。",
          "Windows 首次安装选择“重新配置”，填写监听地址、端口和精确 IP 白名单。Windows 默认 HTTP，可通过本机回环地址访问。",
          "打开安装后显示的控制台地址，在首次访问时设置面板管理员密码。匿名仪表盘只读；管理操作先登录。",
        ],
      ],
      [
        "3. 创建或接管实例",
        [
          "点击“创建新世界”，按分组填写名称、玩家上限、资源和游戏规则，再预检目录与端口并确认创建。",
          "查看实例初始化阶段和任务结果。运行后，在实例列表复制显示的 IP 与 UDP 端口，通过帕鲁游戏客户端连接；游戏端口不能当作网页地址访问。",
          "接管已有实例时，先核对目录和容器；明确写管理权限后再执行维护。",
        ],
      ],
      [
        "4. 修改游戏参数",
        [
          "进入实例详情的“设置”，搜索参数中文名称或字段，点击“修改”在弹窗中编辑。",
          "密码在“访问密码”中修改，可输入或随机生成。确认保存后检查草稿状态；留空保留原值，显式清除游戏密码须按弹窗选项操作。",
          "先保存草稿，再点击“应用并重启”，核对维护预览并确认。等待任务完成后检查参数和实例状态，期间不要重复提交。",
        ],
      ],
      [
        "5. 查看状态与日志",
        [
          "“概览”显示实例基本信息；“日志”以最新内容优先显示。初始化、应用配置和维护的执行阶段在“任务”中查看。",
          "出现错误时先查看失败任务与日志，核对 Docker 引擎和实例运行状态，再按实际错误处理。",
        ],
      ],
      [
        "6. 定时备份与手动恢复",
        [
          "在“备份与恢复”中设置北京时间的每日备份时间并保存策略。需要即时快照时，使用离线备份并确认维护影响。",
          "从恢复点列表选择目标快照，阅读恢复预览并确认。ZIP 导入只用于受支持的世界数据，不应包含不明文件。",
          "加密导出时设置独立口令并妥善保存，定期演练可解密与可恢复性。",
        ],
      ],
      [
        "7. 升级与维护",
        [
          "Windows 升级选择“仅升级程序”，保留配置、账号和实例数据；安装器完整交换程序目录，避免旧版本文件残留。",
          "实例操作栏和“更多”菜单中的操作需要二次确认。执行升级、强制停止或隔离清理前，核对实例并确认备份状态；完成后检查任务结果。",
        ],
      ],
    ],
  },
];

function shell(title, body, active) {
  return `<!doctype html>
<html lang="zh-CN"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><meta name="description" content="${escape(title)} · PalworldPanel"><title>${escape(title)} · PalworldPanel</title><link rel="icon" href="../favicon.svg" type="image/svg+xml"><link rel="stylesheet" href="../styles.css"></head>
<body><a class="skip-link" href="#main">跳到正文</a><header class="header"><a class="brand" href="../index.html"><img src="../favicon.svg" alt="" width="36" height="36"><span>PalworldPanel<small>独立世界 · 受控维护</small></span></a><nav aria-label="主导航"><a href="../index.html">首页</a><a href="../docs/index.html"${active === "docs" ? ' aria-current="page"' : ""}>文档</a><a href="../releases/index.html"${active === "releases" ? ' aria-current="page"' : ""}>发布日志</a><a class="button small" href="https://github.com/LazyWorkshopCreate/PalworldPanel">GitHub ↗</a></nav></header><main id="main" class="document-main">${body}</main><footer><span>PalworldPanel · 单服务器多实例管理</span><span>非官方社区工具，与游戏开发商无隶属关系。</span></footer></body></html>\n`;
}
function markdown(source) {
  const blocks = source.trim().split(/\r?\n\s*\r?\n/);
  return blocks
    .map((block) => {
      const lines = block.split(/\r?\n/);
      if (lines.every((line) => /^- /.test(line)))
        return `<ul>${lines.map((line) => `<li>${inline(line.slice(2))}</li>`).join("")}</ul>`;
      if (lines.length === 1 && /^#{1,3} /.test(block)) {
        const [, hashes, text] = /^(#{1,3}) (.+)$/.exec(block);
        return `<h${hashes.length}>${inline(text)}</h${hashes.length}>`;
      }
      assert.ok(
        !lines.some((line) => /^(?:```|\||>|\d+\. |<|#{1,6} )/.test(line)),
        "Public release notes support headings, paragraphs and bullet lists; unsupported block must be adapted before publication",
      );
      return `<p>${inline(lines.join(" "))}</p>`;
    })
    .join("\n");
}
const pages = new Map();
for (const locale of ["zh", "en"]) {
  const documents = locale === "en" ? englishDocuments : chineseDocuments;
  assert.deepEqual(
    englishDocuments.map((d) => [
      d.slug,
      d.date,
      d.sections.map((s) => s[1].length),
    ]),
    chineseDocuments.map((d) => [
      d.slug,
      d.date,
      d.sections.map((s) => s[1].length),
    ]),
    "Documentation translations must cover the same articles and sections",
  );
  const localizedPages = new Map();
  localizedPages.set(
    "index.html",
    await readFile(
      path.join(root, `scripts/website-templates/home-${locale}.html`),
      "utf8",
    ),
  );
  localizedPages.set(
    "docs/index.html",
    shell(
      "文档",
      `<section class="document-heading"><p class="eyebrow">DOCUMENTATION</p><h1>文档</h1><p>了解功能，按步骤完成部署与日常维护。</p></section><div class="document-list">${documents.map((doc) => `<article><span class="badge">${doc.category}</span><h2><a href="./${doc.slug}.html">${doc.title}</a></h2><p>${doc.summary}</p><a class="text-link" href="./${doc.slug}.html">阅读文档 →</a></article>`).join("")}</div>`,
      "docs",
    ),
  );
  for (const doc of documents) {
    const toc = doc.sections
      .map(([title], i) => `<a href="#chapter-${i + 1}">${escape(title)}</a>`)
      .join("");
    const chapters = doc.sections
      .map(([title, paragraphs], i) => {
        const screenshots =
          doc.slug === "features" ? featureScreenshots[i] || [] : [];
        const figures = screenshots
          .map(({ file, zh, en }) => {
            const caption = locale === "en" ? en : zh;
            const url = `${locale === "en" ? "../../" : "../"}images/${file}`;
            const { width, height } = imageSizes.get(file);
            return `<figure class="documentation-screenshot"><a href="${url}" aria-label="${escape(caption)}"><img src="${url}" alt="${escape(caption)}" width="${width}" height="${height}" loading="lazy" decoding="async"></a><figcaption>${escape(caption)}</figcaption></figure>`;
          })
          .join("");
        return `<section id="chapter-${i + 1}"><h2>${escape(title)}</h2>${paragraphs.map((p) => `<p>${inline(p)}</p>`).join("")}${figures}</section>`;
      })
      .join("");
    const captureNotice =
      doc.slug === "features"
        ? `<p class="article-meta">${locale === "en" ? "Screenshots captured from the local console on 2026-10-06. Sensitive fields are masked or replaced with examples. Click an image to open the full-size capture. The console uses Chinese; runtime values vary by host." : "以下截图采集自 2026-10-06 的本机控制台，敏感字段已遮盖或替换为示例。点击图片可查看原图；运行数值以实际主机为准。"}</p>`
        : "";
    localizedPages.set(
      `docs/${doc.slug}.html`,
      shell(
        doc.title,
        `<nav class="breadcrumbs" aria-label="面包屑"><a href="./index.html">文档</a><span>/</span><span>${doc.title}</span></nav><div class="article-layout"><aside class="article-toc"><strong>本页目录</strong><nav aria-label="本页目录">${toc}</nav></aside><article class="document-article"><header><span class="badge">${doc.category}</span><h1>${doc.title}</h1><p class="article-meta">更新：${doc.date}</p><p>${doc.summary}</p>${captureNotice}</header>${chapters}<a class="text-link" href="./index.html">← 返回文档列表</a></article></div>`,
        "docs",
      ),
    );
  }
  const releases = [];
  const releaseFiles = (await readdir(path.join(root, "docs/releases")))
    .filter((name) => /^\d{4}-\d{2}-\d{2}-v.+\.md$/.test(name))
    .sort();
  assert.deepEqual(
    (await readdir(path.join(root, "docs/releases/en"))).sort(),
    releaseFiles,
    "English release notes must match the Chinese versions",
  );
  for (const name of releaseFiles) {
    const match = /^(\d{4}-\d{2}-\d{2})-v(.+)\.md$/.exec(name);
    if (!match) continue;
    const version = parseVersion(match[2]).version;
    assert.ok(
      !releases.some((entry) => entry.version === version),
      `Duplicate release notes: ${version}`,
    );
    const source = await readFile(
      path.join(root, "docs/releases", name),
      "utf8",
    );
    assert.ok(source.trim(), `Empty release notes: ${name}`);
    const translatedSource =
      locale === "en"
        ? await readFile(path.join(root, "docs/releases/en", name), "utf8")
        : source;
    assert.ok(
      translatedSource.trim(),
      `Empty ${locale} release notes: ${name}`,
    );
    assert.equal(
      translatedSource.split(/\r?\n/, 1)[0],
      `# PalworldPanel v${version}`,
      `Release title must match its filename: ${locale}/${name}`,
    );
    releases.push({ date: match[1], version, source: translatedSource });
  }
  releases.sort(
    (a, b) =>
      b.date.localeCompare(a.date) ||
      b.version.localeCompare(a.version, "en", { numeric: true }),
  );
  localizedPages.set(
    "releases/index.html",
    shell(
      "发布日志",
      `<section class="document-heading"><p class="eyebrow">RELEASE NOTES</p><h1>发布日志</h1><p>按版本查看功能变化和升级说明。发行状态与下载以 GitHub Releases 为准。</p></section><div class="release-list">${releases.map((release) => `<article><time datetime="${release.date}">${release.date}</time><div><h2><a href="./v${release.version}.html">v${release.version}</a></h2><p>功能变化、下载升级与验证范围。</p><a class="text-link" href="./v${release.version}.html">查看版本详情 →</a></div></article>`).join("")}</div>`,
      "releases",
    ),
  );
  for (const release of releases) {
    const body = markdown(release.source.replace(/^# [^\r\n]+\r?\n/, ""));
    localizedPages.set(
      `releases/v${release.version}.html`,
      shell(
        `v${release.version} 发布日志`,
        `<nav class="breadcrumbs" aria-label="面包屑"><a href="./index.html">发布日志</a><span>/</span><span>v${release.version}</span></nav><article class="document-article release-article"><header><p class="eyebrow">RELEASE NOTES</p><h1>v${release.version}</h1><p class="article-meta">${release.date}</p></header>${body}<p><a class="button" href="https://github.com/LazyWorkshopCreate/PalworldPanel/releases">查看发行与下载 ↗</a></p><a class="text-link" href="./index.html">← 返回发布日志列表</a></article>`,
        "releases",
      ),
    );
  }

  for (const [name, source] of localizedPages) {
    const destination = locale === "en" ? `en/${name}` : name;
    let html = source;
    if (locale === "en" && name !== "index.html") {
      html = html.replace('lang="zh-CN"', 'lang="en"');
      for (const [text, translated] of Object.entries(labels).sort(
        (a, b) => b[0].length - a[0].length,
      ))
        html = html.replaceAll(text, translated);
      // Navigation remains inside the selected locale; shared assets live at site root.
      html = html
        .replaceAll("../favicon.svg", "../../favicon.svg")
        .replaceAll("../styles.css", "../../styles.css");
    }
    const relative = (target) => {
      const value = path.posix.relative(
        path.posix.dirname(destination),
        target,
      );
      return value.startsWith(".") ? value : `./${value}`;
    };
    const chinese = relative(name);
    const english = relative(`en/${name}`);
    const alternate = `<link rel="alternate" hreflang="zh-CN" href="${chinese}"><link rel="alternate" hreflang="en" href="${english}"><link rel="alternate" hreflang="x-default" href="${chinese}">`;
    html = html.replace("</head>", `${alternate}</head>`);
    const switcher = `<a class="language-switch" lang="${locale === "en" ? "zh-CN" : "en"}" hreflang="${locale === "en" ? "zh-CN" : "en"}" aria-label="${locale === "en" ? "切换到中文" : "Switch to English"}" href="${locale === "en" ? chinese : english}">${locale === "en" ? "中文" : "English"}</a>`;
    html = html.replace("</nav></header>", `${switcher}</nav></header>`);
    // The hand-authored homepage template uses formatted markup.
    if (!html.includes('class="language-switch"'))
      html = html.replace(/(\s*<\/nav>\s*<\/header>)/, `${switcher}$1`);
    pages.set(destination, html);
  }
}
// Explicit manifest keeps internal docs out of the Pages deployment directory.
pages.set(
  "pages.json",
  JSON.stringify([...pages.keys()].sort(), null, 2) + "\n",
);
for (const [name, html] of pages) {
  const target = path.join(site, name);
  if (process.argv.includes("--check"))
    assert.equal(
      await readFile(target, "utf8"),
      html,
      `${name} is stale; run node scripts/Build-Website.mjs`,
    );
  else {
    await mkdir(path.dirname(target), { recursive: true });
    await writeFile(target, html);
  }
}
console.log(
  `PASS: ${pages.size - 1} public documentation and release pages ${process.argv.includes("--check") ? "match their sources" : "generated"}`,
);
