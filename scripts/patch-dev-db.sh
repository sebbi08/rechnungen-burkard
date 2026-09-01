#!/usr/bin/env bash
# One-off: the dev database applied 20260831163518_InvoicePaymentStatus before
# that migration was consolidated away. This brings it to the consolidated
# migration without touching any data: it adds the columns the database is
# missing and rewrites the history row.
#
# Before touching anything -- including the backup -- this checks that the
# database actually matches the premise it was written for: PaidOn and its
# index already applied, PriceIncludesVat/RecipientCount not yet added, and
# __EFMigrationsHistory recording InvoicePaymentStatus. Run against a database
# in any other shape (already patched, never touched by InvoicePaymentStatus,
# or something in between) and it reports that and changes nothing rather than
# guessing.
#
# Safe to re-run: a database the consolidated migration already applies to
# is recognised and left alone.
#
# The dev stack must be stopped first -- see the -wal check below.
#
# Usage: scripts/patch-dev-db.sh [database]   (default: <repo>/data/invoicing.db)
# It can be run from any directory; nothing here depends on the working one.
set -euo pipefail

# Both the migrations folder and the default database are resolved from the
# script's own location, not from the working directory: a relative path only
# resolves when the script happens to be run from the repository root, and
# resolving to nothing is what the guard below exists to catch.
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DB="${1:-$ROOT/data/invoicing.db}"

# `set -e` does not stop a failing command inside a command substitution, and
# `basename ""` succeeds -- so a missing or renamed migration would leave
# NEW_ID empty and nothing would complain. The precondition check does not look
# at it either, so it would still report PATCH, and the patch body would then
# delete the _InvoicePaymentStatus history row and insert MigrationId = '' in
# its place: a database with the new columns and no record of the migration,
# which EF re-runs on the next start and dies on a duplicate PaidOn column.
# Hence an explicit non-empty check, before anything is read or written.
MIGRATIONS=("$ROOT"/backend/src/Invoicing.Api/Migrations/*_PaymentVatAndGraveShare.cs)
# an unmatched glob stays literal, so count it as none rather than as one
MIGRATION_COUNT="${#MIGRATIONS[@]}"
[ -e "${MIGRATIONS[0]}" ] || MIGRATION_COUNT=0
NEW_ID=""
if [ "$MIGRATION_COUNT" -eq 1 ]; then
    NEW_ID="$(basename "${MIGRATIONS[0]}" .cs)"
fi
if [ -z "$NEW_ID" ]; then
    echo "patch-dev-db.sh: cannot determine the consolidated migration id." >&2
    echo "Expected exactly one file matching" >&2
    echo "  $ROOT/backend/src/Invoicing.Api/Migrations/*_PaymentVatAndGraveShare.cs" >&2
    echo "but found $MIGRATION_COUNT. Nothing was read or changed." >&2
    exit 1
fi

# The backup below is a plain `cp` of the main database file. SQLite in WAL
# mode keeps committed pages in "$DB-wal" until a checkpoint, so with a -wal
# present that copy is not a consistent snapshot -- and whatever is holding the
# database open is a running app whose own migrator this would race.
if [ -e "$DB-wal" ]; then
    echo "patch-dev-db.sh: $DB-wal exists, so something has $DB open in WAL mode." >&2
    echo "A backup taken now would miss the write-ahead log, and the patch would" >&2
    echo "race the running app's migrator. Stop the dev stack first:" >&2
    echo "  docker compose stop backend" >&2
    echo "Nothing was read or changed." >&2
    exit 1
fi

# --- Precondition check: read-only, no backup taken yet. -------------------
set +e
CHECK_OUTPUT="$(python3 - "$DB" "$NEW_ID" <<'PY'
import sqlite3, sys

db_path, new_id = sys.argv[1], sys.argv[2]
db = sqlite3.connect(db_path)


def columns(table):
    return {r[1] for r in db.execute(f"pragma table_info({table})")}


def index_exists(name, table):
    return (
        db.execute(
            "SELECT 1 FROM sqlite_master WHERE type='index' AND name=? AND tbl_name=?",
            (name, table),
        ).fetchone()
        is not None
    )


def table_exists(name):
    return (
        db.execute(
            "SELECT 1 FROM sqlite_master WHERE type='table' AND name=?", (name,)
        ).fetchone()
        is not None
    )


INDEX_NAME = "IX_Invoices_GraveId_IsLatestRevision_PaidOn"

if not table_exists("__EFMigrationsHistory"):
    print("REFUSE")
    print("__EFMigrationsHistory does not exist -- this is not an EF-managed database.")
    sys.exit(3)

history = [
    r[0]
    for r in db.execute("SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId")
]

if not history:
    print("REFUSE")
    print("__EFMigrationsHistory is empty -- cannot tell what has already been applied.")
    sys.exit(3)

has_paidon = "PaidOn" in columns("Invoices")
has_index = index_exists(INDEX_NAME, "Invoices")
has_recipient_count = "RecipientCount" in columns("Invoices")
has_il_vat = "PriceIncludesVat" in columns("InvoiceLines")
has_gi_vat = "PriceIncludesVat" in columns("GraveItems")
has_payment_status_row = any(m.endswith("_InvoicePaymentStatus") for m in history)
has_consolidated_row = new_id in history
last_id = history[-1]

already_applied = (
    has_consolidated_row
    and has_paidon
    and has_index
    and has_recipient_count
    and has_il_vat
    and has_gi_vat
)
pristine = (
    not has_consolidated_row
    and not has_payment_status_row
    and not has_paidon
    and not has_index
    and not has_recipient_count
    and not has_il_vat
    and not has_gi_vat
    and last_id.endswith("_LatestRevisionAndGraveUnitPrice")
)
premise_holds = has_paidon and has_index and has_payment_status_row

if already_applied:
    print("ALREADY_APPLIED")
    print("The consolidated migration is already fully applied. Nothing to do.")
    print("history:", *history, sep="\n  ")
    sys.exit(0)

if pristine:
    print("PRISTINE")
    print(
        "None of the unreleased changes are present, and migration history ends at "
        "LatestRevisionAndGraveUnitPrice. This database has never applied "
        "InvoicePaymentStatus or VatIncludedLinePrices, so there is nothing to hand-patch "
        "-- the app's own migration runner can apply the consolidated migration directly."
    )
    print("history:", *history, sep="\n  ")
    sys.exit(0)

if premise_holds:
    print("PATCH")
    sys.exit(0)

print("REFUSE")
print("This database matches neither the expected premise nor a recognised safe state.")
print(f"  Invoices.PaidOn column present:      {has_paidon}  (expected True)")
print(f"  Index {INDEX_NAME} present: {has_index}  (expected True)")
print(f"  History has a row ending '_InvoicePaymentStatus': {has_payment_status_row}  (expected True)")
print(f"  History has the consolidated id ({new_id}): {has_consolidated_row}")
print(f"  Invoices.RecipientCount already present:       {has_recipient_count}")
print(f"  InvoiceLines.PriceIncludesVat already present: {has_il_vat}")
print(f"  GraveItems.PriceIncludesVat already present:   {has_gi_vat}")
print("  history:", *history, sep="\n    ")
sys.exit(3)
PY
)"
CHECK_STATUS=$?
set -e

CHECK_TAG="$(printf '%s\n' "$CHECK_OUTPUT" | head -n1)"
CHECK_BODY="$(printf '%s\n' "$CHECK_OUTPUT" | tail -n +2)"

if [ "$CHECK_STATUS" -ne 0 ]; then
    echo "patch-dev-db.sh: refusing to touch $DB" >&2
    printf '%s\n' "$CHECK_BODY" >&2
    exit 1
fi

if [ "$CHECK_TAG" = "ALREADY_APPLIED" ]; then
    printf '%s\n' "$CHECK_BODY"
    exit 0
fi

if [ "$CHECK_TAG" = "PRISTINE" ]; then
    printf '%s\n' "$CHECK_BODY"
    exit 0
fi

# --- Precondition holds: back up, then patch. -------------------------------
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
