// Copyright (C) 2026 thomas-fazzari
// SPDX-License-Identifier: GPL-3.0-only

namespace RoslynCodexLsp.Tools;

internal static class ToolDescriptions
{
    public const string File = "Workspace-relative file path.";
    public const string Symbol =
        "C# symbol name, such as Namespace.Type.Member, Type.Member or Type.Method(int). Replaces file, line and character. A constructor needs its parameter list.";
    public const string Line = "One-based line number.";
    public const string Character = "One-based UTF-16 character in the line.";
    public const string Limit = "Maximum returned results, from 1 to 250. Truncation is explicit.";
}
