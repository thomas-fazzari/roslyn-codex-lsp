// Copyright (C) 2026 thomas-fazzari
// SPDX-License-Identifier: GPL-3.0-only

using System.Text.Json.Nodes;
using Microsoft.Extensions.FileSystemGlobbing;
using RoslynCodexLsp.Lsp;

namespace RoslynCodexLsp.Tools;

internal sealed class LspQueries(RoslynSession session, WorkspacePaths paths)
{
    private const int MaximumDiagnosticFiles = 1000;

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

        var results = new JsonArray();
        var remaining = request.Limit;
        var total = 0;
        var belowSeverity = 0;
        var filesWithDiagnostics = 0;
        foreach (var batch in files.Chunk(RoslynSession.MaximumOpenDocuments))
        {
            // Each opened document creates a new solution version and discards diagnostics
            // computed for earlier versions, so open the whole batch before pulling reports
            var uris = new List<string>(batch.Length);
            foreach (var file in batch)
            {
                uris.Add(await session.OpenDocumentAsync(file, cancellationToken));
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

        return new JsonObject
        {
            ["filesChecked"] = files.Length,
            ["filesWithDiagnostics"] = filesWithDiagnostics,
            ["diagnostics"] = results,
            ["complete"] = true,
            ["total"] = total,
            ["belowSeverity"] = belowSeverity,
            ["truncated"] = total > request.Limit,
        };
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
        ArgumentException.ThrowIfNullOrWhiteSpace(request.File, nameof(request));
        if (request.Line is not > 0 || request.Character is not > 0)
        {
            throw new ArgumentException("Provide a one-based line and character.", nameof(request));
        }

        var uri = await session.OpenDocumentAsync(request.File, cancellationToken);
        return new JsonObject
        {
            ["textDocument"] = new JsonObject { ["uri"] = uri },
            ["position"] = new JsonObject
            {
                ["line"] = request.Line.Value - 1,
                ["character"] = request.Character.Value - 1,
            },
        };
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
