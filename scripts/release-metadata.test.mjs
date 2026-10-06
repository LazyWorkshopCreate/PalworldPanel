import { test } from "node:test";
import assert from "node:assert/strict";
import { mkdtemp, mkdir, writeFile, rm } from "node:fs/promises";
import os from "node:os";
import path from "node:path";
import { parseVersion, metadata } from "./release-metadata.mjs";

test("release versions preserve prerelease names and use numeric installer versions", () => {
  assert.deepEqual(parseVersion("1.2.3-rc.1"), {
    version: "1.2.3-rc.1",
    numericVersion: "1.2.3",
    prerelease: true,
  });
  assert.equal(parseVersion("0.1.20").prerelease, false);
  for (const value of [
    "1.2",
    "01.2.3",
    "1.2.3-01",
    "65536.0.1",
    "../1.2.3",
    "1.2.3+build",
    "1.2.3-",
  ])
    assert.throws(() => parseVersion(value));
});
test("tag, version source and dated release notes must agree", async () => {
  const root = await mkdtemp(path.join(os.tmpdir(), "panel-release-"));
  try {
    await mkdir(path.join(root, "docs/releases"), { recursive: true });
    await writeFile(
      path.join(root, "version.json"),
      JSON.stringify({ version: "1.2.3" }),
    );
    await assert.rejects(metadata(root, "1.2.3"));
    await assert.rejects(metadata(root, "v1.2.4"));
    await assert.rejects(metadata(root, "v1.2.3"));
    const notes = path.join(root, "docs/releases/2026-10-06-v1.2.3.md");
    await writeFile(notes, " ");
    await assert.rejects(metadata(root, "v1.2.3"));
    await writeFile(notes, "# Release notes");
    assert.equal(
      (await metadata(root, "v1.2.3")).notes,
      "docs/releases/2026-10-06-v1.2.3.md",
    );
    await writeFile(
      path.join(root, "docs/releases/2026-10-07-v1.2.3.md"),
      "# duplicate",
    );
    await assert.rejects(metadata(root, "v1.2.3"));
  } finally {
    await rm(root, { recursive: true, force: true });
  }
});
