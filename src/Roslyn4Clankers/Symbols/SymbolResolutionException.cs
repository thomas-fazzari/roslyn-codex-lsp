// Copyright (C) 2026 thomas-fazzari
// SPDX-License-Identifier: GPL-3.0-only

using System.Text.Json.Nodes;

namespace Roslyn4Clankers.Symbols;

/// <summary>
/// Reports a symbol name that matches no declaration, or several distinct ones.
/// </summary>
internal sealed class SymbolResolutionException(string code, string message, JsonArray candidates)
    : Exception(message)
{
    public string Code { get; } = code;

    /// <summary>
    /// Declarations the name could refer to. Each symbol value can be sent back as the name.
    /// </summary>
    public JsonArray Candidates { get; } = candidates;
}
