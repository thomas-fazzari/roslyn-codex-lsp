// Copyright (C) 2026 thomas-fazzari
// SPDX-License-Identifier: GPL-3.0-only

using System.Text.Json.Nodes;

namespace RoslynCodexLsp.Lsp;

/// <summary>
/// Describes the standard LSP features implemented by this bridge.
/// </summary>
internal static class ClientCapabilities
{
    public static JsonObject Create(WorkspacePaths paths)
    {
        var capabilities = JsonNode.Parse(
            """
            {
              "workspace": {
                "configuration": true,
                "workspaceFolders": true,
                "applyEdit": true,
                "didChangeWatchedFiles": { "dynamicRegistration": true },
                "fileOperations": { "willRename": true, "didRename": true },
                "workspaceEdit": {
                  "documentChanges": true,
                  "resourceOperations": ["create", "rename", "delete"],
                  "failureHandling": "abort"
                }
              },
              "textDocument": {
                "diagnostic": { "relatedDocumentSupport": false },
                "publishDiagnostics": { "relatedInformation": true, "versionSupport": true },
                "hover": { "contentFormat": ["markdown", "plaintext"] },
                "definition": { "linkSupport": true },
                "implementation": { "linkSupport": true },
                "typeDefinition": { "linkSupport": true },
                "documentSymbol": { "hierarchicalDocumentSymbolSupport": true },
                "rename": { "prepareSupport": true },
                "codeAction": {
                  "dataSupport": true,
                  "disabledSupport": true,
                  "resolveSupport": { "properties": ["edit", "command"] },
                  "codeActionLiteralSupport": {
                    "codeActionKind": {
                      "valueSet": ["quickfix", "refactor", "refactor.extract", "refactor.inline", "refactor.rewrite", "source", "source.organizeImports", "source.fixAll"]
                    }
                  }
                }
              },
              "general": { "positionEncodings": ["utf-16"] }
            }
            """
        );
        return new JsonObject
        {
            ["processId"] = Environment.ProcessId,
            ["rootUri"] = new Uri(paths.Root + Path.DirectorySeparatorChar).AbsoluteUri,
            ["workspaceFolders"] = WorkspaceFolders(paths),
            ["clientInfo"] = new JsonObject
            {
                ["name"] = "roslyn-codex-lsp",
                ["version"] = "1.0.0",
            },
            ["capabilities"] = capabilities,
        };
    }

    public static JsonArray WorkspaceFolders(WorkspacePaths paths) =>
        [
            new JsonObject
            {
                ["uri"] = new Uri(paths.Root + Path.DirectorySeparatorChar).AbsoluteUri,
                ["name"] = Path.GetFileName(paths.Root),
            },
        ];
}
