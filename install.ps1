#Requires -Version 5.1
<#
.SYNOPSIS
Installs the native Roslyn Codex LSP bridge and registers it in Codex and Claude Code.
.PARAMETER Client
Clients to set up: codex, claude or both. Defaults to every installed client.
.PARAMETER Server
Uses an existing language server executable and skips its installation.
The bridge does not require .NET. Installing Roslyn requires the .NET 10 SDK.
#>
[CmdletBinding()]
param(
    [string] $Version,
    [string] $InstallDir = (Join-Path $env:LOCALAPPDATA 'roslyn-codex-lsp'),
    [string] $Server,
    [ValidateSet('codex', 'claude')]
    [string[]] $Client,
    [switch] $Yes,
    [switch] $NoSkill
)

$ErrorActionPreference = 'Stop'
$repository = 'thomas-fazzari/roslyn-codex-lsp'
$installSkill = -not $NoSkill
$codexHome = if ($env:CODEX_HOME) { $env:CODEX_HOME } else { Join-Path $HOME '.codex' }
$claudeHome = if ($env:CLAUDE_CONFIG_DIR) { $env:CLAUDE_CONFIG_DIR } else { Join-Path $HOME '.claude' }
$clientNames = @{ codex = 'Codex'; claude = 'Claude Code' }
$skillDirectories = @{
    codex = Join-Path $codexHome 'skills/roslyn-lsp'
    claude = Join-Path $claudeHome 'skills/roslyn-lsp'
}

if (-not $Client) {
    $Client = @('codex', 'claude' | Where-Object { Get-Command $_ -CommandType Application -ErrorAction SilentlyContinue })
}
if (-not $Client) {
    throw 'Install Codex or Claude Code first.'
}
$clientCommands = @{}
foreach ($name in $Client) {
    $command = Get-Command $name -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $command) { throw "Missing the $($clientNames[$name]) CLI." }
    $clientCommands[$name] = $command.Source
}

if (-not $Yes) {
    Write-Host "👋 Roslyn Codex LSP`n"
    $answer = Read-Host "📁 Installation directory [$InstallDir]"
    if ($answer) { $InstallDir = $answer }
    if (-not $Server) {
        $answer = Read-Host '📦 Install the Roslyn language server (requires .NET 10 SDK)? [Y/n]'
        switch -Regex ($answer) {
            '^(n|no)$' {
                $answer = Read-Host '🔎 Existing server executable [roslyn-language-server]'
                $Server = if ($answer) { $answer } else { 'roslyn-language-server' }
            }
            '^(y|yes)?$' { }
            default { throw 'Expected yes or no.' }
        }
    }
    if ($installSkill) {
        $answer = Read-Host '📚 Install the usage skill? [Y/n]'
        switch -Regex ($answer) {
            '^(n|no)$' { $installSkill = $false }
            '^(y|yes)?$' { }
            default { throw 'Expected yes or no.' }
        }
    }

    Write-Host "`nBridge: $InstallDir"
    if ($Server) {
        Write-Host "Roslyn: use $Server"
    }
    else {
        Write-Host "Roslyn: install in $InstallDir\roslyn (.NET 10 SDK required)"
    }
    Write-Host ('Version: ' + $(if ($Version) { $Version } else { 'latest stable' }))
    foreach ($name in $Client) {
        Write-Host "$($clientNames[$name]): global MCP server named roslyn, using each session workspace"
        if ($installSkill) { Write-Host "$($clientNames[$name]) skill: $($skillDirectories[$name])" }
    }
    Write-Host 'Existing installations and the roslyn registration will be updated.'
    $answer = Read-Host "`n🚀 Continue? [Y/n]"
    switch -Regex ($answer) {
        '^(y|yes)?$' { }
        '^(n|no)$' { Write-Host 'Installation canceled.'; return }
        default { throw 'Expected yes or no.' }
    }
}

if ($env:OS -ne 'Windows_NT' -or [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture -ne 'X64') {
    throw 'This installer requires Windows x64. Use install.sh for macOS and Linux.'
}
$installRoslyn = -not $Server
if ($Server) {
    $Server = (Get-Command $Server -CommandType Application).Source
}
else {
    $dotnet = Get-Command dotnet -CommandType Application -ErrorAction SilentlyContinue
    if (-not $dotnet) {
        throw 'Installing Roslyn requires the .NET 10 SDK. Use -Server for an existing server.'
    }
    $dotnetCommand = $dotnet.Source
    $sdks = & $dotnetCommand --list-sdks
    if ($LASTEXITCODE -ne 0 -or -not ($sdks -match '^10\.')) {
        throw 'Installing Roslyn requires the .NET 10 SDK. Use -Server for an existing server.'
    }
    $runtimes = & $dotnetCommand --list-runtimes
    if ($LASTEXITCODE -ne 0) { throw 'Could not list .NET runtimes.' }
    $dotnetDirectory = $runtimes | ForEach-Object {
        if ($_ -match '^Microsoft\.NETCore\.App 10\.[^ ]* \[(.+)\\shared\\Microsoft\.NETCore\.App\]$') {
            $Matches[1]
        }
    } | Select-Object -Last 1
    if (-not $dotnetDirectory -or -not (Test-Path -LiteralPath (Join-Path $dotnetDirectory 'dotnet.exe'))) {
        throw 'Could not locate the .NET 10 runtime host. Check dotnet --list-runtimes.'
    }
    $dotnetCommand = Join-Path $dotnetDirectory 'dotnet.exe'
}

$temporaryDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ('roslyn-codex-lsp-' + [guid]::NewGuid())
[System.IO.Directory]::CreateDirectory($temporaryDirectory) | Out-Null
try {
    if (-not $Version) {
        Add-Type -AssemblyName System.Net.Http
        $client = [System.Net.Http.HttpClient]::new()
        try {
            $response = $client.GetAsync(
                "https://github.com/$repository/releases/latest",
                [System.Net.Http.HttpCompletionOption]::ResponseHeadersRead
            ).GetAwaiter().GetResult()
            try {
                $response.EnsureSuccessStatusCode() | Out-Null
                $Version = $response.RequestMessage.RequestUri.Segments[-1]
            }
            finally { $response.Dispose() }
        }
        finally { $client.Dispose() }
    }
    if ($Version -cnotmatch '^v[0-9]+\.[0-9]+\.[0-9]+(-[A-Za-z0-9.-]+)?$') {
        throw "Expected a release version such as v1.0.0, received: $Version"
    }

    $asset = 'roslyn-codex-lsp-win-x64.zip'
    $downloadUrl = "https://github.com/$repository/releases/download/$Version"
    $archive = Join-Path $temporaryDirectory $asset
    $checksums = Join-Path $temporaryDirectory 'SHA256SUMS'
    Write-Host "📥 Downloading Roslyn Codex LSP $Version (win-x64)..."
    Invoke-WebRequest -UseBasicParsing -Uri "$downloadUrl/$asset" -OutFile $archive
    Invoke-WebRequest -UseBasicParsing -Uri "$downloadUrl/SHA256SUMS" -OutFile $checksums

    $expectedHashes = @(Get-Content -LiteralPath $checksums | ForEach-Object {
        if ($_ -match ('^([A-Fa-f0-9]{64})\s+' + [regex]::Escape($asset) + '$')) { $Matches[1] }
    })
    if ($expectedHashes.Count -ne 1) { throw "The release checksum is missing or invalid for $asset." }
    $actualHash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash
    if ($actualHash -ne $expectedHashes[0]) { throw "Checksum verification failed for $asset." }
    Write-Host '🔒 Checksum verified.'
    $extractedDirectory = Join-Path $temporaryDirectory 'bridge'
    Expand-Archive -LiteralPath $archive -DestinationPath $extractedDirectory
    $executable = Join-Path $extractedDirectory 'RoslynCodexLsp.exe'
    if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
        throw 'The release archive does not contain the native executable.'
    }

    if ($installSkill) {
        $skillFile = Join-Path $temporaryDirectory 'SKILL.md'
        $skillMetadata = Join-Path $temporaryDirectory 'openai.yaml'
        Write-Host '📚 Downloading the latest usage skill...'
        Invoke-WebRequest -UseBasicParsing `
            -Uri "https://raw.githubusercontent.com/$repository/master/skills/roslyn-lsp/SKILL.md" `
            -OutFile $skillFile
        Invoke-WebRequest -UseBasicParsing `
            -Uri "https://raw.githubusercontent.com/$repository/master/skills/roslyn-lsp/agents/openai.yaml" `
            -OutFile $skillMetadata
    }

    $InstallDir = [System.IO.Path]::GetFullPath($InstallDir)
    $bridgeDirectory = Join-Path $InstallDir 'bridge'
    $environmentArguments = @()
    if ($installRoslyn) {
        $serverDirectory = Join-Path $InstallDir 'roslyn'
        Write-Host '📦 Installing the Roslyn language server...'
        Push-Location $temporaryDirectory
        try {
            & $dotnetCommand tool update roslyn-language-server --prerelease `
                --tool-path $serverDirectory --source https://api.nuget.org/v3/index.json
            if ($LASTEXITCODE -ne 0) { throw 'The Roslyn language server installation failed.' }
        }
        finally { Pop-Location }
        # The .NET tool launcher is a .cmd file
        # The bridge needs the packaged executable
        $serverExecutables = @(Get-ChildItem (Join-Path $serverDirectory '.store') `
            -Recurse -File -Filter 'roslyn-language-server.exe')
        if ($serverExecutables.Count -ne 1) {
            throw 'Expected one Roslyn executable in the installed tool package.'
        }
        $Server = $serverExecutables[0].FullName
        $environmentArguments = @('--env', "PATH=$dotnetDirectory;$serverDirectory;$env:PATH", '--env', "DOTNET_ROOT=$dotnetDirectory")
    }

    [System.IO.Directory]::CreateDirectory($bridgeDirectory) | Out-Null
    $installedExecutable = Join-Path $bridgeDirectory 'RoslynCodexLsp.exe'
    Copy-Item -LiteralPath $executable -Destination $installedExecutable -Force
    Copy-Item -LiteralPath (Join-Path $extractedDirectory 'LICENSE') -Destination $bridgeDirectory -Force
    if ($installSkill) {
        foreach ($name in $Client) {
            $skillDirectory = $skillDirectories[$name]
            [System.IO.Directory]::CreateDirectory($skillDirectory) | Out-Null
            Copy-Item -LiteralPath $skillFile -Destination (Join-Path $skillDirectory 'SKILL.md') -Force
            # Only Codex reads the interface metadata
            if ($name -eq 'codex') {
                $skillAgentsDirectory = Join-Path $skillDirectory 'agents'
                [System.IO.Directory]::CreateDirectory($skillAgentsDirectory) | Out-Null
                Copy-Item -LiteralPath $skillMetadata -Destination (Join-Path $skillAgentsDirectory 'openai.yaml') -Force
            }
        }
    }

    foreach ($name in $Client) {
        Write-Host "🔗 Registering the global $($clientNames[$name]) MCP server..."
        if ($name -eq 'codex') {
            & $clientCommands[$name] mcp add roslyn @environmentArguments -- $installedExecutable --server $Server
        }
        else {
            # Claude Code keeps an existing server instead of replacing it
            & $clientCommands[$name] mcp remove roslyn --scope user 2>$null | Out-Null
            # The scope option ends the list of environment values before the server name
            & $clientCommands[$name] mcp add @environmentArguments --scope user roslyn -- $installedExecutable --server $Server
        }
        if ($LASTEXITCODE -ne 0) { throw "The $($clientNames[$name]) MCP registration failed." }
    }

    Write-Host "`n✅ Installed $Version in $InstallDir"
    if ($installSkill) {
        foreach ($name in $Client) { Write-Host "$($clientNames[$name]) skill installed in $($skillDirectories[$name])" }
    }
    Write-Host 'Open a new session in a C# project and check /mcp.'
}
finally {
    Remove-Item -LiteralPath $temporaryDirectory -Recurse -Force
}
