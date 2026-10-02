// Prettier owns the formatting of every text file in the repository; CSharpier
// owns backend/ (see scripts/csharpier.sh). Both run from .husky/pre-commit via
// lint-staged, and CI re-checks them in the \`format\` job.
//
// printWidth is 100 rather than the default 80 because that is roughly where
// this codebase already wraps, so 80 would have rewrapped nearly every file for
// no gain. It also matches CSharpier's own default width, which keeps the two
// sides of the stack looking alike.
//
// Nothing here turns off ESLint rules: ESLint 10 core and the typescript-eslint
// configs the frontend uses carry no formatting rules, so there is nothing for
// eslint-config-prettier to disable.
module.exports = {
  printWidth: 100,
};
