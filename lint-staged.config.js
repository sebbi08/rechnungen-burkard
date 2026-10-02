// What .husky/pre-commit runs over the staged files. Two formatters, split by
// directory: CSharpier owns backend/, Prettier owns everything else (see
// .prettierignore, which excludes backend/ for exactly this reason).
//
// The C# entry starts a container, so it costs a couple of seconds -- but only
// when C# is actually staged. A docs-only or frontend-only commit never reaches
// it and never needs Docker running.
module.exports = {
  "*.{ts,js,mjs,cjs,json,md,yml,yaml,html,vue,css}": "prettier --write",
  "*.cs": "scripts/csharpier-staged.sh",
};
