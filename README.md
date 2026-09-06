# Roslyn Codex LSP

C# diagnostics, navigation and refactoring in Codex, backed by the official Roslyn language server through the Model Context Protocol (MCP).

Find definitions, implementations and references. Preview and apply renames, fixes and refactorings.
The bridge reads saved files and works independently of your editor.

[Get started](docs/getting-started.md) · [Usage](docs/usage.md) · [Development](docs/development.md)

## Install

Prerequisites: .NET SDK 10.0.400, Codex CLI, Bash, curl and tar.

```sh
curl -fsSL https://raw.githubusercontent.com/thomas-fazzari/roslyn-codex-lsp/master/install.sh | bash
```

Open a new Codex session in your C# project. The `roslyn` MCP server is available globally.

## Contributors

[![Contributors](https://contrib.rocks/image?repo=thomas-fazzari/roslyn-codex-lsp)](https://github.com/thomas-fazzari/roslyn-codex-lsp/graphs/contributors)
