// Copyright (C) 2026 thomas-fazzari
// SPDX-License-Identifier: GPL-3.0-only

using Roslyn4Clankers.Symbols;

namespace Roslyn4Clankers.Tests.Unit.Symbols;

public sealed class SymbolNameTests
{
    [Theory]
    [InlineData("Put(int) : void", new[] { "Put" }, "(int)")]
    [InlineData("Value : T", new[] { "Value" }, null)]
    [InlineData("this[int] : string", new[] { "this" }, "[int]")]
    [InlineData("Map<TResult>(Func<T, TResult>) : TResult", new[] { "Map" }, "(Func<T,TResult>)")]
    [InlineData("Box<T>", new[] { "Box" }, null)]
    [InlineData("Sample.Application", new[] { "Sample", "Application" }, null)]
    [InlineData("Sample.Box<T>.Put( int )", new[] { "Sample", "Box", "Put" }, "(int)")]
    [InlineData("operator <(Money, Money) : bool", new[] { "operator <" }, "(Money,Money)")]
    [InlineData("operator >>(Money, int) : Money", new[] { "operator >>" }, "(Money,int)")]
    [InlineData(
        "Money.operator <=(Money, Money)",
        new[] { "Money", "operator <=" },
        "(Money,Money)"
    )]
    [InlineData(
        "implicit operator decimal(Money) : decimal",
        new[] { "implicit operator decimal" },
        "(Money)"
    )]
    public void ParsesRoslynAndRequestedNames(string text, string[] segments, string? parameters)
    {
        var name = SymbolName.Parse(text);

        name.Segments.Should().Equal(segments);
        name.Parameters.Should().Be(parameters);
    }

    [Theory]
    [InlineData("Put", true)]
    [InlineData("Box.Put", true)]
    [InlineData("Sample.Application.Box.Put", true)]
    [InlineData("Box<T>.Put(int)", true)]
    [InlineData("Box.Put(string)", false)]
    [InlineData("ox.Put", false)]
    [InlineData("Other.Box.Put", false)]
    public void SelectsDeclarationsByTrailingSegmentsAndParameters(string requested, bool selected)
    {
        var declared = SymbolName.Parse("Sample.Application.Box<T>.Put(int)");

        SymbolName.Parse(requested).Selects(declared).Should().Be(selected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Box..Put")]
    [InlineData("Put(int")]
    public void RejectsInvalidNames(string text)
    {
        var parse = () => SymbolName.Parse(text);

        parse.Should().Throw<ArgumentException>();
    }
}
