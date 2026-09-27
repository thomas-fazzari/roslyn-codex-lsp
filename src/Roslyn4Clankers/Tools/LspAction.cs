// Copyright (C) 2026 thomas-fazzari
// SPDX-License-Identifier: GPL-3.0-only

using System.Text.Json.Serialization;

namespace Roslyn4Clankers.Tools;

[JsonConverter(typeof(JsonStringEnumConverter<LspAction>))]
internal enum LspAction
{
    [JsonStringEnumMemberName("diagnostics")]
    Diagnostics = 0,

    [JsonStringEnumMemberName("definition")]
    Definition = 1,

    [JsonStringEnumMemberName("type_definition")]
    TypeDefinition = 2,

    [JsonStringEnumMemberName("implementation")]
    Implementation = 3,

    [JsonStringEnumMemberName("references")]
    References = 4,

    [JsonStringEnumMemberName("hover")]
    Hover = 5,

    [JsonStringEnumMemberName("symbols")]
    Symbols = 6,

    [JsonStringEnumMemberName("rename")]
    Rename = 7,

    [JsonStringEnumMemberName("rename_file")]
    RenameFile = 8,

    [JsonStringEnumMemberName("code_actions")]
    CodeActions = 9,

    [JsonStringEnumMemberName("status")]
    Status = 10,

    [JsonStringEnumMemberName("reload")]
    Reload = 11,

    [JsonStringEnumMemberName("capabilities")]
    Capabilities = 12,

    [JsonStringEnumMemberName("request")]
    Request = 13,

    [JsonStringEnumMemberName("callers")]
    Callers = 14,

    [JsonStringEnumMemberName("callees")]
    Callees = 15,

    [JsonStringEnumMemberName("supertypes")]
    Supertypes = 16,

    [JsonStringEnumMemberName("subtypes")]
    Subtypes = 17,
}
