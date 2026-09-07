// Copyright (C) 2026 thomas-fazzari
// SPDX-License-Identifier: GPL-3.0-only

using System.Runtime.ExceptionServices;
using System.Text.Json.Nodes;
using RoslynCodexLsp.Editing;
using RoslynCodexLsp.Lsp;

namespace RoslynCodexLsp.Tools;

internal sealed class LspChanges(
    RoslynSession session,
    WorkspacePaths paths,
    WorkspaceEditService edits,
    LspQueries queries
)
{
    private const int MaximumPendingProposals = 8;
    private static readonly TimeSpan _proposalLifetime = TimeSpan.FromMinutes(5);

    private readonly Dictionary<string, PendingChange> _pending = new(StringComparer.Ordinal);

    public void Clear() => _pending.Clear();

    public async Task<JsonNode?> ExecuteAsync(
        LspRequest request,
        CancellationToken cancellationToken
    )
    {
        if (request.ProposalId is not null)
        {
            return await UseProposalAsync(request, cancellationToken);
        }

        return request.Action switch
        {
            LspAction.Rename => await RenameAsync(request, cancellationToken),
            LspAction.RenameFile => await RenameFileAsync(request, cancellationToken),
            LspAction.CodeActions => await CodeActionsAsync(request, cancellationToken),
            _ => throw new ArgumentException("Unsupported change action.", nameof(request)),
        };
    }

    private async Task<JsonNode?> RenameAsync(
        LspRequest request,
        CancellationToken cancellationToken
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.NewName, nameof(request));
        var parameters = await queries.PositionAsync(request, cancellationToken);
        var snapshot = await CaptureAsync(cancellationToken);
        parameters["newName"] = request.NewName;
        var edit =
            await session.RequestAsync(LspMethods.TextDocumentRename, parameters, cancellationToken)
                as JsonObject
            ?? throw new InvalidOperationException("Roslyn cannot rename the selected symbol.");
        return await CompleteAsync(
            new PendingChange(request.Action, snapshot, edit, Command: null),
            request.Apply,
            cancellationToken
        );
    }

    private async Task<JsonNode?> RenameFileAsync(
        LspRequest request,
        CancellationToken cancellationToken
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.File, nameof(request));
        ArgumentException.ThrowIfNullOrWhiteSpace(request.NewName, nameof(request));
        if (session.Capabilities?["workspace"]?["fileOperations"]?["willRename"] is null)
        {
            throw new InvalidOperationException("Roslyn does not advertise file rename support.");
        }

        var oldUri = paths.ToUri(request.File);
        var newUri = paths.ToUri(request.NewName);
        var snapshot = await CaptureAsync(cancellationToken);
        var parameters = new JsonObject
        {
            ["files"] = new JsonArray(new JsonObject { ["oldUri"] = oldUri, ["newUri"] = newUri }),
        };
        var edit =
            await session.RequestAsync(
                LspMethods.WorkspaceWillRenameFiles,
                parameters,
                cancellationToken
            ) as JsonObject
            ?? new JsonObject();
        var changes = ToDocumentChanges(edit);
        changes.Add(
            (JsonNode)
                new JsonObject
                {
                    ["kind"] = WorkspaceEditService.RenameFileOperation,
                    ["oldUri"] = oldUri,
                    ["newUri"] = newUri,
                }
        );
        edit = new JsonObject { ["documentChanges"] = changes };
        return await CompleteAsync(
            new PendingChange(
                request.Action,
                snapshot,
                edit,
                Command: null,
                FileRename: parameters
            ),
            request.Apply,
            cancellationToken
        );
    }

    private async Task<JsonNode?> CodeActionsAsync(
        LspRequest request,
        CancellationToken cancellationToken
    )
    {
        var position = await queries.PositionAsync(request, cancellationToken);
        var uri = position["textDocument"]!["uri"]!.GetValue<string>();
        var report = await queries.FileDiagnosticsAsync(uri, cancellationToken);
        var snapshot = await CaptureAsync(cancellationToken);
        var start = position["position"]!.DeepClone();
        var end = EndPosition(request, start);
        var parameters = new JsonObject
        {
            ["textDocument"] = position["textDocument"]!.DeepClone(),
            ["range"] = new JsonObject { ["start"] = start, ["end"] = end },
            ["context"] = new JsonObject
            {
                ["diagnostics"] = report["items"]?.DeepClone() ?? new JsonArray(),
            },
        };
        var actions =
            await session.RequestAsync(
                LspMethods.TextDocumentCodeAction,
                parameters,
                cancellationToken
            ) as JsonArray
            ?? [];
        var pending = new PendingChange(
            request.Action,
            snapshot,
            Edit: null,
            Command: null,
            actions
        );
        if (request.ActionIndex is not null)
        {
            return await SelectActionAsync(pending, request, cancellationToken);
        }

        if (request.Apply)
        {
            throw new ArgumentException(
                "Choose actionIndex before applying a code action.",
                nameof(request)
            );
        }

        var listing = new JsonArray();
        for (var index = 0; index < Math.Min(actions.Count, request.Limit); index++)
        {
            listing.Add(
                (JsonNode)
                    new JsonObject
                    {
                        ["index"] = index,
                        ["title"] = actions[index]?["title"]?.DeepClone(),
                        ["kind"] = actions[index]?["kind"]?.DeepClone(),
                    }
            );
        }

        return new JsonObject
        {
            ["proposalId"] = Remember(pending),
            ["actions"] = listing,
            ["total"] = actions.Count,
            ["truncated"] = actions.Count > request.Limit,
        };
    }

    private async Task<JsonNode?> UseProposalAsync(
        LspRequest request,
        CancellationToken cancellationToken
    )
    {
        if (
            !_pending.TryGetValue(request.ProposalId!, out var pending)
            || pending.Action != request.Action
            || DateTimeOffset.UtcNow - pending.CreatedAt > _proposalLifetime
        )
        {
            throw new ArgumentException(
                "The proposal is unknown or expired. Request a new preview.",
                nameof(request)
            );
        }

        var result = pending.Actions is not null
            ? await SelectActionAsync(pending, request, cancellationToken)
            : await CompleteAsync(pending, request.Apply, cancellationToken);
        if (request.Apply)
        {
            _pending.Remove(request.ProposalId!);
        }

        return result;
    }

    private async Task<JsonNode?> SelectActionAsync(
        PendingChange pending,
        LspRequest request,
        CancellationToken cancellationToken
    )
    {
        var actions = pending.Actions!;
        if (request.ActionIndex is not >= 0 || request.ActionIndex >= actions.Count)
        {
            throw new ArgumentException(
                "Select a valid actionIndex from the code action listing.",
                nameof(request)
            );
        }

        await edits.PreviewAsync(new JsonObject(), pending.Snapshot, cancellationToken);
        var action =
            actions[request.ActionIndex.Value]?.DeepClone() as JsonObject
            ?? throw new InvalidOperationException("The code action is invalid.");
        if (action["data"] is not null)
        {
            action =
                await session.RequestAsync(LspMethods.CodeActionResolve, action, cancellationToken)
                    as JsonObject
                ?? throw new InvalidOperationException("Roslyn could not resolve the code action.");
        }

        var command = action["command"] switch
        {
            JsonObject value => value,
            JsonValue => action,
            _ => null,
        };
        var selected = new PendingChange(
            request.Action,
            pending.Snapshot,
            action["edit"] as JsonObject,
            command
        );
        if (selected.Edit is null && selected.Command is null)
        {
            throw new InvalidOperationException("The selected action contains no edit or command.");
        }

        return await CompleteAsync(selected, request.Apply, cancellationToken);
    }

    internal async Task<JsonObject> CompleteAsync(
        PendingChange change,
        bool apply,
        CancellationToken cancellationToken
    )
    {
        var edit = change.Edit ?? new JsonObject();
        if (!apply)
        {
            var preview = await edits.PreviewAsync(edit, change.Snapshot, cancellationToken);
            preview["proposalId"] = Remember(change);
            preview["command"] = change.Command?.DeepClone();
            return preview;
        }

        var result = await edits.ApplyAsync(edit, change.Snapshot, cancellationToken);
        result["synchronized"] = false;
        var phase = EditApplicationPhase.Notify;
        try
        {
            if (change.FileRename is not null)
            {
                await session.NotifyAsync(
                    LspMethods.WorkspaceDidRenameFiles,
                    change.FileRename,
                    cancellationToken
                );
            }

            var filesChanged = change.FileRename is not null || ChangesFileMembership(result);
            if (change.Command is not null)
            {
                phase = EditApplicationPhase.Command;
                result["commandExecuted"] = false;
                var commandResult = await ExecuteCommandAsync(
                    change.Command,
                    cancellationToken,
                    filesChanged
                );
                result = MergeApplicationResults(result, commandResult);
                result["commandExecuted"] = true;
                result["commandResult"] = commandResult["commandResult"]?.DeepClone();
            }
            else
            {
                phase = EditApplicationPhase.Synchronize;
                await RefreshAfterChangesAsync(filesChanged, cancellationToken);
            }
        }
        catch (EditApplicationException exception)
        {
            throw ApplicationFailure(
                MergeApplicationResults(result, exception.Result),
                exception.Phase,
                exception
            );
        }
        catch (Exception exception)
        {
            throw ApplicationFailure(result, phase, exception);
        }

        result["synchronized"] = true;
        return result;
    }

    public async Task<JsonObject> ExecuteCommandAsync(
        JsonObject command,
        CancellationToken cancellationToken,
        bool filesChanged = false
    )
    {
        var snapshot = await CaptureAsync(cancellationToken);
        var callbacks = new CommandEdits(edits, snapshot, CaptureAsync, cancellationToken);
        session.ApplyWorkspaceEditAsync = callbacks.ApplyAsync;
        JsonNode? commandResult = null;
        Exception? failure = null;
        var commandExecuted = false;
        try
        {
            commandResult = await session.RequestAsync(
                LspMethods.WorkspaceExecuteCommand,
                command,
                cancellationToken
            );
            commandExecuted = true;
        }
        catch (Exception exception)
        {
            failure = exception;
        }
        finally
        {
            session.ApplyWorkspaceEditAsync = null;
            await callbacks.DisposeAsync();
        }

        var result = callbacks.Result;
        result["commandExecuted"] = commandExecuted;
        result["synchronized"] = false;
        failure = callbacks.Failure ?? failure;
        if (failure is not null)
        {
            if (callbacks.HasChanges)
            {
                throw ApplicationFailure(result, EditApplicationPhase.Command, failure);
            }

            ExceptionDispatchInfo.Throw(failure);
        }

        try
        {
            await RefreshAfterChangesAsync(
                filesChanged || callbacks.ChangesFileMembership,
                cancellationToken
            );
        }
        catch (Exception exception)
        {
            throw ApplicationFailure(result, EditApplicationPhase.Synchronize, exception);
        }

        result["commandResult"] = commandResult;
        result["synchronized"] = true;
        return result;
    }

    internal static JsonObject MergeApplicationResults(JsonObject first, JsonObject second)
    {
        var files = new JsonArray();
        var paths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var result in new[] { first, second })
        {
            foreach (var file in result["files"]!.AsArray())
            {
                files.Add(file!.DeepClone());
                paths.Add(file["path"]!.GetValue<string>());
            }
        }

        var applied = first["applied"]!.GetValue<bool>() && second["applied"]!.GetValue<bool>();
        var merged = new JsonObject
        {
            ["applied"] = applied,
            ["partial"] = !applied && paths.Count != 0,
            ["fileCount"] = paths.Count,
            ["operationCount"] = files.Count,
            ["files"] = files,
        };
        if (second.TryGetPropertyValue("commandExecuted", out var commandExecuted))
        {
            merged["commandExecuted"] = commandExecuted?.DeepClone();
        }

        return merged;
    }

    private static EditApplicationException ApplicationFailure(
        JsonObject result,
        EditApplicationPhase phase,
        Exception exception
    )
    {
        result["synchronized"] = false;
        result["partial"] =
            !result["applied"]!.GetValue<bool>() && result["fileCount"]!.GetValue<int>() != 0;
        return exception is EditApplicationException application
            ? new EditApplicationException(
                result,
                application.Phase,
                application.FailedPath,
                application.FailedOperation,
                application.InnerException!
            )
            : new EditApplicationException(
                result,
                phase,
                failedPath: null,
                failedOperation: null,
                exception
            );
    }

    private static bool ChangesFileMembership(JsonObject result) =>
        result["files"] is JsonArray files
        && files.OfType<JsonObject>().Any(IsFileMembershipChange);

    internal static bool IsFileMembershipChange(JsonObject file) =>
        file["kind"]?.GetValue<string>()
            is WorkspaceEditService.CreateFileOperation
                or WorkspaceEditService.DeleteFileOperation
                or WorkspaceEditService.RenameFileOperation;

    private async Task RefreshAfterChangesAsync(
        bool filesChanged,
        CancellationToken cancellationToken
    )
    {
        if (filesChanged)
        {
            _pending.Clear();
            await session.ReloadAsync(cancellationToken);
        }
        else
        {
            await session.SynchronizeAsync(cancellationToken);
        }
    }

    private async Task<WorkspaceSnapshot> CaptureAsync(CancellationToken cancellationToken)
    {
        var snapshot = await edits.CaptureAsync(cancellationToken);
        await session.SynchronizeFilesAsync(snapshot.Fingerprints.Keys, cancellationToken);
        return snapshot with
        {
            DocumentVersions = session.DocumentVersions.ToDictionary(StringComparer.Ordinal),
        };
    }

    private string Remember(PendingChange change)
    {
        foreach (
            var key in _pending
                .Where(static pair =>
                    DateTimeOffset.UtcNow - pair.Value.CreatedAt > _proposalLifetime
                )
                .Select(static pair => pair.Key)
                .ToArray()
        )
        {
            _pending.Remove(key);
        }

        while (_pending.Count >= MaximumPendingProposals)
        {
            _pending.Remove(_pending.First().Key);
        }

        var id = Guid.NewGuid().ToString("N");
        _pending.Add(id, change);
        return id;
    }

    private static JsonNode EndPosition(LspRequest request, JsonNode start)
    {
        if (request.EndLine is null && request.EndCharacter is null)
        {
            return start.DeepClone();
        }

        if (
            request.EndLine is not > 0
            || request.EndCharacter is not > 0
            || request.EndLine < request.Line
            || (request.EndLine == request.Line && request.EndCharacter < request.Character)
        )
        {
            throw new ArgumentException(
                "The selection end must follow its start.",
                nameof(request)
            );
        }

        return new JsonObject
        {
            ["line"] = request.EndLine.Value - 1,
            ["character"] = request.EndCharacter.Value - 1,
        };
    }

    private static JsonArray ToDocumentChanges(JsonObject edit)
    {
        if (edit["documentChanges"] is JsonArray documentChanges)
        {
            return (JsonArray)documentChanges.DeepClone();
        }

        var result = new JsonArray();
        if (edit["changes"] is JsonObject changes)
        {
            foreach (var (uri, edits) in changes)
            {
                result.Add(
                    (JsonNode)
                        new JsonObject
                        {
                            ["textDocument"] = new JsonObject { ["uri"] = uri, ["version"] = null },
                            ["edits"] = edits?.DeepClone(),
                        }
                );
            }
        }

        return result;
    }
}
