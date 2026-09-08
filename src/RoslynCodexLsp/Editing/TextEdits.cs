// Copyright (C) 2026 thomas-fazzari
// SPDX-License-Identifier: GPL-3.0-only

using System.Text;
using System.Text.Json.Nodes;

namespace RoslynCodexLsp.Editing;

internal static class TextEdits
{
    internal const int MaximumTextEditCount = 10_000;

    public static string Apply(string source, JsonArray edits)
    {
        if (edits.Count > MaximumTextEditCount)
        {
            throw new InvalidOperationException(
                $"The edit exceeds {MaximumTextEditCount} text changes."
            );
        }

        var lines = GetLines(source);
        var changes = edits
            .Select((edit, index) => Parse(edit, index, lines))
            .OrderBy(change => change.Start)
            .ThenBy(change => change.End)
            .ThenBy(change => change.Index);
        var result = new StringBuilder(source.Length);
        var offset = 0;
        foreach (var change in changes)
        {
            if (change.Start < offset || change.End < change.Start)
            {
                throw new InvalidOperationException("Text edit ranges overlap or run backwards.");
            }

            result.Append(source, offset, change.Start - offset).Append(change.Text);
            offset = change.End;
            if (result.Length > WorkspaceEditService.MaximumFileBytes)
            {
                throw new InvalidOperationException("The edited document exceeds the size limit.");
            }
        }

        result.Append(source, offset, source.Length - offset);
        return result.ToString();
    }

    private static Change Parse(JsonNode? node, int index, List<(int Start, int Length)> lines)
    {
        var range =
            node?["range"] ?? throw new InvalidOperationException("A text edit needs a range.");
        var start = GetOffset(range["start"], lines);
        var end = GetOffset(range["end"], lines);
        var text =
            node["newText"]?.GetValue<string>()
            ?? throw new InvalidOperationException("A text edit needs newText.");
        return new Change(start, end, text, index);
    }

    private static int GetOffset(JsonNode? position, List<(int Start, int Length)> lines)
    {
        var line = position?["line"]?.GetValue<int>() ?? -1;
        var character = position?["character"]?.GetValue<int>() ?? -1;
        if (line < 0 || line >= lines.Count || character < 0 || character > lines[line].Length)
        {
            throw new InvalidOperationException("The text edit position is outside the document.");
        }

        return lines[line].Start + character;
    }

    internal static List<(int Start, int Length)> GetLines(string source)
    {
        var lines = new List<(int Start, int Length)>();
        var start = 0;
        for (var index = 0; index < source.Length; index++)
        {
            if (source[index] is not ('\r' or '\n'))
            {
                continue;
            }

            lines.Add((start, index - start));
            if (source[index] == '\r' && index + 1 < source.Length && source[index + 1] == '\n')
            {
                index++;
            }

            start = index + 1;
        }

        lines.Add((start, source.Length - start));
        return lines;
    }

    private sealed record Change(int Start, int End, string Text, int Index);
}
