# Changelog

## 0.3.0

- Rename to Roslyn4Clankers, since we now support Claude Code too.
- Install files in `roslyn-for-clankers`. The installer removes the previous `roslyn-codex-lsp` installation once every installed client uses the new one.
- Support Claude Code in the installers. They set up every installed client among Codex and Claude Code, or the one selected with `--client` (`-Client` in PowerShell).
- Add `context` to `navigate` to return the source line of each result.
- **Breaking:** return a `symbol` name for `callers`, `callees`, `supertypes` and `subtypes` results instead of `name` and `detail`. The name can be sent back as the `symbol` argument.

## 0.2.2

- Accept a `symbol` name instead of a position in `navigate` and `edit`, such as `Calculator.Add` or `Calculator.Add(int, int)`.
- Report `symbol_not_found`, or `ambiguous_symbol` with candidate names that can be sent back unchanged.

## 0.2.1

- Report semantic diagnostics for a file created just before the scan, instead of an empty complete report.
- Mark diagnostics incomplete and list `miscellaneousFiles` when Roslyn has not attached a file to a project.

## 0.2.0

- Resend only changed files to Roslyn before previewing a rename or code action.
- Open all scanned files before requesting diagnostics, so repeated scans reuse Roslyn's results.
- Stop leaving MSBuild build host processes running after Roslyn shuts down.
- Return one-based positions from diagnostics, hover and symbols. Only raw requests keep LSP positions.
- Return compact diagnostics with a start position, severity name, code and message.
- Report only errors and warnings by default. The diagnostics `severity` argument includes suggestions.
- Return compact hover text and symbols with relative files and one-based positions.
- Add `callers`, `callees`, `supertypes` and `subtypes` to the navigate tool.
- **Breaking:** replace the single `lsp` tool with `diagnostics`, `navigate`, `symbols`, `edit` and `lsp` tools that take flat arguments. The first three are marked read-only.

## 0.1.2

- Omit clean files from diagnostic results and report scanned file counts.
- Group navigation results by file with one-based positions and occurrence counts (raw requests keep the LSP format).
- Reduce response and edit preview limits to 32 000 characters.
- Reduce the default result limit to 50 and the maximum to 250.

## 0.1.1

- Show changed ranges and positions in edit previews, with bounded output.
- Preserve completed edits in error responses after partial failures.
- Refresh changed files before Roslyn requests, including closed files.
- Update the Codex skill independently of binary releases.
- Fail test runs when no tests execute.

## 0.1.0

- Introduce an initial version of the Roslyn MCP bridge for C# diagnostics, navigation and refactoring in Codex.
- Preview and apply renames, code fixes and refactorings.
- Native AOT binaries and installers for macOS, Linux and Windows.
