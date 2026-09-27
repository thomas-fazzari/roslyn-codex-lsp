// Copyright (C) 2026 thomas-fazzari
// SPDX-License-Identifier: GPL-3.0-only

using System.Text.Json.Nodes;

namespace RoslynCodexLsp.Tools;

/// <summary>
/// One operation dispatched by a tool to the shared Roslyn session.
/// </summary>
internal sealed record LspRequest
{
    internal const int DefaultResultLimit = 50;
    internal const int MaximumResultLimit = 250;

    public required LspAction Action { get; init; }

    public string? File { get; init; }

    public int? Line { get; init; }

    public int? Character { get; init; }

    public int? EndLine { get; init; }

    public int? EndCharacter { get; init; }

    public string? Query { get; init; }

    public string? NewName { get; init; }

    public int? ActionIndex { get; init; }

    public bool Apply { get; init; }

    public string? ProposalId { get; init; }

    public string? Method { get; init; }

    public JsonObject? Parameters { get; init; }

    public DiagnosticSeverity Severity { get; init; } = DiagnosticSeverity.Warning;

    public int Limit { get; init; } = DefaultResultLimit;
}
