import "common.just"

# Compute the binary version.
version:
    bash eng/ci/set-version.sh

# Install Roslyn for integration tests.
roslyn:
    bash eng/ci/setup-roslyn.sh

# Package the native executable.
package:
    bash eng/ci/package.sh

# Publish tested release archives.
release:
    bash eng/ci/release.sh
