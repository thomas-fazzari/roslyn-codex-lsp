// Copyright (C) 2026 thomas-fazzari
// SPDX-License-Identifier: GPL-3.0-only

using System.Globalization;
using RoslynCodexLsp.Editing;

namespace RoslynCodexLsp.Tests.Unit.Editing;

public sealed class TextPreviewTests
{
    [Fact]
    public void LateChangeOnALongLineAppearsWithItsActualPosition()
    {
        var prefix = new string('a', EditedDocument.MaximumPreviewCharacters + 100);

        var preview = TextPreview.Create(prefix + "OldName", prefix + "NewName");

        preview.Truncated.Should().BeFalse();
        preview.Changes.Should().ContainSingle();
        var before = preview.Changes[0]!["before"]!;
        var after = preview.Changes[0]!["after"]!;
        var text = before["text"]!.GetValue<string>();
        text.Should().EndWith("OldName");
        after["text"]!.GetValue<string>().Should().EndWith("NewName");
        before["startLine"]!.GetValue<int>().Should().Be(1);
        before["startCharacter"]!
            .GetValue<int>()
            .Should()
            .Be(prefix.Length + 1 - text.IndexOf("OldName", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    [InlineData("\r")]
    public void SeparatedChangesHaveSeparateRanges(string newline)
    {
        var lines = Enumerable
            .Range(1, 140)
            .Select(index => string.Create(CultureInfo.InvariantCulture, $"line {index}"))
            .ToArray();
        var before = string.Join(newline, lines);
        lines[69] = "changed seventy";
        lines[109] = "changed one hundred ten";

        var preview = TextPreview.Create(before, string.Join(newline, lines));

        preview.Truncated.Should().BeFalse();
        preview.Changes.Should().HaveCount(2);
        preview.Changes[0]!["before"]!["startLine"]!.GetValue<int>().Should().Be(70);
        preview.Changes[1]!["after"]!["startLine"]!.GetValue<int>().Should().Be(110);
        preview.Changes[0]!["before"]!["text"]!.GetValue<string>().Should().Contain("line 70");
        preview.Changes[1]!["after"]!["text"]!
            .GetValue<string>()
            .Should()
            .Contain("changed one hundred ten");
    }

    [Fact]
    public void InsertionShiftsTheLaterAfterRange()
    {
        var preview = TextPreview.Create("one\nkeep\nold\nend", "inserted\none\nkeep\nnew\nend");

        preview.Truncated.Should().BeFalse();
        preview.Changes.Should().HaveCount(2);
        var before = preview.Changes[1]!["before"]!;
        var after = preview.Changes[1]!["after"]!;
        before["text"]!.GetValue<string>().Should().Be("old\n");
        before["startLine"]!.GetValue<int>().Should().Be(3);
        before["endLine"]!.GetValue<int>().Should().Be(4);
        after["text"]!.GetValue<string>().Should().Be("new\n");
        after["startLine"]!.GetValue<int>().Should().Be(4);
        after["endLine"]!.GetValue<int>().Should().Be(5);
        after["endCharacter"]!.GetValue<int>().Should().Be(1);
    }

    [Fact]
    public void DeletionHasAnEmptyAfterRange()
    {
        var preview = TextPreview.Create("one\nremove\nend\n", "one\nend\n");

        preview.Changes.Should().ContainSingle();
        var change = preview.Changes[0]!;
        change["before"]!["text"]!.GetValue<string>().Should().Be("remove\n");
        change["before"]!["startLine"]!.GetValue<int>().Should().Be(2);
        change["before"]!["endLine"]!.GetValue<int>().Should().Be(3);
        change["after"]!["text"]!.GetValue<string>().Should().BeEmpty();
        change["after"]!["startLine"]!.GetValue<int>().Should().Be(2);
        change["after"]!["endLine"]!.GetValue<int>().Should().Be(2);
    }

    [Fact]
    public void LineEndingChangesRemainVisible()
    {
        var preview = TextPreview.Create("one\r\ntwo", "one\ntwo");

        preview.Changes.Should().ContainSingle();
        preview.Changes[0]!["before"]!["text"]!.GetValue<string>().Should().Contain("\r\n");
        preview.Changes[0]!["after"]!["text"]!.GetValue<string>().Should().NotContain("\r");
    }

    [Fact]
    public void LineEndingsAndContentChangesAreBothIncluded()
    {
        var preview = TextPreview.Create("one\r\ntwo\r\nold", "one\ntwo\nnew");

        preview.Truncated.Should().BeFalse();
        var before = string.Concat(
            preview.Changes.Select(change => change!["before"]!["text"]!.GetValue<string>())
        );
        var after = string.Concat(
            preview.Changes.Select(change => change!["after"]!["text"]!.GetValue<string>())
        );
        before.Should().Contain("one\r\n").And.Contain("old");
        after.Should().Contain("one\n").And.Contain("new").And.NotContain("\r");
    }

    [Fact]
    public void RemainingBudgetDoesNotProduceEmptySurrogateRanges()
    {
        const int length = EditedDocument.MaximumPreviewCharacters - 2;
        var preview = TextPreview.Create(
            new string('a', length) + "\nkeep\n😀old\nend\n😀last",
            new string('b', length) + "\nkeep\n😁new\nend\n😁tail"
        );

        preview.Truncated.Should().BeTrue();
        preview.Changes.Should().ContainSingle();
        preview.Changes[0]!["before"]!["text"]!.GetValue<string>().Should().NotBeEmpty();
    }

    [Fact]
    public void OversizedComparisonReportsAnIncompletePreview()
    {
        var preview = TextPreview.Create(
            new string('a', TextPreview.MaximumDiffCharacters),
            new string('b', TextPreview.MaximumDiffCharacters)
        );

        preview.Truncated.Should().BeTrue();
        preview.Changes.Should().ContainSingle();
        preview.Changes[0]!["truncated"]!.GetValue<bool>().Should().BeTrue();
    }
}
