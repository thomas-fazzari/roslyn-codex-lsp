// Copyright (C) 2026 thomas-fazzari
// SPDX-License-Identifier: GPL-3.0-only

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO.Pipelines;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.Logging;
using StreamJsonRpc;

namespace RoslynCodexLsp.Lsp;

/// <summary>
/// Owns one Roslyn process and its workspace for the lifetime of the MCP host.
/// </summary>
internal sealed partial class RoslynSession(
    BridgeOptions options,
    WorkspacePaths paths,
    ILogger<RoslynSession> logger
) : IAsyncDisposable
{
    internal const int MaximumProtocolBytes = 16 * 1024 * 1024;

    private const int StderrBufferCharacters = 2048;
    private static readonly TimeSpan _shutdownTimeout = TimeSpan.FromSeconds(3);
    private Process? _process;
    private JsonRpc? _rpc;
    private SystemTextJsonFormatter? _formatter;
    private HeaderDelimitedMessageHandler? _messageHandler;
    private Task? _stderrTask;
    private CancellationTokenSource? _processLifetime;
    private TaskCompletionSource _initialized = NewCompletionSource();

    private bool _disposed;
    private bool _restartRequired;

    public JsonObject? Capabilities { get; private set; }

    public bool IsRunning =>
        !_restartRequired
        && _process is { HasExited: false }
        && _rpc is { IsDisposed: false, Completion.IsCompleted: false };

    public void RequireReload() => _restartRequired = true;

    public Func<
        JsonObject,
        CancellationToken,
        Task<JsonObject>
    >? ApplyWorkspaceEditAsync { get; set; }

    public async Task EnsureStartedAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (IsRunning && _initialized.Task.IsCompletedSuccessfully)
        {
            return;
        }

        await StopAsync().ConfigureAwait(false);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(options.StartupTimeout);
        try
        {
            StartProcess();
            await InitializeAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            await StopAsync().ConfigureAwait(false);
            throw new TimeoutException(
                "Roslyn did not finish loading the workspace before the startup timeout."
            );
        }
        catch
        {
            await StopAsync().ConfigureAwait(false);
            throw;
        }
    }

    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026",
        Justification = "Parameters are JsonNode values serialized through the generated JSON context, not reflected CLR objects."
    )]
    public async Task<JsonNode?> RequestAsync(
        string method,
        JsonNode? parameters,
        CancellationToken cancellationToken
    )
    {
        var rpc = _rpc ?? throw new InvalidOperationException("The Roslyn session is not started.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(options.RequestTimeout);
        try
        {
            return await rpc.InvokeWithParameterObjectAsync<JsonNode>(
                    method,
                    parameters,
                    timeout.Token
                )
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"Roslyn request '{method}' exceeded the request timeout.");
        }
    }

    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026",
        Justification = "Parameters are JsonNode values serialized through the generated JSON context, not reflected CLR objects."
    )]
    public async Task NotifyAsync(
        string method,
        JsonNode? parameters,
        CancellationToken cancellationToken
    )
    {
        var rpc = _rpc ?? throw new InvalidOperationException("The Roslyn session is not started.");
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            await rpc.NotifyWithParameterObjectAsync(method, parameters)
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            // A canceled write may still reach Roslyn
            // Reloading is necessary before trusting document versions
            _restartRequired = true;
            throw;
        }
    }

    public async Task ReloadAsync(CancellationToken cancellationToken)
    {
        await StopAsync().ConfigureAwait(false);
        await EnsureStartedAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await StopAsync().ConfigureAwait(false);
    }

    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026",
        Justification = "The formatter uses generated JSON metadata. No RPC-marshaled objects are exchanged."
    )]
    [UnconditionalSuppressMessage(
        "AOT",
        "IL3050",
        Justification = "The formatter uses generated JSON metadata. No RPC-marshaled objects are exchanged."
    )]
    internal static SystemTextJsonFormatter CreateFormatter()
    {
        var formatter = new SystemTextJsonFormatter();
        var serializerOptions = formatter.JsonSerializerOptions;
        var requestIdConverter = serializerOptions
            .Converters.OfType<JsonConverter<RequestId>>()
            .Single();
        serializerOptions.TypeInfoResolver = JsonTypeInfoResolver.Combine(
            new RequestIdResolver(requestIdConverter),
            BridgeJsonContext.Default
        );
        return formatter;
    }

    private static TaskCompletionSource NewCompletionSource() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private void StartProcess()
    {
        _restartRequired = false;
        var startInfo = new ProcessStartInfo(options.ServerPath)
        {
            WorkingDirectory = paths.Root,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("--stdio");
        startInfo.ArgumentList.Add("--autoLoadProjects");
        _process =
            Process.Start(startInfo)
            ?? throw new InvalidOperationException("The Roslyn process could not be started.");
        _processLifetime = new CancellationTokenSource();
        _stderrTask = ReadStderrAsync(_process.StandardError, _processLifetime.Token);
        StartRpc(_process);
        StartWatching();
    }

    private void StartRpc(Process process)
    {
        var reader = new BoundedPipeReader(
            PipeReader.Create(process.StandardOutput.BaseStream),
            MaximumProtocolBytes
        );
        _formatter = CreateFormatter();
        var writer = PipeWriter.Create(process.StandardInput.BaseStream);
        _messageHandler = new HeaderDelimitedMessageHandler(writer, reader, _formatter);
        _rpc = new JsonRpc(_messageHandler);
        var callbackMetadata = RpcTargetMetadata.FromShape<ClientCallbacks>();
        var callbacks = new ClientCallbacks(this, paths, logger, _initialized);
        _rpc.AddLocalRpcTarget(callbackMetadata, callbacks, options: null);
        _rpc.StartListening();
    }

    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026",
        Justification = "The initialization payload is a JsonObject serialized through the generated JSON context."
    )]
    private async Task InitializeAsync(CancellationToken cancellationToken)
    {
        var rpc =
            _rpc ?? throw new InvalidOperationException("The Roslyn transport is not started.");

        var initialize = ClientCapabilities.Create(paths);
        var result = await rpc.InvokeWithParameterObjectAsync<JsonObject>(
                LspMethods.Initialize,
                initialize,
                cancellationToken
            )
            .ConfigureAwait(false);
        Capabilities = result["capabilities"]?.AsObject();

        await NotifyAsync(LspMethods.Initialized, new JsonObject(), cancellationToken)
            .ConfigureAwait(false);

        var initializationOrDisconnect = Task.WhenAny(_initialized.Task, rpc.Completion);

        var completed = await initializationOrDisconnect
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        await completed.ConfigureAwait(false);
        if (completed != _initialized.Task)
        {
            throw new IOException("Roslyn disconnected while loading the workspace.");
        }

        _workspaceFiles = ScanWorkspace(cancellationToken);
    }

    private async Task StopAsync()
    {
        var process = _process;
        var rpc = _rpc;
        _rpc = null;
        _process = null;

        ClearWorkspace();

        if (process is null)
        {
            return;
        }

        try
        {
            await ShutdownAsync(rpc, process).ConfigureAwait(false);
        }
        finally
        {
            await DisposeProcessResourcesAsync(rpc, process).ConfigureAwait(false);
        }
    }

    private void ClearWorkspace()
    {
        Capabilities = null;
        _initialized = NewCompletionSource();
        _documents.Clear();
        _openedOrder.Clear();
        _versions.Clear();
        _workspaceFiles.Clear();
        _watchRegistrations.Clear();
        _watcher?.Dispose();
        _watcher = null;
        _pendingFiles.Clear();
        _scanRequested = 0;
    }

    private async Task DisposeProcessResourcesAsync(JsonRpc? rpc, Process process)
    {
        rpc?.Dispose();
        if (_messageHandler is not null)
        {
            await _messageHandler.DisposeAsync().ConfigureAwait(false);
        }

        _messageHandler = null;
        _formatter?.Dispose();
        _formatter = null;
        if (_processLifetime is not null)
        {
            await _processLifetime.CancelAsync().ConfigureAwait(false);
            _processLifetime.Dispose();
            _processLifetime = null;
        }

        if (_stderrTask is not null)
        {
            await _stderrTask.ConfigureAwait(false);
            _stderrTask = null;
        }

        process.Dispose();
    }

    private async Task ShutdownAsync(JsonRpc? rpc, Process process)
    {
        if (process.HasExited)
        {
            return;
        }

        using var timeout = new CancellationTokenSource(_shutdownTimeout);
        try
        {
            if (rpc is { IsDisposed: false })
            {
                await rpc.InvokeWithCancellationAsync(
                        LspMethods.Shutdown,
                        cancellationToken: timeout.Token
                    )
                    .ConfigureAwait(false);
                await rpc.NotifyAsync(LspMethods.Exit)
                    .WaitAsync(timeout.Token)
                    .ConfigureAwait(false);
            }

            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (Exception exception)
            when (exception
                    is OperationCanceledException
                        or IOException
                        or ConnectionLostException
                        or RemoteInvocationException
                        or ObjectDisposedException
            )
        {
            LogShutdownFailure(logger, exception);
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    private async Task ReadStderrAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var buffer = new char[StderrBufferCharacters];
        try
        {
            while (true)
            {
                var count = await reader.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (count == 0)
                {
                    return;
                }

                if (!logger.IsEnabled(LogLevel.Debug))
                {
                    continue;
                }

                var message = new string(buffer, 0, count);
                LogStderr(logger, message);
            }
        }
        catch (Exception exception)
            when (exception is OperationCanceledException or IOException or ObjectDisposedException)
        {
            LogStderrClosed(logger);
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Roslyn did not shut down cleanly.")]
    private static partial void LogShutdownFailure(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Roslyn stderr: {Message}")]
    private static partial void LogStderr(ILogger logger, string message);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Roslyn stderr stream closed.")]
    private static partial void LogStderrClosed(ILogger logger);
}
