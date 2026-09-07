// Copyright (C) 2026 thomas-fazzari
// SPDX-License-Identifier: GPL-3.0-only

using System.Text.Json.Nodes;

namespace RoslynCodexLsp.Lsp;

internal sealed partial class RoslynSession
{
    private const int FileChangeBatchSize = 128;

    private void StartWatching()
    {
        _watcher = new FileSystemWatcher(paths.Root)
        {
            IncludeSubdirectories = true,
            NotifyFilter =
                NotifyFilters.FileName
                | NotifyFilters.DirectoryName
                | NotifyFilters.LastWrite
                | NotifyFilters.Size,
        };
        _watcher.Created += OnFileChanged;
        _watcher.Changed += OnFileChanged;
        _watcher.Deleted += OnFileChanged;
        _watcher.Renamed += OnFileRenamed;
        _watcher.Error += OnWatcherError;
        _watcher.EnableRaisingEvents = true;
    }

    private void OnFileChanged(object sender, FileSystemEventArgs args) => QueueFile(args.FullPath);

    private void OnFileRenamed(object sender, RenamedEventArgs args)
    {
        QueueFile(args.OldFullPath);
        QueueFile(args.FullPath);
    }

    private void OnWatcherError(object sender, ErrorEventArgs args) =>
        Interlocked.Exchange(ref _scanRequested, 1);

    private void QueueFile(string path)
    {
        var relative = Path.GetRelativePath(paths.Root, path);
        if (
            relative
                .Split(Path.DirectorySeparatorChar)
                .Any(static name => !WorkspacePaths.IsWatchedDirectory(name))
        )
        {
            return;
        }

        if (_pendingFiles.Count >= MaximumWorkspaceFiles)
        {
            Interlocked.Exchange(ref _scanRequested, 1);
            return;
        }

        _pendingFiles[path] = 0;
    }

    private async Task SynchronizeWatchedFilesAsync(CancellationToken cancellationToken)
    {
        var changedPaths = _pendingFiles
            .Keys.Where(path => _pendingFiles.TryRemove(path, out _))
            .ToList();

        var forceAllChanges = Interlocked.Exchange(ref _scanRequested, 0) != 0;
        var rescan = forceAllChanges;
        if (!rescan)
        {
            rescan = changedPaths.Exists(path =>
                Directory.Exists(path) || (!File.Exists(path) && !_workspaceFiles.ContainsKey(path))
            );
        }

        try
        {
            if (rescan)
            {
                var current = ScanWorkspace(cancellationToken);
                await NotifyFileChangesAsync(
                        current,
                        changedPaths.ToHashSet(StringComparer.Ordinal),
                        forceAllChanges,
                        cancellationToken
                    )
                    .ConfigureAwait(false);

                _workspaceFiles = current;
            }
            else if (changedPaths.Count > 0)
            {
                await SynchronizeChangedPathsAsync(changedPaths, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch
        {
            Interlocked.Exchange(ref _scanRequested, 1);
            throw;
        }
    }

    private async Task SynchronizeChangedPathsAsync(
        List<string> changedPaths,
        CancellationToken cancellationToken
    )
    {
        var updates = new Dictionary<string, FileStamp?>(StringComparer.Ordinal);
        var changes = new JsonArray();
        foreach (var path in changedPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var file = new FileInfo(path);
            var exists = _workspaceFiles.ContainsKey(path);
            FileStamp? current = file.Exists
                ? new FileStamp(file.Length, file.LastWriteTimeUtc)
                : null;
            if (current is null)
            {
                if (exists)
                {
                    changes.Add((JsonNode)FileChange(path, FileDeleted));
                }
            }
            else
            {
                // A change signal remains valid when size and timestamp are preserved
                changes.Add((JsonNode)FileChange(path, exists ? FileChanged : FileCreated));
            }

            updates.Add(path, current);
        }

        await SendFileChangesAsync(changes, cancellationToken).ConfigureAwait(false);

        foreach (var (path, stamp) in updates)
        {
            if (stamp is { } value)
            {
                _workspaceFiles[path] = value;
            }
            else
            {
                _workspaceFiles.Remove(path);
            }
        }
    }

    private async Task SendFileChangesAsync(JsonArray changes, CancellationToken cancellationToken)
    {
        foreach (var batch in changes.Chunk(FileChangeBatchSize))
        {
            if (!_watchRegistrations.IsEmpty)
            {
                await NotifyAsync(
                        LspMethods.WorkspaceDidChangeWatchedFiles,
                        new JsonObject
                        {
                            ["changes"] = new JsonArray([
                                .. batch.Select(static change => change?.DeepClone()),
                            ]),
                        },
                        cancellationToken
                    )
                    .ConfigureAwait(false);
            }

            foreach (var change in batch)
            {
                if (change!["type"]!.GetValue<int>() is not FileDeleted)
                {
                    await SynchronizeClosedDocumentAsync(
                            change["uri"]!.GetValue<string>(),
                            cancellationToken
                        )
                        .ConfigureAwait(false);
                }
            }
        }
    }
}
