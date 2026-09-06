import { execFileSync } from "node:child_process";
import { lstatSync, readFileSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import { fileURLToPath } from "node:url";

const root = fileURLToPath(new URL("../../", import.meta.url));
const license = "GPL-3.0-only";
const notice = /^\/\/ (?:Copyright \(C\) \d{4}(?:-\d{4})? .+|SPDX-License-Identifier: .+)\r?\n/;

function git(...args) {
  return execFileSync("git", args, { cwd: root, encoding: "utf8" });
}

function contributors(file, hasHistory) {
  const authors = new Map();
  const history = hasHistory
    ? git("log", "--follow", "--format=%aN%x00%ad", "--date=format:%Y", "--", file)
    : "";

  for (const entry of history.trimEnd().split("\n")) {
    if (!entry) continue;
    const [name, year] = entry.split("\0");
    const previous = authors.get(name) ?? [Number(year), Number(year)];
    authors.set(name, [Math.min(previous[0], Number(year)), Math.max(previous[1], Number(year))]);
  }

  if (authors.size === 0) {
    const name = git("config", "user.name").trim();
    if (!name) throw new Error(`No Git author is available for ${file}`);
    const year = new Date().getFullYear();
    authors.set(name, [year, year]);
  }

  return [...authors]
    .sort(([left], [right]) => (left < right ? -1 : left > right ? 1 : 0))
    .map(([name, [first, last]]) => {
      if (/[\r\n]/.test(name)) throw new Error(`Invalid Git author for ${file}`);
      return `// Copyright (C) ${first === last ? first : `${first}-${last}`} ${name}`;
    });
}

function update(file, hasHistory, check) {
  const path = join(root, file);
  let stat;
  try {
    stat = lstatSync(path);
  } catch (error) {
    if (error.code === "ENOENT") return false;
    throw error;
  }
  if (!stat.isFile()) throw new Error(`Expected a regular file: ${file}`);

  const bytes = readFileSync(path);
  const bom = bytes.subarray(0, 3).equals(Buffer.from([0xef, 0xbb, 0xbf]));
  const content = new TextDecoder("utf-8", { fatal: true, ignoreBOM: true }).decode(
    bytes.subarray(bom ? 3 : 0),
  );
  const ending = content.match(/\r?\n/)?.[0] ?? "\n";
  let body = content;
  let hadHeader = false;
  while (notice.test(body)) {
    body = body.replace(notice, "");
    hadHeader = true;
  }
  if (hadHeader) body = body.replace(/^\r?\n/, "");
  const header = [
    ...contributors(file, hasHistory),
    `// SPDX-License-Identifier: ${license}`,
    "",
    "",
  ].join(ending);
  const updated = `${bom ? "\uFEFF" : ""}${header}${body}`;
  if (Buffer.from(updated).equals(bytes)) return false;
  if (!check) writeFileSync(path, updated);
  console.log(file);
  return true;
}

function main() {
  const args = process.argv.slice(2);
  if (args.length === 1 && ["--help", "-h"].includes(args[0])) {
    console.log("Usage: bun eng/scripts/copyrights.mjs [--check]");
    return;
  }
  if (args.length > 1 || (args.length === 1 && args[0] !== "--check")) {
    throw new Error("Usage: bun eng/scripts/copyrights.mjs [--check]");
  }
  const check = args[0] === "--check";
  const hasHistory = git("rev-list", "--all", "--max-count=1").trim() !== "";
  const files = new Set(
    git("ls-files", "-z", "--cached", "--others", "--exclude-standard", "--", "*.cs")
      .split("\0")
      .filter(Boolean),
  );
  let changed = 0;
  for (const file of files) {
    if (update(file, hasHistory, check)) changed++;
  }
  console.log(`${changed} C# file(s) ${check ? "need updates" : "updated"}.`);
  if (check && changed > 0) process.exitCode = 1;
}

try {
  main();
} catch (error) {
  console.error(error.message);
  process.exitCode = 1;
}
