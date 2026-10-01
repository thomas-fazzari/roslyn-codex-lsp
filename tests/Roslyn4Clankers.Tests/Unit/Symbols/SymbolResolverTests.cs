// Copyright (C) 2026 thomas-fazzari
// SPDX-License-Identifier: GPL-3.0-only

using System.Text.Json.Nodes;
using Roslyn4Clankers.Symbols;

namespace Roslyn4Clankers.Tests.Unit.Symbols;

public sealed class SymbolResolverTests
{
    private const string File = "/workspace/Shapes.cs";

    // Document symbols in the shape Roslyn returns them
    private static readonly JsonArray _documentSymbols = new(
        Symbol(
            "Sample.Application",
            3,
            0,
            Symbol(
                "Box<T>",
                5,
                2,
                Symbol("Box(T)", 6, 4),
                Symbol("Value : T", 7, 6),
                Symbol("Put(int) : void", 6, 8),
                Symbol("Put(string) : void", 6, 10),
                Symbol("Inner", 5, 12, Symbol("Run() : void", 6, 14)),
                Symbol("operator <(Box<T>, Box<T>) : bool", 25, 16),
                Symbol("Put(int", 6, 18)
            )
        )
    );

    [Theory]
    [InlineData(
        "Box.Put",
        new[] { "Sample.Application.Box<T>.Put(int)", "Sample.Application.Box<T>.Put(string)" }
    )]
    [InlineData("Put(string)", new[] { "Sample.Application.Box<T>.Put(string)" })]
    [InlineData("Box", new[] { "Sample.Application.Box<T>" })]
    [InlineData("Box(T)", new[] { "Sample.Application.Box<T>.Box(T)" })]
    [InlineData("Box.Inner.Run", new[] { "Sample.Application.Box<T>.Inner.Run()" })]
    [InlineData("operator <", new[] { "Sample.Application.Box<T>.operator <(Box<T>, Box<T>)" })]
    [InlineData("Application", new string[0])]
    public void FindsDeclarationsByQualifiedName(string requested, string[] expected)
    {
        var declarations = SymbolResolver.FindDeclarations(
            _documentSymbols,
            File,
            SymbolName.Parse(requested)
        );

        declarations.Select(declaration => declaration.Name).Should().Equal(expected);
    }

    [Fact]
    public void ReturnsOneBasedPositionOfTheDeclaredName()
    {
        var declaration = SymbolResolver
            .FindDeclarations(_documentSymbols, File, SymbolName.Parse("Box.Value"))
            .Should()
            .ContainSingle()
            .Which;

        declaration.File.Should().Be(File);
        (declaration.Line, declaration.Character).Should().Be((7, 5));
    }

    [Fact]
    public void CandidateNamesResolveBackToTheirDeclaration()
    {
        foreach (
            var candidate in SymbolResolver.FindDeclarations(
                _documentSymbols,
                File,
                SymbolName.Parse("Put")
            )
        )
        {
            SymbolResolver
                .FindDeclarations(_documentSymbols, File, SymbolName.Parse(candidate.Name))
                .Should()
                .ContainSingle()
                .Which.Should()
                .Be(candidate);
        }
    }

    [Theory]
    [InlineData("app", 1)]
    [InlineData("lib", 2)]
    [InlineData(null, 1)]
    public void SameNamedDeclarationsAreOneSymbolOnlyWithinAProject(
        string? secondProject,
        int expected
    )
    {
        var declarations = new[]
        {
            new SymbolDeclaration("Program", 5, "/workspace/App/Program.cs", 1, 1),
            new SymbolDeclaration("Program", 5, "/workspace/Lib/Program.cs", 1, 1),
        };
        var projects = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal)
        {
            ["/workspace/App/Program.cs"] = ["app"],
            ["/workspace/Lib/Program.cs"] = secondProject is null ? [] : [secondProject],
        };

        var symbols = SymbolResolver.DistinctSymbols(declarations, projects);

        symbols.Should().HaveCount(expected);
    }

    private static JsonObject Symbol(string name, int kind, int line, params JsonObject[] children)
    {
        var position = new JsonObject { ["line"] = line, ["character"] = 4 };
        return new JsonObject
        {
            ["name"] = name,
            ["kind"] = kind,
            ["selectionRange"] = new JsonObject
            {
                ["start"] = position,
                ["end"] = position.DeepClone(),
            },
            ["children"] = new JsonArray([.. children]),
        };
    }
}
