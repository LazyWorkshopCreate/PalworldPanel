import assert from "node:assert/strict";
import { readdir, readFile, writeFile } from "node:fs/promises";
import { createHash } from "node:crypto";
import path from "node:path";
import { parseVersion } from "./release-metadata.mjs";
const [dir, value] = process.argv.slice(2);
const { version } = parseVersion(value);
const names = [
  `PalworldPanel-linux-x64-${version}.docker.tar.gz`,
  `PalworldPanel-win-x64-${version}.zip`,
  `PalworldPanel-${version}-win-x64-setup.exe`,
];
assert.deepEqual(
  (await readdir(dir)).filter((n) => n !== "SHA256SUMS").sort(),
  [...names].sort(),
  "Release assets are missing or unexpected",
);
const hashes = [];
for (const name of names) {
  const data = await readFile(path.join(dir, name));
  assert.ok(data.length > 0, `Empty asset: ${name}`);
  hashes.push(`${createHash("sha256").update(data).digest("hex")}  ${name}`);
}
await writeFile(path.join(dir, "SHA256SUMS"), hashes.join("\n") + "\n");
console.log("PASS: three versioned release assets and SHA256SUMS");
