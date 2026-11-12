# Backup and Restore Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Download a consistent copy of the whole database as a backup ZIP, and restore such a ZIP on another (or the same) instance, replacing all of its data.

**Architecture:** A `BackupService` writes the live SQLite database with `VACUUM INTO` into a ZIP beside a JSON manifest. Restoring is two requests: an upload that stages and checks the file and returns a summary and token, and a confirm that swaps the database file behind a `DatabaseGate` (requests answer 503 while it is closed), keeps the old file as a safety copy, migrates, and rolls back on failure. A data generation number in `PRAGMA user_version` changes with every restore; the frontend sends the one it saw with every write, and stale writes are refused.

**Tech Stack:** ASP.NET Core 10 + EF Core SQLite (`Microsoft.Data.Sqlite`), xunit with `ApiTestHost` (`WebApplicationFactory`), Vue 3 + PrimeVue + vue-i18n, Playwright.

**Spec:** `docs/superpowers/specs/2026-11-12-backup-restore-design.md`

## Global Constraints

- Everything the app keeps is in the one SQLite database; the backup is that database, not CSV. No schema change, so no migration (constitution IV).
- File name: `rechnungen-sicherung-YYYY-MM-DD-HHmm.zip`, local time Europe/Berlin (`IClock.GetLocalNow()`).
- Upload limit 2 GB (`2_147_483_648` bytes), not held in memory.
- Restoring replaces **all** data. The replaced database is kept next to it as `<db file name>.before-restore-yyyyMMdd-HHmmss` (sidecars `-wal`/`-shm` likewise); the app never deletes these.
- Refuse: not a backup → `BackupInvalid`; unreadable or `PRAGMA integrity_check` ≠ `ok` → `BackupDamaged`; a migration the running app does not know → `BackupFromNewerVersion`. An older backup is accepted and migrated.
- Downloading, uploading and restoring are refused with 409 `JobAlreadyRunning` while any job is `Queued` or `Running`.
- Any failure after the old file was moved aside puts it back: the instance is unchanged (500 `RestoreFailed`).
- Error bodies `{ error: { code, message } }`; the upload's answer is camelCase like `ImportArchive`'s (`docs/deviations.md` D-001 containment).
- All visible text German, in `frontend/src/i18n/de.json` (constitution VII); restore runs behind `useBlocking().run` (constitution VIII).
- The backup holds personal data: no test, log or report prints its content.
- Backend tests share `backend/obj` with the dev container: `docker compose stop backend`, delete `bin`/`obj` (CLAUDE.md), run `scripts/dotnet.sh test Invoicing.sln`, then `docker compose start backend`.
- E2E only against its own stack (`cd e2e && npm test`), never the dev stack on 8080.

## Review Focus

1. A request arriving while the database is being swapped must get 503 `RestoreInProgress`, never a read from a half-swapped or closed file — Task 3, test `RequestsWhileGateClosed_Answer503`.
2. Uploading the wrong ZIP (the CSV import archive, or a random ZIP) must be refused as `BackupInvalid` and change nothing — Task 2, test `Upload_ImportArchive_IsBackupInvalid`.
3. Confirming a token twice, or after discarding it, must be refused (`BackupTokenUnknown`), not restore again — Task 3, test `Restore_TokenUsedOrDiscarded_IsUnknown`.
4. Writes made just before the download (still in the WAL) must be in the backup — Task 1, test `Download_ContainsWriteJustMade`.
5. A tab opened before a restore must not write into the restored data, while clients that send no generation (seeding, curl) keep working — Task 4, tests `StaleGeneration_WriteRefused`, `NoGenerationHeader_WriteAllowed`.

---

### Task 1: Download a backup

**Files:**

- Create: `backend/src/Invoicing.Api/Services/Backup/BackupManifest.cs`
- Create: `backend/src/Invoicing.Api/Services/Backup/BackupService.cs`
- Create: `backend/src/Invoicing.Api/OData/Controllers/BackupController.cs`
- Modify: `backend/src/Invoicing.Api/Services/JobService.cs` (add `HasAnyActive`)
- Modify: `backend/src/Invoicing.Api/Program.cs` (register `BackupService` scoped)
- Test: `backend/tests/Invoicing.IntegrationTests/BackupTests.cs`

**Interfaces:**

- Produces:
  - `public sealed record BackupManifest(string Format, int FormatVersion, string AppVersion, string LastMigration, DateTime CreatedAtUtc)` with `public const string FormatName = "rechnungen-burkard-backup";`, `FormatVersion` = 1. Serialized as `manifest.json` (camelCase) in the ZIP root; the database as `invoicing.db`.
  - `BackupService.WriteBackupAsync(Stream output, CancellationToken ct): Task` — `VACUUM INTO` a temp file in the database's directory, ZIP manifest + file into `output`, delete the temp file.
  - `BackupService.DatabasePath: string` — from `db.Database.GetConnectionString()` via `SqliteConnectionStringBuilder.DataSource` (the test host swaps the connection string, so never read configuration).
  - `JobService.HasAnyActive(): bool` — any job `Queued` or `Running`, whatever its type.
  - Route `GET /odata/DownloadBackup` → `application/zip`, `Content-Disposition` file name `rechnungen-sicherung-yyyy-MM-dd-HHmm.zip`; 409 `JobAlreadyRunning`.
  - `AppVersion` = the entry assembly's `AssemblyInformationalVersionAttribute` without a `+commit` suffix; `LastMigration` = last of `db.Database.GetAppliedMigrations()`.

- [ ] **Step 1: Write the failing tests** in `BackupTests.cs` (helper `static ZipArchive OpenZip(byte[])`, helper `static string ExtractDb(byte[] zip)` that writes `invoicing.db` to a temp file):

```csharp
[Fact] Download_IsZipWithManifestAndDatabase
// GET /odata/DownloadBackup → 200, Content-Type application/zip,
// file name matches ^rechnungen-sicherung-2026-10-06-\d{4}\.zip$ (ApiTestHost.Now = 2026-10-06 12:00),
// entries exactly { "manifest.json", "invoicing.db" },
// manifest.format == "rechnungen-burkard-backup", formatVersion == 1,
// lastMigration == "20261111213228_GraveContractNumberAndDeletedAt".

[Fact] Download_ContainsWriteJustMade
// POST /odata/Recipients {Name="Backup Probe", Address="…"} then immediately download;
// open the extracted db with SqliteConnection: SELECT COUNT(*) FROM Recipients WHERE Name='Backup Probe' == 1.

[Fact] Download_WhileJobActive_Is409
// insert a Job { Type="GraveInvoices", Status=Running } through AppDbContext (as JobTests does);
// GET → 409, error.code == "JobAlreadyRunning".
```

- [ ] **Step 2: Run them to verify they fail**

Run: `scripts/dotnet.sh test Invoicing.sln --filter FullyQualifiedName~BackupTests`
Expected: FAIL (404 on `/odata/DownloadBackup`).

- [ ] **Step 3: Implement** `BackupManifest`, `BackupService.WriteBackupAsync`, `JobService.HasAnyActive`, and `BackupController` (`[Route("odata")]`, plain MVC like `ImportExportController`). `VACUUM INTO` runs on a fresh `SqliteConnection` to `DatabasePath`; the temp file name `backup-<guid>.db`.

- [ ] **Step 4: Run the tests to verify they pass** (same command). Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Invoicing.Api backend/tests/Invoicing.IntegrationTests/BackupTests.cs
git commit -m "feat: download a backup of the whole database"
```

---

### Task 2: Upload and check a backup

**Files:**

- Create: `backend/src/Invoicing.Api/Services/Backup/BackupStaging.cs` (checks and summary)
- Modify: `backend/src/Invoicing.Api/Services/Backup/BackupService.cs`
- Modify: `backend/src/Invoicing.Api/OData/Controllers/BackupController.cs`
- Test: `backend/tests/Invoicing.IntegrationTests/BackupTests.cs`

**Interfaces:**

- Consumes: Task 1's `BackupManifest`, `DatabasePath`, `HasAnyActive`.
- Produces:
  - `public sealed record BackupSummary(DateTime CreatedAtUtc, string AppVersion, int Graves, int Recipients, int Invoices, bool NewerInvoicesHere)` — `Invoices` = `COUNT(DISTINCT RootId)`; `NewerInvoicesHere` = the live database's `MAX(Invoices.GeneratedAt)` is later than the backup's (false when the live one has none).
  - `public sealed record StagedBackup(string Token, BackupSummary Summary)`.
  - `public sealed class BackupRejectedException(string code) : Exception` with `Code`.
  - `BackupService.StageAsync(Stream upload, CancellationToken ct): Task<StagedBackup>` — saves under `<db dir>/restore-staging/<token>.db` (token = `Guid.NewGuid().ToString("N")`), deletes staging files older than one hour first. Throws `BackupRejectedException` with `BackupInvalid` (not a ZIP, no `manifest.json`, wrong `format`, no `invoicing.db`, no `__EFMigrationsHistory`), `BackupDamaged` (SQLite cannot open it, or `PRAGMA integrity_check` ≠ `ok`), `BackupFromNewerVersion` (a `MigrationId` not in `db.Database.GetMigrations()`). A rejected upload leaves no staging file.
  - `BackupService.Discard(string token): void` — deletes the staged file; unknown token is a no-op.
  - `BackupService.StagedPath(string token): string?` — null for an unknown or malformed token (only `[0-9a-f]{32}` accepted).
  - Route `POST /odata/UploadBackup` (multipart field `file`, `[RequestSizeLimit(2_147_483_648)]`, `[RequestFormLimits(MultipartBodyLengthLimit = 2_147_483_648)]`) → 200 `{ token, summary: { createdAt, appVersion, graves, recipients, invoices, newerInvoicesHere } }`; 400 `ArchiveRequired` / the rejection code; 409 `JobAlreadyRunning`.
  - Route `POST /odata/DiscardBackup` JSON `{ token }` → 204.

- [ ] **Step 1: Write the failing tests** (helper `static byte[] BuildZip(string dbPath, object manifest)`; helper `static async Task<string> StageAsync(HttpClient, byte[] zip)` returning the token):

```csharp
[Fact] Upload_OwnBackup_ReturnsSummary
// seed 1 recipient + 1 cooperative grave (as DeleteGraveTests.SaveAsync), download, upload →
// 200, token matches ^[0-9a-f]{32}$, summary.graves == 1, summary.recipients >= 1,
// summary.appVersion non-empty, newerInvoicesHere == false.

[Fact] Upload_ImportArchive_IsBackupInvalid      // GET /odata/ImportTemplate, upload it → 400 BackupInvalid
[Fact] Upload_NotAZip_IsBackupInvalid            // bytes "hello" → 400 BackupInvalid
[Fact] Upload_GarbageDatabase_IsBackupDamaged    // valid manifest + invoicing.db = 4096 random bytes → 400 BackupDamaged
[Fact] Upload_UnknownMigration_IsBackupFromNewerVersion
// extract own backup, INSERT INTO __EFMigrationsHistory VALUES ('29991231000000_Future','10.0.0'), rezip → 400 BackupFromNewerVersion
[Fact] Upload_WhileJobActive_Is409
[Fact] Upload_LiveHasNewerInvoice_FlagsIt
// download first, then create an invoice (as CreateInvoiceTests does), upload the earlier backup → newerInvoicesHere == true
[Fact] Rejected_And_Discarded_LeaveNoStagingFile
// after each rejection and after DiscardBackup: Directory.GetFiles(<db dir>/restore-staging) has no file for it
```

- [ ] **Step 2: Run them to verify they fail** — `--filter FullyQualifiedName~BackupTests`. Expected: the new tests FAIL (404).

- [ ] **Step 3: Implement** `BackupStaging` (open the staged file with `Mode=ReadOnly`, `Pooling=False`), `StageAsync`, `Discard`, `StagedPath`, and the two routes. The ZIP is read from a temp file on disk, not a `MemoryStream`.

- [ ] **Step 4: Run the tests to verify they pass.** Expected: PASS.

- [ ] **Step 5: Commit** — `git commit -m "feat: upload a backup and check it before restoring"`

---

### Task 3: Restore a staged backup behind a closed gate

**Files:**

- Create: `backend/src/Invoicing.Api/Services/Backup/DatabaseGate.cs`
- Create: `backend/src/Invoicing.Api/Services/Backup/DatabaseGateMiddleware.cs`
- Modify: `backend/src/Invoicing.Api/Services/Backup/BackupService.cs`
- Modify: `backend/src/Invoicing.Api/OData/Controllers/BackupController.cs`
- Modify: `backend/src/Invoicing.Api/Program.cs` (singleton gate, middleware before routing)
- Test: `backend/tests/Invoicing.IntegrationTests/BackupTests.cs`

**Interfaces:**

- Consumes: Task 2's `StagedPath`, `Discard`, `BackupRejectedException`.
- Produces:
  - `public sealed class DatabaseGate` (singleton): `bool TryEnter()`, `void Exit()`, `Task CloseAsync(CancellationToken ct)` (refuses new entries, then waits until none is inside), `void Open()`, `bool IsClosed`.
  - `DatabaseGateMiddleware`: for paths under `/odata` and `/healthz`, except `/odata/RestoreBackup` itself, `TryEnter` or answer 503 `{ error: { code: "RestoreInProgress" } }`; `Exit` in `finally`.
  - `BackupService.RestoreAsync(string token, CancellationToken ct): Task` — throws `BackupRejectedException("BackupTokenUnknown")`, `BackupRejectedException("JobAlreadyRunning")`, `RestoreFailedException`. Order: close gate → `SqliteConnection.ClearAllPools()` → move live file and `-wal`/`-shm` to `.before-restore-yyyyMMdd-HHmmss` names → move staged file into place → in a new DI scope `Database.Migrate()` and `PRAGMA journal_mode=WAL` → open gate. On any exception after the move: clear pools, delete the new file, move the old ones back, open the gate, throw `RestoreFailedException`. The staged file is gone afterwards either way.
  - Route `POST /odata/RestoreBackup` JSON `{ token }` → 204; 404 `BackupTokenUnknown`; 409 `JobAlreadyRunning`; 500 `RestoreFailed`.

- [ ] **Step 1: Write the failing tests:**

```csharp
[Fact] Restore_ReplacesAllData_AndKeepsSafetyCopy
// PATCH Settings StampWord="Vorher"; create recipient "A"; download;
// create recipient "B"; PATCH StampWord="Nachher"; upload + restore → 204;
// Recipients has "A" and not "B"; Settings StampWord == "Vorher";
// a file "<db>.before-restore-20261006-120000" exists next to the db.

[Fact] Restore_OlderBackup_IsMigrated
// build a db with IMigrator.Migrate("20261101095804_RecipientEmailAndCemeteries") on a fresh temp file,
// zip with manifest, upload + restore → 204; GET /odata/Graves?$select=ContractNumber → 200.

[Fact] Restore_MigrationFails_PutsOldDatabaseBack
// own backup with its last __EFMigrationsHistory row deleted (re-applying it fails); create recipient "Live" after download;
// upload + restore → 500 RestoreFailed; Recipients still has "Live"; a following GET answers 200 (gate open).

[Fact] Restore_TokenUsedOrDiscarded_IsUnknown
// restore a token twice → second 404 BackupTokenUnknown; stage, discard, restore → 404 BackupTokenUnknown.

[Fact] Restore_WhileJobActive_Is409

[Fact] RequestsWhileGateClosed_Answer503
// resolve DatabaseGate from host.Services, await CloseAsync; GET /odata/Recipients → 503 RestoreInProgress; Open(); → 200.
```

- [ ] **Step 2: Run them to verify they fail.** Expected: FAIL (404).
- [ ] **Step 3: Implement** gate, middleware, `RestoreAsync`, route. Register the middleware so it also wraps `/healthz`.
- [ ] **Step 4: Run the whole backend suite** — `scripts/dotnet.sh test Invoicing.sln`. Expected: all PASS (the middleware is on every request).
- [ ] **Step 5: Commit** — `git commit -m "feat: restore a backup, keep the replaced database, roll back on failure"`

---

### Task 4: Refuse writes from tabs opened before a restore

**Files:**

- Create: `backend/src/Invoicing.Api/Services/Backup/DataGeneration.cs`
- Modify: `backend/src/Invoicing.Api/Services/Backup/DatabaseGateMiddleware.cs` (stamp and check the header)
- Modify: `backend/src/Invoicing.Api/Services/Backup/BackupService.cs` (renew after a successful restore)
- Modify: `backend/src/Invoicing.Api/Program.cs` (load at startup after `Migrate`)
- Test: `backend/tests/Invoicing.IntegrationTests/BackupTests.cs`

**Interfaces:**

- Consumes: Task 3's middleware and `RestoreAsync`.
- Produces:
  - `public sealed class DataGeneration` (singleton): `int Current`, `void Load(AppDbContext db)` (reads `PRAGMA user_version`), `void Renew(AppDbContext db)` (writes a random positive `int` ≠ the old one with `PRAGMA user_version = n`, then sets `Current`).
  - Header `X-Data-Generation`: on every `/odata` response; on a request other than GET/HEAD/OPTIONS that carries it with a value ≠ `Current` → 409 `{ error: { code: "DataReplaced" } }`. No header → allowed.

- [ ] **Step 1: Write the failing tests:**

```csharp
[Fact] Responses_CarryGeneration          // GET /odata/Recipients has header X-Data-Generation
[Fact] StaleGeneration_WriteRefused       // g = header before restore; restore; POST /odata/Recipients with X-Data-Generation: g → 409 DataReplaced
[Fact] CurrentGeneration_WriteAllowed     // after restore, header from a fresh GET → POST 201
[Fact] NoGenerationHeader_WriteAllowed    // after restore, POST without the header → 201
[Fact] Generation_IsStoredInDatabase      // after restore, PRAGMA user_version read from the db file on a fresh SqliteConnection == the response header's value (so it survives a restart)
```

- [ ] **Step 2: Run them to verify they fail.**
- [ ] **Step 3: Implement** `DataGeneration` and the header handling.
- [ ] **Step 4: Run the whole backend suite.** Expected: all PASS.
- [ ] **Step 5: Commit** — `git commit -m "feat: refuse writes from pages that still show replaced data"`

---

### Task 5: Backup card on the Import/Export page

**Files:**

- Create: `frontend/src/api/dataGeneration.ts`
- Modify: `frontend/src/main.ts` (call `installDataGenerationGuard()` before mounting)
- Modify: `frontend/src/api/endpoints.ts`
- Modify: `frontend/src/pages/ImportExportPage.vue`
- Modify: `frontend/src/i18n/de.json`

**Interfaces:**

- Consumes: routes and codes of Tasks 1–4.
- Produces:
  - `installDataGenerationGuard(): void` — wraps `window.fetch` once: remembers the first `X-Data-Generation` response header it sees and adds it to every non-GET request to a `/odata` URL. Covers both `request()` in `http.ts` and the generated `FetchClient`.
  - `downloadBackup(): Promise<{ blob: Blob; fileName: string }>` (file name from `Content-Disposition`), `uploadBackup(file: File): Promise<StagedBackup>`, `restoreBackup(token: string): Promise<void>`, `discardBackup(token: string): Promise<void>`; `interface BackupSummary { createdAt: string; appVersion: string; graves: number; recipients: number; invoices: number; newerInvoicesHere: boolean }`, `interface StagedBackup { token: string; summary: BackupSummary }`.
  - i18n keys: `backupHeader` "Sicherung", `backupHint` (one sentence: the file holds all data including personal data; keep it safe), `backupDownload` "Sicherung herunterladen", `backupRestore` "Sicherung einspielen", `backupChecking` "Sicherung wird geprüft …", `backupRestoring` "Sicherung wird eingespielt …", `backupConfirm` (taken {0} with version {1}: {2} graves, {3} recipients, {4} invoices; all data of this instance is replaced), `backupConfirmNewer` (this instance holds newer invoices; they are lost and their numbers will be issued again), `backupRestored` "Die Sicherung ist eingespielt.", and `errorBackupInvalid`, `errorBackupDamaged`, `errorBackupFromNewerVersion`, `errorBackupTokenUnknown`, `errorRestoreFailed` (nothing was changed), `errorRestoreInProgress`, `errorDataReplaced` (please reload the page).

- [ ] **Step 1: Implement** the API functions and the guard.
- [ ] **Step 2: Implement** the card (third card, after the import card): download button (loading state, `downloadBlob`); restore button opens the file picker, then `run(t("backupChecking"), () => uploadBackup(file))`, then `useConfirmAsync` with `backupConfirm` (+ `backupConfirmNewer` when flagged), date formatted German; on no → `discardBackup`; on yes → `run(t("backupRestoring"), () => restoreBackup(token))`, `showMessage(t("backupRestored"))`, then `window.location.reload()`. Errors via `showError`.
- [ ] **Step 3: Verify** — `cd frontend && npm run typecheck && npm run lint && npm run build`. Expected: all three clean.
- [ ] **Step 4: Commit** — `git commit -m "feat(frontend): download and restore a backup from the import page"`

---

### Task 6: E2E coverage that leaves the shared stack alone

**Files:**

- Create: `e2e/tests/backup.spec.ts`

**Interfaces:**

- Consumes: Task 5's buttons (by their German labels), `withGenerationLock` from `e2e/generation-lock.ts` (download and upload are refused while another test generates).

The specs run fully parallel on one stack, so no spec confirms a restore (that is covered by Task 3's integration tests); confirming would replace every other test's data.

- [ ] **Step 1: Write the tests:**

```ts
test("download a backup holding the database and its manifest", …)
// inside withGenerationLock: click "Sicherung herunterladen", await download;
// suggestedFilename matches /^rechnungen-sicherung-\d{4}-\d{2}-\d{2}-\d{4}\.zip$/;
// AdmZip entries are exactly manifest.json and invoicing.db. Do not print entry contents.

test("refuse a file that is not a backup", …)
// "Sicherung einspielen" with a ZIP built in the test holding one text file → toast with errorBackupInvalid's text; no dialog.

test("cancelling the confirmation changes nothing", …)
// seed a recipient with unique(); inside withGenerationLock download a backup, then choose it again;
// the confirmation shows the counts; press "Abbrechen"; the recipient is still listed and no reload happened.
```

- [ ] **Step 2: Run** — `cd e2e && npx playwright test tests/backup.spec.ts --project=desktop` (starts its own stack). Expected: 3 passed.
- [ ] **Step 3: Run the full suite** — `cd e2e && npm test`. Expected: green.
- [ ] **Step 4: Commit** — `git commit -m "test(e2e): download a backup, refuse a wrong file, cancel a restore"`

---

### Task 7: Specification, deviations and README

**Files:**

- Modify: `docs/spec.md` (User Story 14 "Download a Backup", User Story 15 "Restore a Backup", FR-078 onwards: contents, file name, consistency, refusal while a job runs, two-step restore with summary and the newer-invoices warning, refusals, older/newer versions, safety copy, rollback, requests during the swap, stale writes)
- Modify: `docs/deviations.md` (D-001: add `DownloadBackup`, `UploadBackup`, `RestoreBackup`, `DiscardBackup`; camelCase for the upload's answer)
- Modify: `README.md` ("Backups": the in-app backup for moving and manual backups; the file-copy procedure stays for scheduled backups outside the app)

- [ ] **Step 1: Write the three changes**, values copied from Global Constraints.
- [ ] **Step 2: Verify** — `npm run format:check`. Expected: clean.
- [ ] **Step 3: Commit** — `git commit -m "docs: specify backing up and restoring all data"`
