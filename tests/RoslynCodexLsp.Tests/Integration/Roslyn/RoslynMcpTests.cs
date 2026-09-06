// Copyright (C) 2026 thomas-fazzari
// SPDX-License-Identifier: GPL-3.0-only

using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using RoslynCodexLsp.Tools;

namespace RoslynCodexLsp.Tests.Integration.Roslyn;

/// <summary>
/// Tests the MCP bridge against the real Roslyn language server in temporary workspaces.
/// These tests are explicit and are excluded from make test and make check.
/// </summary>
/// <remarks>
/// Requires dotnet and roslyn-language-server on PATH. To add local executable directories,
/// copy .env.copyme to .env and set EXTRA_PATH. From the repository root, run:
/// <code>
/// make test-integration
/// </code>
/// The command builds the project. The tests restore their fixtures, launch Roslyn,
/// and delete their temporary workspaces when finished.
/// To test a published native bridge, set its absolute executable path:
/// <code>
/// ROSLYN_CODEX_TEST_EXECUTABLE=/absolute/path/RoslynCodexLsp make test-integration
/// </code>
/// </remarks>
public sealed class RoslynMcpTests
{
    private const int TestTimeoutMilliseconds = 120_000;
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
        tools.Should().ContainSingle().Which.Name.Should().Be(LspTool.ToolName);

        var capabilities = await workspace.CallAsync(
            new JsonObject
            {
                ["action"] = JsonSerializer.SerializeToNode(
                    LspAction.Capabilities,
                    BridgeJsonContext.Default.LspAction
                ),
            }
        );
        capabilities["result"]!["definitionProvider"]!.GetValue<bool>().Should().BeTrue();
        capabilities["result"]!["typeDefinitionProvider"]!.GetValue<bool>().Should().BeTrue();

        var diagnostics = await DiagnosticsAsync(workspace);
        diagnostics
            .Should()
            .Contain(item => Code(item) == "CS0246" && item["severity"]!.GetValue<int>() == 1);
        diagnostics
            .Should()
            .Contain(item => Code(item) == "CS8603" && item["severity"]!.GetValue<int>() == 2);

        var definition = await workspace.CallAsync(
            await workspace.AtAsync(LspAction.Definition, ConsumerFile, "Greet(")
        );
        AssertSingleLocation(definition, workspace.FilePath(ContractFile));

        var typeDefinition = await workspace.CallAsync(
            await workspace.AtAsync(LspAction.TypeDefinition, ConsumerFile, "greeter.Greet")
        );
        AssertSingleLocation(typeDefinition, workspace.FilePath(ContractFile));

        var implementation = await workspace.CallAsync(
            await workspace.AtAsync(LspAction.Implementation, ContractFile, "Greet(")
        );
        AssertSingleLocation(implementation, workspace.FilePath(ImplementationFile));

        var references = await workspace.CallAsync(
            await workspace.AtAsync(LspAction.References, ContractFile, "Greet(")
        );
        Locations(references).Should().Contain(new Uri(workspace.FilePath(ConsumerFile)));
        Locations(references).Should().NotContain(new Uri(workspace.FilePath(OtherGreeterFile)));

        var hover = await workspace.CallAsync(
            await workspace.AtAsync(LspAction.Hover, ConsumerFile, "Greet(")
        );
        hover["result"]!["contents"].Should().NotBeNull();

        var symbols = await workspace.CallAsync(
            new LspRequest { Action = LspAction.Symbols, Query = "IGreeter" }
        );
        symbols["result"]!
            .AsArray()
            .Should()
            .Contain(symbol => symbol!["name"]!.GetValue<string>() == "IGreeter");

        var documentSymbols = await workspace.CallAsync(
            new LspRequest { Action = LspAction.Symbols, File = ContractFile }
        );
        var symbolNames = DocumentSymbolNames(documentSymbols["result"]!.AsArray()).ToArray();
        symbolNames.Should().Contain("IGreeter");
        symbolNames.Should().Contain(name => name.StartsWith("Greet(", StringComparison.Ordinal));

        var text = await workspace.ReadFileAsync(ProblemsFile);
        await workspace.WriteFileAsync(ProblemsFile, "using System.Collections.Generic;\n" + text);

        var refreshed = await DiagnosticsAsync(workspace);
        refreshed.Should().NotContain(item => Code(item) == "CS0246");
        refreshed.Should().Contain(item => Code(item) == "CS8603");
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
        AssertSingleLocation(implementation, workspace.FilePath(destination));
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
            .Be(new Uri(expectedFile), response.ToJsonString());

    private static async Task<JsonObject[]> DiagnosticsAsync(RoslynTestWorkspace workspace)
    {
        var response = await workspace.CallAsync(
            new LspRequest { Action = LspAction.Diagnostics, File = ProblemsFile }
        );
        return
        [
            .. response["result"]!["files"]!
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

    private static Uri[] Locations(JsonObject response) =>
        [
            .. response["result"]!
                .AsArray()
                .Select(location => new Uri(
                    (location!["uri"] ?? location["targetUri"])!.GetValue<string>()
                )),
        ];
}
