// Copyright (C) 2026 thomas-fazzari
// SPDX-License-Identifier: GPL-3.0-only

using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace RoslynCodexLsp.Tools;

[McpServerToolType]
internal sealed class DiagnosticsTool(LspTool lsp)
{
    internal const string Name = "diagnostics";

    [McpServerTool(
        Name = Name,
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false
    )]
    [Description(
        "Report C# compiler and analyzer diagnostics for saved files, with one-based positions."
    )]
    public Task<CallToolResult> DiagnosticsAsync(
        [Description(
            "Workspace-relative file path or glob. Omitting it scans every C# file in the workspace."
        )]
            string? file = null,
        [Description(ToolDescriptions.Limit)] int limit = LspRequest.DefaultResultLimit,
        CancellationToken cancellationToken = default
    ) =>
        lsp.ExecuteAsync(
            new LspRequest
            {
                Action = LspAction.Diagnostics,
                File = file,
                Limit = limit,
            },
            cancellationToken
        );
}
