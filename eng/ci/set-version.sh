#!/usr/bin/env bash
set -euo pipefail

version=0.0.0-ci
if [[ "$GITHUB_REF_TYPE" == tag ]]; then
  if [[ ! "$GITHUB_REF_NAME" =~ ^v(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(-[0-9A-Za-z]+([.-][0-9A-Za-z]+)*)?$ ]]; then
    printf 'Expected a version tag such as v1.2.3 or v1.2.3-rc.1.\n' >&2
    exit 1
  fi
  version="${GITHUB_REF_NAME#v}"
fi
printf 'APP_VERSION=%s\n' "$version" >> "$GITHUB_ENV"
