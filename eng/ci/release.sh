#!/usr/bin/env bash
set -euo pipefail

cd artifacts
sha256sum roslyn-codex-lsp-* > SHA256SUMS

options=(--verify-tag --generate-notes)
if [[ "$RELEASE_TAG" == *-* ]]; then
  options+=(--prerelease --latest=false)
fi
gh release create "$RELEASE_TAG" roslyn-codex-lsp-* SHA256SUMS "${options[@]}"
