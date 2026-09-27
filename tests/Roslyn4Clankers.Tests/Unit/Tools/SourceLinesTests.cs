// Copyright (C) 2026 thomas-fazzari
// SPDX-License-Identifier: GPL-3.0-only

using System.Text.Json.Nodes;
using Roslyn4Clankers.Tools;

namespace Roslyn4Clankers.Tests.Unit.Tools;

public sealed class SourceLinesTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("source-lines-");

    public void Dispose() => _root.Delete(recursive: true);

    [Fact]
    public void AddsEachOccurrenceLineOnceAndTrimmed()
    {
        Write("A.cs", "class A\n{\n    void M() => M() + M();\n}\n");
        var result = Result(Item("A.cs", "positions", "[[3,10],[3,17],[1,7]]"));

        SourceLines.Add(result, new WorkspacePaths(_root.FullName), callSiteFile: null);

        JsonNode
            .DeepEquals(
                result["items"]![0]!["lines"],
                JsonNode.Parse("""{"1":"class A","3":"void M() => M() + M();"}""")
            )
            .Should()
            .BeTrue(result.ToJsonString());
    }

    [Fact]
    public void ReadsCallSitesFromTheCallSiteFile()
    {
        Write("Callee.cs", "void Target() { }\n");
        Write("Caller.cs", "void Source()\n{\n    Target();\n}\n");
        var item = Item("Callee.cs", "position", "[1,6]");
        item["calls"] = JsonNode.Parse("[[3,5]]");
        var result = Result(item);

        SourceLines.Add(
            result,
            new WorkspacePaths(_root.FullName),
            Path.Combine(_root.FullName, "Caller.cs")
        );

        JsonNode
            .DeepEquals(item["lines"], JsonNode.Parse("""{"3":"Target();"}"""))
            .Should()
            .BeTrue(result.ToJsonString());
    }

    [Fact]
    public void ShortensLongLines()
    {
        Write("Long.cs", new string('x', SourceLines.MaximumLineCharacters + 10));
        var result = Result(Item("Long.cs", "positions", "[[1,1]]"));

        SourceLines.Add(result, new WorkspacePaths(_root.FullName), callSiteFile: null);

        result["items"]![0]!["lines"]!["1"]!
            .GetValue<string>()
            .Should()
            .HaveLength(SourceLines.MaximumLineCharacters + 1)
            .And.EndWith("…");
    }

    [Fact]
    public void SkipsLocationsOutsideTheWorkspace()
    {
        var item = Item("roslyn-source-generated://assembly/Generated.cs", "positions", "[[1,1]]");
        var result = Result(item);

        SourceLines.Add(result, new WorkspacePaths(_root.FullName), callSiteFile: null);

        item.ContainsKey("lines").Should().BeFalse();
    }

    private void Write(string file, string text) =>
        File.WriteAllText(Path.Combine(_root.FullName, file), text);

    private static JsonObject Item(string file, string key, string positions) =>
        new() { ["file"] = file, [key] = JsonNode.Parse(positions) };

    private static JsonObject Result(JsonObject item) => new() { ["items"] = new JsonArray(item) };
}
