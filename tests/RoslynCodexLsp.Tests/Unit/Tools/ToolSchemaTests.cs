// Copyright (C) 2026 thomas-fazzari
// SPDX-License-Identifier: GPL-3.0-only

using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using RoslynCodexLsp.Tools;

namespace RoslynCodexLsp.Tests.Unit.Tools;

public sealed class ToolSchemaTests
{
    private static readonly Dictionary<string, Tool> _tools = RegisteredTools();

    [Theory]
    [InlineData(DiagnosticsTool.Name)]
    [InlineData(NavigateTool.Name)]
    [InlineData(SymbolsTool.Name)]
    public void MarksQueryToolsReadOnly(string name)
    {
        var annotations = _tools[name].Annotations!;

        annotations.ReadOnlyHint.Should().BeTrue();
        annotations.DestructiveHint.Should().BeFalse();
    }

    [Theory]
    [InlineData(EditTool.Name)]
    [InlineData(ServerTool.Name)]
    public void MarksToolsThatCanWriteFilesDestructive(string name)
    {
        var annotations = _tools[name].Annotations!;

        annotations.ReadOnlyHint.Should().BeFalse();
        annotations.DestructiveHint.Should().BeTrue();
    }

    [Fact]
    public void ExposesExactlyTheFiveTools() =>
        _tools
            .Keys.Should()
            .BeEquivalentTo([
                DiagnosticsTool.Name,
                NavigateTool.Name,
                SymbolsTool.Name,
                EditTool.Name,
                ServerTool.Name,
            ]);

    [Theory]
    [InlineData(DiagnosticsTool.Name, new string[0], null)]
    [InlineData(
        NavigateTool.Name,
        new[] { "action" },
        new[]
        {
            "definition",
            "type_definition",
            "implementation",
            "references",
            "hover",
            "callers",
            "callees",
            "supertypes",
            "subtypes",
        }
    )]
    [InlineData(SymbolsTool.Name, new string[0], null)]
    [InlineData(
        EditTool.Name,
        new[] { "action" },
        new[] { "rename", "rename_file", "code_actions" }
    )]
    [InlineData(
        ServerTool.Name,
        new[] { "action" },
        new[] { "status", "capabilities", "reload", "request" }
    )]
    public void DeclaresFlatArgumentsAndOnlyTheToolActions(
        string name,
        string[] required,
        string[]? actions
    )
    {
        var schema = _tools[name].InputSchema;

        Names(schema, "required").Should().BeEquivalentTo(required);
        schema.GetProperty("properties").TryGetProperty("request", out _).Should().BeFalse();
        if (actions is not null)
        {
            Names(schema.GetProperty("properties").GetProperty("action"), "enum")
                .Should()
                .BeEquivalentTo(actions);
        }
    }

    private static string[] Names(JsonElement element, string property) =>
        element.TryGetProperty(property, out var values)
            ? [.. values.EnumerateArray().Select(value => value.GetString()!)]
            : [];

    private static Dictionary<string, Tool> RegisteredTools()
    {
        var services = new ServiceCollection();
        services.AddMcpServer().WithRoslynTools();
        using var provider = services.BuildServiceProvider();
        return provider
            .GetServices<McpServerTool>()
            .Select(tool => tool.ProtocolTool)
            .ToDictionary(tool => tool.Name, StringComparer.Ordinal);
    }
}
