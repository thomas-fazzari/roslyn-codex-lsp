// Copyright (C) 2026 thomas-fazzari
// SPDX-License-Identifier: GPL-3.0-only

using System.Text.Json.Nodes;
using RoslynCodexLsp.Tools;

namespace RoslynCodexLsp.Tests.Unit.Tools;

public sealed class LspResultsExtensionsTests
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

        var result = locations.ToCompactLocations(_paths, LspRequest.DefaultResultLimit);

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
        var locations = new JsonArray(
            Location("First.cs", 0, 0),
            Location("First.cs", 1, 0),
            Location("Second.cs", 2, 0)
        );

        var result = locations.ToCompactLocations(_paths, 2);

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

        var result = location.ToCompactLocations(_paths, LspRequest.DefaultResultLimit);

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
        JsonNode? locations = array ? new JsonArray() : null;

        var result = locations.ToCompactLocations(_paths, LspRequest.DefaultResultLimit);

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

        var result = location.ToCompactLocations(_paths, LspRequest.DefaultResultLimit);

        result["items"]![0]!["file"]!.GetValue<string>().Should().Be(value);
    }

    [Fact]
    public void CompactsDiagnosticsToOneBasedStartAndNamedSeverity()
    {
        var diagnostic = new JsonObject
        {
            ["range"] = Range(3, 7),
            ["severity"] = 1,
            ["code"] = "CS0246",
            ["codeDescription"] = new JsonObject { ["href"] = "https://example.com/CS0246" },
            ["message"] = "The type could not be found.",
            ["tags"] = new JsonArray(1),
        };

        var result = diagnostic.ToCompactDiagnostic();

        JsonNode
            .DeepEquals(
                result,
                JsonNode.Parse(
                    """{"line":4,"character":8,"severity":"error","code":"CS0246","message":"The type could not be found."}"""
                )
            )
            .Should()
            .BeTrue(result.ToJsonString());
    }

    [Fact]
    public void CompactsNestedDocumentSymbols()
    {
        var symbols = new JsonArray(
            new JsonObject
            {
                ["name"] = "IGreeter",
                ["detail"] = "IGreeter",
                ["kind"] = 11,
                ["range"] = Range(2, 0),
                ["selectionRange"] = Range(2, 17),
                ["children"] = new JsonArray(
                    new JsonObject
                    {
                        ["name"] = "Greet(string) : string",
                        ["kind"] = 6,
                        ["range"] = Range(4, 4),
                        ["selectionRange"] = Range(4, 11),
                        ["children"] = new JsonArray(),
                    }
                ),
            }
        );

        var result = symbols.ToCompactSymbols(_paths, LspRequest.DefaultResultLimit);

        JsonNode
            .DeepEquals(
                result["items"],
                JsonNode.Parse(
                    """[{"name":"IGreeter","kind":"interface","position":[3,18],"children":[{"name":"Greet(string) : string","kind":"method","position":[5,12]}]}]"""
                )
            )
            .Should()
            .BeTrue(result.ToJsonString());
    }

    [Fact]
    public void CompactsWorkspaceSymbolsWithRelativeFilesAndLimit()
    {
        var symbols = new JsonArray(
            new JsonObject
            {
                ["name"] = "Greeter",
                ["kind"] = 5,
                ["location"] = Location("Application/Greeter.cs", 5, 21),
                ["containerName"] = "Application",
            },
            new JsonObject { ["name"] = "Other", ["kind"] = 5 }
        );

        var result = symbols.ToCompactSymbols(_paths, 1);

        result["total"]!.GetValue<int>().Should().Be(2);
        result["truncated"]!.GetValue<bool>().Should().BeTrue();
        JsonNode
            .DeepEquals(
                result["items"],
                JsonNode.Parse(
                    """[{"name":"Greeter","kind":"class","container":"Application","file":"Application/Greeter.cs","position":[6,22]}]"""
                )
            )
            .Should()
            .BeTrue(result.ToJsonString());
    }

    [Fact]
    public void CompactsIncomingCallsWithCallSites()
    {
        var calls = new JsonArray(
            new JsonObject
            {
                ["from"] = new JsonObject
                {
                    ["name"] = "Consumer.Run(IGreeter)",
                    ["kind"] = 6,
                    ["uri"] = Location("Consumer.cs", 0, 0)["uri"]!.DeepClone(),
                    ["range"] = Range(6, 4),
                    ["selectionRange"] = Range(6, 25),
                },
                ["fromRanges"] = new JsonArray(Range(6, 58), Range(7, 2)),
            }
        );

        var result = calls.ToCompactHierarchy(_paths, LspRequest.DefaultResultLimit);

        var item = result["items"]![0]!;
        item["file"]!.GetValue<string>().Should().Be("Consumer.cs");
        JsonNode.DeepEquals(item["position"], JsonNode.Parse("[7,26]")).Should().BeTrue();
        JsonNode.DeepEquals(item["calls"], JsonNode.Parse("[[7,59],[8,3]]")).Should().BeTrue();
    }

    [Theory]
    [InlineData("""{"kind":"markdown","value":"```csharp\nvoid Greet()\n```\n  \n"}""")]
    [InlineData("""["```csharp\nvoid Greet()\n```"]""")]
    public void ReducesHoverContentsToText(string contents)
    {
        var hover = new JsonObject
        {
            ["contents"] = JsonNode.Parse(contents),
            ["range"] = Range(0, 0),
        };

        var result = hover.ToCompactHover();

        result["text"]!.GetValue<string>().Should().Be("```csharp\nvoid Greet()\n```");
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
