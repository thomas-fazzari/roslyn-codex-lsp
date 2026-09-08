// Copyright (C) 2026 thomas-fazzari
// SPDX-License-Identifier: GPL-3.0-only

using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using RoslynCodexLsp.Editing;

namespace RoslynCodexLsp.Tests.Unit.Editing;

public sealed class WorkspaceEditServiceTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("roslyn-edit-tests-").FullName;

    private static CancellationToken TestCancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ApplyPreservesUtf8BomCrLfAndUtf16PositionsAsync()
    {
        var path = Path.Combine(_root, "Source.cs");
        await File.WriteAllBytesAsync(
            path,
            [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("// 😀 name\r\nclass C {}\r\n")],
            TestCancellation
        );
        var service = CreateService();
        var snapshot = await service.CaptureAsync(TestCancellation);
        var edit = Changes(path, TextEdit(0, 6, 0, 10, "value"));

        var result = await service.ApplyAsync(edit, snapshot, TestCancellation);

        result["applied"]!.GetValue<bool>().Should().BeTrue();
        var bytes = await File.ReadAllBytesAsync(path, TestCancellation);
        bytes
            .Should()
            .Equal([0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("// 😀 value\r\nclass C {}\r\n")]);
    }

    [Fact]
    public async Task ApplyReturnsCompactReceiptForManyLargeTextFilesAsync()
    {
        const int fileCount = 64;
        const int textLength = EditedDocument.MaximumPreviewCharacters + 100;
        var edit = new JsonObject { ["changes"] = new JsonObject() };
        var changes = edit["changes"]!.AsObject();
        for (var index = 0; index < fileCount; index++)
        {
            var path = await WriteAsync(
                string.Create(CultureInfo.InvariantCulture, $"Source{index}.cs"),
                new string('a', textLength) + "\n"
            );
            changes[new Uri(path).AbsoluteUri] = new JsonArray(TextEdit(0, 0, 0, 1, "b"));
        }

        var service = CreateService();
        var snapshot = await service.CaptureAsync(TestCancellation);

        var result = await service.ApplyAsync(edit, snapshot, TestCancellation);

        result["applied"]!.GetValue<bool>().Should().BeTrue();
        result["fileCount"]!.GetValue<int>().Should().Be(fileCount);
        var files = result["files"]!.AsArray();
        files.Should().HaveCount(fileCount);
        files
            .OfType<JsonObject>()
            .Should()
            .AllSatisfy(file =>
            {
                file.Select(property => property.Key).Should().BeEquivalentTo(["path", "kind"]);
                file["path"]!.GetValue<string>().Should().NotBeNullOrWhiteSpace();
                file["kind"]!
                    .GetValue<string>()
                    .Should()
                    .Be(WorkspaceEditService.ChangeFileOperation);
            });
    }

    [Fact]
    public async Task ApplyReportsCompletedWritesWhenLaterDeleteFailsAsync()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("This test requires Unix directory permissions.");
            return;
        }

        Assert.SkipWhen(
            string.Equals(Environment.UserName, "root", StringComparison.OrdinalIgnoreCase),
            "The root user bypasses Unix directory permissions."
        );

        var first = await WriteAsync("First.cs", "class First {}");
        var directory = Directory.CreateDirectory(Path.Combine(_root, "Nested"));
        var deleted = await WriteAsync(Path.Combine("Nested", "Delete.cs"), "class Delete {}");
        var service = CreateService();
        var snapshot = await service.CaptureAsync(TestCancellation);
        var edit = DocumentChanges(
            DocumentEdit(first, version: null, TextEdit(0, 6, 0, 11, "Changed")),
            DeleteFile(deleted)
        );
        var originalMode = File.GetUnixFileMode(directory.FullName);
        File.SetUnixFileMode(directory.FullName, UnixFileMode.UserRead | UnixFileMode.UserExecute);

        try
        {
            var apply = () => service.ApplyAsync(edit, snapshot, TestCancellation);
            var exception = (await apply.Should().ThrowAsync<EditApplicationException>()).Which;

            exception.Phase.Should().Be(EditApplicationPhase.Write);
            exception.FailedPath.Should().Be(Path.Combine("Nested", "Delete.cs"));
            exception.FailedOperation.Should().Be(WorkspaceEditService.DeleteFileOperation);
            (exception.InnerException is IOException or UnauthorizedAccessException)
                .Should()
                .BeTrue();
            exception.Result["applied"]!.GetValue<bool>().Should().BeFalse();
            exception.Result["fileCount"]!.GetValue<int>().Should().Be(1);
            exception.Result["files"]![0]!["path"]!.GetValue<string>().Should().Be("First.cs");
            exception.Result["files"]![0]!["kind"]!
                .GetValue<string>()
                .Should()
                .Be(WorkspaceEditService.ChangeFileOperation);
            await AssertFileTextAsync(first, "class Changed {}");
            File.Exists(deleted).Should().BeTrue();
        }
        finally
        {
            File.SetUnixFileMode(directory.FullName, originalMode);
        }
    }

    [Fact]
    public async Task PreviewDoesNotChangeFilesAsync()
    {
        var path = await WriteAsync("Source.cs", "class A {}");
        var service = CreateService();
        var snapshot = await service.CaptureAsync(TestCancellation);

        var preview = await service.PreviewAsync(
            Changes(path, TextEdit(0, 6, 0, 7, "B")),
            snapshot,
            TestCancellation
        );

        preview["fileCount"]!.GetValue<int>().Should().Be(1);
        preview["files"]![0]!["changes"]![0]!["after"]!["text"]!
            .GetValue<string>()
            .Should()
            .Be("class B {}");
        await AssertFileTextAsync(path, "class A {}");
    }

    [Fact]
    public async Task GlobalPreviewBudgetDoesNotLimitAppliedFilesAsync()
    {
        const int fileCount = 64;
        var before = new string('a', EditedDocument.MaximumPreviewCharacters + 100);
        var after = new string('b', before.Length);
        var changes = new JsonObject();
        var paths = new string[fileCount];
        for (var index = 0; index < fileCount; index++)
        {
            var path = await WriteAsync(
                string.Create(CultureInfo.InvariantCulture, $"Source{index}.cs"),
                before
            );
            paths[index] = path;
            changes[new Uri(path).AbsoluteUri] = new JsonArray(
                TextEdit(0, 0, 0, before.Length, after)
            );
        }

        var edit = new JsonObject { ["changes"] = changes };
        var service = CreateService();
        var snapshot = await service.CaptureAsync(TestCancellation);

        var preview = await service.PreviewAsync(edit, snapshot, TestCancellation);

        preview["fileCount"]!.GetValue<int>().Should().Be(fileCount);
        preview["files"]!.AsArray().Count.Should().BeInRange(1, fileCount - 1);
        preview["filesTruncated"]!.GetValue<bool>().Should().BeTrue();
        preview
            .ToJsonString(BridgeJsonContext.Default.Options)
            .Length.Should()
            .BeLessThanOrEqualTo(EditPreview.MaximumResponseCharacters);
        await AssertFileTextAsync(Path.Combine(_root, "Source0.cs"), before);

        var applied = await service.ApplyAsync(edit, snapshot, TestCancellation);

        applied["fileCount"]!.GetValue<int>().Should().Be(fileCount);
        foreach (var path in paths)
        {
            await AssertFileTextAsync(path, after);
        }
    }

    [Fact]
    public async Task StaleSecondFilePreventsEveryWriteAsync()
    {
        var first = await WriteAsync("First.cs", "class A {}");
        var second = await WriteAsync("Second.cs", "class B {}");
        var service = CreateService();
        var snapshot = await service.CaptureAsync(TestCancellation);
        var edit = Changes(first, TextEdit(0, 6, 0, 7, "Changed"));
        edit["changes"]![new Uri(second).AbsoluteUri] = new JsonArray(
            TextEdit(0, 6, 0, 7, "Changed")
        );
        await File.WriteAllTextAsync(second, "class External {}", TestCancellation);

        var apply = () => service.ApplyAsync(edit, snapshot, TestCancellation);

        await apply.Should().ThrowAsync<InvalidOperationException>();
        await AssertFileTextAsync(first, "class A {}");
        await AssertFileTextAsync(second, "class External {}");
    }

    [Fact]
    public async Task ExternalChangeToUneditedSourceInvalidatesEditAsync()
    {
        var target = await WriteAsync("Target.cs", "class A {}");
        var dependency = await WriteAsync("Dependency.cs", "class B {}");
        var service = CreateService();
        var snapshot = await service.CaptureAsync(TestCancellation);
        await File.WriteAllTextAsync(dependency, "class Other {}", TestCancellation);

        var apply = () =>
            service.ApplyAsync(
                Changes(target, TextEdit(0, 6, 0, 7, "C")),
                snapshot,
                TestCancellation
            );

        await apply.Should().ThrowAsync<InvalidOperationException>();
        await AssertFileTextAsync(target, "class A {}");
    }

    [Fact]
    public async Task ResourceOperationsUseTheirDeclaredOrderAsync()
    {
        var oldPath = await WriteAsync("Old.cs", "class Old {}");
        var deleted = await WriteAsync("Delete.cs", "class Delete {}");
        var created = Path.Combine(_root, "New.cs");
        var renamed = Path.Combine(_root, "Renamed.cs");
        var service = CreateService();
        var snapshot = await service.CaptureAsync(TestCancellation);
        var edit = DocumentChanges(
            CreateFile(created),
            DocumentEdit(created, version: null, TextEdit(0, 0, 0, 0, "class New {}")),
            RenameFile(oldPath, renamed),
            DocumentEdit(renamed, version: null, TextEdit(0, 6, 0, 9, "Renamed")),
            DeleteFile(deleted)
        );

        await service.ApplyAsync(edit, snapshot, TestCancellation);

        await AssertFileTextAsync(created, "class New {}");
        await AssertFileTextAsync(renamed, "class Renamed {}");
        File.Exists(oldPath).Should().BeFalse();
        File.Exists(deleted).Should().BeFalse();
    }

    [Fact]
    public async Task OverlappingRangesAreRejectedAsync()
    {
        var path = await WriteAsync("Source.cs", "class A {}");
        var service = CreateService();
        var snapshot = await service.CaptureAsync(TestCancellation);
        var edit = Changes(path, TextEdit(0, 0, 0, 5, "struct"), TextEdit(0, 3, 0, 7, "invalid"));

        var apply = () => service.ApplyAsync(edit, snapshot, TestCancellation);

        await apply.Should().ThrowAsync<InvalidOperationException>();
        await AssertFileTextAsync(path, "class A {}");
    }

    [Fact]
    public async Task VersionedDocumentEditRequiresCapturedVersionAsync()
    {
        var path = await WriteAsync("Source.cs", "class A {}");
        var service = CreateService();
        var snapshot = await service.CaptureAsync(TestCancellation);
        snapshot = snapshot with
        {
            DocumentVersions = new Dictionary<string, int>(StringComparer.Ordinal) { [path] = 3 },
        };
        var edit = DocumentChanges(DocumentEdit(path, 2, TextEdit(0, 6, 0, 7, "B")));

        var apply = () => service.ApplyAsync(edit, snapshot, TestCancellation);

        await apply.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task EscapingWorkspaceIsRejectedBeforeAnyWriteAsync()
    {
        var service = CreateService();
        var snapshot = await service.CaptureAsync(TestCancellation);
        var edit = Changes(
            Path.Combine(_root, "..", "outside.cs"),
            TextEdit(0, 0, 0, 0, "content")
        );

        var apply = () => service.ApplyAsync(edit, snapshot, TestCancellation);

        await apply.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task RecursiveDirectoryDeleteIsRejectedAsync()
    {
        var source = await WriteAsync("Source.cs", "class A {}");
        var service = CreateService();
        var snapshot = await service.CaptureAsync(TestCancellation);
        var operation = DeleteFile(_root);
        operation["options"] = new JsonObject { ["recursive"] = true };
        var edit = DocumentChanges(operation);

        var apply = () => service.ApplyAsync(edit, snapshot, TestCancellation);

        await apply.Should().ThrowAsync<InvalidOperationException>();
        File.Exists(source).Should().BeTrue();
    }

    [Fact]
    public async Task PlannedFileCannotAlsoBeAParentDirectoryAsync()
    {
        var source = await WriteAsync("Source.cs", "class A {}");
        var parent = Path.Combine(_root, "Parent.cs");
        var child = Path.Combine(parent, "Child.cs");
        var service = CreateService();
        var snapshot = await service.CaptureAsync(TestCancellation);
        var edit = DocumentChanges(
            DocumentEdit(source, version: null, TextEdit(0, 6, 0, 7, "B")),
            CreateFile(parent),
            CreateFile(child)
        );

        var apply = () => service.ApplyAsync(edit, snapshot, TestCancellation);

        await apply.Should().ThrowAsync<InvalidOperationException>();
        await AssertFileTextAsync(source, "class A {}");
        File.Exists(parent).Should().BeFalse();
    }

    [Fact]
    public async Task SamePositionInsertsPreserveProtocolOrderAsync()
    {
        var source = await WriteAsync("Source.cs", "class A {}");
        var service = CreateService();
        var snapshot = await service.CaptureAsync(TestCancellation);
        var edit = Changes(
            source,
            TextEdit(0, 0, 0, 0, "public "),
            TextEdit(0, 0, 0, 0, "sealed ")
        );

        await service.ApplyAsync(edit, snapshot, TestCancellation);

        await AssertFileTextAsync(source, "public sealed class A {}");
    }

    [Fact]
    public async Task SnapshotIncludesOneThousandSourceFilesAsync()
    {
        for (var index = 0; index < 1_000; index++)
        {
            await WriteAsync(
                string.Create(CultureInfo.InvariantCulture, $"Source{index}.cs"),
                string.Create(CultureInfo.InvariantCulture, $"class C{index} {{}}")
            );
        }

        var service = CreateService();
        var started = Stopwatch.GetTimestamp();
        var snapshot = await service.CaptureAsync(TestCancellation);
        var capture = Stopwatch.GetElapsedTime(started);
        started = Stopwatch.GetTimestamp();
        await service.PreviewAsync(
            Changes(Path.Combine(_root, "Source0.cs"), TextEdit(0, 6, 0, 8, "Changed")),
            snapshot,
            TestCancellation
        );
        var preview = Stopwatch.GetElapsedTime(started);

        snapshot.Fingerprints.Should().HaveCount(1_000);
        TestContext.Current.TestOutputHelper!.WriteLine(
            string.Create(
                CultureInfo.InvariantCulture,
                $"1,000 files: capture {capture.TotalMilliseconds:F1} ms, preview {preview.TotalMilliseconds:F1} ms."
            )
        );
    }

    [Fact]
    public async Task RenameCanChangeOnlyFileNameCaseAsync()
    {
        var source = await WriteAsync("Name.cs", "class Name {}");
        var destination = Path.Combine(_root, "name.cs");
        var service = CreateService();
        var snapshot = await service.CaptureAsync(TestCancellation);
        var edit = DocumentChanges(
            RenameFile(source, destination),
            DocumentEdit(destination, version: null, TextEdit(0, 6, 0, 10, "name"))
        );

        await service.ApplyAsync(edit, snapshot, TestCancellation);

        Directory.EnumerateFiles(_root).Select(Path.GetFileName).Should().Equal("name.cs");
        await AssertFileTextAsync(destination, "class name {}");
    }

    [Fact]
    public async Task RenameOnlyCaseAndBackLeavesOriginalFileAsync()
    {
        var source = await WriteAsync("Name.cs", "class Name {}");
        var destination = Path.Combine(_root, "name.cs");
        var service = CreateService();
        var snapshot = await service.CaptureAsync(TestCancellation);
        var edit = DocumentChanges(
            RenameFile(source, destination),
            RenameFile(destination, source)
        );

        var result = await service.ApplyAsync(edit, snapshot, TestCancellation);

        result["fileCount"]!.GetValue<int>().Should().Be(0);
        Directory.EnumerateFiles(_root).Select(Path.GetFileName).Should().Equal("Name.cs");
    }

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
    }

    private WorkspaceEditService CreateService() => new(new WorkspacePaths(_root));

    private async Task<string> WriteAsync(string name, string text)
    {
        var path = Path.Combine(_root, name);
        await File.WriteAllTextAsync(path, text, TestCancellation);
        return path;
    }

    private static async Task AssertFileTextAsync(string path, string expected)
    {
        var actual = await File.ReadAllTextAsync(path, TestCancellation);

        actual.Should().Be(expected, "file {0} should contain the expected text", path);
    }

    private static JsonObject DocumentChanges(params JsonObject[] operations) =>
        new() { ["documentChanges"] = new JsonArray(operations) };

    private static JsonObject CreateFile(string path) =>
        new()
        {
            ["kind"] = WorkspaceEditService.CreateFileOperation,
            ["uri"] = new Uri(path).AbsoluteUri,
        };

    private static JsonObject RenameFile(string source, string destination) =>
        new()
        {
            ["kind"] = WorkspaceEditService.RenameFileOperation,
            ["oldUri"] = new Uri(source).AbsoluteUri,
            ["newUri"] = new Uri(destination).AbsoluteUri,
        };

    private static JsonObject DeleteFile(string path) =>
        new()
        {
            ["kind"] = WorkspaceEditService.DeleteFileOperation,
            ["uri"] = new Uri(path).AbsoluteUri,
        };

    private static JsonObject Changes(string path, params JsonObject[] edits)
    {
        return new JsonObject
        {
            ["changes"] = new JsonObject { [new Uri(path).AbsoluteUri] = new JsonArray(edits) },
        };
    }

    private static JsonObject DocumentEdit(string path, int? version, params JsonObject[] edits)
    {
        return new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = new Uri(path).AbsoluteUri,
                ["version"] = version,
            },
            ["edits"] = new JsonArray(edits),
        };
    }

    private static JsonObject TextEdit(
        int startLine,
        int startCharacter,
        int endLine,
        int endCharacter,
        string text
    )
    {
        return new JsonObject
        {
            ["range"] = new JsonObject
            {
                ["start"] = new JsonObject { ["line"] = startLine, ["character"] = startCharacter },
                ["end"] = new JsonObject { ["line"] = endLine, ["character"] = endCharacter },
            },
            ["newText"] = text,
        };
    }
}
