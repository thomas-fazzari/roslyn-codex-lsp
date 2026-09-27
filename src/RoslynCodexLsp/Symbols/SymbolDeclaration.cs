// Copyright (C) 2026 thomas-fazzari
// SPDX-License-Identifier: GPL-3.0-only

using System.Text.Json.Nodes;
using RoslynCodexLsp.Lsp;

namespace RoslynCodexLsp.Symbols;

/// <summary>
/// A declaration found for a symbol name, at a one-based position in a workspace file.
/// </summary>
/// <param name="Name">Qualified name with its parameter list, which resolves back to this symbol.</param>
internal sealed record SymbolDeclaration(
    string Name,
    int? Kind,
    string File,
    int Line,
    int Character
)
{
    public JsonObject ToJson(WorkspacePaths paths)
    {
        var json = new JsonObject { ["symbol"] = Name };
        if (Kind is { } kind && LspSymbolKinds.TryGetName(kind, out var kindName))
        {
            json["kind"] = kindName;
        }

        json["file"] = paths.RelativePath(File);
        json["position"] = new JsonArray(Line, Character);
        return json;
    }
}
