// Copyright (C) 2026 thomas-fazzari
// SPDX-License-Identifier: GPL-3.0-only

using System.Text.Json.Nodes;
using DiffPlex;

namespace RoslynCodexLsp.Editing;

internal static class TextPreview
{
    internal const int MaximumDiffCharacters = 32_000;
    internal const int MaximumDiffLines = 2_000;
    private const int ContextCharacters = 80;

    public static (JsonArray Changes, bool Truncated) Create(string before, string after)
    {
        var changes = new JsonArray();
        if (StringComparer.Ordinal.Equals(before, after))
        {
            return (changes, false);
        }

        var window = ChangedWindow(before, after);
        var oldText = before[window.Start..window.BeforeEnd];
        var newText = after[window.Start..window.AfterEnd];
        var origin = PositionAt(before, window.Start);
        var remaining = EditedDocument.MaximumPreviewCharacters * 2;

        if (!CanCompareLines(oldText, newText))
        {
            changes.Add(
                (JsonNode)DescribeChange(oldText, newText, origin, origin, ref remaining).Change
            );
            return (changes, true);
        }

        var diff = Differ.Instance.CreateCustomDiffs(
            oldText,
            newText,
            ignoreWhiteSpace: false,
            chunker: ChunkLines
        );
        if (diff.DiffBlocks.Count == 0)
        {
            var single = DescribeChange(oldText, newText, origin, origin, ref remaining);
            changes.Add((JsonNode)single.Change);
            return (changes, single.Truncated);
        }

        var oldLines = TextEdits.GetLines(oldText);
        var newLines = TextEdits.GetLines(newText);
        var truncated = false;
        foreach (var block in diff.DiffBlocks)
        {
            if (remaining < 2)
            {
                truncated = true;
                break;
            }

            var oldStart = LineOffset(oldText, oldLines, block.DeleteStartA);
            var oldEnd = LineOffset(oldText, oldLines, block.DeleteStartA + block.DeleteCountA);
            var newStart = LineOffset(newText, newLines, block.InsertStartB);
            var newEnd = LineOffset(newText, newLines, block.InsertStartB + block.InsertCountB);
            var available = remaining;
            var preview = DescribeChange(
                oldText[oldStart..oldEnd],
                newText[newStart..newEnd],
                PositionAt(oldText, oldStart, origin),
                PositionAt(newText, newStart, origin),
                ref remaining
            );

            if (remaining == available)
            {
                truncated = true;
                break;
            }

            changes.Add((JsonNode)preview.Change);
            truncated |= preview.Truncated;
        }

        return (changes, truncated);
    }

    private static bool CanCompareLines(string before, string after) =>
        before.Length + after.Length <= MaximumDiffCharacters
        && CountLines(before) + CountLines(after) <= MaximumDiffLines;

    private static int CountLines(string text) => PositionAt(text, text.Length).Line;

    private static string[] ChunkLines(string text)
    {
        var lines = TextEdits.GetLines(text);
        var chunks = new string[lines.Count];
        for (var index = 0; index < lines.Count; index++)
        {
            chunks[index] = text[lines[index].Start..LineOffset(text, lines, index + 1)];
        }

        return chunks;
    }

    private static (JsonObject Change, bool Truncated) DescribeChange(
        string before,
        string after,
        Position beforeOrigin,
        Position afterOrigin,
        ref int remaining
    )
    {
        var (Start, BeforeEnd, AfterEnd) = ChangedWindow(before, after);
        beforeOrigin = PositionAt(before, Start, beforeOrigin);
        afterOrigin = PositionAt(after, Start, afterOrigin);
        before = before[Start..BeforeEnd];
        after = after[Start..AfterEnd];
        var beforeLimit = Math.Min(before.Length, after.Length == 0 ? remaining : remaining / 2);
        var afterLimit = Math.Min(after.Length, remaining - beforeLimit);
        beforeLimit = SafeLength(before, beforeLimit);
        afterLimit = SafeLength(after, afterLimit);
        remaining -= beforeLimit + afterLimit;
        var truncated = beforeLimit < before.Length || afterLimit < after.Length;
        return (
            new JsonObject
            {
                ["before"] = DescribeRange(before[..beforeLimit], beforeOrigin),
                ["after"] = DescribeRange(after[..afterLimit], afterOrigin),
                ["truncated"] = truncated,
            },
            truncated
        );
    }

    private static JsonObject DescribeRange(string text, Position start)
    {
        var end = PositionAt(text, text.Length, start);
        return new JsonObject
        {
            ["startLine"] = start.Line,
            ["startCharacter"] = start.Character,
            ["endLine"] = end.Line,
            ["endCharacter"] = end.Character,
            ["text"] = text,
        };
    }

    private static (int Start, int BeforeEnd, int AfterEnd) ChangedWindow(
        string before,
        string after
    )
    {
        var prefix = before.AsSpan().CommonPrefixLength(after);
        var suffix = 0;
        while (
            suffix < Math.Min(before.Length, after.Length) - prefix
            && before[^(suffix + 1)] == after[^(suffix + 1)]
        )
        {
            suffix++;
        }

        var start = SafeLength(before, Math.Max(0, prefix - ContextCharacters));
        var beforeEnd = SafeLength(
            before,
            Math.Min(before.Length, before.Length - suffix + ContextCharacters)
        );
        var afterEnd = SafeLength(
            after,
            Math.Min(after.Length, after.Length - suffix + ContextCharacters)
        );
        return (start, beforeEnd, afterEnd);
    }

    private static int SafeLength(string text, int length) =>
        SplitsCharacter(text, length) ? length - 1 : length;

    private static bool SplitsCharacter(string text, int length) =>
        length > 0
        && length < text.Length
        && (
            char.IsHighSurrogate(text[length - 1])
            || (text[length - 1] == '\r' && text[length] == '\n')
        );

    private static int LineOffset(string text, List<(int Start, int Length)> lines, int line) =>
        line < lines.Count ? lines[line].Start : text.Length;

    private static Position PositionAt(string text, int offset, Position origin = default)
    {
        var line = Math.Max(1, origin.Line);
        var character = Math.Max(1, origin.Character);
        for (var index = 0; index < offset; index++)
        {
            if (text[index] is '\r' or '\n')
            {
                if (text[index] == '\r' && index + 1 < offset && text[index + 1] == '\n')
                {
                    index++;
                }

                line++;
                character = 1;
            }
            else
            {
                character++;
            }
        }

        return new Position(line, character);
    }

    private readonly record struct Position(int Line, int Character);
}
