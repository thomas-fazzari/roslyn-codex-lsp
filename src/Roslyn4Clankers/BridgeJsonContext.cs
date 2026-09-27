// Copyright (C) 2026 thomas-fazzari
// SPDX-License-Identifier: GPL-3.0-only

using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Roslyn4Clankers.Editing;
using Roslyn4Clankers.Tools;
using StreamJsonRpc.Protocol;

namespace Roslyn4Clankers;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(LspAction))]
[JsonSerializable(typeof(NavigationAction))]
[JsonSerializable(typeof(EditAction))]
[JsonSerializable(typeof(ServerAction))]
[JsonSerializable(typeof(DiagnosticSeverity))]
[JsonSerializable(typeof(EditApplicationPhase))]
[JsonSerializable(typeof(JsonNode))]
[JsonSerializable(typeof(JsonObject))]
[JsonSerializable(typeof(JsonArray))]
[JsonSerializable(typeof(object))]
[JsonSerializable(typeof(CommonErrorData))]
internal partial class BridgeJsonContext : JsonSerializerContext;
