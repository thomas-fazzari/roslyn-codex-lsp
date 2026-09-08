// Copyright (C) 2026 thomas-fazzari
// SPDX-License-Identifier: GPL-3.0-only

using System.Text.Json.Nodes;
using RoslynCodexLsp.Editing;
using RoslynCodexLsp.Tools;

namespace RoslynCodexLsp.Tests.Unit.Tools;

public sealed class CommandEditsTests : IDisposable
{
    private readonly string _root = Directory
        .CreateTempSubdirectory("roslyn-command-edits-")
        .FullName;

    private static CancellationToken TestCancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task CaptureFailureAfterApplyAcknowledgesWrittenFilesAndClosesCallbacksAsync()
    {
        var path = await WriteAsync("Source.cs", "class A {}");
        var service = CreateService();
        var snapshot = await service.CaptureAsync(TestCancellation);
        var captureCalls = 0;
        await using var commandEdits = new CommandEdits(
            service,
            snapshot,
            _ =>
            {
                captureCalls++;
                throw new IOException("capture failed");
            },
            TestCancellation
        );

        var acknowledgement = await commandEdits.ApplyAsync(
            Changes(path, TextEdit(0, 6, 0, 7, "B")),
            TestCancellation
        );

        acknowledgement["applied"]!.GetValue<bool>().Should().BeTrue();
        captureCalls.Should().Be(1);
        commandEdits.Failure.Should().BeOfType<EditApplicationException>();
        ((EditApplicationException)commandEdits.Failure!)
            .Phase.Should()
            .Be(EditApplicationPhase.Synchronize);
        commandEdits.Result["fileCount"]!.GetValue<int>().Should().Be(1);
        commandEdits.Result["files"]![0]!["path"]!.GetValue<string>().Should().Be("Source.cs");
        await AssertFileTextAsync(path, "class B {}");

        var rejected = await commandEdits.ApplyAsync(
            Changes(path, TextEdit(0, 6, 0, 7, "C")),
            TestCancellation
        );

        rejected["applied"]!.GetValue<bool>().Should().BeFalse();
        await AssertFileTextAsync(path, "class B {}");
        commandEdits.Result["operationCount"]!.GetValue<int>().Should().Be(1);
    }

    [Fact]
    public async Task SecondCallbackFailureRetainsTheFirstReceiptAsync()
    {
        var path = await WriteAsync("Source.cs", "class A {}");
        var service = CreateService();
        var snapshot = await service.CaptureAsync(TestCancellation);
        await using var commandEdits = new CommandEdits(
            service,
            snapshot,
            token => service.CaptureAsync(token),
            TestCancellation
        );

        var first = await commandEdits.ApplyAsync(
            Changes(path, TextEdit(0, 6, 0, 7, "B")),
            TestCancellation
        );
        var second = await commandEdits.ApplyAsync(
            Changes(Path.Combine(_root, "Missing.cs"), TextEdit(0, 0, 0, 0, "class Missing {}")),
            TestCancellation
        );

        first["applied"]!.GetValue<bool>().Should().BeTrue();
        second["applied"]!.GetValue<bool>().Should().BeFalse();
        commandEdits.Failure.Should().NotBeNull();
        commandEdits.Result["fileCount"]!.GetValue<int>().Should().Be(1);
        commandEdits.Result["operationCount"]!.GetValue<int>().Should().Be(1);
        await AssertFileTextAsync(path, "class B {}");
        File.Exists(Path.Combine(_root, "Missing.cs")).Should().BeFalse();
    }

    [Fact(Timeout = BridgeOptions.DefaultRequestTimeoutSeconds * 1_000)]
    public async Task ConcurrentCallbacksApplyAndCaptureInOrderAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var firstPath = await WriteAsync("First.cs", "class A {}");
        var secondPath = await WriteAsync("Second.cs", "class B {}");
        var service = CreateService();
        var snapshot = await service.CaptureAsync(TestCancellation);
        var firstCaptureStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var releaseFirstCapture = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var captureCalls = 0;

        async Task<WorkspaceSnapshot> CaptureAsync(CancellationToken token)
        {
            if (Interlocked.Increment(ref captureCalls) == 1)
            {
                firstCaptureStarted.TrySetResult();
                await releaseFirstCapture.Task.WaitAsync(token);
            }

            return await service.CaptureAsync(token);
        }

        await using var commandEdits = new CommandEdits(
            service,
            snapshot,
            CaptureAsync,
            TestCancellation
        );
        var first = commandEdits.ApplyAsync(
            Changes(firstPath, TextEdit(0, 6, 0, 7, "Changed")),
            TestCancellation
        );
        await firstCaptureStarted.Task.WaitAsync(cancellationToken);
        var second = commandEdits.ApplyAsync(
            Changes(secondPath, TextEdit(0, 6, 0, 7, "Changed")),
            TestCancellation
        );

        second.IsCompleted.Should().BeFalse();
        releaseFirstCapture.TrySetResult();

        (await first)["applied"]!.GetValue<bool>().Should().BeTrue();
        (await second)["applied"]!.GetValue<bool>().Should().BeTrue();
        captureCalls.Should().Be(2);
        commandEdits.Result["operationCount"]!.GetValue<int>().Should().Be(2);
        await AssertFileTextAsync(firstPath, "class Changed {}");
        await AssertFileTextAsync(secondPath, "class Changed {}");
    }

    [Fact(Timeout = BridgeOptions.DefaultRequestTimeoutSeconds * 1_000)]
    public async Task StopDuringCapturePreservesTheWriteAndRejectsLaterCallbacksAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var path = await WriteAsync("Source.cs", "class A {}");
        var service = CreateService();
        var snapshot = await service.CaptureAsync(TestCancellation);
        var captureStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        async Task<WorkspaceSnapshot> CaptureAsync(CancellationToken token)
        {
            captureStarted.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return await service.CaptureAsync(token);
        }

        await using var commandEdits = new CommandEdits(
            service,
            snapshot,
            CaptureAsync,
            TestCancellation
        );
        var pending = commandEdits.ApplyAsync(
            Changes(path, TextEdit(0, 6, 0, 7, "B")),
            TestCancellation
        );
        await captureStarted.Task.WaitAsync(cancellationToken);

        await commandEdits.StopAsync();

        pending.IsCompleted.Should().BeTrue();
        (await pending)["applied"]!.GetValue<bool>().Should().BeTrue();
        commandEdits
            .Failure.Should()
            .BeOfType<EditApplicationException>()
            .Which.InnerException.Should()
            .BeAssignableTo<OperationCanceledException>();
        commandEdits.Result["applied"]!.GetValue<bool>().Should().BeTrue();
        commandEdits.Result["fileCount"]!.GetValue<int>().Should().Be(1);
        var rejected = await commandEdits.ApplyAsync(
            Changes(path, TextEdit(0, 6, 0, 7, "C")),
            TestCancellation
        );
        rejected["applied"]!.GetValue<bool>().Should().BeFalse();
        await AssertFileTextAsync(path, "class B {}");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private WorkspaceEditService CreateService() => new(new WorkspacePaths(_root));

    private async Task<string> WriteAsync(string name, string text)
    {
        var path = Path.Combine(_root, name);
        await File.WriteAllTextAsync(path, text, TestCancellation);
        return path;
    }

    private static JsonObject Changes(string path, JsonObject edit) =>
        new()
        {
            ["changes"] = new JsonObject { [new Uri(path).AbsoluteUri] = new JsonArray(edit) },
        };

    private static JsonObject TextEdit(
        int startLine,
        int startCharacter,
        int endLine,
        int endCharacter,
        string newText
    ) =>
        new()
        {
            ["range"] = new JsonObject
            {
                ["start"] = new JsonObject { ["line"] = startLine, ["character"] = startCharacter },
                ["end"] = new JsonObject { ["line"] = endLine, ["character"] = endCharacter },
            },
            ["newText"] = newText,
        };

    private static async Task AssertFileTextAsync(string path, string expected)
    {
        (await File.ReadAllTextAsync(path, TestCancellation)).Should().Be(expected);
    }
}
