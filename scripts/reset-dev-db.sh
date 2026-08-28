#!/usr/bin/env bash
# Throw away the dev database and start the dev stack on an empty one.
# Stops the containers first so nothing holds the file open, and removes the
# -wal/-shm journals as well: they carry committed rows that SQLite would
# replay into a fresh database file.
# Usage: scripts/reset-dev-db.sh
set -euo pipefail
REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "${REPO_ROOT}"

docker compose down

rm -f data/invoicing.db data/invoicing.db-wal data/invoicing.db-shm

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
