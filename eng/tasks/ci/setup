#!/usr/bin/env bash
#MISE description="Installs the native AOT build tools on the CI runner"
set -euo pipefail

case "$RUNNER_OS" in
  Linux)
    sudo apt-get update
    sudo apt-get install --yes clang zlib1g-dev
    ;;
esac
