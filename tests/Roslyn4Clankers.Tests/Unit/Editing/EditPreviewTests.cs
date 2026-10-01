// Copyright (C) 2026 thomas-fazzari
// SPDX-License-Identifier: GPL-3.0-only

using System.Text.Json.Nodes;
using Roslyn4Clankers.Editing;
using Roslyn4Clankers.Tools;

namespace Roslyn4Clankers.Tests.Unit.Editing;

public sealed class EditPreviewTests
{
    [Fact]
    public void RepeatedLimitingPreservesEarlierFileTruncation()
    {
        var preview = new JsonObject
        {
            ["fileCount"] = 2,
            ["files"] = new JsonArray(new JsonObject { ["path"] = "Source.cs" }),
            ["filesTruncated"] = true,
            ["command"] = new string('x', LspTool.PreviewBudgetCharacters),
        };

        var limited = EditPreview.Limit(preview, LspTool.PreviewBudgetCharacters);

        limited["fileCount"]!.GetValue<int>().Should().Be(2);
        limited["filesTruncated"]!.GetValue<bool>().Should().BeTrue();
        limited["files"]!.AsArray().Should().ContainSingle();
    }

    [Fact]
    public void OversizedCommandKeepsTheProposalAndFilePreview()
    {
        var preview = new JsonObject
        {
            ["proposalId"] = "proposal",
            ["applied"] = false,
            ["fileCount"] = 1,
            ["files"] = new JsonArray(new JsonObject { ["path"] = "Source.cs" }),
            ["command"] = new string('x', LspTool.PreviewBudgetCharacters),
        };

        var limited = EditPreview.Limit(preview, LspTool.PreviewBudgetCharacters);

        limited["proposalId"]!.GetValue<string>().Should().Be("proposal");
        limited["files"]!.AsArray().Should().ContainSingle();
        limited["filesTruncated"]!.GetValue<bool>().Should().BeFalse();
        limited["commandPreviewTruncated"]!.GetValue<bool>().Should().BeTrue();
        limited
            .ToJsonString(BridgeJsonContext.Default.Options)
            .Length.Should()
            .BeLessThanOrEqualTo(LspTool.PreviewBudgetCharacters);
    }
}
