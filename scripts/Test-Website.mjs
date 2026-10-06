import assert from "node:assert/strict";
import { readFile, readdir, stat, lstat } from "node:fs/promises";
import { fileURLToPath } from "node:url";
import path from "node:path";
import { execFileSync } from "node:child_process";

const root = path.resolve(
  fileURLToPath(new URL("../website/", import.meta.url)),
);
const allowed = new Set([
  "index.html",
  "styles.css",
  "favicon.svg",
  ".nojekyll",
  "images",
  "docs",
  "releases",
  "pages.json",
  "en",
]);
const files = await readdir(root);
assert.deepEqual(
  new Set(files),
  allowed,
  "Only approved public website files may be deployed",
);
execFileSync(
  process.execPath,
  [fileURLToPath(new URL("./Build-Website.mjs", import.meta.url)), "--check"],
  { stdio: "inherit" },
);
const pageNames = JSON.parse(
  await readFile(path.join(root, "pages.json"), "utf8"),
);
async function verifyDirectory(directory) {
  const folder = path.join(root, directory);
  assert.ok(
    !(await lstat(folder)).isSymbolicLink(),
    `${directory} cannot be a link`,
  );
  const expected = new Set(
    pageNames
      .filter((name) => name.startsWith(directory + "/"))
      .map((name) => name.slice(directory.length + 1).split("/")[0]),
  );
  assert.deepEqual(
    new Set(await readdir(folder)),
    expected,
    `Unexpected public ${directory} files`,
  );
  for (const name of expected) {
    const entry = await lstat(path.join(folder, name));
    assert.ok(
      !entry.isSymbolicLink(),
      `Invalid public entry: ${directory}/${name}`,
    );
    if (entry.isDirectory()) await verifyDirectory(`${directory}/${name}`);
    else assert.ok(entry.isFile());
  }
}
for (const directory of ["docs", "releases", "en"])
  await verifyDirectory(directory);
assert.equal(
  pageNames.length,
  new Set(pageNames).size,
  "Unique page manifest entries",
);
for (const name of pageNames.filter((name) => !name.startsWith("en/")))
  assert.ok(
    pageNames.includes(`en/${name}`),
    `Missing English counterpart: ${name}`,
  );
for (const name of pageNames.filter((name) => name.startsWith("en/")))
  assert.ok(
    pageNames.includes(name.slice(3)),
    `Missing Chinese counterpart: ${name}`,
  );
const pages = new Map();
for (const name of pageNames) {
  const file = path.join(root, name);
  assert.ok(
    (await lstat(file)).isFile() && !(await lstat(file)).isSymbolicLink(),
    `Invalid page ${name}`,
  );
  pages.set(file, await readFile(file, "utf8"));
}
const images = new Set([
  "console-dashboard.png",
  "console-instances.png",
  "console-overview.png",
  "console-monitoring.png",
  "console-settings.png",
  "console-password-dialog.png",
  "console-backups.png",
  "console-tasks.png",
]);
const imageRoot = path.join(root, "images");
assert.ok(
  !(await lstat(imageRoot)).isSymbolicLink(),
  "Image directory cannot be a link",
);
assert.deepEqual(
  new Set(await readdir(imageRoot)),
  images,
  "Only reviewed screenshots may be deployed",
);
for (const name of images) {
  const file = path.join(imageRoot, name);
  const info = await lstat(file);
  assert.ok(
    info.isFile() && !info.isSymbolicLink(),
    `Invalid screenshot ${name}`,
  );
  const data = await readFile(file);
  assert.ok(
    data.subarray(0, 8).equals(Buffer.from([137, 80, 78, 71, 13, 10, 26, 10])),
    `Not a PNG screenshot: ${name}`,
  );
}
for (const [page, html] of pages) {
  const name = path.relative(root, page).split(path.sep).join("/");
  const english = name.startsWith("en/");
  assert.match(html, english ? /<html lang="en">/ : /<html lang="zh-CN">/);
  const counterpart = english ? name.slice(3) : `en/${name}`;
  const languageLink = /<a class="language-switch"[^>]*href="([^"]+)"/.exec(
    html,
  );
  assert.ok(languageLink, `Missing language switch: ${name}`);
  assert.equal(
    path.resolve(path.dirname(page), languageLink[1]),
    path.join(root, counterpart),
    `Language switch must preserve the page: ${name}`,
  );
  for (const locale of ["zh-CN", "en", "x-default"]) {
    const alternate = new RegExp(
      `<link rel="alternate" hreflang="${locale}" href="([^\"]+)"`,
    ).exec(html);
    assert.ok(alternate, `Missing ${locale} alternate: ${name}`);
    const chineseName = english ? name.slice(3) : name;
    assert.equal(
      path.resolve(path.dirname(page), alternate[1]),
      path.join(root, locale === "en" ? `en/${chineseName}` : chineseName),
    );
  }
  assert.doesNotMatch(
    html,
    /控制台实拍|href="[^"]*#screenshots"/,
    "Removed screenshot gallery must stay removed",
  );
  assert.match(html, /name="viewport"/);
  assert.doesNotMatch(
    html,
    /<script|<form|127\.0\.0\.1|192\.168\.|C:\\|D:\\/i,
    "Website must be static public content",
  );
  const ids = [...html.matchAll(/\bid="([^"]+)"/g)].map((m) => m[1]);
  assert.equal(ids.length, new Set(ids).size, `Duplicate HTML IDs: ${page}`);
  for (const [, url] of html.matchAll(/(?:href|src)="([^"]+)"/g)) {
    if (/^https:\/\//.test(url)) continue;
    const [relative, fragment] = url.split("#", 2);
    assert.ok(
      !relative || /^\.\.?\//.test(relative),
      `Assets must use project-relative URLs: ${url}`,
    );
    let file = relative ? path.resolve(path.dirname(page), relative) : page;
    assert.ok(
      file === root || file.startsWith(root + path.sep),
      `Asset escapes website: ${url}`,
    );
    if ((await stat(file)).isDirectory()) file = path.join(file, "index.html");
    await stat(file);
    if (fragment) {
      const target = pages.get(file);
      assert.ok(target, `Anchor target is not a page: ${url}`);
      assert.ok(
        [...target.matchAll(/\bid="([^"]+)"/g)].some((m) => m[1] === fragment),
        `Missing anchor ${url} in ${page}`,
      );
    }
  }
}
console.log(
  `PASS: ${pages.size} public pages, reviewed images, relative assets and cross-page anchors`,
);
