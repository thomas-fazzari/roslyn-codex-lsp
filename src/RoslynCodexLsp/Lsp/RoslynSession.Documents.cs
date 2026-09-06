// Copyright (C) 2026 thomas-fazzari
// SPDX-License-Identifier: GPL-3.0-only

using System.Collections.Concurrent;
using System.Text;
using System.Text.Json.Nodes;

namespace RoslynCodexLsp.Lsp;

internal sealed partial class RoslynSession
{
    private const int MaximumDocumentCharacters = 1024 * 1024;
    private const int MaximumOpenDocuments = 128;
    private const int MaximumWorkspaceFiles = 100_000;
    private const int DocumentReadBufferCharacters = 4096;
    private const int InitialDocumentVersion = 1;
    private const int FileCreated = 1;
    private const int FileChanged = 2;
    private const int FileDeleted = 3;
    private readonly Dictionary<string, string> _documents = new(StringComparer.Ordinal);
    private readonly LinkedList<string> _openedOrder = [];
    private readonly Dictionary<string, int> _versions = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _watchRegistrations = new(
        StringComparer.Ordinal
    );
    private readonly ConcurrentDictionary<string, byte> _pendingFiles = new(StringComparer.Ordinal);
    private Dictionary<string, FileStamp> _workspaceFiles = new(StringComparer.Ordinal);
    private FileSystemWatcher? _watcher;
    private int _scanRequested;

    public IReadOnlyDictionary<string, int> DocumentVersions => _versions;

    public async Task<string> OpenDocumentAsync(string file, CancellationToken cancellationToken)
    {
        var path = paths.Resolve(file);
        var uri = paths.ToUri(path);
        if (_documents.ContainsKey(path))
        {
            return uri;
        }

        if (_documents.Count >= MaximumOpenDocuments)
        {
            var oldest =
                _openedOrder.First
                ?? throw new InvalidOperationException("The open document order is empty.");
            await CloseDocumentAsync(oldest.Value, cancellationToken).ConfigureAwait(false);
        }

        var text = await ReadDocumentAsync(path, cancellationToken).ConfigureAwait(false);
        await NotifyAsync(
                LspMethods.TextDocumentDidOpen,
                new JsonObject
                {
                    ["textDocument"] = new JsonObject
                    {
                        ["uri"] = uri,
                        ["languageId"] = "csharp",
                        ["version"] = InitialDocumentVersion,
                        ["text"] = text,
                    },
                },
                cancellationToken
            )
            .ConfigureAwait(false);

        _documents.Add(path, text);
        _openedOrder.AddLast(path);
        _versions.Add(path, InitialDocumentVersion);

        return uri;
    }

    public async Task SynchronizeAsync(CancellationToken cancellationToken)
    {
        foreach (var path in _documents.Keys.ToArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            await SynchronizeDocumentAsync(path, cancellationToken).ConfigureAwait(false);
        }

        await SynchronizeWatchedFilesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task SynchronizeFilesAsync(
        IEnumerable<string> files,
        CancellationToken cancellationToken
    )
    {
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            QueueFile(paths.Resolve(file));
        }

        foreach (var path in _workspaceFiles.Keys)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(path))
            {
                QueueFile(path);
            }
        }

        await SynchronizeAsync(cancellationToken).ConfigureAwait(false);
    }

    internal void RegisterCapabilities(JsonObject parameters)
    {
        if (parameters["registrations"] is not JsonArray registrations)
        {
            return;
        }

        foreach (var registration in registrations.OfType<JsonObject>())
        {
            if (
                string.Equals(
                    registration["method"]?.GetValue<string>(),
                    LspMethods.WorkspaceDidChangeWatchedFiles,
                    StringComparison.Ordinal
                ) && registration["id"]?.GetValue<string>() is { } id
            )
            {
                _watchRegistrations[id] = 0;
            }
        }
    }

    internal void UnregisterCapabilities(JsonObject parameters)
    {
        // The misspelling is part of the LSP 3.17 protocol contract :D
        if (parameters["unregisterations"] is not JsonArray registrations)
        {
            return;
        }

        foreach (var registration in registrations.OfType<JsonObject>())
        {
            if (registration["id"]?.GetValue<string>() is { } id)
            {
                _watchRegistrations.TryRemove(id, out _);
            }
        }
    }

    private static async Task<string> ReadDocumentAsync(
        string path,
        CancellationToken cancellationToken
    )
    {
        await using var stream = new FileStream(
            path,
            new FileStreamOptions
            {
                Access = FileAccess.Read,
                Mode = FileMode.Open,
                Share = FileShare.ReadWrite | FileShare.Delete,
                Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
            }
        );
        using var reader = new StreamReader(
            stream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true
        );
        var builder = new StringBuilder();
        var buffer = new char[DocumentReadBufferCharacters];
        while (true)
        {
            var count = await reader.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (count == 0)
            {
                return builder.ToString();
            }

            if (builder.Length + count > MaximumDocumentCharacters)
            {
                throw new InvalidDataException(
                    "The document exceeded the one million UTF-16 code unit limit."
                );
            }

            builder.Append(buffer, 0, count);
        }
    }

    private async Task SynchronizeDocumentAsync(string path, CancellationToken cancellationToken)
    {
        var uri = paths.ToUri(path);
        if (!File.Exists(path))
        {
            await CloseDocumentAsync(path, cancellationToken).ConfigureAwait(false);
            return;
        }

        var text = await ReadDocumentAsync(path, cancellationToken).ConfigureAwait(false);
        var previous = _documents[path];
        if (string.Equals(previous, text, StringComparison.Ordinal))
        {
            return;
        }

        var version = _versions[path] + 1;
        await NotifyAsync(
                LspMethods.TextDocumentDidChange,
                new JsonObject
                {
                    ["textDocument"] = new JsonObject { ["uri"] = uri, ["version"] = version },
                    ["contentChanges"] = new JsonArray(
                        new JsonObject
                        {
                            ["range"] = new JsonObject
                            {
                                ["start"] = new JsonObject { ["line"] = 0, ["character"] = 0 },
                                ["end"] = EndPosition(previous),
                            },
                            ["text"] = text,
                        }
                    ),
                },
                cancellationToken
            )
            .ConfigureAwait(false);
        _documents[path] = text;
        _versions[path] = version;
    }

    private async Task CloseDocumentAsync(string path, CancellationToken cancellationToken)
    {
        await NotifyAsync(
                LspMethods.TextDocumentDidClose,
                new JsonObject
                {
                    ["textDocument"] = new JsonObject { ["uri"] = paths.ToUri(path) },
                },
                cancellationToken
            )
            .ConfigureAwait(false);
        _documents.Remove(path);
        _versions.Remove(path);
        _openedOrder.Remove(path);
    }

    internal static JsonObject EndPosition(string text)
    {
        var line = 0;
        var character = 0;
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] is '\r' or '\n')
            {
                if (text[index] == '\r' && index + 1 < text.Length && text[index + 1] == '\n')
                {
                    index++;
                }

                line++;
                character = 0;
            }
            else
            {
                character++;
            }
        }

        return new JsonObject { ["line"] = line, ["character"] = character };
    }

    private Dictionary<string, FileStamp> ScanWorkspace(CancellationToken cancellationToken)
    {
        var files = new Dictionary<string, FileStamp>(StringComparer.Ordinal);
        var pending = new Stack<DirectoryInfo>();
        pending.Push(new DirectoryInfo(paths.Root));
        var enumeration = new EnumerationOptions
        {
            AttributesToSkip = FileAttributes.ReparsePoint,
            IgnoreInaccessible = true,
        };

        while (pending.TryPop(out var directory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var entry in directory.EnumerateFileSystemInfos("*", enumeration))
            {
                AddWorkspaceEntry(entry, files, pending);
            }
        }

        return files;
    }

    private static void AddWorkspaceEntry(
        FileSystemInfo entry,
        Dictionary<string, FileStamp> files,
        Stack<DirectoryInfo> pending
    )
    {
        switch (entry)
        {
            case DirectoryInfo directory:
                if (WorkspacePaths.IsWatchedDirectory(directory.Name))
                {
                    pending.Push(directory);
                }
                return;

            case FileInfo when files.Count >= MaximumWorkspaceFiles:
                throw new InvalidOperationException(
                    "The workspace exceeded the 100,000 file limit. Select a narrower workspace root."
                );

            case FileInfo file:
                files.Add(file.FullName, new FileStamp(file.Length, file.LastWriteTimeUtc));
                break;
        }
    }

    private async Task NotifyFileChangesAsync(
        Dictionary<string, FileStamp> current,
        CancellationToken cancellationToken
    )
    {
        var changes = new JsonArray();
        foreach (var (path, stamp) in current)
        {
            if (!_workspaceFiles.TryGetValue(path, out var previous))
            {
                changes.Add((JsonNode)FileChange(path, FileCreated));
            }
            else if (stamp != previous)
            {
                changes.Add((JsonNode)FileChange(path, FileChanged));
            }
        }

        foreach (var path in _workspaceFiles.Keys.Where(path => !current.ContainsKey(path)))
        {
            changes.Add((JsonNode)FileChange(path, FileDeleted));
        }

        await SendFileChangesAsync(changes, cancellationToken).ConfigureAwait(false);
    }

    private JsonObject FileChange(string path, int type) =>
        new() { ["uri"] = paths.ToUri(path), ["type"] = type };

    private readonly record struct FileStamp(long Length, DateTime ModifiedUtc);
}
