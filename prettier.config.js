// Prettier owns the formatting of every text file in the repository; CSharpier
// owns backend/ (see scripts/csharpier.sh). Both run from .husky/pre-commit via
// lint-staged, and CI re-checks them in the `format` job.
//
// printWidth is 100 rather than the default 80 because that is roughly where
// this codebase already wraps -- the widest TypeScript line was 95 columns and
// the UI5 views wrap around 100 -- so 80 would have rewrapped nearly every
// file for no gain. It also matches CSharpier's own default width, which keeps
// the two sides of the stack looking alike.
//
// Nothing here turns off ESLint rules: ESLint 10 core and the typescript-eslint
// configs the frontend uses carry no formatting rules, so there is nothing for
// eslint-config-prettier to disable.
module.exports = {
  printWidth: 100,
  plugins: ["@prettier/plugin-xml"],
  overrides: [
    {
      // UI5 views and fragments are indented with four spaces, and their
      // attributes have to be free to re-wrap -- "ignore" lets the plugin
      // reflow them instead of preserving whatever whitespace is in the file.
      files: "*.xml",
      options: {
        tabWidth: 4,
        xmlWhitespaceSensitivity: "ignore",
      },
    },
  ],
};
