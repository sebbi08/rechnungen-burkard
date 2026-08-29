#!/usr/bin/env bash
# Tests for the release scripts.
#
# Each case builds a throwaway git repository in a temporary directory, gives
# it a synthetic history, and checks what the scripts make of it. Real git, no
# mocks -- the scripts read git and nothing else, so there is nothing worth
# faking.
#
# Run: scripts/test-release-scripts.sh

set -uo pipefail

SCRIPT_DIR=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
REPO_ROOT=$(cd "$SCRIPT_DIR/.." && pwd)
NEXT_VERSION="$SCRIPT_DIR/next-version.sh"
CHANGELOG_ENTRY="$SCRIPT_DIR/changelog-entry.sh"
PREPEND_CHANGELOG="$SCRIPT_DIR/prepend-changelog.sh"

WORK=$(mktemp -d)
trap 'rm -rf "$WORK"' EXIT

passed=0
failed=0
skipped=0

pass() { printf '  ok       %s\n' "$1"; passed=$((passed + 1)); }
skip() { printf '  skipped  %s (%s)\n' "$1" "$2"; skipped=$((skipped + 1)); }

fail() {
  printf '  FAIL     %s\n' "$1"
  printf '    expected: %s\n' "$2"
  printf '    actual:   %s\n' "$3"
  failed=$((failed + 1))
}

assert_eq() { # name expected actual
  if [ "$2" = "$3" ]; then pass "$1"; else fail "$1" "$2" "$3"; fi
}

assert_contains() { # name needle haystack
  case "$3" in
    *"$2"*) pass "$1" ;;
    *) fail "$1" "something containing '$2'" "$3" ;;
  esac
}

# A repository with one seed commit, which the tag cases tag as the release
# everything else is measured against.
new_repo() {
  local dir
  dir=$(mktemp -d "$WORK/repo.XXXXXX")
  git -C "$dir" init -q -b main
  git -C "$dir" config user.email "test@example.com"
  git -C "$dir" config user.name "Test"
  git -C "$dir" config commit.gpgsign false
  echo seed > "$dir/seed.txt"
  git -C "$dir" add -A
  git -C "$dir" commit -qm "chore: seed the repository"
  printf '%s' "$dir"
}

commit() { # dir subject [body]
  local dir=$1 subject=$2 body=${3:-}
  echo "$RANDOM$RANDOM" >> "$dir/file.txt"
  git -C "$dir" add -A
  if [ -n "$body" ]; then
    git -C "$dir" commit -qm "$subject" -m "$body"
  else
    git -C "$dir" commit -qm "$subject"
  fi
}

next_version() { (cd "$1" && "$NEXT_VERSION" 2>&1); }

# Commit hashes differ on every run, so compare against a placeholder.
changelog() {
  local dir=$1
  shift
  (cd "$dir" && "$CHANGELOG_ENTRY" "$@" 2>&1 | sed -E 's/\([0-9a-f]{7,}\)/(SHA)/')
}

echo "next-version.sh"

d=$(new_repo)
assert_eq "no tags at all starts the project at 1.0.0" "1.0.0" "$(next_version "$d")"

d=$(new_repo); git -C "$d" tag v1.0.0
commit "$d" "feat: add a thing"
assert_eq "a feature bumps the minor" "1.1.0" "$(next_version "$d")"

d=$(new_repo); git -C "$d" tag v1.0.0
commit "$d" "fix: correct a thing"
assert_eq "a fix bumps the patch" "1.0.1" "$(next_version "$d")"

d=$(new_repo); git -C "$d" tag v1.0.0
commit "$d" "perf: speed a thing up"
assert_eq "perf bumps the patch" "1.0.1" "$(next_version "$d")"

d=$(new_repo); git -C "$d" tag v1.0.0
commit "$d" "refactor: move a thing"
assert_eq "refactor bumps the patch" "1.0.1" "$(next_version "$d")"

d=$(new_repo); git -C "$d" tag v1.0.0
commit "$d" "feat!: change a thing"
assert_eq "a bang marks a breaking change" "2.0.0" "$(next_version "$d")"

d=$(new_repo); git -C "$d" tag v1.0.0
commit "$d" "fix(api)!: change a thing"
assert_eq "a bang after a scope marks a breaking change" "2.0.0" "$(next_version "$d")"

d=$(new_repo); git -C "$d" tag v1.0.0
commit "$d" "feat: change a thing" "BREAKING CHANGE: the flag is gone"
assert_eq "BREAKING CHANGE in the body marks a breaking change" "2.0.0" "$(next_version "$d")"

d=$(new_repo); git -C "$d" tag v1.0.0
commit "$d" "docs: write it down"
commit "$d" "chore: bump a dependency"
commit "$d" "test: cover a thing"
out=$(next_version "$d"); status=$?
assert_eq "housekeeping alone is not a release" "1" "$status"
assert_contains "and it says so, naming the tag" "no releasable commits since v1.0.0" "$out"

d=$(new_repo); git -C "$d" tag v1.0.0
commit "$d" 'Revert "feat: add a thing"'
assert_eq "an unparseable subject counts as a patch" "1.0.1" "$(next_version "$d")"

d=$(new_repo); git -C "$d" tag v1.0.0
commit "$d" "fix: correct a thing"
commit "$d" "feat: add a thing"
commit "$d" "docs: write it down"
assert_eq "the strongest bump in the range wins" "1.1.0" "$(next_version "$d")"

# Guards the commit range: work released under the tag must not be counted again.
d=$(new_repo)
commit "$d" "feat: a released feature"
git -C "$d" tag v1.0.0
commit "$d" "docs: write it down"
assert_contains "commits behind the tag are not counted again" \
  "no releasable commits since v1.0.0" "$(next_version "$d")"

d=$(new_repo); git -C "$d" tag v2.3.4
commit "$d" "fix: correct a thing"
assert_eq "the bump builds on the latest tag" "2.3.5" "$(next_version "$d")"

echo
echo "changelog-entry.sh"

d=$(new_repo); git -C "$d" tag v1.0.0
commit "$d" "feat: add a thing"
commit "$d" "fix: correct a thing"
commit "$d" "docs: write it down"
commit "$d" "refactor: move a thing"
commit "$d" "chore: bump a dependency"
expected='## v1.1.0 — 2026-01-02

### Features
- add a thing (SHA)

### Fixes
- correct a thing (SHA)

### Other changes
- move a thing (SHA)'
assert_eq "groups the sections and leaves housekeeping out" \
  "$expected" "$(changelog "$d" 1.1.0 2026-01-02)"

d=$(new_repo); git -C "$d" tag v1.0.0
commit "$d" "feat!: change a thing"
expected='## v2.0.0 — 2026-01-02

### Breaking changes
- change a thing (SHA)'
assert_eq "breaking changes lead" "$expected" "$(changelog "$d" 2.0.0 2026-01-02)"

d=$(new_repo); git -C "$d" tag v1.0.0
commit "$d" "feat(invoices): add a thing"
assert_contains "a scope is kept in the entry" \
  "- **invoices**: add a thing (SHA)" "$(changelog "$d" 1.1.0 2026-01-02)"

d=$(new_repo)
commit "$d" "feat: add a thing"
assert_contains "with no tags it covers the whole history" \
  "- add a thing (SHA)" "$(changelog "$d" 1.0.0 2026-01-02)"

d=$(new_repo); git -C "$d" tag v1.0.0
commit "$d" "feat: the older feature"
commit "$d" "feat: the newer feature"
expected='## v1.1.0 — 2026-01-02

### Features
- the newer feature (SHA)
- the older feature (SHA)'
assert_eq "entries run newest first" "$expected" "$(changelog "$d" 1.1.0 2026-01-02)"

echo
echo "prepend-changelog.sh"

intro='# Changelog

How this file is written.'

d=$(mktemp -d "$WORK/changelog.XXXXXX")
printf '%s\n' "$intro" > "$d/CHANGELOG.md"
printf '## v1.0.0 — 2026-01-02\n\n### Features\n- the first thing (abc1234)\n' > "$d/entry.md"
(cd "$d" && "$PREPEND_CHANGELOG" entry.md >/dev/null)
expected='# Changelog

How this file is written.

## v1.0.0 — 2026-01-02

### Features
- the first thing (abc1234)'
assert_eq "the first release goes below the introduction" "$expected" "$(cat "$d/CHANGELOG.md")"

# The second release must land above the first, not at the bottom.
printf '## v1.1.0 — 2026-02-03\n\n### Features\n- the second thing (def5678)\n' > "$d/entry.md"
(cd "$d" && "$PREPEND_CHANGELOG" entry.md >/dev/null)
expected='# Changelog

How this file is written.

## v1.1.0 — 2026-02-03

### Features
- the second thing (def5678)

## v1.0.0 — 2026-01-02

### Features
- the first thing (abc1234)'
assert_eq "later releases go above earlier ones" "$expected" "$(cat "$d/CHANGELOG.md")"

d=$(mktemp -d "$WORK/changelog.XXXXXX")
printf '%s\n' "$intro" > "$d/CHANGELOG.md"
printf 'entry\n' > "$d/entry.md"
out=$( (cd "$d" && "$PREPEND_CHANGELOG" missing.md) 2>&1 ); status=$?
assert_eq "a missing entry file is an error" "1" "$status"
assert_contains "and it says which file" "missing.md" "$out"

echo
echo "commitlint agreement"

# The linter decides which types may be committed; next-version.sh decides what
# each type means for the version. They are separate files and can drift, so
# every type the linter allows must have a bump this script knows.
types=$(cd "$REPO_ROOT" && node -e '
  const m = require("@commitlint/config-conventional");
  const c = m.default ?? m;
  process.stdout.write(c.rules["type-enum"][2].join("\n"));
' 2>/dev/null)

if [ -z "$types" ]; then
  skip "every commitlint type has a bump" "run npm install at the repo root first"
else
  unknown=""
  while IFS= read -r type; do
    [ -n "$type" ] || continue
    bump=$("$NEXT_VERSION" --bump-for "$type" 2>&1)
    case "$bump" in
      major|minor|patch|none) ;;
      *) unknown="$unknown $type" ;;
    esac
  done <<< "$types"
  assert_eq "every commitlint type has a bump" "" "$unknown"
fi

echo
printf '%d passed, %d failed, %d skipped\n' "$passed" "$failed" "$skipped"
[ "$failed" -eq 0 ]
