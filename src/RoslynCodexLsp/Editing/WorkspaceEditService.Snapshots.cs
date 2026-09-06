// Copyright (C) 2026 thomas-fazzari
// SPDX-License-Identifier: GPL-3.0-only

using System.Collections.Frozen;
using System.Security.Cryptography;

namespace RoslynCodexLsp.Editing;

internal sealed partial class WorkspaceEditService
{
    internal const int MaximumSnapshotBytes = 256 * BytesPerMebibyte;

    private const int UnbufferedStreamBufferSize = 1;

    private static readonly FrozenSet<string> _sourceExtensions = new[]
    {
        ".cs",
        ".csx",
        ".csproj",
        ".props",
        ".targets",
        ".sln",
        ".slnx",
        ".json",
        ".config",
        ".editorconfig",
        ".globalconfig",
        ".razor",
        ".cshtml",
        ".resx",
        ".xaml",
        ".txt",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    public async Task<WorkspaceSnapshot> CaptureAsync(CancellationToken cancellationToken)
    {
        var fingerprints = await CaptureFingerprintsAsync(cancellationToken);
        return new WorkspaceSnapshot(fingerprints, FrozenDictionary<string, int>.Empty);
    }

    private async Task<Dictionary<string, string>> CaptureFingerprintsAsync(
        CancellationToken cancellationToken
    )
    {
        var fingerprints = new Dictionary<string, string>(StringComparer.Ordinal);
        var totalBytes = 0L;
        foreach (var file in paths.EnumerateFiles())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsSourceFile(file))
            {
                continue;
            }

            totalBytes += new FileInfo(file).Length;
            if (totalBytes > MaximumSnapshotBytes)
            {
                throw new InvalidOperationException(
                    $"The workspace snapshot exceeds {MaximumSnapshotBytes / BytesPerMebibyte} MiB of source and project files."
                );
            }

            fingerprints.Add(file, await FingerprintAsync(file, cancellationToken));
        }

        return fingerprints;
    }

    private async Task EnsureCurrentAsync(
        WorkspaceSnapshot snapshot,
        CancellationToken cancellationToken
    )
    {
        var current = await CaptureFingerprintsAsync(cancellationToken);
        if (
            current.Count != snapshot.Fingerprints.Count
            || snapshot.Fingerprints.Any(pair =>
                !current.TryGetValue(pair.Key, out var hash)
                || !StringComparer.Ordinal.Equals(pair.Value, hash)
            )
        )
        {
            throw new StaleEditException(
                "Workspace files changed after Roslyn computed this edit. Request the action again."
            );
        }
    }

    private static async Task<byte[]?> ReadOriginalAsync(
        string path,
        WorkspaceSnapshot snapshot,
        CancellationToken cancellationToken
    )
    {
        if (!File.Exists(path))
        {
            if (snapshot.Fingerprints.ContainsKey(path))
            {
                throw new StaleEditException($"The source file was deleted: {path}");
            }

            return null;
        }

        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite,
            bufferSize: UnbufferedStreamBufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan
        );
        if (stream.Length > MaximumFileBytes)
        {
            throw new InvalidOperationException(
                $"The source file exceeds {MaximumFileBytes / BytesPerMebibyte} MiB: {path}"
            );
        }

        var content = new byte[(int)stream.Length];
        await stream.ReadExactlyAsync(content, cancellationToken);
        var fingerprint = Convert.ToHexString(SHA256.HashData(content));
        if (
            stream.Length != content.Length
            || !snapshot.Fingerprints.TryGetValue(path, out var expected)
            || !StringComparer.Ordinal.Equals(expected, fingerprint)
        )
        {
            throw new StaleEditException($"The source file changed after the request: {path}");
        }

        return content;
    }

    private string ResolveEditPath(string value)
    {
        var path = value;
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            if (
                !uri.IsFile
                || !string.IsNullOrEmpty(uri.Host)
                || !string.IsNullOrEmpty(uri.Query)
                || !string.IsNullOrEmpty(uri.Fragment)
            )
            {
                throw new InvalidOperationException("Workspace edits require local file URIs.");
            }

            path = uri.LocalPath;
        }

        var resolved = paths.Resolve(path);
        if (!IsSourceFile(resolved))
        {
            throw new InvalidOperationException(
                "Workspace edits require a supported source or project file extension."
            );
        }
        var components = Path.GetRelativePath(paths.Root, resolved)
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (components.Any(WorkspacePaths.IsExcludedDirectory))
        {
            throw new InvalidOperationException(
                "Workspace edits cannot target excluded workspace directories."
            );
        }

        return resolved;
    }

    private static bool IsSourceFile(string path)
    {
        return Path.GetFileName(path) is ".editorconfig" or ".globalconfig"
            || _sourceExtensions.Contains(Path.GetExtension(path));
    }

    private static async Task<string> FingerprintAsync(
        string path,
        CancellationToken cancellationToken
    )
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite,
            bufferSize: UnbufferedStreamBufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan
        );
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
    }
}
