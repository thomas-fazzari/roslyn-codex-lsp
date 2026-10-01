// Copyright (C) 2026 thomas-fazzari
// SPDX-License-Identifier: GPL-3.0-only

using System.Text.Json.Nodes;

namespace Roslyn4Clankers.Editing;

internal static class EditPreview
{
    /// <summary>
    /// Drops trailing files, then the command, until the preview fits in <paramref name="budget"/> characters.
    /// </summary>
    public static JsonObject Limit(JsonObject preview, int budget)
    {
        var filesTruncated = preview["filesTruncated"]?.GetValue<bool>() ?? false;
        var files = preview["files"]!.AsArray();
        preview.Remove("files");
        var included = new JsonArray();
        preview["files"] = included;
        preview["previewTruncated"] ??= false;
        preview["filesTruncated"] = false;
        var remaining = budget - Length(preview);
        if (remaining < 0 && preview["command"] is not null)
        {
            preview.Remove("command");
            preview["commandPreviewTruncated"] = true;
            preview["previewTruncated"] = true;
            remaining = budget - Length(preview);
        }
        for (var index = 0; index < files.Count; index++)
        {
            var file = files[index]!;
            var length = Length(file) + (included.Count == 0 ? 0 : 1);
            if (length > remaining)
            {
                break;
            }

            files[index] = null;
            included.Add(file);
            remaining -= length;
        }

        preview["filesTruncated"] = filesTruncated || included.Count < files.Count;
        if (preview["filesTruncated"]!.GetValue<bool>())
        {
            preview["previewTruncated"] = true;
        }
        return preview;
    }

    private static int Length(JsonNode node) =>
        node.ToJsonString(BridgeJsonContext.Default.Options).Length;
}
