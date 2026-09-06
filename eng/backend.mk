.PHONY: backend-install backend-build backend-lint backend-format publish test test-integration copyrights copyrights-check

RUNTIME_ID ?=
APP_VERSION ?= 0.0.0-dev
NATIVE_OUTPUT ?= artifacts/native

publish: CONFIGURATION = Release
publish:
	@test -n "$(RUNTIME_ID)" || { echo 'Set RUNTIME_ID, for example osx-arm64.' >&2; exit 1; }
	$(DOTNET) publish src/RoslynCodexLsp/RoslynCodexLsp.csproj --configuration $(CONFIGURATION) --runtime "$(RUNTIME_ID)" -p:Version="$(APP_VERSION)" --output "$(NATIVE_OUTPUT)"

backend-install:
	$(DOTNET) restore $(SOLUTION)
	$(DOTNET) tool restore

backend-build:
	$(DOTNET) build $(SOLUTION) --configuration $(CONFIGURATION)

backend-lint:
	$(DOTNET) tool run csharpier check .

test: backend-build
	$(DOTNET) test --solution $(SOLUTION) --configuration $(CONFIGURATION) --no-build --no-restore -- $(TEST_ARGS) --explicit off

test-integration: backend-build
	$(DOTNET) test --solution $(SOLUTION) --configuration $(CONFIGURATION) --no-build --no-restore -- $(TEST_ARGS) --explicit only --filter-class RoslynCodexLsp.Tests.Integration.Roslyn.RoslynMcpTests

backend-format:
	$(DOTNET) tool run csharpier format .

copyrights:
	$(BUN) eng/scripts/copyrights.mjs

copyrights-check:
	$(BUN) eng/scripts/copyrights.mjs --check
