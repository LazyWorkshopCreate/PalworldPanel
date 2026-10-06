import { test } from "node:test";
import assert from "node:assert/strict";
import { mkdtemp, writeFile, readFile, rm } from "node:fs/promises";
import { execFileSync } from "node:child_process";
import { createHash } from "node:crypto";
import os from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";

test("release assets are exact, nonempty and have reproducible checksums", async () => {
  const dir = await mkdtemp(path.join(os.tmpdir(), "panel-assets-"));
  const script = fileURLToPath(
    new URL("./prepare-release-assets.mjs", import.meta.url),
  );
  const run = () =>
    execFileSync(process.execPath, [script, dir, "1.2.3-rc.1"], {
      stdio: "pipe",
    });
  const files = [
    "PalworldPanel-linux-x64-1.2.3-rc.1.docker.tar.gz",
    "PalworldPanel-win-x64-1.2.3-rc.1.zip",
    "PalworldPanel-1.2.3-rc.1-win-x64-setup.exe",
  ];
  try {
    assert.throws(run);
    for (const file of files)
      await writeFile(path.join(dir, file), "synthetic");
    run();
    const hash = createHash("sha256").update("synthetic").digest("hex");
    const sums = await readFile(path.join(dir, "SHA256SUMS"), "utf8");
    for (const file of files) assert.ok(sums.includes(`${hash}  ${file}`));
    run();
    await writeFile(path.join(dir, "unexpected.txt"), "synthetic");
    assert.throws(run);
    await rm(path.join(dir, "unexpected.txt"));
    await writeFile(path.join(dir, files[0]), "");
    assert.throws(run);
  } finally {
    await rm(dir, { recursive: true, force: true });
  }
});
