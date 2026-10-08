#!/usr/bin/env bash
# Writes the changelog section for a release: the commits since the last v*
# tag, grouped by what they mean to someone using the app.
#
#   scripts/changelog-entry.sh 1.3.0 [2026-08-29]
#
# The date defaults to today (UTC); the release workflow lets it. Housekeeping
# commits -- docs, tests, chores, CI, style, build -- are left out: this is a
# record for the reader of the app, not of the repository.

set -euo pipefail

version=${1:-}
date=${2:-$(date -u +%Y-%m-%d)}

if [ -z "$version" ]; then
  echo "usage: $0 <version> [date]" >&2
  exit 2
fi
version=${version#v}

last_tag=$(git describe --tags --abbrev=0 --match 'v*' 2>/dev/null || true)
if [ -n "$last_tag" ]; then
  range="$last_tag..HEAD"
else
  range="HEAD"
fi

breaking=()
features=()
fixes=()
other=()

# no merge commits: their branches' commits are the entries (see next-version.sh)
while IFS= read -r sha; do
  [ -n "$sha" ] || continue

  short=$(git log -1 --format=%h "$sha")
  subject=$(git log -1 --format=%s "$sha")
  body=$(git log -1 --format=%b "$sha")

  type=""
  scope=""
  marker=""
  description="$subject"
  if [[ "$subject" =~ ^([a-zA-Z]+)(\(([^\)]*)\))?(!)?:[[:space:]]+(.*)$ ]]; then
    type="${BASH_REMATCH[1]}"
    scope="${BASH_REMATCH[3]}"
    marker="${BASH_REMATCH[4]}"
    description="${BASH_REMATCH[5]}"
  fi

  is_breaking=0
  if [ -n "$marker" ]; then
    is_breaking=1
  fi
  if printf '%s\n' "$body" | grep -qE '^BREAKING[ -]CHANGE:'; then
    is_breaking=1
  fi

  entry="- "
  if [ -n "$scope" ]; then
    entry="${entry}**${scope}**: "
  fi
  entry="${entry}${description} (${short})"

  if [ "$is_breaking" -eq 1 ]; then
    breaking+=("$entry")
  else
    case "$type" in
      feat) features+=("$entry") ;;
      fix) fixes+=("$entry") ;;
      build | chore | ci | docs | style | test) ;;
      *) other+=("$entry") ;;
    esac
  fi
done < <(git log --no-merges --format=%H "$range")

lines=("## v${version} — ${date}")

append_section() { # title entries...
  local title=$1
  shift
  [ "$#" -gt 0 ] || return 0
  lines+=("" "### ${title}")
  local entry
  for entry in "$@"; do
    lines+=("$entry")
  done
}

append_section "Breaking changes" ${breaking[@]+"${breaking[@]}"}
append_section "Features" ${features[@]+"${features[@]}"}
append_section "Fixes" ${fixes[@]+"${fixes[@]}"}
append_section "Other changes" ${other[@]+"${other[@]}"}

printf '%s\n' "${lines[@]}"
