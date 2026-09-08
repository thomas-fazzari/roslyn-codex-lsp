// Copyright (C) 2026 thomas-fazzari
// SPDX-License-Identifier: GPL-3.0-only

using System.Text;
using System.Text.Json.Nodes;

namespace RoslynCodexLsp.Editing;

internal sealed class EditedDocument(string path, byte[]? original)
{
    internal const int MaximumPreviewCharacters = 2_000;

    private static readonly Encoding _utf8 = new UTF8Encoding(
        encoderShouldEmitUTF8Identifier: true,
        throwOnInvalidBytes: true
    );
    private static readonly Encoding _utf16LittleEndian = new UnicodeEncoding(
        bigEndian: false,
        byteOrderMark: true,
        throwOnInvalidBytes: true
    );
    private static readonly Encoding _utf16BigEndian = new UnicodeEncoding(
        bigEndian: true,
        byteOrderMark: true,
        throwOnInvalidBytes: true
    );
    private static readonly Encoding _utf32LittleEndian = new UTF32Encoding(
        bigEndian: false,
        byteOrderMark: true,
        throwOnInvalidCharacters: true
    );
    private static readonly Encoding _utf32BigEndian = new UTF32Encoding(
        bigEndian: true,
        byteOrderMark: true,
        throwOnInvalidCharacters: true
    );

    public string Path { get; set; } = path;

    public string OriginalPath { get; } = path;

    public bool CaseOnlyRename => !StringComparer.Ordinal.Equals(Path, OriginalPath);

    public byte[]? Original { get; } = original;

    public byte[]? Content { get; set; } = original;

    public bool Changed => CaseOnlyRename || !SameBytes(Original, Content);

    public void Apply(JsonArray edits)
    {
        var content =
            Content ?? throw new InvalidOperationException($"The file does not exist: {Path}");
        var (encoding, preamble) = GetEncoding(content);
        var source = encoding.GetString(content, preamble, content.Length - preamble);
        var result = TextEdits.Apply(source, edits);
        var length = encoding.GetByteCount(result);
        if (length + preamble > WorkspaceEditService.MaximumFileBytes)
        {
            throw new InvalidOperationException($"The edited file exceeds the size limit: {Path}");
        }

        var bytes = new byte[length + preamble];
        content.AsSpan(0, preamble).CopyTo(bytes);
        encoding.GetBytes(result.AsSpan(), bytes.AsSpan(preamble));
        Content = bytes;
    }

    public JsonObject Describe(string root)
    {
        var preview = TextPreview.Create(Decode(Original), Decode(Content));
        return new JsonObject
        {
            ["path"] = System.IO.Path.GetRelativePath(root, Path),
            ["oldPath"] = CaseOnlyRename
                ? System.IO.Path.GetRelativePath(root, OriginalPath)
                : null,
            ["kind"] = (Original, Content, CaseOnlyRename) switch
            {
                (null, _, _) => WorkspaceEditService.CreateFileOperation,
                (_, null, _) => WorkspaceEditService.DeleteFileOperation,
                (_, _, true) => WorkspaceEditService.RenameFileOperation,
                _ => WorkspaceEditService.ChangeFileOperation,
            },
            ["beforeBytes"] = Original?.Length ?? 0,
            ["afterBytes"] = Content?.Length ?? 0,
            ["changes"] = preview.Changes,
            ["previewTruncated"] = preview.Truncated,
        };
    }

    private static string Decode(byte[]? content)
    {
        if (content is null)
        {
            return string.Empty;
        }

        var (encoding, preamble) = GetEncoding(content);
        return encoding.GetString(content, preamble, content.Length - preamble);
    }

    private static (Encoding Encoding, int Preamble) GetEncoding(byte[] content)
    {
        ReadOnlySpan<byte> bytes = content;
        if (bytes.StartsWith(_utf32BigEndian.Preamble))
        {
            return (_utf32BigEndian, _utf32BigEndian.Preamble.Length);
        }

        if (bytes.StartsWith(_utf32LittleEndian.Preamble))
        {
            return (_utf32LittleEndian, _utf32LittleEndian.Preamble.Length);
        }

        if (bytes.StartsWith(_utf8.Preamble))
        {
            return (_utf8, _utf8.Preamble.Length);
        }

        if (bytes.StartsWith(_utf16LittleEndian.Preamble))
        {
            return (_utf16LittleEndian, _utf16LittleEndian.Preamble.Length);
        }

        return bytes.StartsWith(_utf16BigEndian.Preamble)
            ? (_utf16BigEndian, _utf16BigEndian.Preamble.Length)
            : (_utf8, 0);
    }

    private static bool SameBytes(byte[]? left, byte[]? right)
    {
        return left is null
            ? right is null
            : right is not null && left.AsSpan().SequenceEqual(right);
    }
}
