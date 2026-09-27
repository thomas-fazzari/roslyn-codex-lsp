// Copyright (C) 2026 thomas-fazzari
// SPDX-License-Identifier: GPL-3.0-only

using System.Text.Json.Nodes;
using RoslynCodexLsp.Lsp;

namespace RoslynCodexLsp.Tools;

/// <summary>
/// Converts Roslyn results to the compact, one-based shapes returned by the tool.
/// </summary>
internal static class LspResultsExtensions
{
    extension(JsonNode? result)
    {
        /// <summary>
        /// Reduces hover content to its text, whether Roslyn returns markup, a marked string or an array.
        /// </summary>
        public JsonObject ToCompactHover() =>
            new() { ["text"] = HoverText(result?["contents"]).Trim() };

        /// <summary>
        /// Converts document or workspace symbols to name, kind and one-based position entries.
        /// Document symbols keep their nesting in children.
        /// </summary>
        public JsonObject ToCompactSymbols(WorkspacePaths paths, int limit)
        {
            var symbols = result as JsonArray ?? [];
            return new JsonObject
            {
                ["items"] = new JsonArray([
                    .. symbols
                        .Take(limit)
                        .Select(symbol => (JsonNode)CompactSymbol(symbol!, paths)),
                ]),
                ["total"] = symbols.Count,
                ["truncated"] = symbols.Count > limit,
            };
        }

        /// <summary>
        /// Converts call or type hierarchy results to symbol entries.
        /// Call results also list the one-based positions of their call sites in calls.
        /// </summary>
        public JsonObject ToCompactHierarchy(WorkspacePaths paths, int limit)
        {
            var related = result as JsonArray ?? [];
            var items = new JsonArray();
            foreach (var entry in related.Take(limit))
            {
                var symbol = CompactSymbol(entry!["from"] ?? entry["to"] ?? entry, paths);
                if (entry["fromRanges"] is JsonArray ranges)
                {
                    symbol["calls"] = new JsonArray([
                        .. ranges.Select(range => (JsonNode)OneBased(range!["start"]!)),
                    ]);
                }

                items.Add((JsonNode)symbol);
            }

            return new JsonObject
            {
                ["items"] = items,
                ["total"] = related.Count,
                ["truncated"] = related.Count > limit,
            };
        }

        public JsonObject ToCompactLocations(WorkspacePaths paths, int limit)
        {
            var locations = result switch
            {
                null => [],
                JsonArray array => array,
                JsonObject location => new JsonArray(location.DeepClone()),
                _ => throw new InvalidOperationException(
                    "Roslyn returned an invalid location result."
                ),
            };
            var items = new JsonArray();
            var groups = new Dictionary<string, JsonArray>(StringComparer.Ordinal);
            foreach (var location in locations.Take(limit))
            {
                var uri =
                    (location?["uri"] ?? location?["targetUri"])?.GetValue<string>()
                    ?? throw new InvalidOperationException(
                        "Roslyn returned a location without a URI."
                    );
                var start =
                    (location?["range"] ?? location?["targetSelectionRange"])?["start"]
                    ?? throw new InvalidOperationException(
                        "Roslyn returned a location without a position."
                    );
                var file = LocationFile(uri, paths);
                if (!groups.TryGetValue(file, out var positions))
                {
                    positions = [];
                    groups.Add(file, positions);
                    items.Add(
                        (JsonNode)new JsonObject { ["file"] = file, ["positions"] = positions }
                    );
                }

                positions.Add((JsonNode)OneBased(start));
            }

            return new JsonObject
            {
                ["items"] = items,
                ["total"] = locations.Count,
                ["truncated"] = locations.Count > limit,
            };
        }
    }

    extension(JsonNode diagnostic)
    {
        public JsonObject ToCompactDiagnostic()
        {
            var start =
                diagnostic["range"]?["start"]
                ?? throw new InvalidOperationException(
                    "Roslyn returned a diagnostic without a range."
                );
            var compact = new JsonObject
            {
                ["line"] = start["line"]!.GetValue<int>() + 1,
                ["character"] = start["character"]!.GetValue<int>() + 1,
            };
            if (diagnostic["severity"]?.GetValue<int>() is { } severity)
            {
                compact["severity"] = severity switch
                {
                    1 => "error",
                    2 => "warning",
                    3 => "information",
                    _ => "hint",
                };
            }

            compact["code"] = diagnostic["code"]?.DeepClone();
            compact["message"] = diagnostic["message"]?.DeepClone();
            return compact;
        }
    }

    private static JsonObject CompactSymbol(JsonNode symbol, WorkspacePaths paths)
    {
        var name = symbol["name"]?.GetValue<string>();
        var compact = new JsonObject { ["name"] = name };
        if (
            symbol["kind"]?.GetValue<int>() is { } kind
            && LspSymbolKinds.TryGetName(kind, out var kindName)
        )
        {
            compact["kind"] = kindName;
        }

        // Roslyn often repeats the name as the detail
        AddText(compact, "detail", symbol["detail"], name);
        AddText(compact, "container", symbol["containerName"], name);
        AddLocation(compact, symbol, paths);

        if (symbol["children"] is JsonArray { Count: > 0 } children)
        {
            compact["children"] = new JsonArray([
                .. children.Select(child => (JsonNode)CompactSymbol(child!, paths)),
            ]);
        }

        return compact;
    }

    private static void AddText(JsonObject compact, string key, JsonNode? value, string? name)
    {
        if (
            value?.GetValue<string>() is { Length: > 0 } text
            && !string.Equals(text, name, StringComparison.Ordinal)
        )
        {
            compact[key] = text;
        }
    }

    private static void AddLocation(JsonObject compact, JsonNode symbol, WorkspacePaths paths)
    {
        if ((symbol["location"]?["uri"] ?? symbol["uri"])?.GetValue<string>() is { } uri)
        {
            compact["file"] = LocationFile(uri, paths);
        }

        var range = symbol["selectionRange"] ?? symbol["location"]?["range"] ?? symbol["range"];
        if (range?["start"] is { } start)
        {
            compact["position"] = OneBased(start);
        }
    }

    private static JsonArray OneBased(JsonNode position) =>
        new(position["line"]!.GetValue<int>() + 1, position["character"]!.GetValue<int>() + 1);

    private static string HoverText(JsonNode? contents) =>
        contents switch
        {
            JsonArray parts => string.Join("\n\n", parts.Select(HoverText)),
            JsonObject markup => markup["value"]?.GetValue<string>() ?? string.Empty,
            JsonValue text => text.GetValue<string>(),
            _ => string.Empty,
        };

    // Workspace files use relative paths. External and generated locations keep their URI.
    private static string LocationFile(string value, WorkspacePaths paths) =>
        paths.TryGetWorkspaceFile(value) is { } file ? paths.RelativePath(file) : value;
}
