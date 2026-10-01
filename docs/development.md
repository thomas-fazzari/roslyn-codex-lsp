# Development

Prerequisites: Git, Bash and [mise](https://mise.jdx.dev).
mise installs the .NET SDK, Bun, CSharpier and lefthook at the versions pinned in `mise.toml`.
On Windows, use Git Bash. Run commands from the repository root.

```sh
mise install
mise run install
mise run check
```

## Commands

| Command                     | Purpose                                                            |
| --------------------------- | ------------------------------------------------------------------ |
| `mise run build`            | Build the bridge with analyzers                                    |
| `mise run check`            | Check formatting, build documentation and C#, and run unit tests   |
| `mise run test-integration` | Run explicit tests against the real Roslyn server                  |
| `mise run format`           | Format C#, Markdown and tooling                                    |
| `mise run hooks:install`    | Enable Git hooks: format staged files on commit, check before push |

Run `mise tasks` to list commands, including the `backend`, `docs`, `tooling`, `hooks` and `ci` groups.
For example, `mise run docs:dev` serves the documentation.

Builds use Debug by default. Run `CONFIGURATION=Release mise run build` for a release build.
Stop any bridge process using the build output before rebuilding it.

Copy `.env.copyme` to `.env` for local settings. mise loads it for every task.
Existing environment variables take precedence over `.env`.
Arguments after `--` go to the underlying command, for example `mise run test -- --filter-class Roslyn4Clankers.Tests.Unit.Symbols.SymbolNameTests`.
MCP clients launch the bridge with their own environment.

## Tests

Unit tests cover protocol buffers, text positions and workspace edits.
Integration tests launch the bridge through the MCP client SDK and start the installed `roslyn-language-server`.
They check diagnostics, navigation, renames, code actions, stale edits and shutdown in temporary workspaces.
Set `ROSLYN4CLANKERS_TEST_SERVER` to the server executable when it is not on `PATH`.

Fixtures live under `tests/Roslyn4Clankers.Tests/Fixtures` as `.txt` files.
Integration tests copy them into temporary C# projects, so deliberate errors do not affect the test build.

## Layout

| Path                          | Contents                                     |
| ----------------------------- | -------------------------------------------- |
| `src/Roslyn4Clankers`         | MCP tool, Roslyn session and workspace edits |
| `tests/Roslyn4Clankers.Tests` | Unit tests, integration tests and fixtures   |
| `mise.toml`                   | Tool versions and tasks                      |
| `eng`                         | CI task scripts and tooling scripts          |
| `docs`                        | VitePress pages and configuration            |

The bridge uses the official [Model Context Protocol (MCP) SDK](https://csharp.sdk.modelcontextprotocol.io/) and [StreamJsonRpc](https://microsoft.github.io/vs-streamjsonrpc/).
Protocol messages use standard output. Logs use standard error.

## Native builds and releases

Native ahead-of-time (AOT) compilation is enabled in the bridge project. Tests run on the .NET runtime.
Publish with `RUNTIME_ID=osx-arm64 mise run backend:publish`, using the runtime identifier for your platform. Output goes to `artifacts/native`.
Native compilation requires the [platform build tools](https://learn.microsoft.com/dotnet/core/deploying/native-aot/#prerequisites).

Set `ROSLYN4CLANKERS_TEST_EXECUTABLE` to the absolute path of the published executable, then run `mise run test-integration` to test it against Roslyn.

The build workflow validates all five release targets. Its scripts live in `eng/tasks/ci`.
Push a tag such as `v0.1.0` to publish a release after all checks pass.
Tags such as `v0.1.0-rc.1` create prereleases. Each release contains platform archives and `SHA256SUMS`.
