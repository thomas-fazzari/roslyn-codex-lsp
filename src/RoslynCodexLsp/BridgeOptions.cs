// Copyright (C) 2026 thomas-fazzari
// SPDX-License-Identifier: GPL-3.0-only

using System.Globalization;

namespace RoslynCodexLsp;

internal sealed record BridgeOptions
{
    internal const string WorkspaceArgument = "--workspace";
    internal const string ServerArgument = "--server";
    internal const string StartupTimeoutArgument = "--startup-timeout";
    internal const string RequestTimeoutArgument = "--request-timeout";
    internal const string DefaultServerPath = "roslyn-language-server";

    internal const int DefaultStartupTimeoutSeconds = 120;
    internal const int DefaultRequestTimeoutSeconds = 60;
    private const int MinimumTimeoutSeconds = 1;
    private const int MaximumTimeoutSeconds = 600;

    public string WorkspaceRoot { get; init; } = Directory.GetCurrentDirectory();

    public string ServerPath { get; init; } = DefaultServerPath;

    public TimeSpan StartupTimeout { get; init; } =
        TimeSpan.FromSeconds(DefaultStartupTimeoutSeconds);

    public TimeSpan RequestTimeout { get; init; } =
        TimeSpan.FromSeconds(DefaultRequestTimeoutSeconds);

    public static BridgeOptions Parse(string[] args)
    {
        var options = new BridgeOptions();
        for (var index = 0; index < args.Length; index += 2)
        {
            if (index + 1 >= args.Length)
            {
                throw new ArgumentException($"Missing value for {args[index]}.", nameof(args));
            }

            var value = args[index + 1];
            options = args[index] switch
            {
                WorkspaceArgument => options with { WorkspaceRoot = Path.GetFullPath(value) },
                ServerArgument => options with { ServerPath = value },
                StartupTimeoutArgument => options with { StartupTimeout = ParseTimeout(value) },
                RequestTimeoutArgument => options with { RequestTimeout = ParseTimeout(value) },
                _ => throw new ArgumentException($"Unknown option: {args[index]}.", nameof(args)),
            };
        }

        return !Directory.Exists(options.WorkspaceRoot)
            ? throw new DirectoryNotFoundException(options.WorkspaceRoot)
            : options;
    }

    private static TimeSpan ParseTimeout(string value)
    {
        if (
            !int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
            || seconds is < MinimumTimeoutSeconds or > MaximumTimeoutSeconds
        )
        {
            throw new ArgumentException(
                $"Timeout must be between {MinimumTimeoutSeconds} and {MaximumTimeoutSeconds} seconds.",
                nameof(value)
            );
        }

        return TimeSpan.FromSeconds(seconds);
    }
}
