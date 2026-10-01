// Copyright (C) 2026 thomas-fazzari
// SPDX-License-Identifier: GPL-3.0-only

using Microsoft.Extensions.Logging.Abstractions;
using Roslyn4Clankers.Lsp;
using Roslyn4Clankers.Tools;

namespace Roslyn4Clankers.Tests.Unit.Lsp;

public sealed class SessionStartupTests : IDisposable
{
    private readonly string _root = Directory
        .CreateTempSubdirectory("roslyn-for-clankers-startup-")
        .FullName;

    [Fact]
    public async Task ReportsStderrWhenTheServerExitsDuringStartupAsync()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "The fake server is a shell script.");
        var cancellationToken = TestContext.Current.CancellationToken;
        var server = Path.Combine(_root, "server.sh");
        await File.WriteAllTextAsync(
            server,
            "#!/bin/sh\necho 'You must install .NET to run this application.' >&2\nexit 1\n",
            cancellationToken
        );
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(server, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        }
        var paths = new WorkspacePaths(_root);
        await using var session = new RoslynSession(
            new BridgeOptions { WorkspaceRoot = _root, ServerPath = server },
            paths,
            RoslynProcess.Start,
            NullLogger<RoslynSession>.Instance
        );

        var start = () => session.EnsureStartedAsync(cancellationToken);

        (await start.Should().ThrowAsync<IOException>())
            .Which.Message.Should()
            .Contain("install .NET");
    }

    [Fact]
    public async Task ReportsAMissingServerExecutableAsync()
    {
        var paths = new WorkspacePaths(_root);
        await using var session = new RoslynSession(
            new BridgeOptions
            {
                WorkspaceRoot = _root,
                ServerPath = Path.Combine(_root, "missing-server"),
            },
            paths,
            RoslynProcess.Start,
            NullLogger<RoslynSession>.Instance
        );

        var start = () => session.EnsureStartedAsync(TestContext.Current.CancellationToken);

        var exception = (await start.Should().ThrowAsync<Exception>()).Which;
        LspTool.FailureCode(exception).Should().Be(LspTool.IoErrorCode);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
