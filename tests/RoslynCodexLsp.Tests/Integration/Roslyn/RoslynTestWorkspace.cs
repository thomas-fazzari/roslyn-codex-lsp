// Copyright (C) 2026 thomas-fazzari
// SPDX-License-Identifier: GPL-3.0-only

using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using RoslynCodexLsp.Tools;

namespace RoslynCodexLsp.Tests.Integration.Roslyn;

/// <summary>
/// Runs the built MCP host against an isolated copy of a real C# solution.
/// </summary>
/// <remarks>
/// Set ROSLYN_CODEX_TEST_EXECUTABLE to an absolute executable path to test a published bridge.
/// Otherwise, the fixture launches the built assembly with dotnet.
/// Set ROSLYN_CODEX_TEST_SERVER to select an installed Roslyn server by its executable path.
/// </remarks>
internal sealed class RoslynTestWorkspace : IAsyncDisposable
{
    private readonly DirectoryInfo _directory;
    private readonly CancellationToken _cancellationToken;
    private Process? _process;
    private Task<string>? _standardError;
    private McpClient? _client;

    private RoslynTestWorkspace(DirectoryInfo directory, CancellationToken cancellationToken)
    {
        _directory = directory;
        _cancellationToken = cancellationToken;
    }

    public McpClient Client =>
        _client ?? throw new InvalidOperationException("The MCP client is not connected.");

    public static async Task<RoslynTestWorkspace> CreateAsync(CancellationToken cancellationToken)
    {
        var workspace = new RoslynTestWorkspace(
            Directory.CreateTempSubdirectory("roslyn-codex-lsp-tests-"),
            cancellationToken
        );
        try
        {
            workspace.CopyFixture();
            await workspace.RestoreAsync().ConfigureAwait(false);
            await workspace.ConnectAsync().ConfigureAwait(false);
            return workspace;
        }
        catch
        {
            await workspace.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public string FilePath(string relativePath) => Path.Combine(_directory.FullName, relativePath);

    public Task<string> ReadFileAsync(string relativePath) =>
        File.ReadAllTextAsync(FilePath(relativePath), _cancellationToken);

    public Task WriteFileAsync(string relativePath, string text) =>
        File.WriteAllTextAsync(FilePath(relativePath), text, _cancellationToken);

    public async Task<JsonObject> CallAsync(LspRequest request)
    {
        var response = await CallRawAsync(request).ConfigureAwait(false);
        response.StructuredContent.Should().NotBeNull(JsonSerializer.Serialize(response));
        var content = JsonSerializer.SerializeToNode(response.StructuredContent)!.AsObject();
        response.IsError.Should().NotBeTrue(content.ToJsonString());
        return content;
    }

    public async Task<CallToolResult> CallRawAsync(LspRequest request)
    {
        var (tool, arguments) = ToolCall(request);
        return await Client
            .CallToolAsync(tool, arguments, cancellationToken: _cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<LspRequest> AtAsync(LspAction action, string file, string symbol)
    {
        var text = await File.ReadAllTextAsync(FilePath(file), _cancellationToken)
            .ConfigureAwait(false);
        var offset = text.IndexOf(symbol, StringComparison.Ordinal);
        offset.Should().BeGreaterThanOrEqualTo(0);
        var prefix = text[..offset];
        return new LspRequest
        {
            Action = action,
            File = file,
            Line = prefix.Count(character => character == '\n') + 1,
            Character = offset - prefix.LastIndexOf('\n'),
        };
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_client is not null)
            {
                await _client.DisposeAsync().ConfigureAwait(false);
            }

            if (_process is not null)
            {
                await StopAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            _process?.Dispose();
            _directory.Delete(recursive: true);
        }
    }

    private void CopyFixture()
    {
        var source = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Workspace");
        foreach (var file in Directory.EnumerateFiles(source, "*.txt", SearchOption.AllDirectories))
        {
            var destination = FilePath(Path.GetRelativePath(source, file)[..^4]);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination);
        }
    }

    /// <summary>
    /// Sends a request the way an agent would: to the tool that owns its action, with flat arguments.
    /// </summary>
    private static (string Tool, Dictionary<string, object?> Arguments) ToolCall(LspRequest request)
    {
        var (tool, parameters) = request.Action switch
        {
            LspAction.Diagnostics => (DiagnosticsTool.Name, ["file", "severity", "limit"]),
            LspAction.Symbols => (SymbolsTool.Name, ["file", "query", "limit"]),
            LspAction.Definition
            or LspAction.TypeDefinition
            or LspAction.Implementation
            or LspAction.References
            or LspAction.Hover
            or LspAction.Callers
            or LspAction.Callees
            or LspAction.Supertypes
            or LspAction.Subtypes => (
                NavigateTool.Name,
                new[] { "action", "file", "line", "character", "limit" }
            ),
            LspAction.Rename or LspAction.RenameFile or LspAction.CodeActions => (
                EditTool.Name,
                [
                    "action",
                    "file",
                    "line",
                    "character",
                    "endLine",
                    "endCharacter",
                    "newName",
                    "actionIndex",
                    "proposalId",
                    "apply",
                    "limit",
                ]
            ),
            _ => (ServerTool.Name, ["action", "method", "parameters", "apply"]),
        };
        var values = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["action"] = JsonSerializer
                .SerializeToNode(request.Action, BridgeJsonContext.Default.LspAction)!
                .GetValue<string>(),
            ["file"] = request.File,
            ["severity"] = JsonSerializer
                .SerializeToNode(request.Severity, BridgeJsonContext.Default.DiagnosticSeverity)!
                .GetValue<string>(),
            ["line"] = request.Line,
            ["character"] = request.Character,
            ["endLine"] = request.EndLine,
            ["endCharacter"] = request.EndCharacter,
            ["query"] = request.Query,
            ["newName"] = request.NewName,
            ["actionIndex"] = request.ActionIndex,
            ["proposalId"] = request.ProposalId,
            ["method"] = request.Method,
            ["parameters"] = request.Parameters,
            ["apply"] = request.Apply ? true : null,
            ["limit"] = request.Limit,
        };
        return (
            tool,
            parameters
                .Where(name => values[name] is not null)
                .ToDictionary(name => name, name => values[name], StringComparer.Ordinal)
        );
    }

    private async Task RestoreAsync()
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = _directory.FullName,
            ArgumentList = { "restore", "Workspace.slnx", "--nologo", "--verbosity", "quiet" },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        process.Start().Should().BeTrue();
        var output = process.StandardOutput.ReadToEndAsync(_cancellationToken);
        var error = process.StandardError.ReadToEndAsync(_cancellationToken);
        try
        {
            await process.WaitForExitAsync(_cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }
        process
            .ExitCode.Should()
            .Be(0, await output.ConfigureAwait(false) + await error.ConfigureAwait(false));
    }

    private async Task ConnectAsync()
    {
        var executable = Environment.GetEnvironmentVariable("ROSLYN_CODEX_TEST_EXECUTABLE");
        _process = new Process
        {
            StartInfo = new ProcessStartInfo(executable ?? "dotnet")
            {
                WorkingDirectory = _directory.FullName,
                ArgumentList =
                {
                    BridgeOptions.WorkspaceArgument,
                    _directory.FullName,
                    BridgeOptions.StartupTimeoutArgument,
                    "60",
                    BridgeOptions.RequestTimeoutArgument,
                    "30",
                },
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            },
        };
        if (executable is null)
        {
            _process.StartInfo.ArgumentList.Insert(0, typeof(LspTool).Assembly.Location);
        }

        var server = Environment.GetEnvironmentVariable("ROSLYN_CODEX_TEST_SERVER");
        if (!string.IsNullOrWhiteSpace(server))
        {
            _process.StartInfo.ArgumentList.Add(BridgeOptions.ServerArgument);
            _process.StartInfo.ArgumentList.Add(server);
        }

        _process.Start().Should().BeTrue();
        _standardError = _process.StandardError.ReadToEndAsync(CancellationToken.None);
        _client = await McpClient
            .CreateAsync(
                new StreamClientTransport(
                    _process.StandardInput.BaseStream,
                    _process.StandardOutput.BaseStream
                ),
                cancellationToken: _cancellationToken
            )
            .ConfigureAwait(false);
    }

    private async Task StopAsync()
    {
        _process!.StandardInput.Close();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            await _process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _process.Kill(entireProcessTree: true);
            await _process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            throw new TimeoutException("The MCP host did not stop after its input was closed.");
        }

        var standardError = await _standardError!.ConfigureAwait(false);
        TestContext.Current.TestOutputHelper?.WriteLine(standardError);
        _process.ExitCode.Should().Be(0, standardError);
    }
}
