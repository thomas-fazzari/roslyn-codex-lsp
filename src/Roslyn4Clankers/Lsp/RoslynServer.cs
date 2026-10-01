// Copyright (C) 2026 thomas-fazzari
// SPDX-License-Identifier: GPL-3.0-only

using System.Diagnostics;

namespace Roslyn4Clankers.Lsp;

/// <summary>
/// A running Roslyn language server seen through its standard streams.
/// </summary>
internal interface IRoslynServer : IDisposable
{
    /// <summary>
    /// Stream the bridge writes requests to.
    /// </summary>
    Stream Input { get; }

    /// <summary>
    /// Stream the bridge reads responses from.
    /// </summary>
    Stream Output { get; }

    StreamReader Error { get; }

    bool HasExited { get; }

    void Kill();

    Task WaitForExitAsync(CancellationToken cancellationToken);
}

internal delegate IRoslynServer RoslynLauncher(BridgeOptions options, WorkspacePaths paths);

/// <summary>
/// The Roslyn language server as a child process.
/// </summary>
internal sealed class RoslynProcess(Process process) : IRoslynServer
{
    public static IRoslynServer Start(BridgeOptions options, WorkspacePaths paths)
    {
        var startInfo = new ProcessStartInfo(options.ServerPath)
        {
            WorkingDirectory = paths.Root,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("--stdio");
        startInfo.ArgumentList.Add("--autoLoadProjects");
        var process =
            Process.Start(startInfo)
            ?? throw new InvalidOperationException("The Roslyn process could not be started.");
        return new RoslynProcess(process);
    }

    public Stream Input => process.StandardInput.BaseStream;

    public Stream Output => process.StandardOutput.BaseStream;

    public StreamReader Error => process.StandardError;

    public bool HasExited => process.HasExited;

    // Roslyn leaves its MSBuild build hosts running as orphans after the exit notification.
    // Killing the tree ends them while they are still Roslyn's descendants.
    public void Kill() => process.Kill(entireProcessTree: true);

    public Task WaitForExitAsync(CancellationToken cancellationToken) =>
        process.WaitForExitAsync(cancellationToken);

    public void Dispose() => process.Dispose();
}
