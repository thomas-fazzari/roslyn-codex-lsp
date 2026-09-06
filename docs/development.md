# Development

Prerequisites: Git, Make, .NET SDK 10.0.400 and Bun 1.4.0. Run commands from the repository root.

```sh
make install
make check
```

## Commands

| Command                 | Purpose                                                          |
| ----------------------- | ---------------------------------------------------------------- |
| `make build`            | Build the bridge with analyzers                                  |
| `make check`            | Check formatting, build documentation and C#, and run unit tests |
| `make test-integration` | Run explicit tests against the real Roslyn server                |
| `make format`           | Format C#, Markdown and tooling                                  |
| `make lint-auto`        | Check staged files, or local changes if nothing is staged        |

Builds use Debug by default. Add `CONFIGURATION=Release` for a release build.
Stop any bridge process using the build output before rebuilding it.

Copy `.env.copyme` to `.env` and set `EXTRA_PATH` if executable directories are missing from `PATH`.
These settings apply to Make commands. Codex uses its own MCP environment.

## Tests

Unit tests cover protocol buffers, text positions and workspace edits.
Integration tests launch the bridge through the MCP client SDK and start the installed `roslyn-language-server`.
They check diagnostics, navigation, renames, code actions, stale edits and shutdown in temporary workspaces.

Fixtures live under `tests/RoslynCodexLsp.Tests/Fixtures` as `.txt` files.
Integration tests copy them into temporary C# projects, so deliberate errors do not affect the test build.

## Layout

| Path                         | Contents                                     |
| ---------------------------- | -------------------------------------------- |
| `src/RoslynCodexLsp`         | MCP tool, Roslyn session and workspace edits |
| `tests/RoslynCodexLsp.Tests` | Unit tests, integration tests and fixtures   |
| `eng`                        | Make targets and tooling scripts             |
| `docs`                       | VitePress pages and configuration            |

The bridge uses the official [Model Context Protocol (MCP) SDK](https://csharp.sdk.modelcontextprotocol.io/) and [StreamJsonRpc](https://microsoft.github.io/vs-streamjsonrpc/).
Protocol messages use standard output. Logs use standard error.
