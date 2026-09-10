set working-directory := ".."
set shell := ["bash", "-eu", "-o", "pipefail", "-c"]
set windows-shell := ["bash", "-eu", "-o", "pipefail", "-c"]

BUN := env("BUN", "bun")
DOTNET := env("DOTNET", "dotnet")
EXTRA_PATH := env("EXTRA_PATH", "")
export PATH := if EXTRA_PATH == "" { env("PATH") } else { EXTRA_PATH + ":" + env("PATH") }
