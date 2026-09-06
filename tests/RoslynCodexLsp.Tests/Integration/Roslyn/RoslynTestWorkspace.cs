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

    public async Task<CallToolResult> CallRawAsync(LspRequest request) =>
        await Client
            .CallToolAsync(
                LspTool.ToolName,
                new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["request"] = JsonSerializer.SerializeToNode(
                        request,
                        JsonSerializerOptions.Web
                    ),
                },
                cancellationToken: _cancellationToken
            )
            .ConfigureAwait(false);

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
        _process = new Process
        {
            StartInfo = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = _directory.FullName,
                ArgumentList =
                {
                    typeof(LspTool).Assembly.Location,
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
