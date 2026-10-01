// Copyright (C) 2026 thomas-fazzari
// SPDX-License-Identifier: GPL-3.0-only

using Roslyn4Clankers.Lsp;

namespace Roslyn4Clankers.Tests.Unit.Lsp;

public sealed class SnapshotSynchronizationTests
{
    private static readonly Dictionary<string, string> _snapshot = new(StringComparer.Ordinal)
    {
        ["Application/Greeter.cs"] = "a1",
        ["Application/Consumer.cs"] = "b1",
        ["Application/Application.csproj"] = "c1",
    };

    [Fact]
    public void FirstSnapshotOfASessionResendsNothing()
    {
        var synchronized = new Dictionary<string, string>(StringComparer.Ordinal);

        var stale = RoslynSession.StaleSnapshotFiles(_snapshot, synchronized, baselined: false);

        stale.Should().BeEmpty();
    }

    [Fact]
    public void LaterSnapshotsResendChangedAndInvalidatedFilesOnly()
    {
        var synchronized = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Application/Greeter.cs"] = "a0",
            ["Application/Application.csproj"] = "c1",
        };

        var stale = RoslynSession.StaleSnapshotFiles(_snapshot, synchronized, baselined: true);

        stale.Should().BeEquivalentTo(["Application/Greeter.cs", "Application/Consumer.cs"]);
    }
}
