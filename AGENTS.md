# Agent instructions

## Architecture

The executable project (RoslynCodexLsp.csproj) defines the MCP host and its Roslyn LSP integration.
Use the official MCP SDK and StreamJsonRpc for their protocol responsibilities.
Keep the bridge standalone and scoped to Codex.

- `.gitignore` uses an allowlist: ignore files by default and explicitly allow project paths.
- Reserve standard output for MCP messages, send logs to standard error.
- When applying Roslyn changes, validate that the source still matches the version used to compute them.

## Testing

- Place tests under `tests/RoslynCodexLsp.Tests/Unit` or `Integration`, grouped by subject.
- Keep fixtures under `Fixtures` and reusable test doubles under `Fakes` in that test project.
- Add behavior tests for concrete changes, not dummy tests for project setup.
- Assert error types and structured results instead of exact diagnostic prose.
- Reuse production constants in tests through `internal` access and `InternalsVisibleTo`.
- Run `make check` for C# or tooling changes and `make docs-check` for documentation changes.
- Distinguish simulated protocol tests from real Roslyn and Codex validation.

## Writing

Write `docs/` for humans and keep agent workflow rules here.

- Use short sentences and common words.
- Expand uncommon abbreviations on first use.
- Avoid semicolons, em dashes, filler and repeated facts.
- Use `make docs-format` for Markdown formatting.
