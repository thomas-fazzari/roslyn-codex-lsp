import "common.just"

# Serve documentation with live updates.
dev:
    "{{ BUN }}" --bun run docs:dev

# Build the static documentation site.
build:
    "{{ BUN }}" --bun run docs:build

# Serve the built documentation locally.
preview:
    "{{ BUN }}" --bun run docs:preview

# Lint Markdown files.
lint:
    "{{ BUN }}" --bun run docs:lint

# Format Markdown with Oxfmt.
format:
    "{{ BUN }}" --bun run docs:format

# Check Markdown formatting.
format-check:
    "{{ BUN }}" --bun run docs:format:check

# Check Markdown and build the documentation site.
check: lint format-check build
