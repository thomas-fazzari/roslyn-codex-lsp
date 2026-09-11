# Usage

Ask Codex to use the `roslyn` Model Context Protocol (MCP) server. It exposes one tool: `lsp`.

Example usage:

```text
Use Roslyn to report diagnostics for src/Calculator.cs, find its implementations,
and preview a rename. Do not apply changes.
```

The bridge reads saved files and loads the project independently of your editor.
Available fixes and refactorings depend on the installed Roslyn version and project.

## Operations

| Action                                                                   | Inputs                                 | Result                                               |
| ------------------------------------------------------------------------ | -------------------------------------- | ---------------------------------------------------- |
| `diagnostics`                                                            | `file` path or glob                    | Errors, warnings and suggestions                     |
| `definition`, `type_definition`, `implementation`, `references`, `hover` | `file`, `line`, `character`            | Locations or symbol information                      |
| `symbols`                                                                | `file` or `query`                      | Document or workspace symbols                        |
| `rename`                                                                 | `file`, `line`, `character`, `newName` | Symbol rename preview                                |
| `rename_file`                                                            | `file`, `newName`                      | File rename preview, if supported                    |
| `code_actions`                                                           | `file`, `line`, `character`            | Fixes and refactorings                               |
| `status`, `capabilities`, `reload`                                       | None                                   | Session state, supported features or a fresh session |
| `request`                                                                | `method`, `parameters`                 | Raw Language Server Protocol (LSP) result            |

Pass arguments inside `request`:

```json
{
  "request": {
    "action": "definition",
    "file": "src/Calculator.cs",
    "line": 12,
    "character": 9
  }
}
```

Paths are relative to the workspace. Input coordinates start at **1**, with characters counted as UTF-16 code units.
Navigation results group occurrences by file in `items`, with one-based `[line, character]` pairs in `positions`.
This applies to `definition`, `type_definition`, `implementation` and `references`.
`total` counts occurrences before limiting, and `truncated` reports omitted occurrences.
Workspace files use relative paths. External and generated locations keep their absolute URI.
Location links use the target selection start. Use `action=request` for full LSP ranges and metadata.
Hover, symbols, diagnostic ranges and raw request coordinates keep zero-based LSP positions.

For diagnostics, omit `file` to scan C# files or use a glob such as `src/**/*.cs`.
`filesChecked` counts scanned files. `filesWithDiagnostics` counts files with diagnostics before limiting.
`diagnostics` contains only file entries with returned diagnostics. `total` counts all diagnostics found.
`complete` reports a completed scan. When `truncated` is true, omitted files are not necessarily clean.
`limit` defaults to 50 and accepts 1 to 250. Narrow the query if results are truncated or too large.

## Preview and apply

Renames return an edit preview and a `proposalId`. Apply that preview with the same action:

```json
{
  "request": {
    "action": "rename",
    "proposalId": "id-from-preview",
    "apply": true
  }
}
```

For code actions, request the listing, then send its `proposalId` and zero-based `actionIndex` to resolve a preview.
Apply the preview with its new `proposalId`. Set `endLine` and `endCharacter` when an action needs a selection.
Some Roslyn commands reveal their edits only when executed.

Each preview file contains `changes` with short `before` and `after` excerpts.
Each excerpt gives its start and exclusive end position. Lines and UTF-16 characters start at **1**.
Unchanged surrounding text is omitted. `previewTruncated` means some changes could not be fully shown within the comparison or display limits.
The preview response is limited to 32 000 serialized characters. `filesTruncated` marks omitted files while `fileCount` keeps the full total.
`commandPreviewTruncated` marks an omitted command description. These limits leave the proposed edit unchanged.

Proposals expire after five minutes. If source files change, `stale_edit` requires a new preview.
Reloading also discards proposals. Applied file creation, deletion and renaming reload Roslyn before the next request.

Application results contain a compact receipt, without source excerpts.
`applied` reports whether the requested file writes finished. `synchronized` reports whether Roslyn was refreshed.
If a later step fails, the response includes both `result` and `error`. The receipt still describes completed writes.
`error.phase` identifies the failed step. A write failure also includes its path and operation.
Inspect these fields before retrying. The next request reloads Roslyn after an application failure.

`fileCount` counts distinct paths touched. `files` lists completed operations in order, so a path can appear more than once.
Large receipts retain their totals and set `filesTruncated` when some paths are omitted.
Commands also return `commandExecuted` and `commandResult`. An oversized command result is omitted with `commandResultTruncated`.

## Limits

Edits must stay inside the workspace. Symbolic links below its root are rejected.
Each file replacement is atomic, but a failed operation across several files can leave partial changes.

Diagnostics scans cover at most 1000 files. An edit can affect up to 256 files, with 8 MiB per file and 64 MiB in total.
The source snapshot used to validate edits is limited to 256 MiB.

Raw requests reserve lifecycle and document synchronization methods for the bridge.
Unknown methods and `workspace/executeCommand` require `apply: true` because they may write files.
Use `reload` if results remain stale after an external change.
