// Copyright (C) 2026 thomas-fazzari
// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers;
using System.Text.Json;
using RoslynCodexLsp.Lsp;
using StreamJsonRpc;
using StreamJsonRpc.Protocol;

namespace RoslynCodexLsp.Tests.Unit.Lsp;

public sealed class JsonFormatterTests
{
    [Theory]
    [InlineData("42")]
    [InlineData("\"pending-request\"")]
    [InlineData("null")]
    public void PreservesCancellationRequestIdentifiers(string json)
    {
        using var formatter = RoslynSession.CreateFormatter();
        var options = formatter.JsonSerializerOptions;

        var requestId = JsonSerializer.Deserialize<RequestId>(json, options);
        var serialized = JsonSerializer.Serialize(requestId, options);

        serialized.Should().Be(json);
    }

    [Fact]
    public void SerializesTheIdentifierInsideACancellationNotification()
    {
        using var formatter = RoslynSession.CreateFormatter();
        var requestId = new RequestId("pending-request");
        var notification = new JsonRpcRequest
        {
            Method = "$/cancelRequest",
            NamedArguments = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["id"] = requestId,
            },
        };
        var buffer = new ArrayBufferWriter<byte>();

        formatter.Serialize(buffer, notification);

        using var json = JsonDocument.Parse(buffer.WrittenMemory);
        var identifier = json.RootElement.GetProperty("params").GetProperty("id");
        identifier.ValueKind.Should().Be(JsonValueKind.String);
        identifier.Deserialize<RequestId>(formatter.JsonSerializerOptions).Should().Be(requestId);
    }

    [Fact]
    public void PreservesCallbackErrorDetailsAndInnerErrors()
    {
        using var formatter = RoslynSession.CreateFormatter();
        var options = formatter.JsonSerializerOptions;
        var exception = new InvalidOperationException(
            "Callback failed",
            new IOException("Source could not be read")
        );
        var error = new CommonErrorData(exception);

        var json = JsonSerializer.SerializeToElement(error, options);
        var deserialized = json.Deserialize<CommonErrorData>(options);

        deserialized.Should().BeEquivalentTo(error);
        json.GetProperty("type").GetString().Should().Be(exception.GetType().FullName);
        json.GetProperty("inner")
            .GetProperty("code")
            .GetInt32()
            .Should()
            .Be(exception.InnerException!.HResult);
    }
}
