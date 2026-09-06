// Copyright (C) 2026 thomas-fazzari
// SPDX-License-Identifier: GPL-3.0-only

using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynCodexLsp.Editing;
using RoslynCodexLsp.Lsp;

namespace RoslynCodexLsp.Tests.Unit.Lsp;

public sealed class ClientCallbacksTests : IDisposable
{
    private readonly string _root = Directory
        .CreateTempSubdirectory("roslyn-callback-tests-")
        .FullName;

    private static CancellationToken TestCancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ApplyEditForwardsTheWorkspaceEditToTheFileEditorAsync()
    {
        var source = await WriteSourceAsync("class Before {}");

        var paths = new WorkspacePaths(_root);
        var editor = new WorkspaceEditService(paths);
        var snapshot = await editor.CaptureAsync(TestCancellation);
        await using var session = CreateSession(paths);
        var edit = RenameClassEdit(paths.ToUri(source));
        session.ApplyWorkspaceEditAsync = (received, token) =>
        {
            received.Should().BeSameAs(edit);
            return editor.ApplyAsync(received, snapshot, token);
        };
        var callbacks = CreateCallbacks(session, paths);

        var response = await callbacks.ApplyEditAsync(
            new JsonObject { ["label"] = "Rename class", ["edit"] = edit },
            TestCancellation
        );

        response["applied"]!.GetValue<bool>().Should().BeTrue();
        await AssertSourceTextAsync(source, "class After {}");
    }

    [Fact]
    public async Task ApplyEditPreservesARejectedChangeAsync()
    {
        var paths = new WorkspacePaths(_root);
        await using var session = CreateSession(paths);
        var rejection = new JsonObject
        {
            ["applied"] = false,
            ["failureReason"] = "Source changed",
        };
        session.ApplyWorkspaceEditAsync = (_, _) => Task.FromResult(rejection);
        var callbacks = CreateCallbacks(session, paths);

        var response = await callbacks.ApplyEditAsync(
            new JsonObject { ["edit"] = new JsonObject() },
            TestCancellation
        );

        response.Should().BeSameAs(rejection);
        response["applied"]!.GetValue<bool>().Should().BeFalse();
    }

    [Fact]
    public async Task ApplyEditPassesCancellationToTheFileEditorAsync()
    {
        var source = await WriteSourceAsync("class Before {}");

        var paths = new WorkspacePaths(_root);
        var editor = new WorkspaceEditService(paths);
        var snapshot = await editor.CaptureAsync(TestCancellation);
        await using var session = CreateSession(paths);
        session.ApplyWorkspaceEditAsync = (edit, token) => editor.ApplyAsync(edit, snapshot, token);
        var callbacks = CreateCallbacks(session, paths);
        var cancellationToken = new CancellationToken(canceled: true);

        var apply = () =>
            callbacks.ApplyEditAsync(
                new JsonObject { ["edit"] = RenameClassEdit(paths.ToUri(source)) },
                cancellationToken
            );

        await apply.Should().ThrowAsync<OperationCanceledException>();
        await AssertSourceTextAsync(source, "class Before {}");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private async Task<string> WriteSourceAsync(string text)
    {
        var path = Path.Combine(_root, "Source.cs");
        await File.WriteAllTextAsync(path, text, TestCancellation);
        return path;
    }

    private static async Task AssertSourceTextAsync(string path, string expected)
    {
        var actual = await File.ReadAllTextAsync(path, TestCancellation);

        actual.Should().Be(expected);
    }

    private static RoslynSession CreateSession(WorkspacePaths paths) =>
        new(
            new BridgeOptions { WorkspaceRoot = paths.Root },
            paths,
            NullLogger<RoslynSession>.Instance
        );

    private static ClientCallbacks CreateCallbacks(RoslynSession session, WorkspacePaths paths) =>
        new(session, paths, NullLogger<RoslynSession>.Instance, new TaskCompletionSource());

    private static JsonObject RenameClassEdit(string uri) =>
        new()
        {
            ["changes"] = new JsonObject
            {
                [uri] = new JsonArray(
                    new JsonObject
                    {
                        ["range"] = new JsonObject
                        {
                            ["start"] = new JsonObject { ["line"] = 0, ["character"] = 6 },
                            ["end"] = new JsonObject { ["line"] = 0, ["character"] = 12 },
                        },
                        ["newText"] = "After",
                    }
                ),
            },
        };
}
