// Copyright (C) 2026 thomas-fazzari
// SPDX-License-Identifier: GPL-3.0-only

using System.Text.Json.Nodes;
using Roslyn4Clankers.Lsp;

namespace Roslyn4Clankers.Symbols;

/// <summary>
/// Finds the declaration of a C# symbol from its name.
/// </summary>
/// <remarks>
/// Roslyn's workspace symbol search matches names loosely and reports localized containers.
/// It only selects candidate files. Their document symbols give the exact qualified names.
/// </remarks>
internal sealed class SymbolResolver(RoslynSession session, WorkspacePaths paths)
{
    internal const string NotFoundErrorCode = "symbol_not_found";
    internal const string AmbiguousErrorCode = "ambiguous_symbol";
    internal const int MaximumCandidates = 20;

    private const int NamespaceKind = 3;

    /// <summary>
    /// Returns the workspace file and one-based position of the declaration named by <paramref name="text"/>.
    /// </summary>
    public async Task<SymbolDeclaration> ResolveAsync(
        string text,
        CancellationToken cancellationToken
    )
    {
        var requested = SymbolName.Parse(text);
        var declarations = new List<SymbolDeclaration>();
        foreach (var file in await CandidateFilesAsync(requested, cancellationToken))
        {
            var symbols = await DocumentSymbolsAsync(file, cancellationToken);
            declarations.AddRange(FindDeclarations(symbols, file, requested));
        }

        // Partial declarations of one symbol share its name
        var symbolsByName = declarations
            .GroupBy(declaration => declaration.Name, StringComparer.Ordinal)
            .ToList();
        return symbolsByName.Count switch
        {
            1 => symbolsByName[0].First(),
            0 => throw new SymbolResolutionException(
                NotFoundErrorCode,
                $"No declaration matches '{text}'. Use symbols to search by name.",
                []
            ),
            _ => throw new SymbolResolutionException(
                AmbiguousErrorCode,
                symbolsByName.Count > MaximumCandidates
                    ? $"'{text}' matches {symbolsByName.Count} symbols. The first {MaximumCandidates} are listed. Send one candidate symbol, or a more qualified name."
                    : $"'{text}' matches {symbolsByName.Count} symbols. Send one candidate symbol instead.",
                Candidates(symbolsByName.Select(group => group.First()))
            ),
        };
    }

    /// <summary>
    /// Returns the names of the declarations at one-based positions in workspace files,
    /// in the format <see cref="ResolveAsync"/> accepts. Positions without a declaration are omitted.
    /// </summary>
    public async Task<
        IReadOnlyDictionary<(string File, int Line, int Character), string>
    > NamesAsync(
        IEnumerable<(string File, int Line, int Character)> positions,
        CancellationToken cancellationToken
    )
    {
        var names = new Dictionary<(string File, int Line, int Character), string>();
        foreach (var file in positions.GroupBy(position => position.File, StringComparer.Ordinal))
        {
            var declarations = Declarations(
                    await DocumentSymbolsAsync(file.Key, cancellationToken),
                    file.Key,
                    container: null,
                    containerDisplay: null
                )
                .ToList();
            foreach (var position in file)
            {
                var match = declarations.FirstOrDefault(declared =>
                    declared.Declaration.Line == position.Line
                    && declared.Declaration.Character == position.Character
                );
                if (match.Declaration is not null)
                {
                    names[position] = match.Declaration.Name;
                }
            }
        }

        return names;
    }

    /// <summary>
    /// Returns the declarations in a document symbol tree that <paramref name="requested"/> selects.
    /// A constructor is selected only when the request includes its parameter list.
    /// </summary>
    internal static IEnumerable<SymbolDeclaration> FindDeclarations(
        JsonArray documentSymbols,
        string file,
        SymbolName requested
    ) =>
        Declarations(documentSymbols, file, container: null, containerDisplay: null)
            .Where(declared =>
                requested.Selects(declared.Name)
                && (!declared.IsConstructor || requested.Parameters is not null)
            )
            .Select(declared => declared.Declaration);

    private async Task<JsonArray> DocumentSymbolsAsync(
        string file,
        CancellationToken cancellationToken
    )
    {
        var uri = await session.OpenDocumentAsync(file, cancellationToken);
        var symbols = await session.RequestAsync(
            LspMethods.TextDocumentDocumentSymbol,
            new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = uri } },
            cancellationToken
        );
        return symbols as JsonArray ?? [];
    }

    // Namespaces only qualify their members, so they are not declarations here
    private static IEnumerable<DeclaredSymbol> Declarations(
        JsonArray symbols,
        string file,
        SymbolName? container,
        string? containerDisplay
    )
    {
        foreach (var symbol in symbols.OfType<JsonObject>())
        {
            if (symbol["name"]?.GetValue<string>() is not { Length: > 0 } rawName)
            {
                continue;
            }

            if (!SymbolName.TryParse(rawName, out var member))
            {
                continue;
            }

            var name = container is null ? member : container.Append(member);
            var display = SymbolName.WithoutType(rawName);
            display = containerDisplay is null ? display : $"{containerDisplay}.{display}";
            var kind = symbol["kind"]?.GetValue<int>();
            if (kind is not NamespaceKind && symbol["selectionRange"]?["start"] is { } start)
            {
                yield return new DeclaredSymbol(
                    name,
                    IsConstructor(member, container),
                    new SymbolDeclaration(
                        display,
                        kind,
                        file,
                        start["line"]!.GetValue<int>() + 1,
                        start["character"]!.GetValue<int>() + 1
                    )
                );
            }

            if (symbol["children"] is JsonArray children)
            {
                foreach (var declared in Declarations(children, file, name, display))
                {
                    yield return declared;
                }
            }
        }
    }

    private static bool IsConstructor(SymbolName member, SymbolName? container) =>
        container is not null
        && member.Parameters is not null
        && string.Equals(member.Segments[^1], container.Segments[^1], StringComparison.Ordinal);

    private async Task<IReadOnlyCollection<string>> CandidateFilesAsync(
        SymbolName requested,
        CancellationToken cancellationToken
    )
    {
        var results = await session.RequestAsync(
            LspMethods.WorkspaceSymbol,
            new JsonObject { ["query"] = string.Join('.', requested.Segments) },
            cancellationToken
        );
        var files = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var result in results as JsonArray ?? [])
        {
            if (
                string.Equals(
                    result?["name"]?.GetValue<string>(),
                    requested.Segments[^1],
                    StringComparison.Ordinal
                )
                && result?["location"]?["uri"]?.GetValue<string>() is { } uri
                && paths.TryGetWorkspaceFile(uri) is { } file
            )
            {
                files.Add(file);
            }
        }

        return files;
    }

    private JsonArray Candidates(IEnumerable<SymbolDeclaration> declarations) =>
        [
            .. declarations
                .OrderBy(declaration => declaration.Name, StringComparer.Ordinal)
                .Take(MaximumCandidates)
                .Select(declaration => (JsonNode)declaration.ToJson(paths)),
        ];

    private readonly record struct DeclaredSymbol(
        SymbolName Name,
        bool IsConstructor,
        SymbolDeclaration Declaration
    );
}
