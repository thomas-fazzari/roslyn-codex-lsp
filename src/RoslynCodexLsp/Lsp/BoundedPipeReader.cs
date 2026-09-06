// Copyright (C) 2026 thomas-fazzari
// SPDX-License-Identifier: GPL-3.0-only

using System.Globalization;
using System.IO.Pipelines;

namespace RoslynCodexLsp.Lsp;

/// <summary>
/// Limits bytes retained by the JSON-RPC reader without interpreting its framing.
/// </summary>
internal sealed class BoundedPipeReader(PipeReader inner, long maximumBytes) : PipeReader
{
    public override void AdvanceTo(SequencePosition consumed) => inner.AdvanceTo(consumed);

    public override void AdvanceTo(SequencePosition consumed, SequencePosition examined) =>
        inner.AdvanceTo(consumed, examined);

    public override void CancelPendingRead() => inner.CancelPendingRead();

    public override void Complete(Exception? exception = null) => inner.Complete(exception);

    public override ValueTask CompleteAsync(Exception? exception = null) =>
        inner.CompleteAsync(exception);

    public override async ValueTask<ReadResult> ReadAsync(
        CancellationToken cancellationToken = default
    ) => Validate(await inner.ReadAsync(cancellationToken).ConfigureAwait(false));

    public override bool TryRead(out ReadResult result)
    {
        if (!inner.TryRead(out result))
        {
            return false;
        }

        result = Validate(result);
        return true;
    }

    private ReadResult Validate(ReadResult result) =>
        result.Buffer.Length > maximumBytes
            ? throw new InvalidDataException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"The Roslyn protocol buffer exceeded its {maximumBytes}-byte limit."
                )
            )
            : result;
}
