#!/usr/bin/env bash
set -euo pipefail

mkdir -p artifacts/package/skills/roslyn-lsp artifacts/release
cp "artifacts/native/$EXECUTABLE" LICENSE artifacts/package/
cp skills/roslyn-lsp/SKILL.md artifacts/package/skills/roslyn-lsp/

if [[ "$RUNNER_OS" == Windows ]]; then
  pwsh -NoProfile -NonInteractive -Command "Compress-Archive -Path artifacts/package/* -DestinationPath 'artifacts/release/roslyn-codex-lsp-$RUNTIME_ID.zip'"
else
  COPYFILE_DISABLE=1 tar -czf "artifacts/release/roslyn-codex-lsp-$RUNTIME_ID.tar.gz" -C artifacts/package .
fi
