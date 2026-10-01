// Copyright (C) 2026 thomas-fazzari
// SPDX-License-Identifier: GPL-3.0-only

using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using Roslyn4Clankers.Lsp;
using Roslyn4Clankers.Symbols;
using Roslyn4Clankers.Tools;

namespace Roslyn4Clankers.Tests.Integration.Roslyn;

/// <summary>
/// Tests the MCP bridge against the real Roslyn language server in temporary workspaces.
/// These tests are explicit and are excluded from mise run test and mise run check.
/// </summary>
/// <remarks>
/// Requires roslyn-language-server on PATH, or its path in ROSLYN4CLANKERS_TEST_SERVER.
/// Copy .env.copyme to .env to set it locally. From the repository root, run:
/// <code>
/// mise run test-integration
/// </code>
/// The command builds the project. The tests restore their fixtures, launch Roslyn,
/// and delete their temporary workspaces when finished.
/// To test a published native bridge, set its absolute executable path:
/// <code>
/// ROSLYN4CLANKERS_TEST_EXECUTABLE=/absolute/path/Roslyn4Clankers mise run test-integration
/// </code>
/// </remarks>
public sealed class RoslynMcpTests
{
    private const int TestTimeoutMilliseconds = 300_000;
    private const string ContractFile = "Contracts/IGreeter.cs";
    private const string ImplementationFile = "Application/Greeter.cs";
    private const string ConsumerFile = "Application/Consumer.cs";
    private const string OtherGreeterFile = "Application/OtherGreeter.cs";
    private const string ProblemsFile = "Application/Problems.cs";

    [Fact(Explicit = true, Timeout = TestTimeoutMilliseconds)]
    public async Task ReportsFreshDiagnosticsAndNavigatesAcrossProjectsAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var workspace = await RoslynTestWorkspace.CreateAsync(cancellationToken);
        var tools = await workspace.Client.ListToolsAsync(cancellationToken: cancellationToken);
        tools
            .Select(tool => tool.Name)
            .Should()
            .BeEquivalentTo([
                DiagnosticsTool.Name,
                NavigateTool.Name,
                SymbolsTool.Name,
                EditTool.Name,
                ServerTool.Name,
            ]);

        var capabilities = await workspace.CallAsync(
            new LspRequest { Action = LspAction.Capabilities }
        );
        capabilities["result"]!["definitionProvider"]!.GetValue<bool>().Should().BeTrue();
        capabilities["result"]!["typeDefinitionProvider"]!.GetValue<bool>().Should().BeTrue();

        var diagnostics = await DiagnosticsAsync(workspace);
        diagnostics
            .Should()
            .Contain(item =>
                Code(item) == "CS0246" && item["severity"]!.GetValue<string>() == "error"
            );
        diagnostics
            .Should()
            .Contain(item =>
                Code(item) == "CS8603" && item["severity"]!.GetValue<string>() == "warning"
            );

        var definitionRequest = await workspace.AtAsync(
            LspAction.Definition,
            ConsumerFile,
            "Greet("
        );
        var definition = await workspace.CallAsync(definitionRequest);
        AssertSingleLocation(definition, ContractFile);
        var target = await workspace.AtAsync(LspAction.Definition, ContractFile, "Greet(");
        var position = definition["result"]!["items"]![0]!["positions"]![0]!;
        position[0]!.GetValue<int>().Should().Be(target.Line);
        position[1]!.GetValue<int>().Should().Be(target.Character);
        var raw = await workspace.CallAsync(
            new LspRequest
            {
                Action = LspAction.Request,
                Method = LspMethods.TextDocumentDefinition,
                Parameters = new JsonObject
                {
                    ["textDocument"] = new JsonObject
                    {
                        ["uri"] = new Uri(workspace.FilePath(ConsumerFile)).AbsoluteUri,
                    },
                    ["position"] = new JsonObject
                    {
                        ["line"] = definitionRequest.Line - 1,
                        ["character"] = definitionRequest.Character - 1,
                    },
                },
            }
        );
        var rawLocation = raw["result"]!.AsArray().Should().ContainSingle().Which!;
        (rawLocation["uri"] ?? rawLocation["targetUri"])!
            .GetValue<string>()
            .Should()
            .Be(new Uri(workspace.FilePath(ContractFile)).AbsoluteUri);
        var rawStart = (rawLocation["range"] ?? rawLocation["targetSelectionRange"])!["start"]!;
        rawStart["line"]!.GetValue<int>().Should().Be(target.Line - 1);
        rawStart["character"]!.GetValue<int>().Should().Be(target.Character - 1);

        var typeDefinition = await workspace.CallAsync(
            await workspace.AtAsync(LspAction.TypeDefinition, ConsumerFile, "greeter.Greet")
        );
        AssertSingleLocation(typeDefinition, ContractFile);

        var implementation = await workspace.CallAsync(
            await workspace.AtAsync(LspAction.Implementation, ContractFile, "Greet(")
        );
        AssertSingleLocation(implementation, ImplementationFile);

        var references = await workspace.CallAsync(
            await workspace.AtAsync(LspAction.References, ContractFile, "Greet(")
        );
        Locations(references).Should().Contain(ConsumerFile);
        Locations(references).Should().NotContain(OtherGreeterFile);

        var hover = await workspace.CallAsync(
            await workspace.AtAsync(LspAction.Hover, ConsumerFile, "Greet(")
        );
        hover["result"]!["text"]!.GetValue<string>().Should().Contain("Greet");

        var callers = await workspace.CallAsync(
            await workspace.AtAsync(LspAction.Callers, ContractFile, "Greet(")
        );
        var caller = callers["result"]!["items"]!.AsArray().Should().ContainSingle().Which!;
        caller["file"]!.GetValue<string>().Should().Be(ConsumerFile);
        caller["calls"]!.AsArray().Should().ContainSingle();

        // A returned symbol name resolves back to its declaration
        var callerSymbol = caller["symbol"]!.GetValue<string>();
        AssertSingleLocation(
            await workspace.CallAsync(
                new LspRequest { Action = LspAction.Definition, Symbol = callerSymbol }
            ),
            ConsumerFile
        );

        var withContext = await workspace.CallAsync(
            new LspRequest
            {
                Action = LspAction.References,
                Symbol = "IGreeter.Greet",
                Context = true,
            }
        );
        withContext["result"]!["items"]!
            .AsArray()
            .Single(item =>
                string.Equals(
                    item!["file"]!.GetValue<string>(),
                    ConsumerFile,
                    StringComparison.Ordinal
                )
            )!["lines"]!
            .AsObject()
            .Select(line => line.Value!.GetValue<string>())
            .Should()
            .ContainSingle(line => line.Contains("greeter.Greet(", StringComparison.Ordinal));

        var subtypes = await workspace.CallAsync(
            await workspace.AtAsync(LspAction.Subtypes, ContractFile, "IGreeter")
        );
        subtypes["result"]!["items"]!
            .AsArray()
            .Select(item => item!["file"]!.GetValue<string>())
            .Should()
            .Equal(ImplementationFile);

        var symbols = await workspace.CallAsync(
            new LspRequest { Action = LspAction.Symbols, Query = "IGreeter" }
        );
        symbols["result"]!["items"]!
            .AsArray()
            .Should()
            .Contain(symbol => symbol!["name"]!.GetValue<string>() == "IGreeter");

        var documentSymbols = await workspace.CallAsync(
            new LspRequest { Action = LspAction.Symbols, File = ContractFile }
        );
        var symbolNames = DocumentSymbolNames(documentSymbols["result"]!["items"]!.AsArray())
            .ToArray();
        symbolNames.Should().Contain("IGreeter");
        symbolNames.Should().Contain(name => name.StartsWith("Greet(", StringComparison.Ordinal));

        var text = await workspace.ReadFileAsync(ProblemsFile);
        await workspace.WriteFileAsync(ProblemsFile, "using System.Collections.Generic;\n" + text);

        var refreshed = await DiagnosticsAsync(workspace);
        refreshed.Should().NotContain(item => Code(item) == "CS0246");
        refreshed.Should().Contain(item => Code(item) == "CS8603");
    }

    [Fact(Explicit = true, Timeout = TestTimeoutMilliseconds)]
    public async Task DiagnosticsOmitCleanFilesAndKeepCountsWhenTruncatedAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var workspace = await RoslynTestWorkspace.CreateAsync(cancellationToken);

        const string cleanFile = "Contracts/Clean.cs";
        await workspace.WriteFileAsync(cleanFile, string.Empty);
        var clean = await workspace.CallAsync(
            new LspRequest { Action = LspAction.Diagnostics, File = cleanFile }
        );
        var cleanResult = clean["result"]!;
        cleanResult["filesChecked"]!.GetValue<int>().Should().Be(1);
        cleanResult["filesWithDiagnostics"]!.GetValue<int>().Should().Be(0);
        cleanResult["diagnostics"]!.AsArray().Should().BeEmpty();
        cleanResult["total"]!.GetValue<int>().Should().Be(0);
        cleanResult["complete"]!.GetValue<bool>().Should().BeTrue();
        cleanResult["truncated"]!.GetValue<bool>().Should().BeFalse();

        var request = new LspRequest
        {
            Action = LspAction.Diagnostics,
            File = "Application/*.cs",
            Limit = LspRequest.MaximumResultLimit,
        };
        var full = (await workspace.CallAsync(request))["result"]!;
        var limited = (await workspace.CallAsync(request with { Limit = 1 }))["result"]!;

        full["filesChecked"]!.GetValue<int>().Should().Be(4);
        full["truncated"]!.GetValue<bool>().Should().BeFalse();
        full["filesWithDiagnostics"]!
            .GetValue<int>()
            .Should()
            .Be(full["diagnostics"]!.AsArray().Count);

        foreach (var field in new[] { "filesChecked", "filesWithDiagnostics", "total" })
        {
            limited[field]!.GetValue<int>().Should().Be(full[field]!.GetValue<int>());
        }

        var withSuggestions = (
            await workspace.CallAsync(request with { Severity = DiagnosticSeverity.Hint })
        )["result"]!;
        full["diagnostics"]!
            .AsArray()
            .SelectMany(file => file!["diagnostics"]!.AsArray())
            .Select(item => item!["severity"]!.GetValue<string>())
            .Should()
            .OnlyContain(severity => severity == "error" || severity == "warning");
        withSuggestions["total"]!
            .GetValue<int>()
            .Should()
            .Be(full["total"]!.GetValue<int>() + full["belowSeverity"]!.GetValue<int>());

        limited["complete"]!.GetValue<bool>().Should().BeTrue();
        limited["truncated"]!.GetValue<bool>().Should().BeTrue();
        limited["diagnostics"]!.AsArray().Should().ContainSingle();
        limited["diagnostics"]![0]!["diagnostics"]!.AsArray().Should().ContainSingle();
    }

    [Fact(Explicit = true, Timeout = TestTimeoutMilliseconds)]
    public async Task ImmediatelyReportsSemanticDiagnosticsForANewFileAsync()
    {
        const string newFile = "Application/NewFile.cs";
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var workspace = await RoslynTestWorkspace.CreateAsync(cancellationToken);
        await workspace.CallAsync(
            new LspRequest { Action = LspAction.Diagnostics, File = ConsumerFile }
        );

        // Roslyn first treats a new file as a miscellaneous file, without semantic diagnostics
        await workspace.WriteFileAsync(
            newFile,
            "namespace Sample.Application;\n\npublic static class NewFile\n{\n    public static int Wrong() => \"text\";\n}\n"
        );
        var result = (
            await workspace.CallAsync(
                new LspRequest { Action = LspAction.Diagnostics, File = newFile }
            )
        )["result"]!;

        result["complete"]!.GetValue<bool>().Should().BeTrue(result.ToJsonString());
        result["diagnostics"]!
            .AsArray()
            .SelectMany(file => file!["diagnostics"]!.AsArray())
            .Should()
            .Contain(item => Code(item!.AsObject()) == "CS0029");
    }

    [Fact(Explicit = true, Timeout = TestTimeoutMilliseconds)]
    public async Task SkipsOversizedDocumentsDuringSynchronizationAsync()
    {
        const string bigFile = "Application/Big.cs";
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var workspace = await RoslynTestWorkspace.CreateAsync(cancellationToken);
        var rename = await workspace.AtAsync(LspAction.Rename, ContractFile, "Greet(");
        await workspace.CallAsync(
            new LspRequest { Action = LspAction.Diagnostics, File = ConsumerFile }
        );

        // The watcher reports the file, so synchronization tries to open it
        await workspace.WriteFileAsync(
            bigFile,
            "// " + new string('x', 1_100_000) + "\nnamespace Sample.Application;\n"
        );

        var preview = await workspace.CallAsync(rename with { NewName = "Welcome" });
        PreviewFiles(preview).Should().Contain(ConsumerFile);

        var diagnostics = await workspace.CallAsync(
            new LspRequest { Action = LspAction.Diagnostics, File = ConsumerFile }
        );
        diagnostics["result"]!["complete"]!.GetValue<bool>().Should().BeTrue();
    }

    [Fact(Explicit = true, Timeout = TestTimeoutMilliseconds)]
    public async Task RefreshesUnopenedFilesWithUnchangedMetadataBeforeRenameAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var workspace = await RoslynTestWorkspace.CreateAsync(cancellationToken);
        var request = await workspace.AtAsync(LspAction.References, ContractFile, "Greet(");
        var consumerPath = workspace.FilePath(ConsumerFile);

        var references = await workspace.CallAsync(request);
        Locations(references).Should().Contain(ConsumerFile);
        var rename = request with { Action = LspAction.Rename, NewName = "Welcome" };

        // first preview records fingerprints, second must detect an edit that keeps the metadata
        var initialPreview = await workspace.CallAsync(rename);
        PreviewFiles(initialPreview).Should().Contain(ConsumerFile);

        var original = await workspace.ReadFileAsync(ConsumerFile);
        var changed = original.Replace(
            "greeter.Greet(\"world\")",
            "OtherGreeter.Greet(\"\")",
            StringComparison.Ordinal
        );
        changed.Should().NotBe(original);
        var file = new FileInfo(consumerPath);
        var length = file.Length;
        var lastWriteTime = file.LastWriteTimeUtc;
        await workspace.WriteFileAsync(ConsumerFile, changed);
        File.SetLastWriteTimeUtc(consumerPath, lastWriteTime);
        file.Refresh();
        file.Length.Should().Be(length);
        file.LastWriteTimeUtc.Should().Be(lastWriteTime);

        var preview = await workspace.CallAsync(rename);
        PreviewFiles(preview).Should().BeEquivalentTo([ContractFile, ImplementationFile]);

        var refreshed = await workspace.CallAsync(request);
        Locations(refreshed).Should().NotContain(ConsumerFile);
        (await workspace.ReadFileAsync(ConsumerFile)).Should().Be(changed);
    }

    [Fact(Explicit = true, Timeout = TestTimeoutMilliseconds)]
    public async Task ResolvesSymbolsInFilesDeclaringComparisonOperatorsAsync()
    {
        const string moneyFile = "Application/Money.cs";
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var workspace = await RoslynTestWorkspace.CreateAsync(cancellationToken);
        await workspace.CallAsync(
            new LspRequest { Action = LspAction.Diagnostics, File = ConsumerFile }
        );
        await workspace.WriteFileAsync(
            moneyFile,
            "namespace Sample.Application;\n\npublic readonly record struct Money(decimal Amount)\n{\n"
                + "    public Money Add(Money other) => new(Amount + other.Amount);\n"
                + "    public static bool operator <(Money left, Money right) => left.Amount < right.Amount;\n"
                + "    public static bool operator >(Money left, Money right) => left.Amount > right.Amount;\n}\n"
        );
        await workspace.CallAsync(
            new LspRequest { Action = LspAction.Diagnostics, File = moneyFile }
        );

        var definition = await workspace.CallAsync(
            new LspRequest { Action = LspAction.Definition, Symbol = "Money.Add" }
        );

        Locations(definition).Should().Equal(moneyFile);
    }

    [Fact(Explicit = true, Timeout = TestTimeoutMilliseconds)]
    public async Task ReloadGetsTheStartupBudgetAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var workspace = await RoslynTestWorkspace.CreateAsync(
            cancellationToken,
            requestTimeoutSeconds: 1
        );
        await workspace.CallAsync(
            new LspRequest { Action = LspAction.Diagnostics, File = ConsumerFile }
        );

        var reload = await workspace.CallAsync(new LspRequest { Action = LspAction.Reload });

        reload["result"].Should().NotBeNull();
    }

    [Fact(Explicit = true, Timeout = TestTimeoutMilliseconds)]
    public async Task NavigatesAndRenamesBySymbolNameAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var workspace = await RoslynTestWorkspace.CreateAsync(cancellationToken);

        var references = await workspace.CallAsync(
            new LspRequest { Action = LspAction.References, Symbol = "IGreeter.Greet" }
        );
        Locations(references).Should().Contain(ConsumerFile).And.NotContain(OtherGreeterFile);

        var ambiguous = await workspace.CallRawAsync(
            new LspRequest { Action = LspAction.Definition, Symbol = "Greet" }
        );
        AssertError(ambiguous, SymbolResolver.AmbiguousErrorCode);
        var candidates = JsonSerializer.SerializeToNode(ambiguous.StructuredContent)!["error"]![
            "candidates"
        ]!
            .AsArray()
            .Select(candidate => candidate!["symbol"]!.GetValue<string>())
            .ToArray();
        candidates.Should().HaveCount(3);
        var contract = candidates.Should().ContainSingle(name => name.Contains("IGreeter")).Which;
        AssertSingleLocation(
            await workspace.CallAsync(
                new LspRequest { Action = LspAction.Definition, Symbol = contract }
            ),
            ContractFile
        );

        var preview = await workspace.CallAsync(
            new LspRequest
            {
                Action = LspAction.Rename,
                Symbol = "Consumer.Run",
                NewName = "Execute",
            }
        );
        PreviewFiles(preview).Should().Equal(ConsumerFile);
    }

    [Fact(Explicit = true, Timeout = TestTimeoutMilliseconds)]
    public async Task RenamesFilesOnlyWhenRoslynAdvertisesSupportAsync()
    {
        const string destination = "Application/RenamedGreeter.cs";
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var workspace = await RoslynTestWorkspace.CreateAsync(cancellationToken);
        await workspace.CallAsync(
            new LspRequest { Action = LspAction.Symbols, File = ImplementationFile }
        );
        var request = new LspRequest
        {
            Action = LspAction.RenameFile,
            File = ImplementationFile,
            NewName = destination,
        };

        var capabilities = await workspace.CallAsync(
            new LspRequest { Action = LspAction.Capabilities }
        );
        if (capabilities["result"]?["workspace"]?["fileOperations"]?["willRename"] is null)
        {
            var original = await workspace.ReadFileAsync(ImplementationFile);
            var rejected = await workspace.CallRawAsync(request);
            AssertError(rejected, LspTool.InvalidRequestErrorCode);
            (await workspace.ReadFileAsync(ImplementationFile)).Should().Be(original);
            File.Exists(workspace.FilePath(destination)).Should().BeFalse();
            TestContext.Current.TestOutputHelper!.WriteLine(
                "Roslyn did not advertise file rename support. Refusal preserved the source."
            );
            return;
        }

        var preview = await workspace.CallAsync(request);
        File.Exists(workspace.FilePath(ImplementationFile)).Should().BeTrue();
        File.Exists(workspace.FilePath(destination)).Should().BeFalse();

        var applied = await workspace.CallAsync(ApplyProposal(LspAction.RenameFile, preview));
        AssertApplied(applied);
        File.Exists(workspace.FilePath(ImplementationFile)).Should().BeFalse();
        File.Exists(workspace.FilePath(destination)).Should().BeTrue();

        var implementation = await workspace.CallAsync(
            await workspace.AtAsync(LspAction.Implementation, ContractFile, "Greet(")
        );
        AssertSingleLocation(implementation, destination);
        TestContext.Current.TestOutputHelper!.WriteLine(
            "Roslyn advertised file rename support. Preview, apply and navigation to the new path passed."
        );
    }

    [Fact(Explicit = true, Timeout = TestTimeoutMilliseconds)]
    public async Task AppliesRenameAcrossProjectsWithoutRenamingAHomonymAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var workspace = await RoslynTestWorkspace.CreateAsync(cancellationToken);
        var unrelatedBefore = await workspace.ReadFileAsync(OtherGreeterFile);
        var request = await workspace.AtAsync(LspAction.Rename, ContractFile, "Greet(");
        request = request with { NewName = "Welcome" };

        var preview = await workspace.CallAsync(request);
        var original = await workspace.ReadFileAsync(ContractFile);
        original.Should().Contain("Greet(");
        original.Should().NotContain("Welcome(");
        var previewChanges = preview["result"]!["files"]!
            .AsArray()
            .SelectMany(file => file!["changes"]!.AsArray());
        previewChanges
            .Should()
            .NotBeEmpty()
            .And.AllSatisfy(change =>
            {
                change!["before"]!["text"]!.GetValue<string>().Should().Contain("Greet");
                change["after"]!["text"]!.GetValue<string>().Should().Contain("Welcome");
                change["after"]!["startLine"]!.GetValue<int>().Should().BePositive();
            });

        var applied = await workspace.CallAsync(ApplyProposal(LspAction.Rename, preview));
        AssertApplied(applied);

        foreach (var file in new[] { ContractFile, ImplementationFile, ConsumerFile })
        {
            var content = await workspace.ReadFileAsync(file);
            content.Should().Contain("Welcome(");
            content.Should().NotContain("Greet(");
        }

        var unrelatedAfter = await workspace.ReadFileAsync(OtherGreeterFile);
        unrelatedAfter.Should().Be(unrelatedBefore);
    }

    [Fact(Explicit = true, Timeout = TestTimeoutMilliseconds)]
    public async Task RejectsRenamePreviewAfterAnExternalEditAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var workspace = await RoslynTestWorkspace.CreateAsync(cancellationToken);
        var request = await workspace.AtAsync(LspAction.Rename, ContractFile, "Greet(");
        request = request with { NewName = "Welcome" };

        var preview = await workspace.CallAsync(request);
        var original = await workspace.ReadFileAsync(ConsumerFile);
        var changed = original.Replace("world", "friend", StringComparison.Ordinal);
        await workspace.WriteFileAsync(ConsumerFile, changed);

        var rejected = await workspace.CallRawAsync(ApplyProposal(LspAction.Rename, preview));
        AssertError(rejected, LspTool.StaleEditErrorCode);
        (await workspace.ReadFileAsync(ConsumerFile)).Should().Be(changed);
        var contract = await workspace.ReadFileAsync(ContractFile);
        contract.Should().Contain("Greet(");
        contract.Should().NotContain("Welcome(");
    }

    [Theory(Explicit = true, Timeout = TestTimeoutMilliseconds)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ResolvesAndAppliesRoslynCodeActionAsync(bool applyFromListing)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var workspace = await RoslynTestWorkspace.CreateAsync(cancellationToken);
        var request = await workspace.AtAsync(LspAction.CodeActions, ProblemsFile, "List<string>");
        var listing = await workspace.CallAsync(request);
        var action = listing["result"]!["actions"]!
            .AsArray()
            .Should()
            .ContainSingle(item =>
                item!["title"]!.GetValue<string>() == "using System.Collections.Generic;"
            )
            .Which!;
        request = request with { ActionIndex = action["index"]!.GetValue<int>() };

        var preview = await workspace.CallAsync(request);
        (await DiagnosticsAsync(workspace)).Should().Contain(item => Code(item) == "CS0246");

        var applyRequest = ApplyProposal(
            LspAction.CodeActions,
            applyFromListing ? listing : preview
        ) with
        {
            ActionIndex = request.ActionIndex,
        };
        var applied = await workspace.CallAsync(applyRequest);
        AssertApplied(applied);
        AssertError(await workspace.CallRawAsync(applyRequest), LspTool.InvalidRequestErrorCode);
        var text = await workspace.ReadFileAsync(ProblemsFile);
        text.Should().Contain("using System.Collections.Generic;");

        var diagnostics = await DiagnosticsAsync(workspace);
        diagnostics.Should().NotContain(item => Code(item) == "CS0246");
        diagnostics.Should().Contain(item => Code(item) == "CS8603");
    }

    private static LspRequest ApplyProposal(LspAction action, JsonObject preview) =>
        new()
        {
            Action = action,
            ProposalId = preview["result"]!["proposalId"]!.GetValue<string>(),
            Apply = true,
        };

    private static void AssertApplied(JsonObject response) =>
        response["result"]!["applied"]!.GetValue<bool>().Should().BeTrue();

    private static void AssertError(CallToolResult response, string expectedCode)
    {
        response.IsError.Should().BeTrue();

        var error = JsonSerializer.SerializeToNode(response.StructuredContent)!;
        error["error"]!["code"]!.GetValue<string>().Should().Be(expectedCode);
    }

    private static void AssertSingleLocation(JsonObject response, string expectedFile) =>
        Locations(response)
            .Should()
            .ContainSingle()
            .Which.Should()
            .Be(expectedFile, response.ToJsonString());

    private static async Task<JsonObject[]> DiagnosticsAsync(RoslynTestWorkspace workspace)
    {
        var response = await workspace.CallAsync(
            new LspRequest { Action = LspAction.Diagnostics, File = ProblemsFile }
        );
        return
        [
            .. response["result"]!["diagnostics"]!
                .AsArray()
                .SelectMany(file => file!["diagnostics"]!.AsArray())
                .Select(item => item!.AsObject()),
        ];
    }

    private static string Code(JsonObject diagnostic) => diagnostic["code"]!.ToString();

    private static IEnumerable<string> DocumentSymbolNames(JsonArray symbols)
    {
        foreach (var symbol in symbols)
        {
            yield return symbol!["name"]!.GetValue<string>();
            if (symbol["children"] is not JsonArray children)
            {
                continue;
            }

            foreach (var name in DocumentSymbolNames(children))
            {
                yield return name;
            }
        }
    }

    private static string[] PreviewFiles(JsonObject response) =>
        [
            .. response["result"]!["files"]!
                .AsArray()
                .Select(item =>
                    item!["path"]!.GetValue<string>().Replace(Path.DirectorySeparatorChar, '/')
                ),
        ];

    private static string[] Locations(JsonObject response) =>
        [
            .. response["result"]!["items"]!
                .AsArray()
                .Select(location => location!["file"]!.GetValue<string>()),
        ];
}
