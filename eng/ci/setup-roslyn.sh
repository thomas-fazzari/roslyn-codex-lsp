#!/usr/bin/env bash
set -euo pipefail

dotnet tool install roslyn-language-server --version 5.12.0-1.26426.8 \
  --tool-path "$RUNNER_TEMP/roslyn" --source https://api.nuget.org/v3/index.json
printf '%s\n' "$RUNNER_TEMP/roslyn" >> "$GITHUB_PATH"
