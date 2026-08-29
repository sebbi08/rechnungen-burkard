// Commit messages follow Conventional Commits, unmodified: the standard
// config already matches how this repository has been writing them, and
// scripts/next-version.sh reads the same types to work out the release
// version. Adding a type here means giving it a bump there --
// scripts/test-release-scripts.sh fails if one is missing.
module.exports = { extends: ["@commitlint/config-conventional"] };
