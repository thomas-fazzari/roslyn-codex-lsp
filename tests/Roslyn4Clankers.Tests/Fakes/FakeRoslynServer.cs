// Copyright (C) 2026 thomas-fazzari
// SPDX-License-Identifier: GPL-3.0-only

using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json.Nodes;
using Nerdbank.Streams;
using Roslyn4Clankers.Lsp;
using StreamJsonRpc;

namespace Roslyn4Clankers.Tests.Fakes;

internal sealed class FakeRoslynServer : IRoslynServer
{
    private readonly Stream _serverSide;
    private readonly Stream _errorWriter;
    private readonly SystemTextJsonFormatter _formatter = new();
    private readonly JsonRpc _rpc;
    private readonly TaskCompletionSource _exited = new(
        TaskCreationOptions.RunContinuationsAsynchronously
    );

    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "JsonRpc owns and disposes the message handler."
    )]
    public FakeRoslynServer()
    {
        (Input, _serverSide) = FullDuplexStream.CreatePair();
        var (errorReader, errorWriter) = FullDuplexStream.CreatePair();
        _errorWriter = errorWriter;
        Error = new StreamReader(errorReader, Encoding.UTF8);
        _rpc = new JsonRpc(new HeaderDelimitedMessageHandler(_serverSide, _serverSide, _formatter));
        _rpc.AddLocalRpcTarget(new Target(this), options: null);
        _rpc.StartListening();
    }

    /// <summary>Request handler, keyed by LSP method. Unhandled requests return null.</summary>
    public Func<string, JsonNode?, JsonNode?> Handle { get; set; } = (_, _) => null;

    public Stream Input { get; }

    public Stream Output => Input;

    public StreamReader Error { get; }

    public bool HasExited { get; private set; }

    public async Task WriteErrorAsync(string text, CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        await _errorWriter.WriteAsync(bytes, cancellationToken);
        await _errorWriter.FlushAsync(cancellationToken);
    }

    /// <summary>Ends the process abruptly, as a crash would.</summary>
    public void Crash()
    {
        if (HasExited)
        {
            return;
        }

        HasExited = true;
        _rpc.Dispose();
        _formatter.Dispose();
        _serverSide.Dispose();
        _errorWriter.Dispose();
        _exited.TrySetResult();
    }

    public void Kill() => Crash();

    public Task WaitForExitAsync(CancellationToken cancellationToken) =>
        _exited.Task.WaitAsync(cancellationToken);

    public void Dispose()
    {
        Crash();
        Input.Dispose();
        Error.Dispose();
    }

    private sealed class Target(FakeRoslynServer server)
    {
        [JsonRpcMethod(LspMethods.Initialize, UseSingleObjectParameterDeserialization = true)]
        public static JsonObject Initialize(JsonObject parameters)
        {
            _ = parameters;
            return new JsonObject { ["capabilities"] = new JsonObject() };
        }

        // Roslyn reports the project load after the handshake
        [JsonRpcMethod(LspMethods.Initialized, UseSingleObjectParameterDeserialization = true)]
        public async Task InitializedAsync(JsonObject parameters)
        {
            _ = parameters;
            await server._rpc.NotifyAsync(LspMethods.WorkspaceProjectInitializationComplete);
        }

        [JsonRpcMethod(LspMethods.WorkspaceSymbol, UseSingleObjectParameterDeserialization = true)]
        public JsonNode? WorkspaceSymbol(JsonNode? parameters) =>
            server.Handle(LspMethods.WorkspaceSymbol, parameters);

        [JsonRpcMethod(LspMethods.Shutdown)]
        public static void Shutdown() { }

        [JsonRpcMethod(LspMethods.Exit)]
        public void Exit() => server.Crash();
    }
}
