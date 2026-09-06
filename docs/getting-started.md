# Get started

## Install

Prerequisites: .NET SDK 10.0.400, Codex CLI, Bash, curl and tar.

```sh
curl -fsSL https://raw.githubusercontent.com/thomas-fazzari/roslyn-codex-lsp/master/install.sh | bash
```

The script builds the bridge, installs the official Roslyn language server and registers `roslyn` globally in Codex.
It asks for an installation directory and offers a Codex skill for C# diagnostics, navigation and edits before confirming the changes.
Files are stored in `$XDG_DATA_HOME/roslyn-codex-lsp`, or `~/.local/share/roslyn-codex-lsp` when unset.
Run the same command to update.

From a source checkout, use `bash install.sh --source .`.
Add `--yes` to accept defaults without a terminal, using `bash -s -- --yes` when piping the script.

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
