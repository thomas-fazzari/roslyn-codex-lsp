// Copyright (C) 2026 thomas-fazzari
// SPDX-License-Identifier: GPL-3.0-only

namespace RoslynCodexLsp.Tests.Unit;

public sealed class WorkspacePathsTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("roslyn-path-tests-").FullName;

    [Fact]
    public async Task EnumerateFilesSkipsExcludedSubtreesAsync()
    {
        foreach (var directory in WorkspacePaths.ExcludedDirectories)
        {
            await WriteSourceAsync(Path.Combine(directory, "Nested", "Generated.cs"));
        }

        var source = await WriteSourceAsync(Path.Combine("src", "Source.cs"));
        var paths = new WorkspacePaths(_root);

        paths.EnumerateFiles().Should().Equal(source);
    }

    [Theory]
    [InlineData("analysis")]
    [InlineData("research")]
    [InlineData("vendor")]
    [InlineData("wwwroot")]
    public async Task EnumerateFilesKeepsSourceDirectoriesAsync(string directory)
    {
        var source = await WriteSourceAsync(Path.Combine(directory, "Source.cs"));
        var paths = new WorkspacePaths(_root);

        paths.EnumerateFiles().Should().Equal(source);
        WorkspacePaths.IsWatchedDirectory(directory).Should().BeTrue();
    }

    [Fact]
    public void IntermediateOutputRemainsWatchedForRestoreChanges()
    {
        WorkspacePaths
            .IsWatchedDirectory(WorkspacePaths.IntermediateDirectoryName)
            .Should()
            .BeTrue();
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private async Task<string> WriteSourceAsync(string relativePath)
    {
        var path = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(
            path,
            "class Source {}",
            TestContext.Current.CancellationToken
        );
        return path;
    }
}
