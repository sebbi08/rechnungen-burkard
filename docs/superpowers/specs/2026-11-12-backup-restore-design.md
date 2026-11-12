# Design: Backing Up and Restoring All Data

**Created**: 2026-11-12

**Status**: Approved design (pre-implementation), approved by the owner on 2026-11-12

**Source**: The owner's request of 2026-11-12 ("Bau die Möglichkeit ein, dass man
alle Daten exportieren kann, so dass man das Ganze umziehen kann. Auch die
Settings sollen in diesem Export vorhanden sein. Die soll auch als Backup
dienen.") and their answers in the same conversation.

## 1. Intent

The owner wants to move the whole application from one instance to another
(another server, another machine) and end up with **everything exactly as it
was**: the same graves, recipients, items, invoices with their numbers,
revisions, payments and PDFs, deleted graves, invoice runs, and settings. The
same file doubles as a manual backup: if an instance is lost, the last backup
file brings it back.

Backups are taken **by hand** from the app. Scheduled backups run entirely
outside the app (the owner's own tooling), which also restores them itself by
putting all files back; the app neither makes nor reads those (the owner's
decision). "Sicherung einspielen" takes only backups the app downloaded.

## 2. What "all data" is

Everything the app keeps lives in its one SQLite database (the connection
string `ConnectionStrings:Invoicing`, `/data/invoicing.db` in the containers):
master data, invoices with their generated PDF documents (stored as blobs in
the `Documents` table), invoice runs and jobs, and the settings (VAT rates,
cemeteries, ending text, stamp word). There are no files beside it that would
have to move. The letterhead and fonts are part of the image, not data.

So a backup is a consistent copy of that database, and a restore replaces it.
The existing CSV import/export (FR-049 ff.) stays as it is: it is for bringing
data in from elsewhere, not for moving an instance.

## 3. Decisions

| Topic                    | Decision                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                        |
| ------------------------ | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Where                    | A new card "Sicherung" on the Import/Export page, beside the existing template and import cards, with two actions: "Sicherung herunterladen" and "Sicherung einspielen".                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                        |
| What the backup is       | A copy of the whole database, taken consistently while the app runs (SQLite's online backup or `VACUUM INTO`, not a plain file copy of a database in use). Not CSV: only the database itself carries invoices, revisions, PDFs and history exactly.                                                                                                                                                                                                                                                                                                                                                                                                                                             |
| File                     | One ZIP, named `rechnungen-sicherung-YYYY-MM-DD-HHmm.zip` (local time, Europe/Berlin), holding the database file and a small manifest (app version, the newest migration the database has, the time it was taken). Claude's call.                                                                                                                                                                                                                                                                                                                                                                                                                                                               |
| Size                     | No small fixed limit on download or upload: the file grows with every invoice's PDFs over the years. The upload limit is set well above any realistic size (e.g. 2 GB), and the upload is streamed to disk rather than held in memory. Claude's call.                                                                                                                                                                                                                                                                                                                                                                                                                                           |
| Restoring replaces       | Restoring replaces **all** data of the instance with the backup's, whatever the instance held before (the owner's decision). Afterwards the instance is exactly the instance the backup came from.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                              |
| Confirmation             | Restoring is two steps (Claude's call). First the file is uploaded and checked (see the next rows) without changing anything; the app answers with what the backup holds: when it was taken, from which app version, and the counts of graves, recipients and invoices. Then a dialog shows that, warns plainly that every piece of data on this instance is replaced by the backup's, and the owner confirms explicitly. If this instance holds invoices newer than the backup's newest, the dialog says so in its own warning: they are lost, and their invoice numbers will be issued again. A checked upload that is never confirmed is discarded (on cancel, and otherwise after a while). |
| Safety copy              | The replaced database is not deleted: it is moved next to the database with a timestamp (e.g. `invoicing.db.before-restore-20261112-1430`), as `scripts/reset-dev-db.sh` keeps the old one, so a restore made by mistake can be undone by hand. The app never cleans these up. Claude's call.                                                                                                                                                                                                                                                                                                                                                                                                   |
| Checked before replacing | Nothing is replaced unless the uploaded file is a backup this version can take: a ZIP with the manifest and a database that SQLite opens and finds intact (`PRAGMA integrity_check`), that has this app's migration history, and whose migrations are all known to the running version. A file that fails any of this is refused with a message saying why, and the instance is unchanged. Claude's call.                                                                                                                                                                                                                                                                                       |
| Older and newer versions | A backup from an **older** app version is accepted and migrated to the running version as part of the restore. A backup from a **newer** version (it has migrations the running version does not know) is refused: the owner updates the target instance first. Claude's call.                                                                                                                                                                                                                                                                                                                                                                                                                  |
| Failure during restore   | If anything fails after the old database was moved aside (moving the new one in, migrating it), the old database is put back and the instance is unchanged. Claude's call.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                      |
| While a job is active    | Both downloading and restoring are refused while any job is queued or running (an invoice run or PDF generation), with the "already running" conflict the grave delete uses. A backup therefore never contains a half-finished run. While a restore is in progress, no new job starts and no other request touches the database (Claude's call).                                                                                                                                                                                                                                                                                                                                                |
| During the restore       | The restore is a long-running task: the UI shows the blocking progress indicator (constitution VIII). Other requests arriving meanwhile wait for the swap to finish (or fail cleanly); none of them may read from or write to a database that is half swapped. Claude's call.                                                                                                                                                                                                                                                                                                                                                                                                                   |
| After the restore        | The page reports success and reloads the app, so nothing shown comes from the old data. Other open browsers (there is no login, and phones and tablets are supported) must not write their stale state into the restored data: every database gets an identity that changes with each restore, the frontend sends the one it loaded with every write, and a write carrying an old one is refused with a message asking to reload (Claude's call).                                                                                                                                                                                                                                               |
| API                      | On the import/export controller, beside `ImportTemplate` and `ImportArchive`: a GET that streams the backup ZIP; a POST that takes an upload (multipart, field `file`, as the import), checks it and answers with its summary and a token; a POST that confirms a token and swaps; and a way to discard a token. Error answers use the existing `{ error: { code, message } }` shape with codes for "not a backup", "damaged", "from a newer version", "job running" and "data replaced, reload". Claude's call.                                                                                                                                                                                |
| Specification            | Two new user stories (download a backup; restore a backup) and new functional requirements continuing after FR-077 go into `docs/spec.md`. Claude's call.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                       |
| Migration                | None: the schema does not change.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                               |

## 4. Non-goals

- Scheduled or automatic backups inside the app (they run outside it).
- Merging a backup into existing data, or restoring only part of it.
- Exporting to other programs (CSV of invoices and the like).
- Encrypting the backup file. It holds customers' personal data; keeping it
  safe is up to whoever stores it (the UI may say so in one sentence).
- Managing or deleting the safety copies from the app.

## 5. Constraints

- Constitution IV: SQLite stays the store; no schema change is needed.
- Constitution VII: all visible texts in German, in the i18n catalogue.
- Constitution VIII: the restore blocks the UI with a progress indicator.
- `Microsoft.Data.Sqlite` pools connections, and a pooled connection keeps the
  old file open: the swap has to clear the pools and make sure no
  `AppDbContext` holds a connection across it.
- The E2E suite runs fully parallel against one shared stack (CLAUDE.md). A
  test that restores a backup replaces the data every other running test
  depends on, and a test that downloads one sees everyone's data. Whatever
  covers the restore end to end must not run alongside the other specs.
- The backup holds personal data. No test, log or report may print its content.

## 6. Left to the builder

- Online backup API versus `VACUUM INTO`, and the manifest's exact format.
- How the database identity of the stale-tab guard is stored and carried (a header on writes, a value in the database or beside it).
- How requests are held off during the swap (a gate in front of the database,
  a middleware, a lock) and how a request that arrives meanwhile is answered.
- Where the upload is staged on disk before it is checked (it must be on the
  same volume as the database so the final move is a rename).
- How the restore is covered by tests: integration tests (round trip, refusals,
  rollback on a failed migration) are the natural home; whether an E2E spec
  restores, and how it is kept apart from the parallel specs (its own project
  run after the others, or its own stack), is the builder's choice within the
  constraint above.
- The German wording of the card, dialog and messages.
