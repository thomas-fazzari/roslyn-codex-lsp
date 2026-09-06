# Roslyn Codex LSP

[![Documentation](https://img.shields.io/badge/docs-VitePress-646CFF?logo=vitepress&logoColor=white)](https://thomas-fazzari.github.io/roslyn-codex-lsp/)
[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/github/license/thomas-fazzari/roslyn-codex-lsp)](LICENSE)

C# diagnostics, navigation and refactoring in Codex, backed by the official Roslyn language server through the Model Context Protocol (MCP).

Find definitions, implementations and references. Preview and apply renames, fixes and refactorings.
The bridge reads saved files and works independently of your editor.

[Get started](https://thomas-fazzari.github.io/roslyn-codex-lsp/getting-started.html) · [Usage](https://thomas-fazzari.github.io/roslyn-codex-lsp/usage.html) · [Development](https://thomas-fazzari.github.io/roslyn-codex-lsp/development.html)

## Install

Prerequisites: .NET SDK 10.0.400, Codex CLI, Bash, curl and tar.
The SDK builds the tool during installation. Your C# project can target a different .NET version.

```sh
curl -fsSL https://raw.githubusercontent.com/thomas-fazzari/roslyn-codex-lsp/master/install.sh | bash
```

Open a new Codex session in your C# project. The `roslyn` MCP server is available globally.

## Contributors

[![Contributors](https://contrib.rocks/image?repo=thomas-fazzari/roslyn-codex-lsp)](https://github.com/thomas-fazzari/roslyn-codex-lsp/graphs/contributors)
