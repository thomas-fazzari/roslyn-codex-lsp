// Copyright (C) 2026 thomas-fazzari
// SPDX-License-Identifier: GPL-3.0-only

using System.Text.Json.Nodes;
using RoslynCodexLsp.Editing;

namespace RoslynCodexLsp.Tools;

internal sealed class CommandEdits(
    WorkspaceEditService edits,
    WorkspaceSnapshot snapshot,
    Func<CancellationToken, Task<WorkspaceSnapshot>> capture,
    CancellationToken cancellationToken
) : IAsyncDisposable
{
    private readonly CancellationTokenSource _lifetime =
        CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Lock _admission = new();
    private readonly List<JsonObject> _receipts = [];
    private readonly HashSet<string> _paths = new(StringComparer.Ordinal);

    private WorkspaceSnapshot _snapshot = snapshot;
    private TaskCompletionSource? _callbacksDrained;
    private Exception? _failure;
    private int _activeCallbacks;
    private bool _accepting = true;
    private bool _applied = true;
    private bool _changesFileMembership;

    public Exception? Failure => _failure;

    public bool ChangesFileMembership => _changesFileMembership;

    public bool HasChanges => _paths.Count != 0;

    public JsonObject Result
    {
        get
        {
            lock (_admission)
            {
                return BuildResult();
            }
        }
    }

    public async Task<JsonObject> ApplyAsync(JsonObject edit, CancellationToken callbackToken)
    {
        ArgumentNullException.ThrowIfNull(edit);
        if (!TryBeginCallback())
        {
            return RejectedResponse();
        }

        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                _lifetime.Token,
                callbackToken
            );
            try
            {
                await _gate.WaitAsync(linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException exception)
            {
                return IsAcceptingEdits()
                    ? FailCallback(exception, EditApplicationPhase.Write, applied: false)
                    : RejectedResponse();
            }

            try
            {
                return IsAcceptingEdits()
                    ? await ApplyAndCaptureAsync(edit, linked.Token).ConfigureAwait(false)
                    : RejectedResponse();
            }
            finally
            {
                _gate.Release();
            }
        }
        finally
        {
            EndCallback();
        }
    }

    private async Task<JsonObject> ApplyAndCaptureAsync(JsonObject edit, CancellationToken token)
    {
        JsonObject receipt;
        try
        {
            receipt = await edits.ApplyAsync(edit, _snapshot, token).ConfigureAwait(false);
        }
        catch (EditApplicationException exception)
        {
            RecordReceipt(exception.Result);
            return FailCallback(
                exception,
                exception.Phase,
                applied: exception.Result["applied"]!.GetValue<bool>()
            );
        }
        catch (Exception exception)
        {
            return FailCallback(exception, EditApplicationPhase.Write, applied: false);
        }

        RecordReceipt(receipt);
        try
        {
            _snapshot = await capture(token).ConfigureAwait(false);
            return new JsonObject { ["applied"] = true };
        }
        catch (Exception exception)
        {
            return FailCallback(exception, EditApplicationPhase.Synchronize, applied: true);
        }
    }

    public async Task StopAsync()
    {
        Task drained;
        lock (_admission)
        {
            _accepting = false;
            drained = _callbacksDrained?.Task ?? Task.CompletedTask;
        }

        await _lifetime.CancelAsync().ConfigureAwait(false);
        await drained.ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _lifetime.Dispose();
        _gate.Dispose();
    }

    private bool TryBeginCallback()
    {
        lock (_admission)
        {
            if (!_accepting)
            {
                return false;
            }

            if (_activeCallbacks++ == 0)
            {
                _callbacksDrained = new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously
                );
            }

            return true;
        }
    }

    private void EndCallback()
    {
        lock (_admission)
        {
            if (--_activeCallbacks == 0)
            {
                _callbacksDrained!.TrySetResult();
            }
        }
    }

    private bool IsAcceptingEdits()
    {
        lock (_admission)
        {
            return _accepting;
        }
    }

    private void RecordReceipt(JsonObject result)
    {
        lock (_admission)
        {
            _receipts.Add(result);
            foreach (var file in result["files"]!.AsArray().OfType<JsonObject>())
            {
                _paths.Add(file["path"]!.GetValue<string>());
                _changesFileMembership |= LspChanges.IsFileMembershipChange(file);
            }
        }
    }

    private JsonObject FailCallback(Exception exception, EditApplicationPhase phase, bool applied)
    {
        lock (_admission)
        {
            _applied &= applied;
            _failure ??=
                applied && exception is not EditApplicationException
                    ? new EditApplicationException(
                        BuildResult(),
                        phase,
                        failedPath: null,
                        failedOperation: null,
                        exception
                    )
                    : exception;

            _accepting = false;
            return new JsonObject
            {
                ["applied"] = applied,
                ["failureReason"] = _failure.InnerException?.Message ?? _failure.Message,
            };
        }
    }

    private JsonObject BuildResult()
    {
        var files = new JsonArray();
        foreach (var receipt in _receipts)
        {
            foreach (var file in receipt["files"]!.AsArray())
            {
                files.Add(file!.DeepClone());
            }
        }

        return new JsonObject
        {
            ["applied"] = _applied,
            ["partial"] = !_applied && _paths.Count != 0,
            ["fileCount"] = _paths.Count,
            ["operationCount"] = files.Count,
            ["files"] = files,
        };
    }

    private static JsonObject RejectedResponse() =>
        new()
        {
            ["applied"] = false,
            ["failureReason"] = "The workspace edit callback is no longer accepting edits.",
        };
}
