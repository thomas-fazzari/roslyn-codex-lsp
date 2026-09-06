// Copyright (C) 2026 thomas-fazzari
// SPDX-License-Identifier: GPL-3.0-only

using System.Text;
using RoslynCodexLsp.Editing;

namespace RoslynCodexLsp.Tests.Unit.Editing;

public sealed class EditedDocumentTests
{
    [Theory]
    [InlineData("utf-8")]
    [InlineData("utf-16")]
    [InlineData("utf-16BE")]
    [InlineData("utf-32")]
    [InlineData("utf-32BE")]
    public void PreviewPreservesEncodingAndDoesNotSplitSurrogatePairs(string encodingName)
    {
        var encoding = Encoding.GetEncoding(encodingName);
        var prefix = new string('a', EditedDocument.MaximumPreviewCharacters - 1);
        byte[] bytes = [.. encoding.GetPreamble(), .. encoding.GetBytes(prefix + "😀tail")];
        var document = new EditedDocument("Source.cs", bytes);

        var result = document.Describe(".");

        result["before"]!.GetValue<string>().Should().Be(prefix);
        result["after"]!.GetValue<string>().Should().Be(prefix);
        result["previewTruncated"]!.GetValue<bool>().Should().BeTrue();
    }

    [Fact]
    public void PreviewValidatesBytesBeyondTheVisiblePrefix()
    {
        byte[] bytes =
        [
            .. Encoding.UTF8.GetBytes(new string('a', EditedDocument.MaximumPreviewCharacters + 1)),
            0xFF,
        ];
        var document = new EditedDocument("Source.cs", bytes);

        var describe = () => document.Describe(".");

        describe.Should().Throw<DecoderFallbackException>();
    }
}
