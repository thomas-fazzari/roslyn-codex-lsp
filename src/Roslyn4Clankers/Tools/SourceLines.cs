// Copyright (C) 2026 thomas-fazzari
// SPDX-License-Identifier: GPL-3.0-only

using System.Globalization;
using System.Text.Json.Nodes;

namespace Roslyn4Clankers.Tools;

/// <summary>
/// Adds the saved source lines of navigation results, so callers can read each occurrence in place.
/// </summary>
internal static class SourceLines
{
    internal const int MaximumLineCharacters = 200;

    /// <summary>
    /// Adds <c>lines</c>, a map from one-based line number to trimmed text, to each result item.
    /// Items with call sites get the lines of their calls, which are in <paramref name="callSiteFile"/>
    /// when it is set, and in the item's own file otherwise. Other items get the lines of their positions.
    /// </summary>
    public static void Add(JsonObject result, WorkspacePaths paths, string? callSiteFile)
    {
        var files = new Dictionary<string, string[]?>(StringComparer.Ordinal);
        foreach (var item in (result["items"] as JsonArray ?? []).OfType<JsonObject>())
        {
            var (file, positions) = Targets(item, paths, callSiteFile);
            if (file is null || Read(file, files) is not { } text)
            {
                continue;
            }

            var lines = new JsonObject();
            foreach (
                var line in positions
                    .Select(position => position![0]!.GetValue<int>())
                    .Where(line => line >= 1 && line <= text.Length)
                    .Distinct()
                    .Order()
            )
            {
                lines[line.ToString(CultureInfo.InvariantCulture)] = Trim(text[line - 1]);
            }

            item["lines"] = lines;
        }
    }

    private static (string? File, JsonArray Positions) Targets(
        JsonObject item,
        WorkspacePaths paths,
        string? callSiteFile
    )
    {
        if (item["calls"] is JsonArray calls)
        {
            return (callSiteFile ?? WorkspaceFile(item, paths), calls);
        }

        if (item["positions"] is JsonArray positions)
        {
            return (WorkspaceFile(item, paths), positions);
        }

        return item["position"] is JsonArray position
            ? (WorkspaceFile(item, paths), new JsonArray(position.DeepClone()))
            : (null, []);
    }

    private static string? WorkspaceFile(JsonObject item, WorkspacePaths paths) =>
        item["file"]?.GetValue<string>() is { } file ? paths.ResultFilePath(file) : null;

    private static string[]? Read(string file, Dictionary<string, string[]?> files)
    {
        if (!files.TryGetValue(file, out var text))
        {
            try
            {
                text = File.ReadAllLines(file);
            }
            catch (Exception exception)
                when (exception is IOException or UnauthorizedAccessException)
            {
                text = null;
            }

            files[file] = text;
        }

        return text;
    }

    private static string Trim(string line)
    {
        var text = line.Trim();
        return text.Length <= MaximumLineCharacters
            ? text
            : $"{text.AsSpan(0, MaximumLineCharacters)}…";
    }
}
