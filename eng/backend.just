import "common.just"

SOLUTION := "RoslynCodexLsp.slnx"
CONFIGURATION := env("CONFIGURATION", "Debug")
TEST_ARGS := env("TEST_ARGS", "")
RUNTIME_ID := env("RUNTIME_ID", "")
APP_VERSION := env("APP_VERSION", "0.0.0-dev")
NATIVE_OUTPUT := env("NATIVE_OUTPUT", "artifacts/native")

# Restore .NET dependencies and local tools.
install:
    "{{ DOTNET }}" restore {{ SOLUTION }}
    "{{ DOTNET }}" tool restore

# Build the .NET solution.
build:
    "{{ DOTNET }}" build {{ SOLUTION }} --configuration "{{ CONFIGURATION }}"

# Check C# formatting.
lint:
    "{{ DOTNET }}" tool run csharpier check .

# Build and run normal .NET tests.
test: build
    "{{ DOTNET }}" test --solution {{ SOLUTION }} --configuration "{{ CONFIGURATION }}" --no-build --no-restore -- {{ TEST_ARGS }} --explicit off

# Build and run explicit tests against real Roslyn.
test-integration: build
    "{{ DOTNET }}" test --solution {{ SOLUTION }} --configuration "{{ CONFIGURATION }}" --no-build --no-restore -- {{ TEST_ARGS }} --explicit only --filter-class RoslynCodexLsp.Tests.Integration.Roslyn.RoslynMcpTests

# Publish the native bridge in Release mode for RUNTIME_ID.
publish:
    @test -n "{{ RUNTIME_ID }}" || { echo 'Set RUNTIME_ID, for example osx-arm64.' >&2; exit 1; }
    "{{ DOTNET }}" publish src/RoslynCodexLsp/RoslynCodexLsp.csproj --configuration Release --runtime "{{ RUNTIME_ID }}" -p:Version="{{ APP_VERSION }}" --output "{{ NATIVE_OUTPUT }}"

# Format C# files.
format:
    "{{ DOTNET }}" tool run csharpier format .

# Update copyright headers.
copyrights:
    "{{ BUN }}" eng/scripts/copyrights.mjs

# Check copyright headers.
copyrights-check:
    "{{ BUN }}" eng/scripts/copyrights.mjs --check
