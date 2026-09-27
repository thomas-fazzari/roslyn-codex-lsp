// Copyright (C) 2026 thomas-fazzari
// SPDX-License-Identifier: GPL-3.0-only

using System.ComponentModel;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Roslyn4Clankers.Tools;

[McpServerToolType]
internal sealed class ServerTool(LspTool lsp)
{
    internal const string Name = "lsp";

    [McpServerTool(
        Name = Name,
        ReadOnly = false,
        Destructive = true,
        Idempotent = false,
        OpenWorld = false
    )]
    [Description(
        "Inspect or reload the Roslyn session, or send a raw LSP request. Raw requests use zero-based LSP positions. Unknown methods and workspace/executeCommand require apply=true because they may write files."
    )]
    public Task<CallToolResult> ServerAsync(
        [Description("Session operation, or request for a raw LSP call.")] ServerAction action,
        [Description("LSP method for action=request.")] string? method = null,
        [Description("Raw LSP parameters for action=request.")] JsonObject? parameters = null,
        [Description("Allow a raw request that may change the workspace.")] bool apply = false,
        CancellationToken cancellationToken = default
    ) =>
        lsp.ExecuteAsync(
            new LspRequest
            {
                Action = action switch
                {
                    ServerAction.Status => LspAction.Status,
                    ServerAction.Capabilities => LspAction.Capabilities,
                    ServerAction.Reload => LspAction.Reload,
                    _ => LspAction.Request,
                },
                Method = method,
                Parameters = parameters,
                Apply = apply,
            },
            cancellationToken
        );
}

[JsonConverter(typeof(JsonStringEnumConverter<ServerAction>))]
internal enum ServerAction
{
    [JsonStringEnumMemberName("status")]
    Status = 0,

    [JsonStringEnumMemberName("capabilities")]
    Capabilities = 1,

    [JsonStringEnumMemberName("reload")]
    Reload = 2,

    [JsonStringEnumMemberName("request")]
    Request = 3,
}
