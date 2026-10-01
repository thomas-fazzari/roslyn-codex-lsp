// Copyright (C) 2026 thomas-fazzari
// SPDX-License-Identifier: GPL-3.0-only

using System.Diagnostics;

namespace Roslyn4Clankers.Editing;

[DebuggerDisplay("{Fingerprints.Count} files, {DocumentVersions.Count} open documents")]
internal sealed record WorkspaceSnapshot(
    IReadOnlyDictionary<string, string> Fingerprints,
    IReadOnlyDictionary<string, int> DocumentVersions
);
