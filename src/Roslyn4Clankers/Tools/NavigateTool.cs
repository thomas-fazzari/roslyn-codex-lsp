// Copyright (C) 2026 thomas-fazzari
// SPDX-License-Identifier: GPL-3.0-only

using System.ComponentModel;
using System.Text.Json.Serialization;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Roslyn4Clankers.Tools;

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
        "Find the definition, type definition, implementations, references, callers, callees, supertypes or subtypes of a C# symbol, or read its hover information. Name the symbol, or give its one-based position."
    )]
    public Task<CallToolResult> NavigateAsync(
        [Description("Navigation to perform.")] NavigationAction action,
        [Description(ToolDescriptions.Symbol)] string? symbol = null,
        [Description(ToolDescriptions.File)] string? file = null,
        [Description(ToolDescriptions.Line)] int? line = null,
        [Description(ToolDescriptions.Character)] int? character = null,
        [Description(
            "Add lines, a map from line number to the trimmed source line of each result. Uses more tokens."
        )]
            bool context = false,
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
                    NavigationAction.Hover => LspAction.Hover,
                    NavigationAction.Callers => LspAction.Callers,
                    NavigationAction.Callees => LspAction.Callees,
                    NavigationAction.Supertypes => LspAction.Supertypes,
                    _ => LspAction.Subtypes,
                },
                Symbol = symbol,
                File = file,
                Line = line,
                Character = character,
                Context = context,
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

    [JsonStringEnumMemberName("callers")]
    Callers = 5,

    [JsonStringEnumMemberName("callees")]
    Callees = 6,

    [JsonStringEnumMemberName("supertypes")]
    Supertypes = 7,

    [JsonStringEnumMemberName("subtypes")]
    Subtypes = 8,
}
