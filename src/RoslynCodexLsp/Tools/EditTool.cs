// Copyright (C) 2026 thomas-fazzari
// SPDX-License-Identifier: GPL-3.0-only

using System.ComponentModel;
using System.Text.Json.Serialization;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace RoslynCodexLsp.Tools;

[McpServerToolType]
internal sealed class EditTool(LspTool lsp)
{
    internal const string Name = "edit";

    [McpServerTool(
        Name = Name,
        ReadOnly = false,
        Destructive = true,
        Idempotent = false,
        OpenWorld = false
    )]
    [Description(
        "Preview and apply C# renames, file renames, fixes and refactorings. Calls without apply=true return a preview and a proposalId. Apply that proposal with the same action, its proposalId and apply=true. Positions are one-based."
    )]
    public Task<CallToolResult> EditAsync(
        [Description("Edit to preview or apply.")] EditAction action,
        [Description(ToolDescriptions.File)] string? file = null,
        [Description(ToolDescriptions.Line)] int? line = null,
        [Description(ToolDescriptions.Character)] int? character = null,
        [Description("Optional one-based end line for a code action selection.")]
            int? endLine = null,
        [Description("Optional one-based UTF-16 end character for a code action selection.")]
            int? endCharacter = null,
        [Description("New symbol name, or the workspace-relative destination for rename_file.")]
            string? newName = null,
        [Description("Zero-based index of a code action from a fresh listing.")]
            int? actionIndex = null,
        [Description("Proposal identifier returned by a previous preview or listing.")]
            string? proposalId = null,
        [Description("Apply the proposal. Without it, the call only returns a preview.")]
            bool apply = false,
        [Description(ToolDescriptions.Limit)] int limit = LspRequest.DefaultResultLimit,
        CancellationToken cancellationToken = default
    ) =>
        lsp.ExecuteAsync(
            new LspRequest
            {
                Limit = limit,
                Action = action switch
                {
                    EditAction.Rename => LspAction.Rename,
                    EditAction.RenameFile => LspAction.RenameFile,
                    _ => LspAction.CodeActions,
                },
                File = file,
                Line = line,
                Character = character,
                EndLine = endLine,
                EndCharacter = endCharacter,
                NewName = newName,
                ActionIndex = actionIndex,
                ProposalId = proposalId,
                Apply = apply,
            },
            cancellationToken
        );
}

[JsonConverter(typeof(JsonStringEnumConverter<EditAction>))]
internal enum EditAction
{
    [JsonStringEnumMemberName("rename")]
    Rename = 0,

    [JsonStringEnumMemberName("rename_file")]
    RenameFile = 1,

    [JsonStringEnumMemberName("code_actions")]
    CodeActions = 2,
}
