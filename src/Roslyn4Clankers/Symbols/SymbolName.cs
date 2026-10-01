// Copyright (C) 2026 thomas-fazzari
// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Roslyn4Clankers.Symbols;

/// <summary>
/// A dotted C# symbol name with an optional parameter list, such as <c>Sample.Box&lt;T&gt;.Put(int)</c>.
/// Generic arguments are ignored when names are compared.
/// </summary>
internal sealed record SymbolName(ImmutableArray<string> Segments, string? Parameters)
{
    private const string OperatorKeyword = "operator";
    private static readonly SearchValues<char> _operatorCharacters = SearchValues.Create(
        "<>=!+-*/%&|^~"
    );
    private static readonly SearchValues<char> _brackets = SearchValues.Create("()[]");

    /// <summary>
    /// Parses a requested name or a Roslyn document symbol name.
    /// A trailing type, as in <c>Value : int</c> or <c>Put(int) : void</c>, is removed.
    /// </summary>
    public static SymbolName Parse(string text) =>
        TryParse(text, out var name)
            ? name
            : throw new ArgumentException($"'{text}' is not a valid symbol name.", nameof(text));

    public static bool TryParse(string text, [NotNullWhen(true)] out SymbolName? result)
    {
        result = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var name = WithoutType(text);

        string? parameters = null;
        if (name[^1] is ')' or ']')
        {
            var open = MatchingOpen(name);
            if (open < 0)
            {
                return false;
            }

            parameters = Normalize(name[open..]);
            name = name[..open];
        }

        var segments = SplitAtTopLevel(name, '.')
            .Select(segment => StripGenericArguments(segment).Trim())
            .ToImmutableArray();
        if (segments.Any(segment => segment.Length == 0 || segment.AsSpan().ContainsAny(_brackets)))
        {
            return false;
        }

        result = new SymbolName(segments, parameters);
        return true;
    }

    /// <summary>
    /// Removes the trailing type that Roslyn appends to member names, as in <c>Put(int) : void</c>.
    /// </summary>
    public static string WithoutType(string text)
    {
        var name = text.Trim();
        var typeSeparator = IndexAtTopLevel(name, " : ");
        return typeSeparator >= 0 ? name[..typeSeparator] : name;
    }

    /// <summary>
    /// Returns whether this requested name selects a declared symbol.
    /// The requested segments must end the declared segments, and a requested parameter list must be equal.
    /// </summary>
    public bool Selects(SymbolName declared) =>
        Segments.Length <= declared.Segments.Length
        && declared.Segments[^Segments.Length..].SequenceEqual(Segments, StringComparer.Ordinal)
        && (
            Parameters is null
            || string.Equals(Parameters, declared.Parameters, StringComparison.Ordinal)
        );

    /// <summary>
    /// Appends a member name to the name of its container.
    /// </summary>
    public SymbolName Append(SymbolName member) =>
        new([.. Segments, .. member.Segments], member.Parameters);

    private static string Normalize(string parameters)
    {
        var builder = new StringBuilder(parameters.Length);
        foreach (var character in parameters.Where(character => !char.IsWhiteSpace(character)))
        {
            builder.Append(character);
        }

        return builder.ToString();
    }

    private static string StripGenericArguments(string segment)
    {
        var open = segment.IndexOf('<', StringComparison.Ordinal);
        return open > 0 && segment[^1] == '>' ? segment[..open] : segment;
    }

    private static int MatchingOpen(string text)
    {
        var depth = 0;
        for (var index = text.Length - 1; index >= 0; index--)
        {
            depth -= Depth(text, index);
            if (depth == 0)
            {
                return index;
            }
        }

        return -1;
    }

    private static int IndexAtTopLevel(string text, string value)
    {
        var depth = 0;
        for (var index = 0; index < text.Length; index++)
        {
            depth += Depth(text, index);
            if (depth == 0 && string.CompareOrdinal(text, index, value, 0, value.Length) == 0)
            {
                return index;
            }
        }

        return -1;
    }

    private static IEnumerable<string> SplitAtTopLevel(string text, char separator)
    {
        var depth = 0;
        var start = 0;
        for (var index = 0; index < text.Length; index++)
        {
            depth += Depth(text, index);
            if (depth == 0 && text[index] == separator)
            {
                yield return text[start..index];
                start = index + 1;
            }
        }

        yield return text[start..];
    }

    private static int Depth(string text, int index)
    {
        var depth = text[index] switch
        {
            '(' or '[' or '<' => 1,
            ')' or ']' or '>' => -1,
            _ => 0,
        };
        return text[index] is '<' or '>' && IsOperatorToken(text, index) ? 0 : depth;
    }

    // Angle brackets in operator names (e.g. "operator <(Money, Money)") are not generic arguments
    private static bool IsOperatorToken(string text, int index)
    {
        var start = index;
        while (start > 0 && _operatorCharacters.Contains(text[start - 1]))
        {
            start--;
        }

        var keywordEnd = start;
        while (keywordEnd > 0 && text[keywordEnd - 1] == ' ')
        {
            keywordEnd--;
        }

        var keywordStart = keywordEnd - OperatorKeyword.Length;
        return keywordStart >= 0
            && string.CompareOrdinal(text, keywordStart, OperatorKeyword, 0, OperatorKeyword.Length)
                == 0
            && (keywordStart == 0 || !char.IsLetterOrDigit(text[keywordStart - 1]));
    }
}
