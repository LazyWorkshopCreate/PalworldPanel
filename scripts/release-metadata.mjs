import { readFile, readdir } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";

export function parseVersion(value) {
  const match =
    /^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?$/.exec(
      value,
    );
  if (
    !match ||
    match.slice(1, 4).some((n) => Number(n) > 65535) ||
    (match[4] &&
      match[4]
        .split(".")
        .some((n) => /^\d+$/.test(n) && n.length > 1 && n.startsWith("0")))
  )
    throw new Error("Invalid release version");
  return {
    version: value,
    numericVersion: match.slice(1, 4).join("."),
    prerelease: Boolean(match[4]),
  };
}
export async function metadata(root, tag) {
  if (!tag.startsWith("v")) throw new Error("Release tag must start with v");
  const version = parseVersion(tag.slice(1));
  const config = JSON.parse(
    await readFile(path.join(root, "version.json"), "utf8"),
  );
  if (version.version !== config.version)
    throw new Error("Tag does not match version.json");
  const dir = path.join(root, "docs/releases");
  const names = (await readdir(dir)).filter(
    (name) => /^\d{4}-\d{2}-\d{2}-/.test(name) && name.endsWith(`-${tag}.md`),
  );
  if (names.length !== 1)
    throw new Error("Expected exactly one dated release notes file");
  const notes = `docs/releases/${names[0]}`;
  if (!(await readFile(path.join(root, notes), "utf8")).trim())
    throw new Error("Release notes are empty");
  return { ...version, tag, notes };
}
if (
  process.argv[1] &&
  path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)
) {
  const root = fileURLToPath(new URL("../", import.meta.url));
  console.log(JSON.stringify(await metadata(root, process.argv[2])));
}
