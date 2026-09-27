---
name: roslyn-lsp
description: Use the Roslyn MCP for C# diagnostics, symbol navigation and semantic edits.
---

# Roslyn LSP

Use the `roslyn` server for C# diagnostics, navigation and semantic edits.
It reads saved files in the current workspace. Its tools take flat arguments:

- `diagnostics`: `{ "file": "src/Calculator.cs" }`. It reports errors and warnings unless `severity` is `information` or `hint`
- `navigate`: `action` (`definition`, `type_definition`, `implementation`, `references`, `callers`, `callees`, `supertypes`, `subtypes`, `hover`), and `symbol` or `file`, `line`, `character`
- `symbols`: `file`, or `query` for a workspace search
- `edit`: `action` (`rename`, `rename_file`, `code_actions`) with its inputs, `proposalId` and `apply`
- `lsp`: `action` (`status`, `capabilities`, `reload`, `request`), and `method` with `parameters` for raw requests

`diagnostics`, `navigate` and `symbols` are read-only.

## Scope and diagnostics

- Start with the files relevant to the task. To check that the MCP works, request diagnostics for one known C# file.
- Set `file` to a specific path or narrow glob. Omitting it, using `"*"` or `"**/*.cs"` selects all C# files. Use that scope only for a requested workspace audit. Split large audits by project or directory.
- `limit` caps returned items. It does not reduce the files analyzed or the analysis time.
- After a timeout, narrow the selection. If a single-file retry also times out, report the failure and stop retrying that request. Attribute the cause only when logs support it.
- Read the structured result and report the checked scope. Split truncated results into narrower requests. A failed, incomplete or truncated response cannot establish that the workspace is clean.

## Navigation

Use `navigate` with `definition`, `implementation` or `references` when symbol identity matters.
Prefer `symbol` over a position when you know the name, for example `"symbol": "Calculator.Add"`. It avoids reading the file first.
The name matches the end of the qualified name. Add a parameter list for an overload or a constructor.
On `ambiguous_symbol`, send back one of the returned `candidates` unchanged.
Pass `file`, `line` and `character`. Input positions start at 1 and count UTF-16 code units.
Navigation returns `items` grouped by `file`, with one-based `[line, character]` pairs in `positions`.
`total` counts occurrences before limiting. Check `truncated` before claiming completeness.
Use `callers`, `callees`, `supertypes` and `subtypes` for call and type relationships.
Diagnostics, symbols and related symbols also return one-based positions. Only raw `lsp` requests use zero-based LSP positions.
Use `lsp` with `capabilities` when an operation's support is uncertain.

## Edits

`edit` returns a preview and a `proposalId`. Apply the proposal with the same action, its `proposalId` and `apply: true`.
For code actions, send the listing's `proposalId` and zero-based `actionIndex` to resolve an edit preview, then apply its new `proposalId`.

Stay within the requested change. A preview-only request does not authorize applying it.
When implementation is requested, carry authorized edits through without adding a separate approval step.
After applying an edit, check diagnostics for the affected files. On `stale_edit`, request a fresh proposal against the current files.
