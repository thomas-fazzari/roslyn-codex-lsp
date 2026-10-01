// Copyright (C) 2026 thomas-fazzari
// SPDX-License-Identifier: GPL-3.0-only

using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Roslyn4Clankers.Lsp;
using Roslyn4Clankers.Tests.Fakes;

namespace Roslyn4Clankers.Tests.Unit.Lsp;

public sealed class SessionLifecycleTests : IDisposable
{
    private readonly string _root = Directory
        .CreateTempSubdirectory("roslyn-for-clankers-session-")
        .FullName;

    [Fact]
    public async Task CrashDuringARequestReportsStderrAndRestartsOnTheNextCallAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var servers = new List<FakeRoslynServer>();
        var paths = new WorkspacePaths(_root);
        await using var session = new RoslynSession(
            new BridgeOptions { WorkspaceRoot = _root },
            paths,
            (_, _) =>
            {
                var server = new FakeRoslynServer();
                servers.Add(server);
                return server;
            },
            NullLogger<RoslynSession>.Instance
        );
        await session.EnsureStartedAsync(cancellationToken);
        await servers[0].WriteErrorAsync("Unhandled exception: out of memory", cancellationToken);
        servers[0].Handle = (_, _) =>
        {
            servers[0].Crash();
            return null;
        };

        var request = () =>
            session.RequestAsync(
                LspMethods.WorkspaceSymbol,
                new JsonObject { ["query"] = "Greeter" },
                cancellationToken
            );

        var failure = await request.Should().ThrowAsync<IOException>();
        failure.Which.Message.Should().Contain("out of memory");
        session.IsRunning.Should().BeFalse();

        await session.EnsureStartedAsync(cancellationToken);

        servers.Should().HaveCount(2);
        session.IsRunning.Should().BeTrue();
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
