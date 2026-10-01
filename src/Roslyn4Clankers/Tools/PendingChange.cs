// Copyright (C) 2026 thomas-fazzari
// SPDX-License-Identifier: GPL-3.0-only

using System.Diagnostics;
using System.Text.Json.Nodes;
using Roslyn4Clankers.Editing;

namespace Roslyn4Clankers.Tools;

[DebuggerDisplay("{Action} created {CreatedAt}, {Snapshot.Fingerprints.Count} files")]
internal sealed record PendingChange(
    LspAction Action,
    WorkspaceSnapshot Snapshot,
    JsonObject? Edit,
    JsonObject? Command,
    JsonArray? Actions = null,
    bool RenamesFiles = false
)
{
    public DateTimeOffset CreatedAt { get; } = DateTimeOffset.UtcNow;
}
