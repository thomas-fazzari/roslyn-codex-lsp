// Copyright (C) 2026 thomas-fazzari
// SPDX-License-Identifier: GPL-3.0-only

using System.IO.Pipelines;
using RoslynCodexLsp.Lsp;

namespace RoslynCodexLsp.Tests.Unit.Lsp;

public sealed class ProtocolTests
{
    [Theory]
    [InlineData("", 0, 0)]
    [InlineData("a\r\nb\rc\n😀", 3, 2)]
    [InlineData("😀x", 0, 3)]
    [InlineData("a\r\n", 1, 0)]
    public void ReplacementRangeUsesUtf16AndAllLineEndings(string text, int line, int character)
    {
        var position = RoslynSession.EndPosition(text);

        position["line"]!.GetValue<int>().Should().Be(line);
        position["character"]!.GetValue<int>().Should().Be(character);
    }

    [Fact]
    public async Task ReaderRejectsAnOversizedUnconsumedMessageAsync()
    {
        await using var stream = new MemoryStream(new byte[17]);
        var reader = new BoundedPipeReader(PipeReader.Create(stream), maximumBytes: 16);
        try
        {
            var read = async () => await reader.ReadAsync(TestContext.Current.CancellationToken);

            await read.Should().ThrowAsync<InvalidDataException>();
        }
        finally
        {
            await reader.CompleteAsync();
        }
    }

    [Fact]
    public async Task ReaderReleasesConsumedBytesBeforeTheNextMessageAsync()
    {
        var pipe = new Pipe();
        var reader = new BoundedPipeReader(pipe.Reader, maximumBytes: 16);
        try
        {
            for (var index = 0; index < 3; index++)
            {
                await pipe.Writer.WriteAsync(new byte[16], TestContext.Current.CancellationToken);
                var read = await reader.ReadAsync(TestContext.Current.CancellationToken);

                read.Buffer.Length.Should().Be(16);
                reader.AdvanceTo(read.Buffer.End);
            }
        }
        finally
        {
            await pipe.Writer.CompleteAsync();
            await reader.CompleteAsync();
        }
    }
}
