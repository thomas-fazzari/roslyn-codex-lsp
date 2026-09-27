# Changelog

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
