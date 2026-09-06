// Copyright (C) 2026 thomas-fazzari
// SPDX-License-Identifier: GPL-3.0-only

using System.ComponentModel;
using System.Text.Json.Nodes;

namespace RoslynCodexLsp.Tools;

internal sealed record LspRequest(int Limit = LspRequest.DefaultResultLimit)
{
    internal const int DefaultResultLimit = 100;
    internal const int MaximumResultLimit = 1000;

    [Description("LSP operation to perform.")]
    public required LspAction Action { get; init; }

    [Description(
        "Workspace-relative file path. Diagnostics also accepts a glob or * for all C# files."
    )]
    public string? File { get; init; }

    [Description("One-based line number.")]
    public int? Line { get; init; }

    [Description("One-based UTF-16 character in the line.")]
    public int? Character { get; init; }

    [Description("Optional one-based end line for a code action selection.")]
    public int? EndLine { get; init; }

    [Description("Optional one-based UTF-16 end character for a code action selection.")]
    public int? EndCharacter { get; init; }

    [Description("Search text for workspace symbols.")]
    public string? Query { get; init; }

    [Description("New symbol name or workspace-relative destination for rename_file.")]
    public string? NewName { get; init; }

    [Description("Zero-based code action index from a fresh listing.")]
    public int? ActionIndex { get; init; }

    [Description("Apply the selected change. False returns an editable proposal preview.")]
    public bool Apply { get; init; }

    [Description(
        "Proposal identifier from a previous preview. Use apply=true to apply that exact change."
    )]
    public string? ProposalId { get; init; }

    [Description(
        "LSP method for action=request. Lifecycle and document synchronization methods are managed by the bridge."
    )]
    public string? Method { get; init; }

    [Description("Raw LSP parameters. Positions here follow LSP's zero-based convention.")]
    public JsonObject? Parameters { get; init; }

    [Description(
        "Maximum returned diagnostics or results, from 1 to 1000. Truncation is explicit."
    )]
    public int Limit { get; init; } = Limit;
}
