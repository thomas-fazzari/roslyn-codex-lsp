// Copyright (C) 2026 thomas-fazzari
// SPDX-License-Identifier: GPL-3.0-only

namespace RoslynCodexLsp.Lsp;

/// <summary>
/// Wire names shared by LSP requests, callbacks and method permissions.
/// </summary>
internal static class LspMethods
{
    public const string Progress = "$/progress";
    public const string CallHierarchyIncomingCalls = "callHierarchy/incomingCalls";
    public const string CallHierarchyOutgoingCalls = "callHierarchy/outgoingCalls";
    public const string ClientRegisterCapability = "client/registerCapability";
    public const string ClientUnregisterCapability = "client/unregisterCapability";
    public const string CodeActionResolve = "codeAction/resolve";
    public const string CompletionItemResolve = "completionItem/resolve";
    public const string TextDocumentCodeAction = "textDocument/codeAction";
    public const string TextDocumentCompletion = "textDocument/completion";
    public const string TextDocumentDefinition = "textDocument/definition";
    public const string TextDocumentDiagnostic = "textDocument/diagnostic";
    public const string TextDocumentDidChange = "textDocument/didChange";
    public const string TextDocumentDidClose = "textDocument/didClose";
    public const string TextDocumentDidOpen = "textDocument/didOpen";
    public const string TextDocumentDocumentSymbol = "textDocument/documentSymbol";
    public const string TextDocumentFormatting = "textDocument/formatting";
    public const string TextDocumentHover = "textDocument/hover";
    public const string TextDocumentImplementation = "textDocument/implementation";
    public const string TextDocumentPrepareCallHierarchy = "textDocument/prepareCallHierarchy";
    public const string TextDocumentPrepareRename = "textDocument/prepareRename";
    public const string TextDocumentPrepareTypeHierarchy = "textDocument/prepareTypeHierarchy";
    public const string TextDocumentPublishDiagnostics = "textDocument/publishDiagnostics";
    public const string TextDocumentRangeFormatting = "textDocument/rangeFormatting";
    public const string TextDocumentReferences = "textDocument/references";
    public const string TextDocumentRename = "textDocument/rename";
    public const string TextDocumentSignatureHelp = "textDocument/signatureHelp";
    public const string TextDocumentTypeDefinition = "textDocument/typeDefinition";
    public const string TypeHierarchySubtypes = "typeHierarchy/subtypes";
    public const string TypeHierarchySupertypes = "typeHierarchy/supertypes";
    public const string WindowLogMessage = "window/logMessage";
    public const string WindowShowMessage = "window/showMessage";
    public const string WindowWorkDoneProgressCreate = "window/workDoneProgress/create";
    public const string WorkspaceApplyEdit = "workspace/applyEdit";
    public const string WorkspaceConfiguration = "workspace/configuration";
    public const string WorkspaceDiagnostic = "workspace/diagnostic";
    public const string WorkspaceDiagnosticRefresh = "workspace/diagnostic/refresh";
    public const string WorkspaceDidChangeWatchedFiles = "workspace/didChangeWatchedFiles";
    public const string WorkspaceDidRenameFiles = "workspace/didRenameFiles";
    public const string WorkspaceExecuteCommand = "workspace/executeCommand";
    public const string WorkspaceProjectInitializationComplete =
        "workspace/projectInitializationComplete";
    public const string WorkspaceSymbol = "workspace/symbol";
    public const string WorkspaceWillRenameFiles = "workspace/willRenameFiles";
    public const string WorkspaceWorkspaceFolders = "workspace/workspaceFolders";
    public const string WorkspaceSymbolResolve = "workspaceSymbol/resolve";
    public const string Initialize = "initialize";
    public const string Initialized = "initialized";
    public const string Shutdown = "shutdown";
    public const string Exit = "exit";
}
