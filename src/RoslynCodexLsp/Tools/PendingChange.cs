// Copyright (C) 2026 thomas-fazzari
// SPDX-License-Identifier: GPL-3.0-only

using System.Text.Json.Nodes;
using RoslynCodexLsp.Editing;

namespace RoslynCodexLsp.Tools;

internal sealed record PendingChange(
    LspAction Action,
    WorkspaceSnapshot Snapshot,
    JsonObject? Edit,
    JsonObject? Command,
    JsonArray? Actions = null,
    JsonObject? FileRename = null
)
{
    public DateTimeOffset CreatedAt { get; } = DateTimeOffset.UtcNow;
}
