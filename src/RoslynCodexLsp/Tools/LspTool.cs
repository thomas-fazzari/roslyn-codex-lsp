// Copyright (C) 2026 thomas-fazzari
// SPDX-License-Identifier: GPL-3.0-only

using System.Collections.Frozen;
using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using RoslynCodexLsp.Editing;
using RoslynCodexLsp.Lsp;
using StreamJsonRpc;

namespace RoslynCodexLsp.Tools;

[McpServerToolType]
internal sealed partial class LspTool(
    BridgeOptions options,
    RoslynSession session,
    LspQueries queries,
    LspChanges changes,
    ILogger<LspTool> logger
) : IDisposable
{
    internal const string ToolName = "lsp";
    internal const string InvalidRequestErrorCode = "invalid_request";
    internal const string StaleEditErrorCode = "stale_edit";
    internal const string TimeoutErrorCode = "timeout";
    internal const string CancelledErrorCode = "cancelled";
    internal const string LspErrorCode = "lsp_error";
    internal const string IoErrorCode = "io_error";
    internal const string ResultTooLargeErrorCode = "result_too_large";

    internal const int MaximumResponseCharacters = 256_000;
    private const int MaximumErrorCharacters = 2_000;

    private static readonly FrozenSet<string> _readOnlyMethods = new[]
    {
        LspMethods.TextDocumentDefinition,
        LspMethods.TextDocumentTypeDefinition,
        LspMethods.TextDocumentImplementation,
        LspMethods.TextDocumentReferences,
        LspMethods.TextDocumentHover,
        LspMethods.TextDocumentDocumentSymbol,
        LspMethods.TextDocumentCompletion,
        LspMethods.CompletionItemResolve,
        LspMethods.TextDocumentSignatureHelp,
        LspMethods.TextDocumentCodeAction,
        LspMethods.CodeActionResolve,
        LspMethods.TextDocumentPrepareRename,
        LspMethods.TextDocumentRename,
        LspMethods.TextDocumentDiagnostic,
        LspMethods.WorkspaceDiagnostic,
        LspMethods.WorkspaceSymbol,
        LspMethods.WorkspaceSymbolResolve,
        LspMethods.TextDocumentPrepareCallHierarchy,
        LspMethods.CallHierarchyIncomingCalls,
        LspMethods.CallHierarchyOutgoingCalls,
        LspMethods.TextDocumentPrepareTypeHierarchy,
        LspMethods.TypeHierarchySupertypes,
        LspMethods.TypeHierarchySubtypes,
        LspMethods.TextDocumentFormatting,
        LspMethods.TextDocumentRangeFormatting,
    }.ToFrozenSet(StringComparer.Ordinal);

    private readonly SemaphoreSlim _gate = new(1, 1);

    [McpServerTool(Name = ToolName, ReadOnly = false, Destructive = true, OpenWorld = false)]
    [Description(
        "Use the persistent Roslyn language server for C# diagnostics, definitions, implementations, references, hover, symbols, renames and code actions. Coordinates are one-based UTF-16. Changes preview by default. Apply a returned proposalId with apply=true. Files changed by Codex are synchronized before queries. Raw LSP results retain zero-based positions."
    )]
    public async Task<CallToolResult> ExecuteAsync(
        LspRequest request,
        CancellationToken cancellationToken
    )
    {
        await _gate.WaitAsync(cancellationToken);
        var started = Stopwatch.GetTimestamp();
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(
                session.IsRunning
                    ? options.RequestTimeout
                    : options.StartupTimeout + options.RequestTimeout
            );
            var result = await DispatchAsync(request, timeout.Token);
            if (logger.IsEnabled(LogLevel.Debug))
            {
                var elapsedMilliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                OperationCompleted(logger, request.Action, elapsedMilliseconds);
            }

            return Response(
                request.Action,
                result,
                preserveApplication: IsApplicationRequest(request)
            );
        }
        catch (EditApplicationException exception)
        {
            return ReportApplicationFailure(request.Action, exception, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Failure(
                request.Action,
                TimeoutErrorCode,
                "The LSP operation timed out. No successful result is available."
            );
        }
        catch (StaleEditException exception)
        {
            return Failure(request.Action, StaleEditErrorCode, exception.Message);
        }
        catch (RemoteInvocationException exception)
        {
            return Failure(request.Action, LspErrorCode, exception.Message, exception.ErrorCode);
        }
        catch (Exception exception)
            when (exception
                    is ArgumentException
                        or InvalidOperationException
                        or IOException
                        or TimeoutException
                        or JsonException
            )
        {
            return ReportFailure(request.Action, exception);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();

    private CallToolResult ReportFailure(LspAction action, Exception exception)
    {
        OperationFailed(logger, action, exception);
        var code = exception switch
        {
            TimeoutException => TimeoutErrorCode,
            IOException => IoErrorCode,
            _ => InvalidRequestErrorCode,
        };
        return Failure(action, code, exception.Message);
    }

    private CallToolResult ReportApplicationFailure(
        LspAction action,
        EditApplicationException exception,
        CancellationToken cancellationToken
    )
    {
        changes.Clear();
        session.RequireReload();
        OperationFailed(logger, action, exception);
        return ApplicationFailure(action, exception, cancellationToken.IsCancellationRequested);
    }

    private static bool IsApplicationRequest(LspRequest request) =>
        request.Apply
        && (
            request.Action switch
            {
                LspAction.Rename or LspAction.RenameFile or LspAction.CodeActions => true,
                LspAction.Request => request.Method is LspMethods.WorkspaceExecuteCommand,
                _ => false,
            }
        );

    private async Task<JsonNode?> DispatchAsync(
        LspRequest request,
        CancellationToken cancellationToken
    )
    {
        if (request.Limit is < 1 or > LspRequest.MaximumResultLimit)
        {
            throw new ArgumentException(
                $"Limit must be between 1 and {LspRequest.MaximumResultLimit}.",
                nameof(request)
            );
        }

        switch (request.Action)
        {
            case LspAction.Status:
                return new JsonObject
                {
                    ["running"] = session.IsRunning,
                    ["workspace"] = options.WorkspaceRoot,
                    ["server"] = options.ServerPath,
                };
            case LspAction.Reload:
                changes.Clear();
                await session.ReloadAsync(cancellationToken);
                return new JsonObject { ["running"] = true };
        }

        await session.EnsureStartedAsync(cancellationToken);
        await session.SynchronizeAsync(cancellationToken);
        return request.Action switch
        {
            LspAction.Capabilities => session.Capabilities?.DeepClone(),
            LspAction.Diagnostics => await queries.DiagnosticsAsync(request, cancellationToken),
            LspAction.Symbols => await queries.SymbolsAsync(request, cancellationToken),
            LspAction.Definition
            or LspAction.TypeDefinition
            or LspAction.Implementation
            or LspAction.References
            or LspAction.Hover => await queries.NavigationAsync(request, cancellationToken),
            LspAction.Rename or LspAction.RenameFile or LspAction.CodeActions =>
                await changes.ExecuteAsync(request, cancellationToken),
            LspAction.Request => await RawRequestAsync(request, cancellationToken),
            _ => throw new ArgumentException("Unsupported LSP action.", nameof(request)),
        };
    }

    private async Task<JsonNode?> RawRequestAsync(
        LspRequest request,
        CancellationToken cancellationToken
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Method, nameof(request));

        if (IsManagedMethod(request.Method))
        {
            throw new ArgumentException(
                "The bridge owns lifecycle and synchronization methods.",
                nameof(request)
            );
        }

        if (!_readOnlyMethods.Contains(request.Method) && !request.Apply)
        {
            throw new ArgumentException(
                "This method may change the workspace. Set apply=true explicitly.",
                nameof(request)
            );
        }

        if (request.Parameters?["textDocument"]?["uri"] is JsonValue uri)
        {
            await session.OpenDocumentAsync(uri.GetValue<string>(), cancellationToken);
        }

        if (request.Method is LspMethods.WorkspaceExecuteCommand)
        {
            return await changes.ExecuteCommandAsync(
                request.Parameters ?? new JsonObject(),
                cancellationToken
            );
        }

        return await session.RequestAsync(request.Method, request.Parameters, cancellationToken);
    }

    private static bool IsManagedMethod(string method)
    {
        var isLifecycle =
            method
            is LspMethods.Initialize
                or LspMethods.Initialized
                or LspMethods.Shutdown
                or LspMethods.Exit;

        var isSynchronization =
            method.StartsWith("textDocument/did", StringComparison.Ordinal)
            || method.StartsWith("workspace/did", StringComparison.Ordinal);

        return isLifecycle || isSynchronization || method is LspMethods.WorkspaceApplyEdit;
    }

    internal static CallToolResult Response(
        LspAction action,
        JsonNode? result,
        JsonObject? error = null,
        bool preserveApplication = false
    )
    {
        var payload = new JsonObject
        {
            ["action"] = JsonSerializer.SerializeToNode(
                action,
                BridgeJsonContext.Default.LspAction
            ),
        };
        if (error is not null)
        {
            payload["error"] = error;
        }

        if (result is not null || error is null)
        {
            payload["result"] = result;
        }

        var element = JsonSerializer.SerializeToElement(
            payload,
            BridgeJsonContext.Default.JsonObject
        );
        if (element.GetRawText().Length > MaximumResponseCharacters)
        {
            if (!preserveApplication || result is not JsonObject application)
            {
                return Failure(
                    action,
                    ResultTooLargeErrorCode,
                    $"The result exceeds {MaximumResponseCharacters} characters. Narrow the query or lower limit."
                );
            }

            CompactApplication(payload, application);
            element = JsonSerializer.SerializeToElement(
                payload,
                BridgeJsonContext.Default.JsonObject
            );
        }

        var isError = error is not null;
        return new CallToolResult
        {
            IsError = isError,
            StructuredContent = element,
            Content =
            [
                new TextContentBlock
                {
                    Text = isError
                        ? "LSP operation failed. See the structured error and any applied changes."
                        : "LSP operation completed. See the structured result.",
                },
            ],
        };
    }

    private static void CompactApplication(JsonObject payload, JsonObject result)
    {
        if (result.Remove("commandResult"))
        {
            result["commandResultTruncated"] = true;
        }

        if (
            payload.ToJsonString(BridgeJsonContext.Default.Options).Length
            <= MaximumResponseCharacters
        )
        {
            return;
        }

        var files = result["files"]!.AsArray();
        result.Remove("files");
        var included = new JsonArray();
        result["files"] = included;
        result["filesTruncated"] = true;
        var remaining =
            MaximumResponseCharacters
            - payload.ToJsonString(BridgeJsonContext.Default.Options).Length;
        foreach (var file in files)
        {
            var length =
                file!.ToJsonString(BridgeJsonContext.Default.Options).Length
                + (included.Count == 0 ? 0 : 1);
            if (length > remaining)
            {
                break;
            }

            included.Add(file.DeepClone());
            remaining -= length;
        }
    }

    internal static CallToolResult ApplicationFailure(
        LspAction action,
        EditApplicationException exception,
        bool cancellationRequested
    )
    {
        var cause = exception.InnerException!;
        var code = cause switch
        {
            OperationCanceledException => cancellationRequested
                ? CancelledErrorCode
                : TimeoutErrorCode,
            TimeoutException => TimeoutErrorCode,
            RemoteInvocationException => LspErrorCode,
            IOException or UnauthorizedAccessException => IoErrorCode,
            _ => InvalidRequestErrorCode,
        };
        exception.Result["synchronized"] = false;
        return Response(
            action,
            exception.Result,
            new JsonObject
            {
                ["code"] = code,
                ["message"] = cause.Message[
                    ..Math.Min(cause.Message.Length, MaximumErrorCharacters)
                ],
                ["phase"] = JsonSerializer.SerializeToNode(
                    exception.Phase,
                    BridgeJsonContext.Default.EditApplicationPhase
                ),
                ["path"] = exception.FailedPath,
                ["operation"] = exception.FailedOperation,
            },
            preserveApplication: true
        );
    }

    private static CallToolResult Failure(
        LspAction action,
        string code,
        string message,
        int? rpcCode = null
    ) =>
        Response(
            action,
            result: null,
            new JsonObject
            {
                ["code"] = code,
                ["message"] = message,
                ["rpcCode"] = rpcCode,
            }
        );

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "LSP {Action} completed in {ElapsedMilliseconds} ms"
    )]
    private static partial void OperationCompleted(
        ILogger logger,
        LspAction action,
        double elapsedMilliseconds
    );

    [LoggerMessage(Level = LogLevel.Warning, Message = "LSP {Action} failed")]
    private static partial void OperationFailed(
        ILogger logger,
        LspAction action,
        Exception exception
    );
}
