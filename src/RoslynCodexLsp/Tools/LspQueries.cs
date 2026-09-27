// Copyright (C) 2026 thomas-fazzari
// SPDX-License-Identifier: GPL-3.0-only

using System.Diagnostics;
using System.Text.Json.Nodes;
using Microsoft.Extensions.FileSystemGlobbing;
using RoslynCodexLsp.Lsp;
using RoslynCodexLsp.Symbols;
using StreamJsonRpc;

namespace RoslynCodexLsp.Tools;

internal sealed class LspQueries(
    RoslynSession session,
    WorkspacePaths paths,
    SymbolResolver resolver
)
{
    private const int MaximumDiagnosticFiles = 1000;
    private static readonly TimeSpan _projectAttachTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan _projectAttachPollInterval = TimeSpan.FromMilliseconds(200);

    public async Task<JsonNode?> NavigationAsync(
        LspRequest request,
        CancellationToken cancellationToken
    )
    {
        var parameters = await PositionAsync(request, cancellationToken);
        if (
            request.Action
            is LspAction.Callers
                or LspAction.Callees
                or LspAction.Supertypes
                or LspAction.Subtypes
        )
        {
            return await HierarchyAsync(request, parameters, cancellationToken);
        }

        var method = request.Action switch
        {
            LspAction.Definition => LspMethods.TextDocumentDefinition,
            LspAction.TypeDefinition => LspMethods.TextDocumentTypeDefinition,
            LspAction.Implementation => LspMethods.TextDocumentImplementation,
            LspAction.References => LspMethods.TextDocumentReferences,
            LspAction.Hover => LspMethods.TextDocumentHover,
            _ => throw new ArgumentException("Unsupported navigation action.", nameof(request)),
        };

        if (request.Action is LspAction.References)
        {
            parameters["context"] = new JsonObject { ["includeDeclaration"] = true };
        }

        var result = await session.RequestAsync(method, parameters, cancellationToken);
        return request.Action is LspAction.Hover
            ? result.ToCompactHover()
            : result.ToCompactLocations(paths, request.Limit);
    }

    private async Task<JsonObject> HierarchyAsync(
        LspRequest request,
        JsonObject position,
        CancellationToken cancellationToken
    )
    {
        var (prepare, expand) = request.Action switch
        {
            LspAction.Callers => (
                LspMethods.TextDocumentPrepareCallHierarchy,
                LspMethods.CallHierarchyIncomingCalls
            ),
            LspAction.Callees => (
                LspMethods.TextDocumentPrepareCallHierarchy,
                LspMethods.CallHierarchyOutgoingCalls
            ),
            LspAction.Supertypes => (
                LspMethods.TextDocumentPrepareTypeHierarchy,
                LspMethods.TypeHierarchySupertypes
            ),
            _ => (LspMethods.TextDocumentPrepareTypeHierarchy, LspMethods.TypeHierarchySubtypes),
        };
        var related = new JsonArray();
        var items = await session.RequestAsync(prepare, position, cancellationToken) as JsonArray;

        foreach (var item in items ?? [])
        {
            var expanded = await session.RequestAsync(
                expand,
                new JsonObject { ["item"] = item!.DeepClone() },
                cancellationToken
            );
            foreach (var entry in expanded as JsonArray ?? [])
            {
                related.Add(entry!.DeepClone());
            }
        }

        return related.ToCompactHierarchy(paths, request.Limit);
    }

    public async Task<JsonNode?> SymbolsAsync(
        LspRequest request,
        CancellationToken cancellationToken
    )
    {
        if (request.File is null)
        {
            var symbols = await session.RequestAsync(
                LspMethods.WorkspaceSymbol,
                new JsonObject { ["query"] = request.Query ?? string.Empty },
                cancellationToken
            );
            return symbols.ToCompactSymbols(paths, request.Limit);
        }

        var uri = await session.OpenDocumentAsync(request.File, cancellationToken);
        var documentSymbols = await session.RequestAsync(
            LspMethods.TextDocumentDocumentSymbol,
            Document(uri),
            cancellationToken
        );
        return documentSymbols.ToCompactSymbols(paths, request.Limit);
    }

    public async Task<JsonObject> DiagnosticsAsync(
        LspRequest request,
        CancellationToken cancellationToken
    )
    {
        var files = SelectFiles(request.File).Take(MaximumDiagnosticFiles + 1).ToArray();
        if (files.Length > MaximumDiagnosticFiles)
        {
            throw new ArgumentException(
                $"Diagnostics accepts at most {MaximumDiagnosticFiles} files. Use a narrower glob.",
                nameof(request)
            );
        }

        await session.SynchronizeNewFilesAsync(files, cancellationToken);

        var results = new JsonArray();
        var remaining = request.Limit;
        var total = 0;
        var belowSeverity = 0;
        var filesWithDiagnostics = 0;
        var miscellaneousFiles = new JsonArray();
        foreach (var batch in files.Chunk(RoslynSession.MaximumOpenDocuments))
        {
            // Each opened document creates a new solution version and discards diagnostics
            // computed for earlier versions, so open the whole batch before pulling reports
            var uris = new List<string>(batch.Length);
            foreach (var file in batch)
            {
                uris.Add(await session.OpenDocumentAsync(file, cancellationToken));
            }

            foreach (var uri in await WaitForProjectsAsync(uris, cancellationToken))
            {
                miscellaneousFiles.Add(
                    (JsonNode)Path.GetRelativePath(paths.Root, new Uri(uri).LocalPath)
                );
            }

            foreach (var (file, uri) in batch.Zip(uris))
            {
                var report = await FileDiagnosticsAsync(uri, cancellationToken);
                var reported = report["items"] as JsonArray ?? [];
                var diagnostics = reported
                    .Where(diagnostic => IsReported(diagnostic!, request.Severity))
                    .ToList();
                belowSeverity += reported.Count - diagnostics.Count;
                total += diagnostics.Count;
                if (diagnostics.Count == 0)
                {
                    continue;
                }

                filesWithDiagnostics++;
                if (remaining == 0)
                {
                    continue;
                }

                var selected = new JsonArray();

                foreach (var diagnostic in diagnostics.Take(remaining))
                {
                    selected.Add((JsonNode)diagnostic!.ToCompactDiagnostic());
                }

                remaining -= selected.Count;
                results.Add(
                    (JsonNode)
                        new JsonObject
                        {
                            ["file"] = Path.GetRelativePath(paths.Root, file),
                            ["diagnostics"] = selected,
                        }
                );
            }
        }

        var result = new JsonObject
        {
            ["filesChecked"] = files.Length,
            ["filesWithDiagnostics"] = filesWithDiagnostics,
            ["diagnostics"] = results,
            ["complete"] = miscellaneousFiles.Count == 0,
            ["total"] = total,
            ["belowSeverity"] = belowSeverity,
            ["truncated"] = total > request.Limit,
        };

        if (miscellaneousFiles.Count > 0)
        {
            result["miscellaneousFiles"] = miscellaneousFiles;
        }

        return result;
    }

    /// <summary>
    /// Waits until Roslyn attaches the documents to a project and returns those still outside one.
    /// Roslyn treats a new file as a miscellaneous file, without semantic diagnostics,
    /// until its project picks the file up.
    /// </summary>
    private async Task<HashSet<string>> WaitForProjectsAsync(
        IReadOnlyList<string> uris,
        CancellationToken cancellationToken
    )
    {
        var pending = new HashSet<string>(uris, StringComparer.Ordinal);
        var started = Stopwatch.GetTimestamp();
        while (true)
        {
            foreach (var uri in pending.ToArray())
            {
                if (!await IsMiscellaneousAsync(uri, cancellationToken))
                {
                    pending.Remove(uri);
                }
            }

            if (pending.Count == 0 || Stopwatch.GetElapsedTime(started) >= _projectAttachTimeout)
            {
                return pending;
            }

            await Task.Delay(_projectAttachPollInterval, cancellationToken);
        }
    }

    private async Task<bool> IsMiscellaneousAsync(string uri, CancellationToken cancellationToken)
    {
        JsonNode? result;
        try
        {
            result = await session.RequestAsync(
                LspMethods.TextDocumentGetProjectContexts,
                new JsonObject { ["_vs_textDocument"] = new JsonObject { ["uri"] = uri } },
                cancellationToken
            );
        }
        catch (RemoteInvocationException)
        {
            // Servers without this extension cannot report the state, so the file is not held back
            return false;
        }

        return result?["_vs_projectContexts"] is JsonArray { Count: > 0 } contexts
            && contexts.All(context => context?["_vs_is_miscellaneous"]?.GetValue<bool>() is true);
    }

    // LSP leaves a missing severity to the client. Reporting it as an error keeps it visible
    private static bool IsReported(JsonNode diagnostic, DiagnosticSeverity minimum) =>
        (diagnostic["severity"]?.GetValue<int>() ?? (int)DiagnosticSeverity.Error) <= (int)minimum;

    public async Task<JsonObject> FileDiagnosticsAsync(
        string uri,
        CancellationToken cancellationToken
    )
    {
        var response = await session.RequestAsync(
            LspMethods.TextDocumentDiagnostic,
            Document(uri),
            cancellationToken
        );
        return response as JsonObject
            ?? throw new InvalidOperationException("Roslyn did not return a diagnostic report.");
    }

    public async Task<JsonObject> PositionAsync(
        LspRequest request,
        CancellationToken cancellationToken
    )
    {
        var (file, line, character) = await LocateAsync(request, cancellationToken);
        var uri = await session.OpenDocumentAsync(file, cancellationToken);
        return new JsonObject
        {
            ["textDocument"] = new JsonObject { ["uri"] = uri },
            ["position"] = new JsonObject { ["line"] = line - 1, ["character"] = character - 1 },
        };
    }

    /// <summary>
    /// Returns the one-based position a request targets, from its symbol name or its explicit position.
    /// </summary>
    private async Task<(string File, int Line, int Character)> LocateAsync(
        LspRequest request,
        CancellationToken cancellationToken
    )
    {
        if (request.Symbol is not null)
        {
            if (
                request
                is not {
                    File: null,
                    Line: null,
                    Character: null,
                    EndLine: null,
                    EndCharacter: null,
                }
            )
            {
                throw new ArgumentException(
                    "Pass either symbol, or a file with a line and character.",
                    nameof(request)
                );
            }

            var declaration = await resolver.ResolveAsync(request.Symbol, cancellationToken);
            return (declaration.File, declaration.Line, declaration.Character);
        }

        if (request is not { File: not null, Line: > 0, Character: > 0 })
        {
            throw new ArgumentException(
                "Provide symbol, or a file with a one-based line and character.",
                nameof(request)
            );
        }

        return (request.File, request.Line.Value, request.Character.Value);
    }

    public static JsonObject Document(string uri) =>
        new() { ["textDocument"] = new JsonObject { ["uri"] = uri } };

    private IEnumerable<string> SelectFiles(string? file)
    {
        if (
            file?.Contains('*', StringComparison.Ordinal) is false
            && !file.Contains('?', StringComparison.Ordinal)
        )
        {
            yield return paths.Resolve(file);
            yield break;
        }

        var matcher = new Matcher(StringComparison.Ordinal);
        matcher.AddInclude(file is null or "*" ? "**/*.cs" : file);
        foreach (var candidate in paths.EnumerateFiles().Order(StringComparer.Ordinal))
        {
            if (matcher.Match(paths.Root, candidate).HasMatches)
            {
                yield return candidate;
            }
        }
    }
}
