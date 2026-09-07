// Copyright (C) 2026 thomas-fazzari
// SPDX-License-Identifier: GPL-3.0-only

using System.Text.Json.Serialization;

namespace RoslynCodexLsp.Editing;

[JsonConverter(typeof(JsonStringEnumConverter<EditApplicationPhase>))]
internal enum EditApplicationPhase
{
    [JsonStringEnumMemberName("write")]
    Write = 0,

    [JsonStringEnumMemberName("cleanup")]
    Cleanup = 1,

    [JsonStringEnumMemberName("notify")]
    Notify = 2,

    [JsonStringEnumMemberName("command")]
    Command = 3,

    [JsonStringEnumMemberName("synchronize")]
    Synchronize = 4,
}
