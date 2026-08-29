#!/usr/bin/env bash
# Puts a release section into CHANGELOG.md, newest first.
#
#   scripts/changelog-entry.sh 1.3.0 > entry.md
#   scripts/prepend-changelog.sh entry.md [CHANGELOG.md]
#
# The section goes immediately above the newest release already in the file --
# the first line starting with "## " -- so the introduction at the top stays
# where it is. With no releases in the file yet, it goes at the end.

set -euo pipefail

entry_file=${1:-}
changelog=${2:-CHANGELOG.md}

if [ -z "$entry_file" ]; then
  echo "usage: $0 <entry file> [changelog]" >&2
  exit 2
fi

if [ ! -f "$entry_file" ]; then
  echo "no such entry file: $entry_file" >&2
  exit 1
fi

if [ ! -f "$changelog" ]; then
  echo "no such changelog: $changelog" >&2
  exit 1
fi

tmp=$(mktemp)
trap 'rm -f "$tmp"' EXIT

first_release=$(grep -n '^## ' "$changelog" | head -1 | cut -d: -f1 || true)

if [ -n "$first_release" ]; then
  head -n "$((first_release - 1))" "$changelog" > "$tmp"
  cat "$entry_file" >> "$tmp"
  echo >> "$tmp"
  tail -n "+${first_release}" "$changelog" >> "$tmp"
else
  cat "$changelog" > "$tmp"
  # Exactly one blank line between the introduction and the first release.
  [ -n "$(tail -c 1 "$tmp")" ] && echo >> "$tmp"
  echo >> "$tmp"
  cat "$entry_file" >> "$tmp"
fi

cat "$tmp" > "$changelog"
