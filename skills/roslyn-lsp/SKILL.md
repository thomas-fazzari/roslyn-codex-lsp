---
name: roslyn-lsp
description: Use the Roslyn MCP for C# diagnostics, symbol navigation and semantic edits.
---

# Roslyn LSP

Use the `roslyn` server's `lsp` tool for C# diagnostics, navigation and semantic edits.
It reads saved files in the current workspace.

Pass arguments inside `request`, for example:

```json
{ "request": { "action": "diagnostics", "file": "src/Calculator.cs" } }
```

## Scope and diagnostics

- Start with the files relevant to the task. To check that the MCP works, request diagnostics for one known C# file.
- Set `file` to a specific path or narrow glob. Omitting it, using `"*"` or `"**/*.cs"` selects all C# files. Use that scope only for a requested workspace audit. Split large audits by project or directory.
- `limit` caps returned items. It does not reduce the files analyzed or the analysis time.
- After a timeout, narrow the selection. If a single-file retry also times out, report the failure and stop retrying that request. Attribute the cause only when logs support it.
- Read the structured result and report the checked scope. Split truncated results into narrower requests. A failed, incomplete or truncated response cannot establish that the workspace is clean.

## Navigation

Use `definition`, `implementation` and `references` when symbol identity matters.
Pass `file`, `line` and `character`. Input positions start at 1 and count UTF-16 code units.
Returned LSP locations and positions inside raw `request.parameters` start at 0.
Use `capabilities` when an operation's support is uncertain.

## Edits

Renames return a preview and `proposalId`. Apply the proposal with its action, `proposalId` and `apply: true`.
For code actions, send the listing's `proposalId` and zero-based `actionIndex` to resolve an edit preview, then apply its new `proposalId`.

Stay within the requested change. A preview-only request does not authorize applying it.
When implementation is requested, carry authorized edits through without adding a separate approval step.
After applying an edit, check diagnostics for the affected files. On `stale_edit`, request a fresh proposal against the current files.
