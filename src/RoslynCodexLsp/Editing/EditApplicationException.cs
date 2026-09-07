// Copyright (C) 2026 thomas-fazzari
// SPDX-License-Identifier: GPL-3.0-only

using System.Text.Json.Nodes;

namespace RoslynCodexLsp.Editing;

internal sealed class EditApplicationException(
    JsonObject result,
    EditApplicationPhase phase,
    string? failedPath,
    string? failedOperation,
    Exception innerException
) : Exception("The workspace change did not fully complete.", innerException)
{
    public JsonObject Result { get; } = result;

    public EditApplicationPhase Phase { get; } = phase;

    public string? FailedPath { get; } = failedPath;

    public string? FailedOperation { get; } = failedOperation;
}
