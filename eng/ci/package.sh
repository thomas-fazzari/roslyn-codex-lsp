#!/usr/bin/env bash
set -euo pipefail

mkdir -p artifacts/package artifacts/release
cp "artifacts/native/$EXECUTABLE" LICENSE artifacts/package/

if [[ "$RUNNER_OS" == Windows ]]; then
  pwsh -NoProfile -NonInteractive -Command "Compress-Archive -Path artifacts/package/* -DestinationPath 'artifacts/release/roslyn-codex-lsp-$RUNTIME_ID.zip'"
else
  COPYFILE_DISABLE=1 tar -czf "artifacts/release/roslyn-codex-lsp-$RUNTIME_ID.tar.gz" -C artifacts/package .
fi
