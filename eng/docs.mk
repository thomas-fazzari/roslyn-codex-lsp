.PHONY: docs-dev docs-build docs-preview docs-lint docs-format docs-format-check docs-check

docs-dev:
	$(BUN) --bun run docs:dev

docs-build:
	$(BUN) --bun run docs:build

docs-preview:
	$(BUN) --bun run docs:preview

docs-lint:
	$(BUN) --bun run docs:lint

docs-format:
	$(BUN) --bun run docs:format

docs-format-check:
	$(BUN) --bun run docs:format:check

docs-check: docs-lint docs-format-check docs-build
