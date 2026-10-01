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
    /// Returns the workspace file and one-based position of the declaration named by <paramref name="text"/>,
    /// searching only <paramref name="file"/> when it is given.
    /// </summary>
    public async Task<SymbolDeclaration> ResolveAsync(
        string text,
        string? file,
        CancellationToken cancellationToken
    )
    {
        var requested = SymbolName.Parse(text);
        var files = file is null
            ? await CandidateFilesAsync(requested, cancellationToken)
            : [paths.Resolve(file)];
        var declarations = new List<SymbolDeclaration>();
        foreach (var candidate in files)
        {
            var documentSymbols = await DocumentSymbolsAsync(candidate, cancellationToken);
            declarations.AddRange(FindDeclarations(documentSymbols, candidate, requested));
        }

        var symbols = DistinctSymbols(
            declarations,
            await ProjectsByFileAsync(declarations, cancellationToken)
        );
        if (symbols.Count == 1)
        {
            return symbols[0];
        }

        if (symbols.Count == 0)
        {
            throw new SymbolResolutionException(
                NotFoundErrorCode,
                $"No declaration matches '{text}'. Use symbols to search by name.",
                []
            );
        }

        var sameNames = symbols.DistinctBy(symbol => symbol.Name, StringComparer.Ordinal).Count();
        var advice =
            sameNames < symbols.Count
                ? "Send one candidate symbol with its file."
                : "Send one candidate symbol instead.";
        throw new SymbolResolutionException(
            AmbiguousErrorCode,
            symbols.Count > MaximumCandidates
                ? $"'{text}' matches {symbols.Count} symbols. The first {MaximumCandidates} are listed. {advice}"
                : $"'{text}' matches {symbols.Count} symbols. {advice}",
            Candidates(symbols)
        );
    }

    /// <summary>
    /// Keeps one declaration per symbol. Partial declarations share a name and a project,
    /// while same-named types in different projects are separate symbols.
    /// </summary>
    internal static List<SymbolDeclaration> DistinctSymbols(
        IEnumerable<SymbolDeclaration> declarations,
        IReadOnlyDictionary<string, HashSet<string>> projectsByFile
    )
    {
        var symbols = new List<SymbolDeclaration>();
        foreach (
            var group in declarations.GroupBy(
                declaration => declaration.Name,
                StringComparer.Ordinal
            )
        )
        {
            var representatives = new List<SymbolDeclaration>();
            foreach (var declaration in group)
            {
                if (
                    !representatives.Exists(representative =>
                        SharesProject(representative.File, declaration.File, projectsByFile)
                    )
                )
                {
                    representatives.Add(declaration);
                }
            }

            symbols.AddRange(representatives);
        }

        return symbols;
    }

    // Unknown project membership keeps the previous assumption that same-named declarations are partial
    private static bool SharesProject(
        string first,
        string second,
        IReadOnlyDictionary<string, HashSet<string>> projectsByFile
    ) =>
        string.Equals(first, second, StringComparison.Ordinal)
        || !projectsByFile.TryGetValue(first, out var firstProjects)
        || !projectsByFile.TryGetValue(second, out var secondProjects)
        || firstProjects.Count == 0
        || secondProjects.Count == 0
        || firstProjects.Overlaps(secondProjects);

    private async Task<Dictionary<string, HashSet<string>>> ProjectsByFileAsync(
        List<SymbolDeclaration> declarations,
        CancellationToken cancellationToken
    )
    {
        var projectsByFile = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var sharedNames = declarations
            .GroupBy(declaration => declaration.Name, StringComparer.Ordinal)
            .Where(group =>
                group.DistinctBy(declaration => declaration.File, StringComparer.Ordinal).Count()
                > 1
            );
        foreach (
            var file in sharedNames
                .SelectMany(group => group)
                .Select(d => d.File)
                .Distinct(StringComparer.Ordinal)
        )
        {
            var uri = await session.OpenDocumentAsync(file, cancellationToken);
            var contexts = await session.ProjectContextsAsync(uri, cancellationToken);
            projectsByFile[file] = contexts
                .Select(context => context?["_vs_id"]?.GetValue<string>())
                .OfType<string>()
                .ToHashSet(StringComparer.Ordinal);
        }

        return projectsByFile;
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
                .ThenBy(declaration => declaration.File, StringComparer.Ordinal)
                .Take(MaximumCandidates)
                .Select(declaration => (JsonNode)declaration.ToJson(paths)),
        ];

    private readonly record struct DeclaredSymbol(
        SymbolName Name,
        bool IsConstructor,
        SymbolDeclaration Declaration
    );
}
