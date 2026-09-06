.PHONY: ci-version ci-roslyn ci-package ci-release

ci-version:
	bash eng/ci/set-version.sh

ci-roslyn:
	bash eng/ci/setup-roslyn.sh

ci-package:
	bash eng/ci/package.sh

ci-release:
	bash eng/ci/release.sh
