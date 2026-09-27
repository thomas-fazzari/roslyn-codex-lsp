// Copyright (C) 2026 thomas-fazzari
// SPDX-License-Identifier: GPL-3.0-only

using System.Collections.Immutable;
using System.Text;

namespace RoslynCodexLsp.Symbols;

/// <summary>
/// A dotted C# symbol name with an optional parameter list, such as <c>Sample.Box&lt;T&gt;.Put(int)</c>.
/// Generic arguments are ignored when names are compared.
/// </summary>
internal sealed record SymbolName(ImmutableArray<string> Segments, string? Parameters)
{
    private static readonly char[] _brackets = ['(', ')', '[', ']'];

    /// <summary>
    /// Parses a requested name or a Roslyn document symbol name.
    /// A trailing type, as in <c>Value : int</c> or <c>Put(int) : void</c>, is removed.
    /// </summary>
    public static SymbolName Parse(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        var name = WithoutType(text);

        string? parameters = null;
        if (name[^1] is ')' or ']')
        {
            var open = MatchingOpen(name);
            parameters = Normalize(name[open..]);
            name = name[..open];
        }

        var segments = SplitAtTopLevel(name, '.')
            .Select(segment => StripGenericArguments(segment).Trim())
            .ToImmutableArray();
        if (segments.Any(segment => segment.Length == 0 || segment.IndexOfAny(_brackets) >= 0))
        {
            throw new ArgumentException($"'{text}' is not a valid symbol name.", nameof(text));
        }

        return new SymbolName(segments, parameters);
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
            depth += text[index] switch
            {
                ')' or ']' or '>' => 1,
                '(' or '[' or '<' => -1,
                _ => 0,
            };
            if (depth == 0)
            {
                return index;
            }
        }

        throw new ArgumentException($"'{text}' has an unbalanced parameter list.", nameof(text));
    }

    private static int IndexAtTopLevel(string text, string value)
    {
        var depth = 0;
        for (var index = 0; index < text.Length; index++)
        {
            depth += Depth(text[index]);
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
            depth += Depth(text[index]);
            if (depth == 0 && text[index] == separator)
            {
                yield return text[start..index];
                start = index + 1;
            }
        }

        yield return text[start..];
    }

    private static int Depth(char character) =>
        character switch
        {
            '(' or '[' or '<' => 1,
            ')' or ']' or '>' => -1,
            _ => 0,
        };
}
