#!/usr/bin/env bash
set -euo pipefail

repository="thomas-fazzari/roslyn-codex-lsp"
revision="master"
source_directory=""
install_directory="${XDG_DATA_HOME:-$HOME/.local/share}/roslyn-codex-lsp"
assume_yes=false
install_skill=true
skill_directory="${CODEX_HOME:-$HOME/.codex}/skills/roslyn-lsp"

usage() {
  printf '%s\n' 'Usage: bash install.sh [--yes] [--source DIRECTORY] [--ref REVISION] [--install-dir DIRECTORY]'
}

while (($#)); do
  case "$1" in
    --help|-h) usage; exit 0 ;;
    --yes) assume_yes=true; shift ;;
    --source|--ref|--install-dir)
      if (($# < 2)) || [[ -z "$2" ]]; then usage >&2; exit 2; fi
      case "$1" in
        --source) source_directory="$2" ;;
        --ref) revision="$2" ;;
        --install-dir) install_directory="$2" ;;
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
  printf '📚 Install the Codex usage skill? [Y/n]: ' >&3
  IFS= read -r answer <&3
  case "$answer" in
    n|N|no|NO) install_skill=false ;;
    ''|y|Y|yes|YES) ;;
    *) printf 'Expected yes or no.\n' >&2; exit 2 ;;
  esac

  printf '\nBridge and Roslyn: %s\n' "$install_directory"
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

for command in dotnet codex; do
  command -v "$command" >/dev/null || { printf 'Missing required command: %s\n' "$command" >&2; exit 1; }
done

# Use the runtime host, even when PATH points to an SDK launcher
dotnet_command="$(command -v dotnet)"
dotnet_directory="$("$dotnet_command" --list-runtimes | sed -n 's/^Microsoft.NETCore.App 10\.[^ ]* \[\(.*\)\/shared\/Microsoft.NETCore.App\]$/\1/p' | tail -n 1)"
if [[ ! -x "$dotnet_directory/dotnet" ]]; then
  printf 'Could not locate the .NET 10 runtime host. Check dotnet --list-runtimes.\n' >&2
  exit 1
fi
dotnet_command="$dotnet_directory/dotnet"

temporary_directory="$(mktemp -d)"
trap 'rm -rf "$temporary_directory"' EXIT

if [[ -z "$source_directory" ]]; then
  for command in curl tar; do
    command -v "$command" >/dev/null || { printf 'Missing required command: %s\n' "$command" >&2; exit 1; }
  done
  curl --fail --silent --show-error --location \
    "https://api.github.com/repos/$repository/tarball/$revision" \
    --output "$temporary_directory/source.tar.gz"
  mkdir "$temporary_directory/source"
  tar -xzf "$temporary_directory/source.tar.gz" -C "$temporary_directory/source" --strip-components=1
  source_directory="$temporary_directory/source"
fi
source_directory="$(cd "$source_directory" && pwd -P)"
mkdir -p "$install_directory"
install_directory="$(cd "$install_directory" && pwd -P)"
server_directory="$install_directory/roslyn"

printf '🔨 Building Roslyn Codex LSP...\n'
(
  cd "$source_directory"
  "$dotnet_command" publish src/RoslynCodexLsp/RoslynCodexLsp.csproj \
    --configuration Release --output "$temporary_directory/bridge" --nologo
)

printf '📦 Installing the Roslyn language server...\n'
(
  cd "$source_directory"
  "$dotnet_command" tool update roslyn-language-server --prerelease \
    --tool-path "$server_directory" --source https://api.nuget.org/v3/index.json
)

mkdir -p "$install_directory/bridge"
cp -R "$temporary_directory/bridge/." "$install_directory/bridge/"
if [[ "$install_skill" == true ]]; then
  mkdir -p "$skill_directory"
  cp "$source_directory/skills/roslyn-lsp/SKILL.md" "$skill_directory/SKILL.md"
fi

printf '🔗 Registering the global Codex MCP server...\n'
codex mcp add roslyn \
  --env "PATH=$dotnet_directory:$server_directory:$PATH" \
  --env "DOTNET_ROOT=$dotnet_directory" \
  -- "$dotnet_command" "$install_directory/bridge/RoslynCodexLsp.dll" \
  --server "$server_directory/roslyn-language-server"

printf '\n✅ Installed in %s\n' "$install_directory"
if [[ "$install_skill" == true ]]; then printf 'Skill installed in %s\n' "$skill_directory"; fi
printf 'Open a new Codex session in a C# project and check /mcp.\n'
