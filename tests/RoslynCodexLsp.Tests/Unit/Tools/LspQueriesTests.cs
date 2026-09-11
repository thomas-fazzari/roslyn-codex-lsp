// Copyright (C) 2026 thomas-fazzari
// SPDX-License-Identifier: GPL-3.0-only

using System.Text.Json.Nodes;
using RoslynCodexLsp.Tools;

namespace RoslynCodexLsp.Tests.Unit.Tools;

public sealed class LspQueriesTests
{
    private static readonly WorkspacePaths _paths = new(
        Path.Combine(Path.GetTempPath(), "queries")
    );

    [Fact]
    public void GroupsLocationsAndConvertsPositionsToOneBasedCoordinates()
    {
        var locations = new JsonArray(
            Location("First.cs", 0, 4),
            Location("Second.cs", 10, 2),
            Location("First.cs", 20, 8)
        );

        var result = LspQueries.CompactLocations(locations, _paths, LspRequest.DefaultResultLimit);

        result["total"]!.GetValue<int>().Should().Be(3);
        result["truncated"]!.GetValue<bool>().Should().BeFalse();
        var items = result["items"]!.AsArray();
        items.Should().HaveCount(2);
        items[0]!["file"]!.GetValue<string>().Should().Be("First.cs");
        JsonNode
            .DeepEquals(items[0]!["positions"], JsonNode.Parse("[[1,5],[21,9]]"))
            .Should()
            .BeTrue();
        items[1]!["file"]!.GetValue<string>().Should().Be("Second.cs");
        JsonNode.DeepEquals(items[1]!["positions"], JsonNode.Parse("[[11,3]]")).Should().BeTrue();
        locations.Should().HaveCount(3);
        locations[0]!["range"].Should().NotBeNull();
    }

    [Fact]
    public void LimitsOccurrencesBeforeGroupingAndPreservesTotal()
    {
        var result = LspQueries.CompactLocations(
            new JsonArray(
                Location("First.cs", 0, 0),
                Location("First.cs", 1, 0),
                Location("Second.cs", 2, 0)
            ),
            _paths,
            2
        );

        result["total"]!.GetValue<int>().Should().Be(3);
        result["truncated"]!.GetValue<bool>().Should().BeTrue();
        result["items"]!.AsArray().Should().ContainSingle();
        result["items"]![0]!["positions"]!.AsArray().Should().HaveCount(2);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AcceptsSingleLocationsAndLocationLinks(bool link)
    {
        var location = Location("Source.cs", 4, 8);
        if (link)
        {
            location = new JsonObject
            {
                ["targetUri"] = location["uri"]!.DeepClone(),
                ["targetRange"] = Range(0, 0),
                ["targetSelectionRange"] = location["range"]!.DeepClone(),
                ["originSelectionRange"] = Range(1, 1),
            };
        }

        var result = LspQueries.CompactLocations(location, _paths, LspRequest.DefaultResultLimit);

        result["total"]!.GetValue<int>().Should().Be(1);
        result["truncated"]!.GetValue<bool>().Should().BeFalse();
        JsonNode
            .DeepEquals(result["items"]![0]!["positions"], JsonNode.Parse("[[5,9]]"))
            .Should()
            .BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EmptyResultsHaveTheSameEnvelope(bool array)
    {
        var result = LspQueries.CompactLocations(
            array ? new JsonArray() : null,
            _paths,
            LspRequest.DefaultResultLimit
        );

        result["items"]!.AsArray().Should().BeEmpty();
        result["total"]!.GetValue<int>().Should().Be(0);
        result["truncated"]!.GetValue<bool>().Should().BeFalse();
    }

    [Theory]
    [InlineData("roslyn-source-generated://assembly/Source.cs")]
    [InlineData("file:///outside/Source.cs")]
    public void PreservesUrisOutsideTheWorkspace(string value)
    {
        var location = new JsonObject { ["uri"] = value, ["range"] = Range(0, 0) };

        var result = LspQueries.CompactLocations(location, _paths, LspRequest.DefaultResultLimit);

        result["items"]![0]!["file"]!.GetValue<string>().Should().Be(value);
    }

    private static JsonObject Location(string file, int line, int character) =>
        new()
        {
            ["uri"] = new Uri(Path.Combine(_paths.Root, file)).AbsoluteUri,
            ["range"] = Range(line, character),
        };

    private static JsonObject Range(int line, int character) =>
        new()
        {
            ["start"] = new JsonObject { ["line"] = line, ["character"] = character },
            ["end"] = new JsonObject { ["line"] = line, ["character"] = character + 1 },
        };
}
