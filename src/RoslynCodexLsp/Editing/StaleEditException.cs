// Copyright (C) 2026 thomas-fazzari
// SPDX-License-Identifier: GPL-3.0-only

namespace RoslynCodexLsp.Editing;

internal sealed class StaleEditException(string message) : InvalidOperationException(message);
