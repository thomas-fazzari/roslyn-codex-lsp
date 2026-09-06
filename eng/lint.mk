.PHONY: tooling-install tooling-format tooling-format-check lint-staged lint-changed lint-auto hooks-install

tooling-install:
	$(BUN) install --frozen-lockfile

tooling-format:
	$(BUN) --bun run tooling:format

tooling-format-check:
	$(BUN) --bun run tooling:format:check

lint-staged:
	$(BUN) eng/scripts/lint.mjs staged

lint-changed:
	$(BUN) eng/scripts/lint.mjs changed

lint-auto:
	$(BUN) eng/scripts/lint.mjs auto

hooks-install:
	chmod +x .githooks/pre-commit
	git config --local core.hooksPath .githooks
