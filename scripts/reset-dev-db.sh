#!/usr/bin/env bash
# Start the dev stack on an empty database. The old one is not deleted but
# moved aside, -wal/-shm journals included (they carry committed rows that
# SQLite would replay into a fresh file), into data/backup-<time>/. Putting
# those three files back restores it.
#
# It asks first, since the dev database holds the user's own data; --yes skips
# the question. Stops the containers first so nothing holds the file open.
#
# Usage: scripts/reset-dev-db.sh [--yes]
set -euo pipefail
REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "${REPO_ROOT}"

assume_yes=0
case "${1:-}" in
  "") ;;
  -y | --yes) assume_yes=1 ;;
  *) echo "usage: scripts/reset-dev-db.sh [--yes]" >&2; exit 2 ;;
esac

files=()
for f in data/invoicing.db data/invoicing.db-wal data/invoicing.db-shm; do
  [ -e "$f" ] && files+=("$f")
done

backup=""
if [ "${#files[@]}" -gt 0 ]; then
  backup="data/backup-$(date +%Y%m%d-%H%M%S)"
  if [ "$assume_yes" -eq 0 ]; then
    if [ ! -t 0 ]; then
      echo "not a terminal -- pass --yes to reset without the question" >&2
      exit 1
    fi
    printf 'Start over on an empty dev database? The current one is moved to %s/. [y/N] ' "$backup"
    read -r reply
    case "$reply" in
      y | Y | yes | Yes) ;;
      *) echo "nothing changed"; exit 1 ;;
    esac
  fi
fi

docker compose down

if [ -n "$backup" ]; then
  mkdir -p "$backup"
  mv "${files[@]}" "$backup/"
  echo "the old database is in $backup/"
fi

# env, not a plain assignment: UID is readonly in bash, and docker-compose.yml
# reads UID/GID to run both containers as the host user
env "UID=$(id -u)" "GID=$(id -g)" docker compose up -d

printf 'waiting for the stack'
for _ in $(seq 120); do
  if curl -sf http://localhost:8080/odata/Settings >/dev/null; then
    printf ' — ready on http://localhost:8080\n'
    exit 0
  fi
  printf '.'
  sleep 1
done

printf '\n'
echo "the stack did not answer on http://localhost:8080 within 120s" >&2
exit 1
