// Copyright (C) 2026 thomas-fazzari
// SPDX-License-Identifier: GPL-3.0-only

using System.Text.Json.Nodes;

namespace RoslynCodexLsp.Tools;

/// <summary>
/// Converts Roslyn results to the compact, one-based shapes returned by the tool.
/// </summary>
internal static class LspResultsExtensions
{
    extension(JsonNode? result)
    {
        /// <summary>
        /// Converts every LSP position in a result to one-based lines and characters, in place.
        /// </summary>
        public JsonNode? ToOneBasedPositions()
        {
            switch (result)
            {
                case JsonObject position
                    when position["line"] is JsonValue line
                        && position["character"] is JsonValue character:
                    position["line"] = line.GetValue<int>() + 1;
                    position["character"] = character.GetValue<int>() + 1;
                    break;
                case JsonObject value:
                    foreach (var (_, child) in value)
                    {
                        child.ToOneBasedPositions();
                    }
                    break;
                case JsonArray array:
                    foreach (var child in array)
                    {
                        child.ToOneBasedPositions();
                    }
                    break;
            }

            return result;
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

                positions.Add(
                    (JsonNode)
                        new JsonArray(
                            start["line"]!.GetValue<int>() + 1,
                            start["character"]!.GetValue<int>() + 1
                        )
                );
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

    private static string LocationFile(string value, WorkspacePaths paths)
    {
        var uri = new Uri(value);
        if (!uri.IsFile)
        {
            return value;
        }

        var relative = Path.GetRelativePath(paths.Root, uri.LocalPath);
        return
            Path.IsPathRooted(relative)
            || string.Equals(relative, "..", StringComparison.Ordinal)
            || relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            ? value
            : relative.Replace(Path.DirectorySeparatorChar, '/');
    }
}
