#!/usr/bin/env bash
set -euo pipefail

case "$RUNNER_OS" in
  Linux)
    sudo apt-get update
    sudo apt-get install --yes clang zlib1g-dev
    ;;
  Windows)
    choco install make --yes --no-progress
    ;;
esac
