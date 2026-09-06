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

        return Limit(
            await session.RequestAsync(method, parameters, cancellationToken),
            request.Limit
        );
    }

    public async Task<JsonNode?> SymbolsAsync(
        LspRequest request,
        CancellationToken cancellationToken
    )
    {
        if (request.File is null)
        {
            return Limit(
                await session.RequestAsync(
                    LspMethods.WorkspaceSymbol,
                    new JsonObject { ["query"] = request.Query ?? string.Empty },
                    cancellationToken
                ),
                request.Limit
            );
        }

        var uri = await session.OpenDocumentAsync(request.File, cancellationToken);
        return Limit(
            await session.RequestAsync(
                LspMethods.TextDocumentDocumentSymbol,
                Document(uri),
                cancellationToken
            ),
            request.Limit
        );
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
        foreach (var file in files)
        {
            var uri = await session.OpenDocumentAsync(file, cancellationToken);
            var report = await FileDiagnosticsAsync(uri, cancellationToken);
            var diagnostics = report["items"] as JsonArray ?? [];
            total += diagnostics.Count;
            var selected = new JsonArray();

            foreach (var diagnostic in diagnostics.Take(remaining))
            {
                selected.Add(diagnostic?.DeepClone());
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

        return new JsonObject
        {
            ["files"] = results,
            ["complete"] = true,
            ["total"] = total,
            ["truncated"] = total > request.Limit,
        };
    }

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

    private static JsonNode? Limit(JsonNode? result, int limit)
    {
        if (result is not JsonArray array || array.Count <= limit)
        {
            return result;
        }

        return new JsonObject
        {
            ["items"] = new JsonArray(
                array.Take(limit).Select(static item => item?.DeepClone()).ToArray()
            ),
            ["total"] = array.Count,
            ["truncated"] = true,
        };
    }
}
