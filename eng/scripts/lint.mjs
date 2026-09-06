import { $ } from "bun";
import { lstatSync } from "node:fs";
import path from "node:path";
import lintStaged from "lint-staged";
import rules from "../lint-staged.config.mjs";

/** @param {string[]} args */
const gitFiles = async (args) => (await $`git ${args}`.text()).split("\0").filter(Boolean);

try {
  process.chdir((await $`git rev-parse --show-toplevel`.text()).trim());
  const mode = process.argv[2];
  if (!["staged", "changed", "auto"].includes(mode)) {
    throw new Error("Usage: bun eng/scripts/lint.mjs staged|changed|auto");
  }
  if ((await gitFiles(["diff", "--name-only", "-z", "--diff-filter=U"])).length) {
    throw new Error("Resolve merge conflicts before linting.");
  }
  const staged = await gitFiles(["diff", "--cached", "--name-only", "-z"]);
  if (mode === "staged" || (mode === "auto" && staged.length > 0)) {
    process.exitCode = (await lintStaged({
      config: rules,
      concurrent: false,
      failOnChanges: true,
    }))
      ? 0
      : 1;
  } else {
    const changed = [
      ...new Set([
        ...staged,
        ...(await gitFiles(["diff", "--name-only", "-z"])),
        ...(await gitFiles(["ls-files", "--others", "--exclude-standard", "-z"])),
      ]),
    ].filter((file) => lstatSync(file, { throwIfNoEntry: false })?.isFile());
    console.log(`Checking ${changed.length} changed files.`);
    for (const [pattern, commands] of Object.entries(rules)) {
      const files = changed
        .filter((file) => new Bun.Glob(pattern).match(file))
        .map((file) => path.resolve(file));
      for (let offset = 0; offset < files.length; offset += 100) {
        for (const command of [commands].flat()) {
          await $`${{ raw: command }} ${files.slice(offset, offset + 100)}`;
        }
      }
    }
  }
} catch (error) {
  console.error(error instanceof Error ? error.message : error);
  process.exitCode = 1;
}
