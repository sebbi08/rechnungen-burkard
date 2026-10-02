#!/usr/bin/env bash
# One-off: a dev database that ran main between the German-only change and the
# grave site fields applied 20261002064856_RemoveDocumentLanguage, which was
# then consolidated into *_GraveSiteFieldsAndGermanOnly. This puts such a
# database back where the consolidated migration starts: it re-adds the two
# Language columns that migration drops (nothing reads them any more) and
# deletes the stale history row. The backend's next start then applies the
# consolidated migration as a whole.
#
# Stop the backend first. Safe to re-run: every step is guarded. Takes a backup.
set -euo pipefail
DB="${1:-data/invoicing.db}"

cp "$DB" "$DB.backup-$(date +%Y%m%d%H%M%S)"

python3 - "$DB" <<'PY'
import sqlite3, sys
db = sqlite3.connect(sys.argv[1])
stale = "20261002064856_RemoveDocumentLanguage"
if not db.execute(
    "SELECT 1 FROM __EFMigrationsHistory WHERE MigrationId = ?", (stale,)
).fetchone():
    print("nothing to do:", stale, "was never applied")
    sys.exit(0)

def columns(table):
    return {r[1] for r in db.execute(f"pragma table_info({table})")}

for table in ("Invoices", "GlobalSettings"):
    if "Language" not in columns(table):
        db.execute(f"ALTER TABLE {table} ADD COLUMN Language TEXT NOT NULL DEFAULT 'de'")
db.execute("DELETE FROM __EFMigrationsHistory WHERE MigrationId = ?", (stale,))
db.commit()
print("reverted", stale, "- the next start applies the consolidated migration")
PY
