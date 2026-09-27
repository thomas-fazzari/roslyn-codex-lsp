// Copyright (C) 2026 thomas-fazzari
// SPDX-License-Identifier: GPL-3.0-only

using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Roslyn4Clankers.Tools;

[McpServerToolType]
internal sealed class SymbolsTool(LspTool lsp)
{
    internal const string Name = "symbols";

    [McpServerTool(
        Name = Name,
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false
    )]
    [Description(
        "List the symbols declared in a C# file, or search workspace symbols by name. Positions are one-based."
    )]
    public Task<CallToolResult> SymbolsAsync(
        [Description("Workspace-relative file path. Omit it to search the workspace.")]
            string? file = null,
        [Description("Symbol name to search when file is omitted.")] string? query = null,
        [Description(ToolDescriptions.Limit)] int limit = LspRequest.DefaultResultLimit,
        CancellationToken cancellationToken = default
    ) =>
        lsp.ExecuteAsync(
            new LspRequest
            {
                Limit = limit,
                Action = LspAction.Symbols,
                File = file,
                Query = query,
            },
            cancellationToken
        );
}
