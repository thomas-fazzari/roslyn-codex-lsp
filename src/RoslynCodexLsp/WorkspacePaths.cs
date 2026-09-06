// Copyright (C) 2026 thomas-fazzari
// SPDX-License-Identifier: GPL-3.0-only

using System.Collections.Frozen;

namespace RoslynCodexLsp;

internal sealed class WorkspacePaths(string root)
{
    internal const int MaximumFileCount = 50_000;

    private static readonly FrozenSet<string> _excludedDirectories = new[]
    {
        ".git",
        ".vs",
        ".idea",
        ".vscode",
        "bin",
        "obj",
        "node_modules",
        "analysis",
        "research",
    }.ToFrozenSet(StringComparer.Ordinal);

    public string Root { get; } = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));

    public string Resolve(string file)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(file);
        var local =
            Uri.TryCreate(file, UriKind.Absolute, out var uri) && uri.IsFile ? uri.LocalPath : file;
        var fullPath = Path.GetFullPath(local, Root);
        var relative = Path.GetRelativePath(Root, fullPath);
        if (
            Path.IsPathRooted(relative)
            || string.Equals(relative, "..", StringComparison.Ordinal)
            || relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
        )
        {
            throw new ArgumentException("The path must be inside the workspace.", nameof(file));
        }

        var current = Root;
        foreach (var part in relative.Split(Path.DirectorySeparatorChar))
        {
            current = Path.Combine(current, part);
            FileSystemInfo entry = Directory.Exists(current)
                ? new DirectoryInfo(current)
                : new FileInfo(current);
            if (entry.LinkTarget is not null)
            {
                throw new ArgumentException(
                    "Symbolic links inside the workspace are not supported.",
                    nameof(file)
                );
            }
        }

        return fullPath;
    }

    public string ToUri(string file) => new Uri(Resolve(file)).AbsoluteUri;

    public static bool IsExcludedDirectory(string name) => _excludedDirectories.Contains(name);

    public IEnumerable<string> EnumerateFiles()
    {
        var pending = new Stack<string>();
        pending.Push(Root);
        var count = 0;
        while (pending.TryPop(out var directory))
        {
            foreach (var entry in new DirectoryInfo(directory).EnumerateFileSystemInfos())
            {
                if (entry.LinkTarget is not null)
                {
                    continue;
                }

                if (entry is DirectoryInfo)
                {
                    if (!IsExcludedDirectory(entry.Name))
                    {
                        pending.Push(entry.FullName);
                    }
                }
                else
                {
                    if (++count > MaximumFileCount)
                    {
                        throw new InvalidOperationException(
                            $"The workspace exceeds {MaximumFileCount} files. Select a narrower root."
                        );
                    }

                    yield return entry.FullName;
                }
            }
        }
    }
}
