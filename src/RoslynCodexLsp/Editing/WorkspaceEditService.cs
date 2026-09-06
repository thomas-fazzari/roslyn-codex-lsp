// Copyright (C) 2026 thomas-fazzari
// SPDX-License-Identifier: GPL-3.0-only

using System.Text.Json.Nodes;

namespace RoslynCodexLsp.Editing;

internal sealed partial class WorkspaceEditService(WorkspacePaths paths)
{
    internal const int MaximumFileBytes = 8 * BytesPerMebibyte;
    internal const string CreateFileOperation = "create";
    internal const string RenameFileOperation = "rename";
    internal const string DeleteFileOperation = "delete";

    private const int BytesPerMebibyte = 1024 * 1024;
    private const int MaximumEditBytes = 64 * BytesPerMebibyte;
    private const int MaximumEditedFiles = 256;

    public async Task<JsonObject> PreviewAsync(
        JsonObject edit,
        WorkspaceSnapshot snapshot,
        CancellationToken cancellationToken
    )
    {
        var documents = await PlanAsync(edit, snapshot, cancellationToken);
        await EnsureCurrentAsync(snapshot, cancellationToken);
        return Describe(documents, applied: false);
    }

    public async Task<JsonObject> ApplyAsync(
        JsonObject edit,
        WorkspaceSnapshot snapshot,
        CancellationToken cancellationToken
    )
    {
        var documents = await PlanAsync(edit, snapshot, cancellationToken);
        var result = Describe(documents, applied: true);
        await EnsureCurrentAsync(snapshot, cancellationToken);
        var temporaryFiles = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            await StageAsync(documents, temporaryFiles, cancellationToken);
            await EnsureCurrentAsync(snapshot, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var (destination, temporary) in temporaryFiles)
            {
                paths.Resolve(destination);
                File.Move(temporary, destination, overwrite: true);
            }

            foreach (
                var document in documents.Where(document =>
                    document is { CaseOnlyRename: true, Content: not null }
                )
            )
            {
                File.Move(document.OriginalPath, document.Path, overwrite: true);
            }

            foreach (
                var document in documents.Where(document =>
                    document is { Changed: true, Content: null }
                )
            )
            {
                paths.Resolve(document.Path);
                File.Delete(document.Path);
            }
        }
        finally
        {
            foreach (var temporary in temporaryFiles.Values)
            {
                File.Delete(temporary);
            }
        }

        return result;
    }

    private async Task StageAsync(
        List<EditedDocument> documents,
        Dictionary<string, string> temporaryFiles,
        CancellationToken cancellationToken
    )
    {
        foreach (
            var document in documents.Where(document =>
                document is { Changed: true, Content: not null }
            )
        )
        {
            paths.Resolve(document.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(document.Path)!);
            var temporary = $"{document.Path}.{Guid.NewGuid():N}.tmp";
            temporaryFiles.Add(document.Path, temporary);
            await File.WriteAllBytesAsync(temporary, document.Content!, cancellationToken);
            if (!OperatingSystem.IsWindows() && document.Original is not null)
            {
                File.SetUnixFileMode(temporary, File.GetUnixFileMode(document.OriginalPath));
            }
        }
    }

    private async Task<List<EditedDocument>> PlanAsync(
        JsonObject edit,
        WorkspaceSnapshot snapshot,
        CancellationToken cancellationToken
    )
    {
        var documents = new Dictionary<string, EditedDocument>(StringComparer.Ordinal);
        if (edit["changes"] is not null && edit["documentChanges"] is not null)
        {
            throw new InvalidOperationException(
                "A workspace edit cannot use both changes and documentChanges."
            );
        }

        if (edit["changes"] is { } changesNode)
        {
            var changes = changesNode.AsObject();
            foreach (var (uri, value) in changes)
            {
                var document = await GetDocumentAsync(uri, documents, snapshot, cancellationToken);
                document.Apply(
                    value?.AsArray() ?? throw new InvalidOperationException("Missing text edits.")
                );
            }
        }

        if (edit["documentChanges"] is { } operationsNode)
        {
            foreach (var operation in operationsNode.AsArray())
            {
                await PlanOperationAsync(
                    operation?.AsObject()
                        ?? throw new InvalidOperationException("Missing document operation."),
                    documents,
                    snapshot,
                    cancellationToken
                );
                CheckSize(documents.Values);
            }
        }

        CheckSize(documents.Values);
        ValidateParents(documents);
        return [.. documents.Values];
    }

    private void ValidateParents(Dictionary<string, EditedDocument> documents)
    {
        foreach (var document in documents.Values.Where(document => document.Content is not null))
        {
            var parent = Path.GetDirectoryName(document.Path);
            while (parent is not null && !StringComparer.Ordinal.Equals(parent, paths.Root))
            {
                if (
                    File.Exists(parent)
                    || (
                        documents.TryGetValue(parent, out var planned)
                        && planned.Content is not null
                    )
                )
                {
                    throw new InvalidOperationException(
                        $"A file blocks the target directory: {parent}"
                    );
                }

                parent = Path.GetDirectoryName(parent);
            }
        }
    }

    private async Task PlanOperationAsync(
        JsonObject operation,
        Dictionary<string, EditedDocument> documents,
        WorkspaceSnapshot snapshot,
        CancellationToken cancellationToken
    )
    {
        switch (operation["kind"]?.GetValue<string>())
        {
            case null:
                await PlanTextEditAsync(operation, documents, snapshot, cancellationToken);
                break;
            case CreateFileOperation:
                await PlanCreateAsync(operation, documents, snapshot, cancellationToken);
                break;
            case RenameFileOperation:
                await PlanRenameAsync(operation, documents, snapshot, cancellationToken);
                break;
            case DeleteFileOperation:
                await PlanDeleteAsync(operation, documents, snapshot, cancellationToken);
                break;
            default:
                throw new InvalidOperationException(
                    $"Unsupported workspace operation: {operation["kind"]}"
                );
        }
    }

    private async Task PlanTextEditAsync(
        JsonObject operation,
        Dictionary<string, EditedDocument> documents,
        WorkspaceSnapshot snapshot,
        CancellationToken cancellationToken
    )
    {
        var identifier =
            operation["textDocument"]
            ?? throw new InvalidOperationException("A document edit needs textDocument.");
        var document = await GetDocumentAsync(
            RequiredString(identifier, "uri"),
            documents,
            snapshot,
            cancellationToken
        );
        if (identifier["version"] is JsonValue version)
        {
            if (
                !snapshot.DocumentVersions.TryGetValue(document.OriginalPath, out var expected)
                || version.GetValue<int>() != expected
            )
            {
                throw new StaleEditException(
                    $"The document version is stale or unknown: {document.Path}"
                );
            }
        }

        document.Apply(
            operation["edits"]?.AsArray()
                ?? throw new InvalidOperationException("Missing text edits.")
        );
    }

    private async Task PlanCreateAsync(
        JsonObject operation,
        Dictionary<string, EditedDocument> documents,
        WorkspaceSnapshot snapshot,
        CancellationToken cancellationToken
    )
    {
        var document = await GetDocumentAsync(
            RequiredString(operation, "uri"),
            documents,
            snapshot,
            cancellationToken
        );
        if (document.Content is not null && !Option(operation, "overwrite"))
        {
            if (Option(operation, "ignoreIfExists"))
            {
                return;
            }

            throw new InvalidOperationException($"The file already exists: {document.Path}");
        }

        document.Content = [];
    }

    private async Task PlanRenameAsync(
        JsonObject operation,
        Dictionary<string, EditedDocument> documents,
        WorkspaceSnapshot snapshot,
        CancellationToken cancellationToken
    )
    {
        var source = await GetDocumentAsync(
            RequiredString(operation, "oldUri"),
            documents,
            snapshot,
            cancellationToken
        );
        if (source.Content is null)
        {
            throw new InvalidOperationException(
                $"The file to rename does not exist: {source.Path}"
            );
        }

        var newUri = RequiredString(operation, "newUri");
        var destinationPath = ResolveEditPath(newUri);
        if (IsCaseOnlyAlias(source, destinationPath, snapshot))
        {
            source.Path = destinationPath;
            return;
        }

        var destination = await GetDocumentAsync(newUri, documents, snapshot, cancellationToken);
        if (ReferenceEquals(source, destination))
        {
            return;
        }

        if (destination.Content is not null && !Option(operation, "overwrite"))
        {
            if (Option(operation, "ignoreIfExists"))
            {
                return;
            }

            throw new InvalidOperationException(
                $"The rename target already exists: {destination.Path}"
            );
        }

        destination.Content = source.Content;
        source.Content = null;
    }

    private static bool IsCaseOnlyAlias(
        EditedDocument source,
        string destination,
        WorkspaceSnapshot snapshot
    )
    {
        return !StringComparer.Ordinal.Equals(source.Path, destination)
            && StringComparer.OrdinalIgnoreCase.Equals(source.Path, destination)
            && StringComparer.Ordinal.Equals(
                Path.GetDirectoryName(source.Path),
                Path.GetDirectoryName(destination)
            )
            && File.Exists(destination)
            && (
                !snapshot.Fingerprints.ContainsKey(destination)
                || StringComparer.Ordinal.Equals(source.OriginalPath, destination)
            );
    }

    private async Task PlanDeleteAsync(
        JsonObject operation,
        Dictionary<string, EditedDocument> documents,
        WorkspaceSnapshot snapshot,
        CancellationToken cancellationToken
    )
    {
        if (Option(operation, "recursive"))
        {
            throw new InvalidOperationException("Recursive delete operations are not supported.");
        }

        var document = await GetDocumentAsync(
            RequiredString(operation, "uri"),
            documents,
            snapshot,
            cancellationToken
        );
        if (document.Content is null && !Option(operation, "ignoreIfNotExists"))
        {
            throw new InvalidOperationException(
                $"The file to delete does not exist: {document.Path}"
            );
        }

        document.Content = null;
    }

    private async Task<EditedDocument> GetDocumentAsync(
        string uri,
        Dictionary<string, EditedDocument> documents,
        WorkspaceSnapshot snapshot,
        CancellationToken cancellationToken
    )
    {
        var path = ResolveEditPath(uri);
        if (documents.TryGetValue(path, out var document))
        {
            return document;
        }

        document = documents.Values.FirstOrDefault(candidate =>
            candidate.CaseOnlyRename && StringComparer.Ordinal.Equals(candidate.Path, path)
        );
        if (document is not null)
        {
            return document;
        }

        if (Directory.Exists(path))
        {
            throw new InvalidOperationException($"Workspace edits support files only: {path}");
        }

        var content = await ReadOriginalAsync(path, snapshot, cancellationToken);
        document = new EditedDocument(path, content);
        documents.Add(path, document);
        CheckSize(documents.Values);
        return document;
    }

    private JsonObject Describe(List<EditedDocument> documents, bool applied)
    {
        var changed = documents.Where(document => document.Changed).ToList();
        return new JsonObject
        {
            ["applied"] = applied,
            ["fileCount"] = changed.Count,
            ["files"] = new JsonArray(
                changed.Select(document => (JsonNode)document.Describe(paths.Root)).ToArray()
            ),
        };
    }

    private static string RequiredString(JsonNode node, string property)
    {
        return node[property]?.GetValue<string>()
            ?? throw new InvalidOperationException($"Missing {property}.");
    }

    private static bool Option(JsonObject operation, string option)
    {
        return operation["options"]?[option]?.GetValue<bool>() ?? false;
    }

    private static void CheckSize(Dictionary<string, EditedDocument>.ValueCollection documents)
    {
        var total = documents.Sum(document =>
            (long)(document.Original?.Length ?? 0)
            + (
                ReferenceEquals(document.Original, document.Content)
                    ? 0
                    : document.Content?.Length ?? 0
            )
        );
        if (documents.Count > MaximumEditedFiles || total > MaximumEditBytes)
        {
            throw new InvalidOperationException(
                $"The workspace edit exceeds {MaximumEditedFiles} files or {MaximumEditBytes / BytesPerMebibyte} MiB."
            );
        }
    }
}
