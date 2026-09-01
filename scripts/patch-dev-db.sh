#!/usr/bin/env bash
# One-off: the dev database applied 20260831163518_InvoicePaymentStatus before
# that migration was consolidated away. This brings it to the consolidated
# migration without touching any data: it adds the columns the database is
# missing and rewrites the history row.
#
# Safe to re-run: every step is guarded. Takes a backup first.
set -euo pipefail
DB="${1:-data/invoicing.db}"
NEW_ID="$(basename "$(ls backend/src/Invoicing.Api/Migrations/*_PaymentVatAndGraveShare.cs)" .cs)"

cp "$DB" "$DB.backup-$(date +%Y%m%d%H%M%S)"

python3 - "$DB" "$NEW_ID" <<'PY'
import sqlite3, sys
db, new_id = sqlite3.connect(sys.argv[1]), sys.argv[2]
def columns(table):
    return {r[1] for r in db.execute(f"pragma table_info({table})")}

if "PriceIncludesVat" not in columns("InvoiceLines"):
    db.execute("ALTER TABLE InvoiceLines ADD COLUMN PriceIncludesVat INTEGER NOT NULL DEFAULT 0")
if "PriceIncludesVat" not in columns("GraveItems"):
    db.execute("ALTER TABLE GraveItems ADD COLUMN PriceIncludesVat INTEGER NOT NULL DEFAULT 0")
if "RecipientCount" not in columns("Invoices"):
    db.execute("ALTER TABLE Invoices ADD COLUMN RecipientCount INTEGER NULL")

version = db.execute("SELECT ProductVersion FROM __EFMigrationsHistory LIMIT 1").fetchone()[0]
db.execute("DELETE FROM __EFMigrationsHistory WHERE MigrationId LIKE '%InvoicePaymentStatus'")
db.execute("DELETE FROM __EFMigrationsHistory WHERE MigrationId = ?", (new_id,))
db.execute(
    "INSERT INTO __EFMigrationsHistory (MigrationId, ProductVersion) VALUES (?, ?)",
    (new_id, version),
)
db.commit()
print("applied:", *[r[0] for r in db.execute(
    "SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId")], sep="\n  ")
PY
