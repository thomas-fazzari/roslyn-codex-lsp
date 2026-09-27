// Copyright (C) 2026 thomas-fazzari
// SPDX-License-Identifier: GPL-3.0-only

using System.ComponentModel;
using System.Text.Json.Serialization;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace RoslynCodexLsp.Tools;

[McpServerToolType]
internal sealed class NavigateTool(LspTool lsp)
{
    internal const string Name = "navigate";

    [McpServerTool(
        Name = Name,
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false
    )]
    [Description(
        "Find the definition, type definition, implementations or references of the C# symbol at a position, or read its hover information. Positions are one-based."
    )]
    public Task<CallToolResult> NavigateAsync(
        [Description("Navigation to perform.")] NavigationAction action,
        [Description(ToolDescriptions.File)] string file,
        [Description(ToolDescriptions.Line)] int line,
        [Description(ToolDescriptions.Character)] int character,
        [Description(ToolDescriptions.Limit)] int limit = LspRequest.DefaultResultLimit,
        CancellationToken cancellationToken = default
    ) =>
        lsp.ExecuteAsync(
            new LspRequest
            {
                Limit = limit,
                Action = action switch
                {
                    NavigationAction.Definition => LspAction.Definition,
                    NavigationAction.TypeDefinition => LspAction.TypeDefinition,
                    NavigationAction.Implementation => LspAction.Implementation,
                    NavigationAction.References => LspAction.References,
                    _ => LspAction.Hover,
                },
                File = file,
                Line = line,
                Character = character,
            },
            cancellationToken
        );
}

[JsonConverter(typeof(JsonStringEnumConverter<NavigationAction>))]
internal enum NavigationAction
{
    [JsonStringEnumMemberName("definition")]
    Definition = 0,

    [JsonStringEnumMemberName("type_definition")]
    TypeDefinition = 1,

    [JsonStringEnumMemberName("implementation")]
    Implementation = 2,

    [JsonStringEnumMemberName("references")]
    References = 3,

    [JsonStringEnumMemberName("hover")]
    Hover = 4,
}
