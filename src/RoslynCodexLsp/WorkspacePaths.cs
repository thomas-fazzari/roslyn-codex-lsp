// Copyright (C) 2026 thomas-fazzari
// SPDX-License-Identifier: GPL-3.0-only

using System.Collections.Frozen;

namespace RoslynCodexLsp;

internal sealed class WorkspacePaths(string root)
{
    internal const int MaximumFileCount = 50_000;
    internal const string IntermediateDirectoryName = "obj";

    internal static FrozenSet<string> ExcludedDirectories { get; } =
        new[]
        {
            // VCS
            ".git",
            ".hg",
            ".svn",
            ".jj",
            ".bzr",
            // Editors / IDE
            ".vs",
            ".idea",
            ".vscode",
            ".fleet",
            ".ionide",
            ".history",
            // .NET / build
            "bin",
            IntermediateDirectoryName,
            "packages",
            "publish",
            "artifacts",
            "TestResults",
            "BenchmarkDotNet.Artifacts",
            ".sonarqube",
            ".sonarlint",
            ".cache",
            // Full-stack
            "node_modules",
            ".next",
            ".nuxt",
            ".svelte-kit",
            ".angular",
            ".astro",
            ".docusaurus",
            ".parcel-cache",
            ".turbo",
            ".pnpm-store",
            ".vercel",
            ".serverless",
            "dist",
            "build",
            "out",
            "coverage",
            // Python (e.g. tooling/scripts)
            ".venv",
            "venv",
            "__pycache__",
            // Rust (e.g. tooling)
            "target",
        }.ToFrozenSet(StringComparer.Ordinal);

    public string Root { get; } = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));

    public string Resolve(string file)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(file);
        var local =
            Uri.TryCreate(file, UriKind.Absolute, out var uri) && uri.IsFile ? uri.LocalPath : file;
        var fullPath = Path.GetFullPath(local, Root);
        var relative = Path.GetRelativePath(Root, fullPath);
        if (IsOutside(relative))
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

    /// <summary>
    /// Returns the full path of a file URI inside the workspace, or null for any other location.
    /// </summary>
    public string? TryGetWorkspaceFile(string uri)
    {
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var location) || !location.IsFile)
        {
            return null;
        }

        var fullPath = Path.GetFullPath(location.LocalPath);
        return IsOutside(Path.GetRelativePath(Root, fullPath)) ? null : fullPath;
    }

    /// <summary>
    /// Returns the full path of a file reported in a tool result, which is workspace-relative,
    /// or null for an external or generated location, which keeps its URI.
    /// </summary>
    public string? ResultFilePath(string file) =>
        file.Contains("://", StringComparison.Ordinal) ? null : Path.GetFullPath(file, Root);

    public string RelativePath(string fullPath) =>
        Path.GetRelativePath(Root, fullPath).Replace(Path.DirectorySeparatorChar, '/');

    private static bool IsOutside(string relative) =>
        Path.IsPathRooted(relative)
        || string.Equals(relative, "..", StringComparison.Ordinal)
        || relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    public static bool IsExcludedDirectory(string name) => ExcludedDirectories.Contains(name);

    // Restore changes under obj must still reach Roslyn's file watchers
    public static bool IsWatchedDirectory(string name) =>
        string.Equals(name, IntermediateDirectoryName, StringComparison.Ordinal)
        || !IsExcludedDirectory(name);

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
