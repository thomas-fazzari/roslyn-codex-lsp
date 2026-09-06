// Copyright (C) 2026 thomas-fazzari
// SPDX-License-Identifier: GPL-3.0-only

using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using PolyType;
using StreamJsonRpc;

namespace RoslynCodexLsp.Lsp;

/// <summary>
/// Answers Roslyn requests and consumes its notifications while tool calls are pending.
/// </summary>
[GenerateShape(IncludeMethods = MethodShapeFlags.AllPublic)]
internal sealed partial class ClientCallbacks(
    RoslynSession session,
    WorkspacePaths paths,
    ILogger<RoslynSession> logger,
    TaskCompletionSource initialized
)
{
    private const int MaximumLogMessageCharacters = 2048;

    [JsonRpcMethod(
        LspMethods.WorkspaceConfiguration,
        UseSingleObjectParameterDeserialization = true
    )]
    public static JsonArray Configuration(JsonObject parameters)
    {
        var result = new JsonArray();
        if (parameters["items"] is not JsonArray items)
        {
            return result;
        }

        for (var index = 0; index < items.Count; index++)
        {
            result.Add(null);
        }

        return result;
    }

    [JsonRpcMethod(LspMethods.WorkspaceWorkspaceFolders)]
    public JsonArray WorkspaceFolders() => ClientCapabilities.WorkspaceFolders(paths);

    [JsonRpcMethod(LspMethods.WorkspaceApplyEdit, UseSingleObjectParameterDeserialization = true)]
    public Task<JsonObject> ApplyEditAsync(
        JsonObject parameters,
        CancellationToken cancellationToken
    )
    {
        if (session.ApplyWorkspaceEditAsync is { } apply && parameters["edit"] is JsonObject edit)
        {
            return apply(edit, cancellationToken);
        }

        return Task.FromResult(
            new JsonObject
            {
                ["applied"] = false,
                ["failureReason"] = "No workspace edit is being applied by the current tool call.",
            }
        );
    }

    [JsonRpcMethod(
        LspMethods.ClientRegisterCapability,
        UseSingleObjectParameterDeserialization = true
    )]
    public void RegisterCapability(JsonObject parameters) =>
        session.RegisterCapabilities(parameters);

    [JsonRpcMethod(
        LspMethods.ClientUnregisterCapability,
        UseSingleObjectParameterDeserialization = true
    )]
    public void UnregisterCapability(JsonObject parameters) =>
        session.UnregisterCapabilities(parameters);

    [JsonRpcMethod(LspMethods.WorkspaceProjectInitializationComplete)]
    public void InitializationComplete() => initialized.TrySetResult();

    [JsonRpcMethod(LspMethods.WindowLogMessage, UseSingleObjectParameterDeserialization = true)]
    public void LogMessage(JsonObject parameters)
    {
        if (!logger.IsEnabled(LogLevel.Debug))
        {
            return;
        }

        var message = parameters["message"]?.GetValue<string>() ?? string.Empty;
        var boundedMessage = message
            .AsSpan(0, Math.Min(message.Length, MaximumLogMessageCharacters))
            .ToString();
        LogRoslynMessage(logger, boundedMessage);
    }

    [JsonRpcMethod(LspMethods.WindowShowMessage, UseSingleObjectParameterDeserialization = true)]
    public void ShowMessage(JsonObject parameters) => LogMessage(parameters);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Roslyn: {Message}")]
    private static partial void LogRoslynMessage(ILogger logger, string message);

    [JsonRpcMethod(
        LspMethods.TextDocumentPublishDiagnostics,
        UseSingleObjectParameterDeserialization = true
    )]
    public static void PublishDiagnostics(JsonObject parameters)
    {
        // Diagnostics are pulled for the synchronized document when the tool requests them
        _ = parameters;
    }

    [JsonRpcMethod(LspMethods.Progress, UseSingleObjectParameterDeserialization = true)]
    public static void Progress(JsonObject parameters) => _ = parameters;

    [JsonRpcMethod(
        LspMethods.WindowWorkDoneProgressCreate,
        UseSingleObjectParameterDeserialization = true
    )]
    public static void CreateProgress(JsonObject parameters) => _ = parameters;

    [JsonRpcMethod(LspMethods.WorkspaceDiagnosticRefresh)]
    public static void RefreshDiagnostics() { }
}
