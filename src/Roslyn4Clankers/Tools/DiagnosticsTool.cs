// Copyright (C) 2026 thomas-fazzari
// SPDX-License-Identifier: GPL-3.0-only

using System.ComponentModel;
using System.Text.Json.Serialization;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Roslyn4Clankers.Tools;

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
        [Description(
            "Lowest severity to report. The default reports errors and warnings. Use information or hint to include analyzer suggestions."
        )]
            DiagnosticSeverity severity = DiagnosticSeverity.Warning,
        [Description(ToolDescriptions.Limit)] int limit = LspRequest.DefaultResultLimit,
        CancellationToken cancellationToken = default
    ) =>
        lsp.ExecuteAsync(
            new LspRequest
            {
                Action = LspAction.Diagnostics,
                File = file,
                Severity = severity,
                Limit = limit,
            },
            cancellationToken
        );
}

/// <summary>
/// LSP diagnostic severities, from most to least severe.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<DiagnosticSeverity>))]
internal enum DiagnosticSeverity
{
    [JsonStringEnumMemberName("error")]
    Error = 1,

    [JsonStringEnumMemberName("warning")]
    Warning = 2,

    [JsonStringEnumMemberName("information")]
    Information = 3,

    [JsonStringEnumMemberName("hint")]
    Hint = 4,
}
