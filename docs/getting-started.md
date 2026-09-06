# Get started

## Install

Prerequisite: Codex CLI. On macOS and Linux, also use Bash, curl and tar.

macOS and Linux:

```sh
curl -fsSL https://raw.githubusercontent.com/thomas-fazzari/roslyn-codex-lsp/master/install.sh | bash
```

Windows x64, in PowerShell:

```powershell
& ([scriptblock]::Create((Invoke-RestMethod https://raw.githubusercontent.com/thomas-fazzari/roslyn-codex-lsp/master/install.ps1)))
```

The installer downloads the latest stable release, verifies its SHA-256 checksum and registers `roslyn` globally in Codex.
Before making changes, it asks for an installation directory and offers an optional skill for C# diagnostics, navigation and edits.
The skill comes from `master`, independently of the binary version.
Files are stored in `$XDG_DATA_HOME/roslyn-codex-lsp`, or `~/.local/share/roslyn-codex-lsp` when unset.
Windows uses `%LOCALAPPDATA%\roslyn-codex-lsp`.
Run the same command to update the bridge and skill.

Releases target macOS x64 and ARM64, Linux x64 and ARM64 with glibc, and Windows x64.

Use `--version vX.Y.Z` to select a release, or `--yes` for unattended installation.
When piping the shell installer, pass options through `bash -s -- --yes`.
PowerShell accepts `-Version vX.Y.Z` and `-Yes`.
Use `--no-skill` or `-NoSkill` to skip skill installation.

### Language server

The installer offers to install the official Roslyn language server. This optional step requires the .NET 10 SDK to run `dotnet tool install`.
Roslyn itself uses the .NET 10 runtime. The native bridge has no .NET runtime requirement.

If Roslyn is already installed, choose its executable during setup or pass `--server /path/to/roslyn-language-server` (`-Server` in PowerShell).
The installer then skips the .NET SDK check and server installation.

Your C# project can target a different .NET version. Keep its required SDK and workloads installed, including any SDK pinned in its `global.json`.

## Use it

Restore your C# solution with `dotnet restore`, then open a new Codex session in that project.
In the CLI, use `/mcp` to check that `roslyn` is connected.

```text
Use the Roslyn MCP to report diagnostics for a C# file, find definitions and
implementations, then preview a rename and a suggested fix. Do not apply changes.
```

Approve the tool call if prompted. The bridge uses the session's working directory as its workspace.
Save editor changes before requesting diagnostics.

## Configuration

Codex stores the global registration in `~/.codex/config.toml`, or under `CODEX_HOME` when set.
A project-level `.codex/config.toml` can override it.
See [Codex MCP configuration](https://developers.openai.com/codex/mcp/) for client settings and tool approvals.

Roslyn loads the solution on the first language request.
For large projects, set `tool_timeout_sec = 200` under `[mcp_servers.roslyn]`.

| Bridge argument     | Default                            |
| ------------------- | ---------------------------------- |
| `--workspace`       | Current working directory          |
| `--server`          | `roslyn-language-server` on `PATH` |
| `--startup-timeout` | 120 seconds                        |
| `--request-timeout` | 60 seconds                         |

Timeout arguments accept 1 to 600 seconds. Keep Codex's tool timeout above the startup and request timeout sum when increasing these limits.

## Remove

Run `codex mcp remove roslyn`, then delete the installation directory and optional skill directory printed by the installer.
