#!/usr/bin/env bash
set -euo pipefail

repository="thomas-fazzari/roslyn-codex-lsp"
version=""
server_command=""
install_directory="${XDG_DATA_HOME:-$HOME/.local/share}/roslyn-codex-lsp"
assume_yes=false
install_skill=true
skill_directory="${CODEX_HOME:-$HOME/.codex}/skills/roslyn-lsp"

usage() {
  printf '%s\n' \
    'Usage: bash install.sh [--yes] [--no-skill] [--version vX.Y.Z] [--install-dir DIRECTORY] [--server EXECUTABLE]' \
    'The native bridge does not require .NET. Installing Roslyn requires the .NET 10 SDK.' \
    'Use --server to reuse an existing language server and skip its installation.'
}

while (($#)); do
  case "$1" in
    --help|-h) usage; exit 0 ;;
    --yes) assume_yes=true; shift ;;
    --no-skill) install_skill=false; shift ;;
    --version|--install-dir|--server)
      if (($# < 2)) || [[ -z "$2" ]]; then usage >&2; exit 2; fi
      case "$1" in
        --version) version="$2" ;;
        --install-dir) install_directory="$2" ;;
        --server) server_command="$2" ;;
      esac
      shift 2
      ;;
    *) usage >&2; exit 2 ;;
  esac
done

if [[ "$assume_yes" == false ]]; then
  # Read from the terminal even when the script itself arrives through a pipe
  if ! { exec 3<>/dev/tty; } 2>/dev/null; then
    printf 'No terminal available. Run with --yes for unattended installation.\n' >&2
    exit 1
  fi
  printf '👋 Roslyn Codex LSP\n\n'
  printf '📁 Installation directory [%s]: ' "$install_directory" >&3
  IFS= read -r answer <&3
  install_directory="${answer:-$install_directory}"
  if [[ -z "$server_command" ]]; then
    printf '📦 Install the Roslyn language server (requires .NET 10 SDK)? [Y/n]: ' >&3
    IFS= read -r answer <&3
    case "$answer" in
      n|N|no|NO)
        printf '🔎 Existing server executable [roslyn-language-server]: ' >&3
        IFS= read -r answer <&3
        server_command="${answer:-roslyn-language-server}"
        ;;
      ''|y|Y|yes|YES) ;;
      *) printf 'Expected yes or no.\n' >&2; exit 2 ;;
    esac
  fi
  if [[ "$install_skill" == true ]]; then
    printf '📚 Install the Codex usage skill? [Y/n]: ' >&3
    IFS= read -r answer <&3
    case "$answer" in
      n|N|no|NO) install_skill=false ;;
      ''|y|Y|yes|YES) ;;
      *) printf 'Expected yes or no.\n' >&2; exit 2 ;;
    esac
  fi

  printf '\nBridge: %s\n' "$install_directory"
  if [[ -n "$server_command" ]]; then
    printf 'Roslyn: use %s\n' "$server_command"
  else
    printf 'Roslyn: install in %s/roslyn (.NET 10 SDK required)\n' "$install_directory"
  fi
  printf 'Version: %s\n' "${version:-latest stable}"
  printf 'Codex: global MCP server named roslyn, using each session workspace\n'
  if [[ "$install_skill" == true ]]; then printf 'Skill: %s\n' "$skill_directory"; fi
  printf 'Existing installations and the roslyn registration will be updated.\n'
  printf '\n🚀 Continue? [Y/n]: ' >&3
  IFS= read -r answer <&3
  case "$answer" in
    ''|y|Y|yes|YES) ;;
    n|N|no|NO) printf 'Installation canceled.\n'; exit 0 ;;
    *) printf 'Expected yes or no.\n' >&2; exit 2 ;;
  esac
  exec 3>&-
fi

for command in codex curl tar; do
  command -v "$command" >/dev/null || { printf 'Missing required command: %s\n' "$command" >&2; exit 1; }
done

case "$(uname -s)/$(uname -m)" in
  Darwin/arm64) runtime_identifier="osx-arm64" ;;
  Darwin/x86_64) runtime_identifier="osx-x64" ;;
  Linux/aarch64|Linux/arm64) runtime_identifier="linux-arm64" ;;
  Linux/x86_64) runtime_identifier="linux-x64" ;;
  *) printf 'Unsupported platform. Use install.ps1 for Windows x64.\n' >&2; exit 1 ;;
esac

install_roslyn=false
if [[ -n "$server_command" ]]; then
  server_path="$(command -v "$server_command")" || {
    printf 'Could not find the language server executable: %s\n' "$server_command" >&2
    exit 1
  }
  if [[ ! -f "$server_path" || ! -x "$server_path" ]]; then
    printf 'The language server must be an executable file: %s\n' "$server_path" >&2
    exit 1
  fi
  server_command="$(cd "$(dirname "$server_path")" && pwd -P)/$(basename "$server_path")"
else
  install_roslyn=true
  # Use the runtime host, even when PATH points to an SDK launcher
  dotnet_command="$(command -v dotnet)" || {
    printf 'Installing Roslyn requires the .NET 10 SDK. Use --server for an existing server.\n' >&2
    exit 1
  }
  if [[ -z "$("$dotnet_command" --list-sdks | sed -n '/^10\./p')" ]]; then
    printf 'Installing Roslyn requires the .NET 10 SDK. Use --server for an existing server.\n' >&2
    exit 1
  fi
  dotnet_directory="$("$dotnet_command" --list-runtimes | sed -n 's/^Microsoft.NETCore.App 10\.[^ ]* \[\(.*\)\/shared\/Microsoft.NETCore.App\]$/\1/p' | tail -n 1)"
  if [[ ! -x "$dotnet_directory/dotnet" ]]; then
    printf 'Could not locate the .NET 10 runtime host. Check dotnet --list-runtimes.\n' >&2
    exit 1
  fi
  dotnet_command="$dotnet_directory/dotnet"
fi

temporary_directory="$(mktemp -d)"
trap 'rm -rf "$temporary_directory"' EXIT

if [[ -z "$version" ]]; then
  release_url="$(curl --fail --silent --show-error --location --output /dev/null \
    --write-out '%{url_effective}' "https://github.com/$repository/releases/latest")"
  version="${release_url##*/}"
fi
if [[ ! "$version" =~ ^v[0-9]+\.[0-9]+\.[0-9]+(-[A-Za-z0-9.-]+)?$ ]]; then
  printf 'Expected a release version such as v1.0.0, received: %s\n' "$version" >&2
  exit 2
fi

asset="roslyn-codex-lsp-$runtime_identifier.tar.gz"
download_url="https://github.com/$repository/releases/download/$version"
printf '📥 Downloading Roslyn Codex LSP %s (%s)...\n' "$version" "$runtime_identifier"
curl --fail --silent --show-error --location "$download_url/$asset" \
  --output "$temporary_directory/$asset"
curl --fail --silent --show-error --location "$download_url/SHA256SUMS" \
  --output "$temporary_directory/SHA256SUMS"

expected_hash="$(awk -v asset="$asset" '$2 == asset { print $1 }' "$temporary_directory/SHA256SUMS")"
if [[ ! "$expected_hash" =~ ^[[:xdigit:]]{64}$ ]]; then
  printf 'The release checksum is missing or invalid for %s.\n' "$asset" >&2
  exit 1
fi
if command -v sha256sum >/dev/null; then
  actual_hash="$(sha256sum "$temporary_directory/$asset")"
else
  actual_hash="$(shasum -a 256 "$temporary_directory/$asset")"
fi
if [[ "${actual_hash%% *}" != "$expected_hash" ]]; then
  printf 'Checksum verification failed for %s.\n' "$asset" >&2
  exit 1
fi
printf '🔒 Checksum verified.\n'
mkdir "$temporary_directory/bridge"
tar -xzf "$temporary_directory/$asset" -C "$temporary_directory/bridge"
if [[ ! -x "$temporary_directory/bridge/RoslynCodexLsp" ]]; then
  printf 'The release archive does not contain the native executable.\n' >&2
  exit 1
fi

if [[ "$install_skill" == true ]]; then
  printf '📚 Downloading the latest Codex skill...\n'
  curl --fail --silent --show-error --location \
    "https://raw.githubusercontent.com/$repository/master/skills/roslyn-lsp/SKILL.md" \
    --output "$temporary_directory/SKILL.md"
  curl --fail --silent --show-error --location \
    "https://raw.githubusercontent.com/$repository/master/skills/roslyn-lsp/agents/openai.yaml" \
    --output "$temporary_directory/openai.yaml"
fi

mkdir -p "$install_directory"
install_directory="$(cd "$install_directory" && pwd -P)"
bridge_directory="$install_directory/bridge"

mcp_arguments=(mcp add roslyn)
if [[ "$install_roslyn" == true ]]; then
  server_directory="$install_directory/roslyn"
  server_command="$server_directory/roslyn-language-server"
  printf '📦 Installing the Roslyn language server...\n'
  (
    # Ignore SDK pins and NuGet settings in the current project
    cd "$temporary_directory"
    "$dotnet_command" tool update roslyn-language-server --prerelease \
      --tool-path "$server_directory" --source https://api.nuget.org/v3/index.json
  )
  mcp_arguments+=(--env "PATH=$dotnet_directory:$server_directory:$PATH" --env "DOTNET_ROOT=$dotnet_directory")
fi

mkdir -p "$bridge_directory"
# Replace the executable by rename so running sessions can keep their loaded binary
cp "$temporary_directory/bridge/RoslynCodexLsp" "$bridge_directory/RoslynCodexLsp.new"
mv -f "$bridge_directory/RoslynCodexLsp.new" "$bridge_directory/RoslynCodexLsp"
cp "$temporary_directory/bridge/LICENSE" "$bridge_directory/LICENSE"
if [[ "$install_skill" == true ]]; then
  mkdir -p "$skill_directory/agents"
  cp "$temporary_directory/SKILL.md" "$skill_directory/SKILL.md"
  cp "$temporary_directory/openai.yaml" "$skill_directory/agents/openai.yaml"
fi

printf '🔗 Registering the global Codex MCP server...\n'
codex "${mcp_arguments[@]}" \
  -- "$bridge_directory/RoslynCodexLsp" \
  --server "$server_command"

printf '\n✅ Installed %s in %s\n' "$version" "$install_directory"
if [[ "$install_skill" == true ]]; then printf 'Skill installed in %s\n' "$skill_directory"; fi
printf 'Open a new Codex session in a C# project and check /mcp.\n'
