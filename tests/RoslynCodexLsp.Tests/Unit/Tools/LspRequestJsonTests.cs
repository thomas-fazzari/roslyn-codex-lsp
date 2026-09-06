// Copyright (C) 2026 thomas-fazzari
// SPDX-License-Identifier: GPL-3.0-only

using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using RoslynCodexLsp.Tools;

namespace RoslynCodexLsp.Tests.Unit.Tools;

public sealed class LspRequestJsonTests
{
    [Fact]
    public void UsesDefaultLimitWhenThePropertyIsOmitted()
    {
        var json = RequestJson(LspAction.Diagnostics);

        var request = json.Deserialize(BridgeJsonContext.Default.LspRequest);

        request.Should().NotBeNull();
        request.Limit.Should().Be(LspRequest.DefaultResultLimit);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    [InlineData(LspRequest.MaximumResultLimit)]
    public void PreservesAnExplicitLimit(int limit)
    {
        var json = RequestJson(LspAction.Diagnostics);
        json["limit"] = limit;

        var request = json.Deserialize(BridgeJsonContext.Default.LspRequest);

        request.Should().NotBeNull();
        request.Limit.Should().Be(limit);
    }

    [Theory]
    [InlineData(nameof(LspAction.TypeDefinition))]
    [InlineData(nameof(LspAction.RenameFile))]
    [InlineData(nameof(LspAction.CodeActions))]
    public void UsesTheDeclaredActionNameInBothDirections(string memberName)
    {
        var action = Enum.Parse<LspAction>(memberName);
        var wireName = typeof(LspAction)
            .GetField(memberName)!
            .GetCustomAttribute<JsonStringEnumMemberNameAttribute>()!
            .Name;
        var json = new JsonObject { ["action"] = wireName };

        var request = json.Deserialize(BridgeJsonContext.Default.LspRequest);
        var serialized = JsonSerializer.SerializeToNode(
            request,
            BridgeJsonContext.Default.LspRequest
        );

        request.Should().NotBeNull();
        request.Action.Should().Be(action);
        serialized!["action"]!.GetValue<string>().Should().Be(wireName);
    }

    [Fact]
    public void RejectsARequestWithoutAnAction()
    {
        var deserialize = () => new JsonObject().Deserialize(BridgeJsonContext.Default.LspRequest);

        deserialize.Should().Throw<JsonException>();
    }

    private static JsonObject RequestJson(LspAction action) =>
        new()
        {
            ["action"] = JsonSerializer.SerializeToNode(
                action,
                BridgeJsonContext.Default.LspAction
            ),
        };
}
