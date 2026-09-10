set dotenv-load

mod backend "eng/backend.just"
mod ci "eng/ci.just"
mod docs "eng/docs.just"
mod tooling "eng/tooling.just"

# List all commands, including module recipes.
help:
    @just --list --list-submodules

# Restore tooling, .NET packages and local tools.
install: tooling::install backend::install

# Build the .NET solution.
build: backend::build

# Check formatting, Markdown and build analyzers.
lint: docs::check tooling::check backend::lint backend::build

# Check formatting, build documentation and C#, and run normal tests.
check: lint test

# Format task recipes, Markdown, tooling and C# files.
format: docs::format tooling::format backend::format

# Build and run normal .NET tests.
test: backend::test

# Build and run explicit tests against real Roslyn.
test-integration: backend::test-integration
