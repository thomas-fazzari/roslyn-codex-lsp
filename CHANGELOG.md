# Changelog

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
