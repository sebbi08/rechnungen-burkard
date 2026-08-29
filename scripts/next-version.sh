#!/usr/bin/env bash
# Works out the version of the next release from the commits since the last
# v* tag, following Conventional Commits.
#
#   scripts/next-version.sh               print the next version, e.g. 1.3.0
#   scripts/next-version.sh --bump-for T  print what commit type T means
#
# Exits 1 with a message on stderr when nothing since the last tag is worth
# releasing, so the release workflow stops instead of shipping an empty
# version. With no tag at all this is the project's first release: 1.0.0.

set -euo pipefail

# What each commit type does to the version. Every type commitlint accepts must
# appear here -- scripts/test-release-scripts.sh checks that it does, because
# the two lists live in different files and would otherwise drift apart.
bump_for_type() {
  case "$1" in
    feat) echo minor ;;
    fix | perf | refactor | revert) echo patch ;;
    build | chore | ci | docs | style | test) echo none ;;
    *) echo unknown; return 1 ;;
  esac
}

rank_of() { # bump -> sortable number
  case "$1" in
    major) echo 3 ;;
    minor) echo 2 ;;
    patch) echo 1 ;;
    *) echo 0 ;;
  esac
}

if [ "${1:-}" = "--bump-for" ]; then
  if [ -z "${2:-}" ]; then
    echo "usage: $0 --bump-for <commit type>" >&2
    exit 2
  fi
  bump_for_type "$2"
  exit $?
fi

last_tag=$(git describe --tags --abbrev=0 --match 'v*' 2>/dev/null || true)

if [ -z "$last_tag" ]; then
  # Nothing has ever been released. The app is in use, so it starts at 1.0.0
  # rather than working its way up from 0.x through the commit types.
  echo "1.0.0"
  exit 0
fi

highest=0
while IFS= read -r sha; do
  [ -n "$sha" ] || continue

  subject=$(git log -1 --format=%s "$sha")
  body=$(git log -1 --format=%b "$sha")

  if [[ "$subject" =~ ^([a-zA-Z]+)(\([^\)]*\))?(!)?:[[:space:]] ]]; then
    type="${BASH_REMATCH[1]}"
    breaking_marker="${BASH_REMATCH[3]}"
    if [ -n "$breaking_marker" ]; then
      bump=major
    else
      # An unknown type still changed something, so it counts as a patch
      # rather than disappearing from the release.
      bump=$(bump_for_type "$type" || echo patch)
      [ "$bump" = "unknown" ] && bump=patch
    fi
  else
    # Reverts and anything else off-format: real changes, so at least a patch.
    bump=patch
  fi

  if printf '%s\n' "$body" | grep -qE '^BREAKING[ -]CHANGE:'; then
    bump=major
  fi

  rank=$(rank_of "$bump")
  [ "$rank" -gt "$highest" ] && highest=$rank
done < <(git log --format=%H "$last_tag..HEAD")

if [ "$highest" -eq 0 ]; then
  echo "no releasable commits since $last_tag" >&2
  exit 1
fi

current=${last_tag#v}
IFS=. read -r major minor patch <<< "$current"

case "$highest" in
  3) major=$((major + 1)); minor=0; patch=0 ;;
  2) minor=$((minor + 1)); patch=0 ;;
  1) patch=$((patch + 1)) ;;
esac

echo "${major}.${minor}.${patch}"
