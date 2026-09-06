.DEFAULT_GOAL := help
MAKEFLAGS += --no-builtin-rules --warn-undefined-variables

include eng/common.mk
include eng/backend.mk
include eng/docs.mk
include eng/lint.mk
include eng/ci.mk

.PHONY: help install build lint check format

help:
	@printf '%-28s %s\n' \
		'make install' 'Restore tooling, .NET packages and local tools' \
		'make build' 'Build the .NET solution' \
		'make publish RUNTIME_ID=...' 'Publish the native bridge for a platform' \
		'make check' 'Check formatting, Markdown, build and tests' \
		'make test' 'Build and run .NET tests' \
		'make test-integration' 'Build and run explicit tests against real Roslyn' \
		'make lint' 'Check formatting, Markdown and build analyzers' \
		'make lint-staged' 'Lint staged files only' \
		'make lint-changed' 'Lint local changes and untracked files' \
		'make lint-auto' 'Lint staged files, or local changes if none are staged' \
		'make hooks-install' 'Enable repository Git hooks in this checkout' \
		'make docs-dev' 'Serve documentation at http://127.0.0.1:5174' \
		'make docs-build' 'Build the documentation site' \
		'make docs-preview' 'Preview the built documentation site' \
		'make docs-check' 'Check Markdown and build the documentation site' \
		'make docs-format' 'Format Markdown with Oxfmt' \
		'make format' 'Format Markdown, tooling and C# files' \
		'docs/development.md' 'Setup, validation and command details'

install: tooling-install backend-install

build: backend-build

lint: docs-check tooling-format-check backend-lint backend-build

check: lint test

format: docs-format tooling-format backend-format
