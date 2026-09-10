import "common.just"

# Restore repository tooling dependencies.
install:
    "{{ BUN }}" install --frozen-lockfile

# Lint staged files only.
staged:
    "{{ BUN }}" eng/scripts/lint.mjs staged

# Lint local changes and untracked files.
changed:
    "{{ BUN }}" eng/scripts/lint.mjs changed

# Lint staged files, or local changes if none are staged.
auto:
    "{{ BUN }}" eng/scripts/lint.mjs auto

# Check task recipes and tooling formatting.
check:
    for file in justfile eng/*.just; do just --justfile "$file" --fmt --check || exit; done
    "{{ BUN }}" --bun run tooling:format:check

# Format task recipes and tooling.
format:
    for file in justfile eng/*.just; do just --justfile "$file" --fmt || exit; done
    "{{ BUN }}" --bun run tooling:format

# Enable repository Git hooks in this checkout.
hooks-install:
    chmod +x .githooks/pre-commit
    git config --local core.hooksPath .githooks
