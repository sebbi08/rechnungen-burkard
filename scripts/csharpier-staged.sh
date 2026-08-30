#!/usr/bin/env bash
# Formats the staged C# files lint-staged passes in.
#
# lint-staged hands absolute host paths, but CSharpier runs in a container that
# sees backend/ as /src, so the paths have to be translated. Files outside
# backend/ are skipped rather than passed through: an untranslated path would
# not exist in the container, and CSharpier would fail on it.
set -euo pipefail
REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"

paths=()
for file in "$@"; do
  case "$file" in
    "$REPO_ROOT"/backend/*) paths+=("/src/${file#"$REPO_ROOT"/backend/}") ;;
    backend/*) paths+=("/src/${file#backend/}") ;;
    *) ;;
  esac
done

if [ ${#paths[@]} -eq 0 ]; then
  exit 0
fi

exec "$REPO_ROOT/scripts/csharpier.sh" format "${paths[@]}"
