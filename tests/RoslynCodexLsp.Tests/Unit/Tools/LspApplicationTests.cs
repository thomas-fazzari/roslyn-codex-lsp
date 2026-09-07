// Copyright (C) 2026 thomas-fazzari
// SPDX-License-Identifier: GPL-3.0-only

using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynCodexLsp.Editing;
using RoslynCodexLsp.Lsp;
using RoslynCodexLsp.Tools;

namespace RoslynCodexLsp.Tests.Unit.Tools;

public sealed class LspApplicationTests
{
    [Fact]
    public void LargeReadOnlyResultsStillReportTheLimit()
    {
        var response = LspTool.Response(
            LspAction.References,
            JsonValue.Create(new string('x', LspTool.MaximumResponseCharacters))
        );

        response.IsError.Should().BeTrue();
        response
            .StructuredContent!.Value.GetProperty("error")
            .GetProperty("code")
            .GetString()
            .Should()
            .Be(LspTool.ResultTooLargeErrorCode);
    }

    [Fact]
    public void LargeCommandResponsePreservesAppliedFiles()
    {
        var result = ApplicationResult(new JsonObject { ["path"] = "Source.cs" });
        result["commandResult"] = new string('x', LspTool.MaximumResponseCharacters);

        var response = LspTool.Response(LspAction.Request, result, preserveApplication: true);

        response.IsError.Should().NotBeTrue();
        var payload = response.StructuredContent!.Value;
        var application = payload.GetProperty("result");
        application.GetProperty("applied").GetBoolean().Should().BeTrue();
        application.GetProperty("fileCount").GetInt32().Should().Be(1);
        application
            .GetProperty("files")[0]
            .GetProperty("path")
            .GetString()
            .Should()
            .Be("Source.cs");
        application.GetProperty("commandResultTruncated").GetBoolean().Should().BeTrue();
        application.TryGetProperty("commandResult", out _).Should().BeFalse();
        payload.GetRawText().Length.Should().BeLessThanOrEqualTo(LspTool.MaximumResponseCharacters);
    }

    [Fact]
    public void LargePathListsKeepExactPathsAndTotalCounts()
    {
        var path = new string('é', 2_000);
        var files = Enumerable
            .Range(0, 64)
            .Select(index => new JsonObject
            {
                ["path"] = path + index.ToString(CultureInfo.InvariantCulture),
            })
            .ToArray();

        var response = LspTool.Response(
            LspAction.Rename,
            ApplicationResult(files),
            preserveApplication: true
        );

        response.IsError.Should().NotBeTrue();
        var payload = response.StructuredContent!.Value;
        var application = payload.GetProperty("result");
        application.GetProperty("applied").GetBoolean().Should().BeTrue();
        application.GetProperty("fileCount").GetInt32().Should().Be(files.Length);
        application.GetProperty("filesTruncated").GetBoolean().Should().BeTrue();
        var included = application.GetProperty("files");
        included.GetArrayLength().Should().BeInRange(1, files.Length - 1);
        included[0].GetProperty("path").GetString().Should().Be(path + "0");
        payload.GetRawText().Length.Should().BeLessThanOrEqualTo(LspTool.MaximumResponseCharacters);
    }

    [Theory]
    [InlineData(true, LspTool.CancelledErrorCode)]
    [InlineData(false, LspTool.TimeoutErrorCode)]
    public void CancellationKeepsTheCompletedWriteReceipt(bool cancelled, string expectedCode)
    {
        var exception = new EditApplicationException(
            ApplicationResult(new JsonObject { ["path"] = "Source.cs" }),
            EditApplicationPhase.Synchronize,
            failedPath: null,
            failedOperation: null,
            new OperationCanceledException()
        );

        var response = LspTool.ApplicationFailure(LspAction.Rename, exception, cancelled);

        response.IsError.Should().BeTrue();
        var payload = response.StructuredContent!.Value;
        payload.GetProperty("error").GetProperty("code").GetString().Should().Be(expectedCode);
        payload.GetProperty("result").GetProperty("applied").GetBoolean().Should().BeTrue();
        payload.GetProperty("result").GetProperty("synchronized").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task RenameNotificationFailurePreservesTheCompletedWriteAsync()
    {
        var root = Directory.CreateTempSubdirectory("roslyn-application-tests-").FullName;
        try
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            var path = Path.Combine(root, "Source.cs");
            await File.WriteAllTextAsync(path, "class A {}", cancellationToken);
            var paths = new WorkspacePaths(root);
            var edits = new WorkspaceEditService(paths);
            await using var session = new RoslynSession(
                new BridgeOptions { WorkspaceRoot = root },
                paths,
                NullLogger<RoslynSession>.Instance
            );
            var changes = new LspChanges(session, paths, edits, new LspQueries(session, paths));
            var snapshot = await edits.CaptureAsync(cancellationToken);
            var edit = new JsonObject
            {
                ["changes"] = new JsonObject
                {
                    [new Uri(path).AbsoluteUri] = new JsonArray(
                        JsonNode.Parse(
                            """{"range":{"start":{"line":0,"character":6},"end":{"line":0,"character":7}},"newText":"B"}"""
                        )
                    ),
                },
            };
            var pending = new PendingChange(
                LspAction.RenameFile,
                snapshot,
                edit,
                Command: null,
                FileRename: new JsonObject()
            );

            var apply = () => changes.CompleteAsync(pending, apply: true, cancellationToken);
            var failure = await apply.Should().ThrowAsync<EditApplicationException>();

            failure.Which.Phase.Should().Be(EditApplicationPhase.Notify);
            failure.Which.InnerException.Should().BeOfType<InvalidOperationException>();
            failure.Which.Result["applied"]!.GetValue<bool>().Should().BeTrue();
            failure.Which.Result["fileCount"]!.GetValue<int>().Should().Be(1);
            (await File.ReadAllTextAsync(path, cancellationToken)).Should().Be("class B {}");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static JsonObject ApplicationResult(params JsonObject[] files) =>
        new()
        {
            ["applied"] = true,
            ["fileCount"] = files.Length,
            ["files"] = new JsonArray(files),
        };
}
