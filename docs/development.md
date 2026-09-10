# Development

Prerequisites: Git, Bash, [just](https://just.systems) 1.58.0, .NET SDK 10.0.400 and Bun 1.4.2.
On Windows, use Git Bash. Run commands from the repository root.

```sh
just install
just check
```

## Commands

| Command                 | Purpose                                                          |
| ----------------------- | ---------------------------------------------------------------- |
| `just build`            | Build the bridge with analyzers                                  |
| `just check`            | Check formatting, build documentation and C#, and run unit tests |
| `just test-integration` | Run explicit tests against the real Roslyn server                |
| `just format`           | Format task recipes, C#, Markdown and tooling                    |
| `just tooling::auto`    | Check staged files, or local changes if nothing is staged        |

Run `just` to list commands, including the `backend`, `docs`, `tooling` and `ci` modules.
For example, `just docs::dev` serves the documentation and `just tooling::hooks-install` enables Git hooks.

Builds use Debug by default. Run `CONFIGURATION=Release just build` for a release build.
Stop any bridge process using the build output before rebuilding it.

Copy `.env.copyme` to `.env` and set `EXTRA_PATH` if executable directories are missing from `PATH`.
These settings apply to just commands. Existing environment variables take precedence over `.env`.
Set `DOTNET`, `BUN` or `TEST_ARGS` in the environment to override executables or add test arguments.
Codex uses its own MCP environment.

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
| `eng`                        | Just modules and tooling scripts             |
| `docs`                       | VitePress pages and configuration            |

The bridge uses the official [Model Context Protocol (MCP) SDK](https://csharp.sdk.modelcontextprotocol.io/) and [StreamJsonRpc](https://microsoft.github.io/vs-streamjsonrpc/).
Protocol messages use standard output. Logs use standard error.

## Native builds and releases

Native AOT is enabled in the bridge project. Tests run on the .NET runtime.
Publish with `RUNTIME_ID=osx-arm64 just backend::publish`, using the runtime identifier for your platform. Output goes to `artifacts/native`.
Native compilation requires the [platform build tools](https://learn.microsoft.com/dotnet/core/deploying/native-aot/#prerequisites).

Set `ROSLYN_CODEX_TEST_EXECUTABLE` to the absolute path of the published executable, then run `just test-integration` to test it against Roslyn.

The build workflow validates all five release targets. Its scripts live in `eng/ci`.
Push a tag such as `v0.1.0` to publish a release after all checks pass.
Tags such as `v0.1.0-rc.1` create prereleases. Each release contains platform archives and `SHA256SUMS`.
