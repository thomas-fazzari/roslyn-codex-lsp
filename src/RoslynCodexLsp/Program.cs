// Copyright (C) 2026 thomas-fazzari
// SPDX-License-Identifier: GPL-3.0-only

using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using RoslynCodexLsp.Editing;
using RoslynCodexLsp.Lsp;
using RoslynCodexLsp.Tools;

namespace RoslynCodexLsp;

internal static class Program
{
    public static async Task Main(string[] args)
    {
        if (args is ["--help"] or ["-h"])
        {
            await Console.Error.WriteLineAsync(
                "roslyn-codex-lsp [--workspace DIRECTORY] [--server EXECUTABLE] [--startup-timeout SECONDS] [--request-timeout SECONDS]"
            );
            return;
        }

        var options = BridgeOptions.Parse(args);
        var builder = Host.CreateApplicationBuilder(
            new HostApplicationBuilderSettings { ContentRootPath = AppContext.BaseDirectory }
        );
        builder.Logging.AddConsole(static options =>
            options.LogToStandardErrorThreshold = LogLevel.Trace
        );
        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton(new WorkspacePaths(options.WorkspaceRoot));
        builder.Services.AddSingleton<RoslynSession>();
        builder.Services.AddSingleton<WorkspaceEditService>();
        builder.Services.AddSingleton<LspQueries>();
        builder.Services.AddSingleton<LspChanges>();
        builder.Services.AddSingleton<LspTool>();

        var jsonOptions = new JsonSerializerOptions(McpJsonUtilities.DefaultOptions);
        jsonOptions.TypeInfoResolverChain.Insert(0, BridgeJsonContext.Default);
        builder
            .Services.AddMcpServer()
            .WithStdioServerTransport()
            .WithTools<LspTool>(serializerOptions: jsonOptions);

        using var host = builder.Build();
        await host.RunAsync(CancellationToken.None);
    }
}
