# Roslyn for Clankers

[![CI](https://img.shields.io/github/actions/workflow/status/thomas-fazzari/roslyn-for-clankers/ci.yml?branch=master&label=CI)](https://github.com/thomas-fazzari/roslyn-for-clankers/actions/workflows/ci.yml)
[![Version](https://img.shields.io/github/v/release/thomas-fazzari/roslyn-for-clankers)](https://github.com/thomas-fazzari/roslyn-for-clankers/releases/latest)
[![Documentation](https://img.shields.io/badge/docs-VitePress-646CFF?logo=vitepress&logoColor=white)](https://thomas-fazzari.github.io/roslyn-for-clankers/)
[![License](https://img.shields.io/github/license/thomas-fazzari/roslyn-for-clankers)](LICENSE)

C# diagnostics, navigation and refactoring in Codex and Claude Code, backed by the official Roslyn language server through the Model Context Protocol (MCP).

Find definitions, implementations and references. Preview and apply renames, fixes and refactorings.
The bridge reads saved files and works independently of your editor.

[Get started](https://thomas-fazzari.github.io/roslyn-for-clankers/getting-started.html) · [Usage](https://thomas-fazzari.github.io/roslyn-for-clankers/usage.html) · [Development](https://thomas-fazzari.github.io/roslyn-for-clankers/development.html)

## Install

Prerequisite: Codex CLI or Claude Code. The installer can use an existing Roslyn language server or install one for you.

macOS and Linux, with Bash, curl and tar:

```sh
curl -fsSL https://raw.githubusercontent.com/thomas-fazzari/roslyn-for-clankers/master/install.sh | bash
```

Windows x64, in PowerShell:

```powershell
& ([scriptblock]::Create((Invoke-RestMethod https://raw.githubusercontent.com/thomas-fazzari/roslyn-for-clankers/master/install.ps1)))
```

Open a new session in your C# project. The `roslyn` MCP server is available globally in each installed client.

## Contributors

[![Contributors](https://contrib.rocks/image?repo=thomas-fazzari/roslyn-for-clankers)](https://github.com/thomas-fazzari/roslyn-for-clankers/graphs/contributors)
