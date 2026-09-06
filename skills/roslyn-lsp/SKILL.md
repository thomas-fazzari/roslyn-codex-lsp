---
name: roslyn-lsp
description: Use the Roslyn MCP for C# diagnostics, symbol navigation and semantic edits.
---

# Roslyn LSP

Use the `roslyn` server's `lsp` tool for compiler diagnostics and symbol-aware operations.
It reads saved files in the current workspace. Narrow diagnostics to affected files or a glob.
Use definitions, implementations and references when symbol identity matters.

Pass arguments inside `request`, for example:

```json
{ "request": { "action": "definition", "file": "src/Calculator.cs", "line": 12, "character": 9 } }
```

Input coordinates start at 1 and count UTF-16 code units. Returned LSP locations start at 0.
Read the structured result. An incomplete or truncated diagnostic response does not establish that the workspace is clean.

## Edits

Renames return a preview and `proposalId`. Apply the proposal with its action, `proposalId` and `apply: true`.
For code actions, send the listing's `proposalId` and zero-based `actionIndex` to resolve an edit preview, then apply its new `proposalId`.

Stay within the requested change. A preview-only request does not authorize applying it.
When implementation is requested, carry authorized edits through without adding a separate approval step.
After applying an edit, check affected diagnostics. On `stale_edit`, request a fresh proposal against the current files.

Use `capabilities` when an operation's support is uncertain. Report unavailable or failed Roslyn calls as such.
