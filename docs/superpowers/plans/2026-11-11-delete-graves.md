# Deleting Graves Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A grave can be deleted (marked, confirmed, restorable until the next invoice run), deleted graves and their invoices leave every list and count, and ended or deleted graves get a page "Inaktive Gräber".

**Architecture:** `Grave.DeletedAt` (new migration); `DeleteGrave`/`RestoreGrave` actions beside `SaveGrave`; every list, run and invoice action filters or refuses deleted graves; the detail page carries the actions; a new page lists ended and deleted graves in four panels.

**Tech Stack:** .NET / EF Core (SQLite) / ASP.NET OData, xUnit; Vue 3 + PrimeVue, Playwright.

**Spec:** `docs/superpowers/specs/2026-11-11-delete-graves-design.md` (read §2 first).

## Global Constraints

- Branch `feat/delete-graves` (checked out). Never push; never start the dev stack on 8080; never run `scripts/reset-dev-db.sh`; never read `data/` or `export/`.
- Code in English; every UI string in `frontend/src/i18n/de.json`, German only. Names: action "Grab löschen", "Wiederherstellen", page "Inaktive Gräber", panels "Beendete Gräber", "Gelöschte Gräber", "Beendete Genossenschaftsgräber", "Gelöschte Genossenschaftsgräber". Error codes: `GraveDeleted`, `GraveNotDeleted`, `RestoreWindowClosed` (and the existing `GraveNumberInUse` for a restore that would overlap).
- New EF migration `GraveDeletedAt` via `scripts/ef.sh migrations add GraveDeletedAt --project src/Invoicing.Api` (from `backend/`). Existing migrations stay untouched.
- New routes follow `SaveGrave`: `POST odata/DeleteGrave` and `POST odata/RestoreGrave`, body `{ "graveId": <id> }`, PascalCase success JSON, camelCase error bodies, hand-typed in `frontend/src/api/endpoints.ts`.
- "A run since the deletion" = a `Job` with `Type == "GraveInvoices"`, `Status == Succeeded`, `FinishedAt > grave.DeletedAt`.
- Backend tests: clear `backend/**/bin|obj` on NETSDK1064, `scripts/dotnet.sh test Invoicing.sln`, never `docker compose start backend` afterwards.
- Frontend: `npm run typecheck && npm run lint && npm run build` clean; after the EDM change `npm run gen:odata` on the E2E stack, keep `metadata.xml` pretty-printed (Prettier 3 + `@prettier/plugin-xml`, `--tab-width 4 --print-width 100 --xml-whitespace-sensitivity ignore`, plugin not in package.json).
- E2E only via `cd e2e && npm test …` (own stack, 8081), `e2e/vue.ts` helpers, `npm run stack:down` afterwards. No URL may contain `;`. Graves an E2E test needs uninvoiced get a term no test generates (from 2044).
- Each task updates the `docs/spec.md` requirements it touches. Conventional Commits ending with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>` and `Claude-Session: https://claude.ai/code/session_015EooesU4igSCakatgHZKpJ`.

## Review Focus

1. A deleted grave must not be invoiced by a run, the correction precheck or a direct generator call, even when fully billable. Pinned in Task 2.
2. Restoring must be refused after a succeeded run for ANY half-year, but not after a failed one. Pinned in Task 1.
3. Restoring a grave whose number a newer grave now uses for an overlapping term is refused with `GraveNumberInUse`, and the grave stays deleted. Pinned in Task 1.
4. A deleted grave's invoices can neither be marked paid nor revised, but its grave invoice page still opens. Pinned in Tasks 2 and 3.
5. A deleted cooperative grave appears neither in "Genossenschaftsgräber" nor under "Beendete Genossenschaftsgräber", only under "Gelöschte Genossenschaftsgräber". Pinned in Task 4.

---

### Task 1: Delete and restore on the server

**Files:**

- Modify: `backend/src/Invoicing.Domain/Entities/Grave.cs`, `backend/src/Invoicing.Api/Services/GraveService.cs`, `backend/src/Invoicing.Api/OData/Controllers/GravesController.cs`, `docs/spec.md` (new FR-018e)
- Create: migration `GraveDeletedAt`
- Test: create `backend/tests/Invoicing.IntegrationTests/DeleteGraveTests.cs`

**Interfaces:**

- Produces: `Grave.DeletedAt` (`DateTime?`); `GraveService.DeleteAsync(long graveId, CancellationToken)` and `RestoreAsync(long graveId, CancellationToken)`; routes `POST odata/DeleteGrave` / `odata/RestoreGrave` returning `{ Id }`; `Graves(key)` JSON gains `DeletedAt` and `CanRestore` (`bool`: deleted and no succeeded GraveInvoices job finished after `DeletedAt`); the `Graves` set exposes `DeletedAt`.

- [ ] **Step 1: Failing tests** (`DeleteGraveTests`):
  - `Deleting_marks_the_grave` — 200; `Graves(id).DeletedAt` set, `CanRestore` true; deleting again → 400 `GraveDeleted`; unknown id → 404 or `GraveNotFound` (follow `SaveGrave`).
  - `A_deleted_grave_cannot_be_saved` — SaveGrave on it → 400 `GraveDeleted`; a hand-over (`succeedsGraveId`) from it → 400 `GraveDeleted`.
  - `Restoring_brings_the_grave_back` — 200; `DeletedAt` null; it can be deleted again.
  - `Restoring_an_undeleted_grave_is_refused` → 400 `GraveNotDeleted`.
  - `A_succeeded_run_for_any_half_year_closes_the_restore_window` — delete, run H1 2027 for an unrelated grave (`GraveGenerationHelper.RunAsync`), restore → 400 `RestoreWindowClosed`; `CanRestore` false.
  - `A_failed_run_leaves_the_window_open` — insert a `Job` (`Type "GraveInvoices"`, `Status Failed`, `FinishedAt` after deletion) through the DbContext → restore 200.
  - `A_deleted_number_is_free_and_restoring_into_an_overlap_is_refused` — delete K-1 (2026-01, no end), save a new grave K-1 with the same term → 200; restore the old one → 400 `GraveNumberInUse`, still deleted.
- [ ] **Step 2: Run, expect FAIL.**
- [ ] **Step 3: Implement** — entity + migration; service methods (refusals before writes); `SaveAsync` refuses a stored deleted grave and a deleted predecessor; the overlap check (FR-018a) ignores deleted graves; controller routes and `Get(key)` projection.
- [ ] **Step 4: Full backend suite, expect PASS.**
- [ ] **Step 5: Commit** `feat: delete a grave and restore it until the next invoice run`

### Task 2: Deleted graves leave runs, lists and invoice actions

**Files:**

- Modify: `backend/src/Invoicing.Api/Services/GraveBilling.cs` (`OpenForHalfYearAsync`), `Services/GraveCorrectionCheck.cs`, `Services/GraveInvoiceGenerator.cs` (skip reason `"Deleted"`), `OData/Controllers/GraveInvoicingStatusController.cs`, `OData/Controllers/ItemGravesController.cs`, `OData/Controllers/BulkDownloadController.cs`, `OData/Controllers/InvoiceActionsController.cs` (`SetInvoicePaid`, `CreateGraveInvoiceRevision` → 400 `GraveDeleted`), `frontend/src/i18n/de.json` (`skipReasonDeleted`, `errorGraveDeleted`), `docs/spec.md` (FR-024, FR-025, the bulk download and payment requirements)
- Test: `backend/tests/Invoicing.IntegrationTests/DeleteGraveTests.cs`

**Interfaces:**

- Consumes: `Grave.DeletedAt` (Task 1).

- [ ] **Step 1: Failing tests:**
  - `A_run_never_invoices_a_deleted_grave` — billable grave, delete, run → no invoices, no run row; generator called directly → skip reason `"Deleted"`; the correction precheck ignores it (3 recipients, 100,00 €).
  - `The_overview_leaves_deleted_graves_out` — also one that was invoiced before deletion is absent from that half-year.
  - `Item_graves_leave_deleted_graves_out`.
  - `The_bulk_download_leaves_out_invoices_of_deleted_graves` — two graves invoiced in a half-year, delete one → the ZIP holds only the other's documents (see `BulkDownloadTests` for the request).
  - `Invoices_of_a_deleted_grave_cannot_be_marked_paid_or_revised` → 400 `GraveDeleted` each; `GraveInvoiceGroup` for it still answers 200.
- [ ] **Step 2: Run, expect FAIL.** **Step 3: Implement.** **Step 4: Full backend suite, PASS.**
- [ ] **Step 5: Commit** `feat: keep deleted graves and their invoices out of runs, lists and payments`

### Task 3: Frontend — the actions on the detail page, the cooperative list

**Files:**

- Modify: `frontend/src/api/endpoints.ts`, `frontend/src/api/metadata.xml` + `src/api/generated/*` (gen:odata), `frontend/src/pages/GraveDetailPage.vue`, `frontend/src/pages/GraveInvoiceDetailPage.vue`, `frontend/src/pages/CooperativeGravesPage.vue`, `frontend/src/i18n/de.json`, `docs/spec.md` (FR-024a)
- Test: `e2e/tests/graves.spec.ts`, `e2e/tests/cooperative-graves.spec.ts`

**Interfaces:**

- Consumes: Task 1–2 routes and fields.
- Produces: `deleteGrave(graveId: number): Promise<void>`, `restoreGrave(graveId: number): Promise<void>`; `GraveDetails.DeletedAt: string | null`, `GraveDetails.CanRestore: boolean`; `CooperativeGraveRow.DeletedAt: string | null`.

- [ ] **Step 1: Failing E2E tests:**
  - `deleting a grave asks first and explains what follows` — "Grab löschen" opens a dialog naming the consequences (lists, statistics, no more invoices, invoices no longer count, restore only until the next run); "Abbrechen" changes nothing; confirming → the page shows the grave as deleted, "Bearbeiten" is gone, "Wiederherstellen" is offered.
  - `a deleted grave can be restored before the next run` — term from 2044 (no run touches it; a run for another half-year may still close the window, so assert only that either "Wiederherstellen" works and the grave is back, or the page explains that restoring is no longer possible — prefer asserting the action right after deleting, accepting the race as negligible within one test; say so in a comment).
  - `the grave invoice page of a deleted grave offers no payment or revision` — seed and invoice a grave in a far half-year inside the serial group (or via an existing invoiced-grave helper), delete it, open its invoice page → no "Als bezahlt markieren", no revision action.
  - cooperative-graves.spec `the list shows only current and future cooperative graves` — one ended last year, one running, one starting 2044, one deleted → only the running and the future one; no "Laufzeit" select.
- [ ] **Step 2: FAIL. Step 3: Implement** (confirm dialog via PrimeVue `useConfirm` or a Dialog as the app does elsewhere; the cooperative list drops its `term` filter and keeps rows with `!DeletedAt && (EndDate == null || EndDate >= first month of the current half-year)`). **Step 4: PASS + checks.**
- [ ] **Step 5: Commit** `feat: delete and restore a grave from its page, list only current cooperative graves`

### Task 4: The page "Inaktive Gräber"

**Files:**

- Create: `frontend/src/pages/InactiveGravesPage.vue`
- Modify: `frontend/src/router.ts` (`/inactive-graves`; detail pages opened from it keep the menu marked — add `/inactive-graves/:graveId` like `/cooperative-graves/:graveId`, with `list: "/inactive-graves"`), `frontend/src/components/NavLinks.vue` (after "Genossenschaftsgräber", key `menuInactiveGraves`), `frontend/src/pages/GraveDetailPage.vue` (the `list` prop gains `"/inactive-graves"`), `frontend/src/api/endpoints.ts` (`fetchInactiveGraves(): Promise<InactiveGraveRow[]>` via the `Graves` set, no `;`), `frontend/src/i18n/de.json`, `docs/spec.md` (new FR-024b)
- Test: create `e2e/tests/inactive-graves.spec.ts`; `e2e/tests/layout.spec.ts` (phone/tablet)

**Interfaces:**

- Produces: `InactiveGraveRow { Id, GraveNumber, SiteName, CemeteryName, StartDate, EndDate, IsCooperative, ContractNumber, DeletedAt }`.

- [ ] **Step 1: Failing E2E tests:**
  - `the page sorts graves into its four panels` — seed an ended ordinary grave (end last year), a deleted ordinary one, an ended cooperative one, a deleted cooperative one (deleted while running, and one deleted and ended: it shows only under "Gelöscht…"), and a running grave → each panel lists exactly its grave(s) by marker; the running one is nowhere.
  - `a panel searches and opens a grave` — text search narrows a panel; a row opens `/inactive-graves/<id>` with the menu entry "Inaktive Gräber" marked and a breadcrumb back.
  - layout: phone and tablet fit, as for the other list pages.
- [ ] **Step 2: FAIL. Step 3: Implement** — ended = `EndDate` before the first month of the current half-year (browser's current half-year, as the other lists); deleted = `DeletedAt` set; deleted wins over ended; panels as cards or an accordion (builder's call). **Step 4: PASS + checks.**
- [ ] **Step 5: Commit** `feat: list ended and deleted graves on a page of their own`

### Task 5: Close out

- [ ] **Step 1:** Full backend suite, full E2E (all projects), frontend checks, `npm run format:check` (ignore `.claude/settings.local.json`).
- [ ] **Step 2:** Whole-branch review, fixes, superpowers:finishing-a-development-branch.
