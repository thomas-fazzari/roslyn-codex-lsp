#!/usr/bin/env bash
set -euo pipefail

dotnet tool install roslyn-language-server --version 5.12.0-1.26426.8 \
  --tool-path "$RUNNER_TEMP/roslyn" --source https://api.nuget.org/v3/index.json

server="$RUNNER_TEMP/roslyn/roslyn-language-server"
if [[ "$RUNNER_OS" == Windows ]]; then
  # The .NET tool launcher is a .cmd file
  # The bridge needs the packaged executable
  server="$(find "$RUNNER_TEMP/roslyn/.store" -type f -name roslyn-language-server.exe)"
  if [[ ! -f "$server" ]]; then
    printf 'Expected one Roslyn executable in the installed tool package.\n' >&2
    exit 1
  fi
  server="$(cygpath -w "$server")"
fi
printf 'ROSLYN_CODEX_TEST_SERVER=%s\n' "$server" >> "$GITHUB_ENV"
