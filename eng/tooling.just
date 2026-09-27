import "common.just"

# Restore repository tooling dependencies.
install:
    "{{ BUN }}" install --frozen-lockfile

# Check task recipes, tooling formatting and Git hook configuration.
check: hooks-check
    for file in justfile eng/*.just; do just --justfile "$file" --fmt --check || exit; done
    "{{ BUN }}" --bun run tooling:format:check

# Format task recipes and tooling.
format:
    for file in justfile eng/*.just; do just --justfile "$file" --fmt || exit; done
    "{{ BUN }}" --bun run tooling:format

# Enable repository Git hooks in this checkout.
hooks-install:
    "{{ BUN }}" x lefthook install

# Validate the Git hook configuration.
hooks-check:
    "{{ BUN }}" x lefthook validate
