.PHONY: backend-install backend-build backend-lint backend-format test test-integration copyrights copyrights-check

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
