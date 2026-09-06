// Copyright (C) 2026 thomas-fazzari
// SPDX-License-Identifier: GPL-3.0-only

using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using RoslynCodexLsp.Tools;
using StreamJsonRpc.Protocol;

namespace RoslynCodexLsp;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(LspRequest))]
[JsonSerializable(typeof(LspAction))]
[JsonSerializable(typeof(JsonNode))]
[JsonSerializable(typeof(JsonObject))]
[JsonSerializable(typeof(JsonArray))]
[JsonSerializable(typeof(object))]
[JsonSerializable(typeof(CommonErrorData))]
internal partial class BridgeJsonContext : JsonSerializerContext;
