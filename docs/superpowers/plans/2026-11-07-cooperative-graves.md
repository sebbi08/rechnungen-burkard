# Cooperative Graves Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Graves the cooperative bills ("Genossenschaftsgräber") live in the app like any grave, never get an invoice, show in every grave list and in a list of their own, and come in from FriedWin.

**Architecture:** A boolean `IsCooperative` on `Grave`. Billing selection (`GraveBilling`, `GraveCorrectionCheck`, `GraveInvoiceGenerator`) leaves such graves out; `GraveService` relaxes the billability rules for them; the overview, detail page and item dialog show them with a tag; a new page lists them through the `Graves` OData set. A change "from a month on" ends a grave and starts a successor of the other kind in one `SaveGrave` request. The import gets an optional `Cooperative` column, which the FriedWin conversion fills.

**Tech Stack:** .NET / EF Core (SQLite) / ASP.NET OData, xUnit integration tests; Vue 3 + PrimeVue + TanStack Query, Playwright E2E; Python 3 converter.

**Spec:** `docs/superpowers/specs/2026-11-07-cooperative-graves-design.md` (read it first; this plan does not repeat its reasons).

## Global Constraints

- Branch: `git switch -c feat/cooperative-graves` from the current `fix/friedwin-field-widths` HEAD (it carries the converter fixes Task 12 builds on).
- Code in English; every UI string in `frontend/src/i18n/de.json`, German only (constitution VII). Names fixed by the design: "Genossenschaftsgrab", "Genossenschaftsgräber", tag "Genossenschaft", action "Ab Monat … selbst abrechnen" / "Ab Monat … über die Genossenschaft", button "Neues Genossenschaftsgrab".
- One migration per version: the column goes into `20261101095804_RecipientEmailAndCemeteries` (+ Designer + `AppDbContextModelSnapshot.cs`), no new migration (constitution IV).
- SaveGrave, GraveInvoicingStatus and ItemGraves are non-EDM routes with PascalCase JSON and camelCase error bodies (D-001/D-002); hand-typed in `frontend/src/api/endpoints.ts`.
- Backend tests: `docker compose stop backend`, delete `backend/**/bin|obj`, `scripts/dotnet.sh test Invoicing.sln`, `docker compose start backend` (CLAUDE.md).
- Frontend: `npm run typecheck`, `npm run lint`, `npm run build` clean. After the EDM change: `npm run gen:odata` against the E2E stack, commit `src/api/metadata.xml` with `src/api/generated/`.
- E2E only against `docker-compose.e2e.yml` (`cd e2e && npm test`), never port 8080; dialogs through `e2e/vue.ts` helpers; tests touching the current half-year go into the serial group in `e2e/tests/graves.spec.ts`.
- The dev DB already applied the changed migration. Before restarting the dev backend on the new schema, ask the owner, commit, then `scripts/reset-dev-db.sh` (it has no graves or invoices).
- Each task updates the `docs/spec.md` requirements it touches (listed per task).
- Conventional Commits; `feat`/`fix`/`test`/`docs` as fits.

## Review Focus

1. A cooperative grave that _is_ fully billable (recipient + priced all-year item) must still get no invoice from a run, the precheck or a direct generator call — the grave most likely to slip through `IsDue`. Pinned in Task 2.
2. An existing invoice of a grave that is then split into a cooperative successor stays viewable, downloadable and revisable. Pinned in Task 4.
3. A successor month at or before the predecessor's start, or inside a half-year the predecessor is already invoiced for, is refused with a named error, not a 500 or a silently shortened term. Pinned in Task 4.
4. An archive written before the column (no `Cooperative` header) still imports as ordinary graves; a value like `x` is a problem naming the row. Pinned in Task 6.
5. Unticking a cooperative grave that has no recipient or no all-year item is refused with `NoRecipients` / `AllYearItemRequired` in the editor and on the server, and once fixed the grave shows "offen" in the overview. Pinned in Tasks 1, 3 and 7.

---

### Task 1: The flag, the migration and saving a cooperative grave

**Files:**

- Modify: `backend/src/Invoicing.Domain/Entities/Grave.cs`, `backend/src/Invoicing.Api/Migrations/20261101095804_RecipientEmailAndCemeteries.cs` (+ `.Designer.cs`, `AppDbContextModelSnapshot.cs`), `backend/src/Invoicing.Api/Services/GraveService.cs`, `backend/src/Invoicing.Api/OData/Controllers/GravesController.cs`, `docs/spec.md` (FR-018, FR-019, FR-020, FR-020b)
- Test: create `backend/tests/Invoicing.IntegrationTests/CooperativeGraveTests.cs` (seed like `SaveGraveTests.SeedAsync`)

**Interfaces:**

- Produces: `Grave.IsCooperative` (`bool`, default `false`, column `IsCooperative` NOT NULL DEFAULT 0); `GraveFields(..., DateOnly? EndDate, bool IsCooperative)` (new last member); `SaveGraveRequest.IsCooperative` (`bool?`, null = false); `Graves(key)` JSON gains `IsCooperative` and `HasInvoices` (`bool`: any `Invoice` with this `GraveId`); new error codes `CooperativeFlagFixed`.

- [ ] **Step 1: Write the failing tests** in `CooperativeGraveTests`:
  - `A_cooperative_grave_saves_without_recipients_or_items` — POST SaveGrave `isCooperative=true`, `items=[]`, `recipientIds=[]` → 200; `GET /odata/Graves({id})` has `IsCooperative=true`, `HasInvoices=false`, empty `Items` and `Recipients`.
  - `A_cooperative_grave_may_link_an_item_without_a_price` — item `4002` (no default) without unit price → 200.
  - `A_cooperative_grave_stores_no_corrections` — 3 recipients, item 100,00 € (does not divide), `corrections=[]` → 200, `GraveBillingCorrections` for it empty.
  - `A_never_invoiced_cooperative_grave_may_change_its_recipients` — create with recipient A, save again with B → 200.
  - `Unticking_a_cooperative_grave_without_recipients_is_refused` — save existing cooperative grave with `isCooperative=false` → 400 `NoRecipients`; with a recipient but only quarter items → 400 `AllYearItemRequired`.
  - `Unticking_applies_the_ordinary_rules` — cooperative grave with one recipient and item 4001, untick → 200, `IsCooperative=false`.
  - `The_flag_cannot_change_once_the_grave_has_invoices` — ordinary grave, run H1 2026 (`GraveGenerationHelper.RunAsync`), save with `isCooperative=true` → 400 `CooperativeFlagFixed`.
  - `An_ordinary_grave_keeps_its_rules` — `isCooperative` omitted, no recipients → 400 `NoRecipients` (unchanged behaviour).

- [ ] **Step 2: Run them, expect failures** (`scripts/dotnet.sh test Invoicing.sln --filter CooperativeGraveTests`; unknown property / 400s).

- [ ] **Step 3: Implement**
  - Entity property and migration column (`migrationBuilder.AddColumn<bool>("IsCooperative", "Graves", nullable: false, defaultValue: false)` in Up, `DropColumn` in Down; regenerate Designer/snapshot by hand to match).
  - `GraveService.SaveAsync`: when `fields.IsCooperative`, skip `NoRecipients`, `PriceRequired`, `AllYearItemRequired` and the split evaluation (return `null` evaluation, so `computed = []` and only an empty `corrections` list passes); keep duplicate-link, `TooManyRecipients`, `InvalidCount`, `InvalidPrice`, `ItemNotFound`, `RecipientNotFound`. `GraveRecipientsImmutable` applies only when the stored grave has an invoice. `CooperativeFlagFixed` when the stored flag differs from `fields.IsCooperative` and the grave has an invoice. Assign `grave.IsCooperative`.
  - `GravesController`: pass `request.IsCooperative ?? false`; add `IsCooperative`, `HasInvoices` to the `Get(key)` projection.

- [ ] **Step 4: Run the tests, expect PASS**; run the whole `SaveGraveTests` class too.

- [ ] **Step 5: Commit** `feat: let a grave be a cooperative grave that needs no recipient or price`

### Task 2: No invoice for a cooperative grave

**Files:**

- Modify: `backend/src/Invoicing.Api/Services/GraveBilling.cs` (`OpenForHalfYearAsync`), `Services/GraveCorrectionCheck.cs` (`OutdatedGraveNumbersAsync`), `Services/GraveInvoiceGenerator.cs` (`GenerateForGraveAsync`), `frontend/src/i18n/de.json` (`skipReasonCooperative`), `docs/spec.md` (FR-025)
- Test: `backend/tests/Invoicing.IntegrationTests/CooperativeGraveTests.cs`

**Interfaces:**

- Consumes: `Grave.IsCooperative` (Task 1).
- Produces: skip reason `"Cooperative"` from `GenerateForGraveAsync`; `OpenForHalfYearAsync` never returns a cooperative grave.

- [ ] **Step 1: Write the failing tests:**
  - `A_run_never_invoices_a_cooperative_grave` — cooperative grave _with_ a recipient and item 4001 at 120,00 €, plus an ordinary grave; run H1 2026 → only the ordinary grave has invoices; `GraveInvoiceRuns` has no row for the cooperative one.
  - `A_run_with_only_cooperative_graves_due_is_refused_as_nothing_open` — only a cooperative grave → `GenerateGraveInvoices` answers `GravesAlreadyInvoiced` (the existing "nothing open" refusal).
  - `The_correction_precheck_ignores_cooperative_graves` — cooperative grave with 3 recipients and 100,00 € (would need a correction) → run is not stopped by `OutdatedGraveNumbers`.
  - `The_generator_refuses_a_cooperative_grave_directly` — resolve `GraveInvoiceGenerator` from the host's services, call `GenerateForGraveAsync` → `Invoices` empty, skip reason `"Cooperative"`.

- [ ] **Step 2: Run, expect FAIL** (invoices created for the cooperative grave).

- [ ] **Step 3: Implement:** filter `!g.IsCooperative` in `OpenForHalfYearAsync` and in the precheck's loop; first check in `GenerateForGraveAsync` returns `new GraveGenerationResult(null, "Cooperative")`. Add the German skip text.

- [ ] **Step 4: Run all generation tests, expect PASS** (`--filter "FullyQualifiedName~Grave"`).

- [ ] **Step 5: Commit** `feat: never invoice a cooperative grave`

### Task 3: The overview, the item dialog and the cooperative list read the flag

**Files:**

- Modify: `backend/src/Invoicing.Api/OData/Controllers/GraveInvoicingStatusController.cs`, `OData/Controllers/ItemGravesController.cs`, `docs/spec.md` (FR-024, FR-028, new FR-024a for the list)
- Test: `CooperativeGraveTests.cs`

**Interfaces:**

- Produces: status rows gain `Cooperative` (`bool`); `ItemGraves` groups' `Graves[]` gain `IsCooperative`; the cooperative list reads `GET /odata/Graves?$filter=IsCooperative eq true&$expand=Cemetery,Recipients($expand=Recipient)&$orderby=GraveNumber` (raise `MaxExpansionDepth` on `GravesController.Get()` if 2 is refused).

- [ ] **Step 1: Write the failing tests:**
  - `The_overview_lists_a_cooperative_grave_in_every_half_year_its_term_touches` — cooperative grave 2026-03…2026-08 without items → listed for H1 and H2 2026 with `Cooperative=true`, `Invoiced=false`; not listed for H1 2027.
  - `The_overview_ranks_cooperative_graves_after_open_ones` — open ordinary grave, cooperative grave with a lower number → ordinary first.
  - `An_unticked_grave_shows_open` — cooperative grave with recipient and item 4001, untick → H1 2026 row `Cooperative=false`, `Invoiced=false`, ranked with the open ones.
  - `Item_graves_mark_cooperative_graves` — item linked by one of each → `IsCooperative` true/false per grave.
  - `The_graves_set_filters_cooperative_graves` — the OData URL above returns only the cooperative grave, with its cemetery and recipients.

- [ ] **Step 2: Run, expect FAIL.**

- [ ] **Step 3: Implement:** project `g.IsCooperative`; keep a grave when `invoiced || (g.IsCooperative ? HalfYearBilling.PeriodIn(start, end, year, half) is not null : IsDue(...))`; order by `invoiced || g.IsCooperative`, then number; `Cooperative = g.IsCooperative && !invoiced`.

- [ ] **Step 4: Run, expect PASS** (plus `GraveTests`, `ItemGravesTests`).

- [ ] **Step 5: Commit** `feat: list cooperative graves in the overview, the item dialog and the graves set`

### Task 4: A successor "from a month on"

**Files:**

- Modify: `GravesController.cs` (`SaveGraveRequest.SucceedsGraveId`), `GraveService.cs` (`SaveAsync` gains `long? succeedsGraveId`), `docs/spec.md` (new FR-018d)
- Test: `CooperativeGraveTests.cs`

**Interfaces:**

- Produces: `SaveAsync(long? graveId, GraveFields fields, ..., IReadOnlyList<GraveCorrectionInput> corrections, long? succeedsGraveId, CancellationToken ct)`; request field `succeedsGraveId` (only with `graveId = null`). In one transaction: the predecessor's `EndDate` becomes `fields.StartDate.AddMonths(-1)`, then the new grave is saved. Error codes: `SuccessorSameKind` (predecessor's flag equals the new one), `SuccessorStartTooEarly` (start ≤ predecessor's start), `SuccessorStartInvoiced` (predecessor has a `GraveInvoiceRun` for a half-year containing a month ≥ the new start), `SuccessorNumberDiffers` (grave numbers differ), `GraveNotFound`.

- [ ] **Step 1: Write the failing tests:**
  - `A_successor_ends_its_predecessor_the_month_before` — cooperative grave from 2024-01, successor ordinary from 2026-07 with recipient and item → predecessor `EndDate` 2026-06-01, successor saved; a run for H2 2026 invoices only the successor; H1 2026 overview shows the predecessor as `Cooperative`.
  - `A_successor_of_the_same_kind_is_refused` → `SuccessorSameKind`, predecessor unchanged.
  - `A_successor_starting_at_or_before_the_predecessor_is_refused` → `SuccessorStartTooEarly`.
  - `A_successor_inside_an_invoiced_half_year_is_refused` — ordinary grave invoiced H2 2026, cooperative successor from 2026-10 → `SuccessorStartInvoiced`; from 2027-01 → 200.
  - `Invoices_of_the_predecessor_stay_usable` — after the 2027-01 split above: the H2 2026 invoice downloads (`DocumentDownloadTests` route), marks paid, and `CreateGraveInvoiceRevision` succeeds.
  - `A_half_year_with_invoices_shows_invoiced` — overview H2 2026 shows the predecessor `Invoiced=true`, `Cooperative=false`.
  - `A_failed_successor_leaves_the_predecessor_alone` — successor without recipients but ordinary → 400 `NoRecipients`, predecessor's end unchanged.

- [ ] **Step 2: Run, expect FAIL.**

- [ ] **Step 3: Implement** inside the existing transaction; the overlap check (FR-018a) runs against the predecessor's shortened term.

- [ ] **Step 4: Run, expect PASS.**

- [ ] **Step 5: Commit** `feat: hand a grave over between the cooperative and the business from a month on`

### Task 5: Frontend API types and the generated client

**Files:**

- Modify: `frontend/src/api/endpoints.ts`, `frontend/src/api/metadata.xml`, `frontend/src/api/generated/*` (via `npm run gen:odata` on the E2E stack), `e2e/seed.ts`

**Interfaces:**

- Produces: `SaveGraveInput.isCooperative: boolean`, `SaveGraveInput.succeedsGraveId?: number | null`; `GraveDetails.IsCooperative: boolean`, `GraveDetails.HasInvoices: boolean`; `GraveStatusRow.Cooperative: boolean`; `ItemGraveGroup.Graves: { Id: number; GraveNumber: string; IsCooperative: boolean }[]`; `fetchCooperativeGraves(): Promise<CooperativeGraveRow[]>` with `CooperativeGraveRow { Id, GraveNumber, FieldNumber, SiteName, CemeteryId, CemeteryName, StartDate, EndDate, Recipients: GraveStatusRecipient[] }`; `SeedGraveInput.isCooperative?: boolean` passed through by `seedGrave` (and its doc comment names the exception).

- [ ] **Step 1:** Start the E2E stack, run `npm run gen:odata`; add the types and `fetchCooperativeGraves` (maps the OData answer to `CooperativeGraveRow`).
- [ ] **Step 2:** `npm run typecheck && npm run lint && npm run build` — clean.
- [ ] **Step 3: Commit** `feat: type the cooperative flag in the frontend API`

### Task 6: The import column

**Files:**

- Modify: `backend/src/Invoicing.Api/Services/Import/ImportArchiveLayout.cs` (`GravesHeader` + `"Cooperative"`, `OptionalTrailingColumns[GravesFile] = 1`), `ImportModel.cs` (`GraveRow` gains `bool Cooperative`), `ImportValidator.cs` (`ReadGraves`, `CheckGraveRecipients`, `CheckGraveAllYearItems`, `CheckLinkedItemPrices`, `CheckGraveSplits`), `ImportService.cs` (sets `IsCooperative`, stores no corrections for them), `frontend/src/i18n/de.json` (`importProblemCooperativeInvalid`), `docs/spec.md` (FR-052, FR-058a)
- Test: `backend/tests/Invoicing.IntegrationTests/ImportTests.cs`

**Interfaces:**

- Produces: column `Cooperative` parsed with `TryParseBool`; problem code `CooperativeInvalid` (message key `CooperativeInvalid`, args `[raw]`); the template download carries the new header.

- [ ] **Step 1: Write the failing tests:**
  - `A_cooperative_grave_imports_without_recipient_or_price` — row `...,true`, one unpriced item link, no recipient link → imported, `IsCooperative=true`.
  - `An_archive_without_the_Cooperative_column_imports_ordinary_graves` — old 7-column header → imported, `IsCooperative=false`, and an ordinary grave without recipient is still refused `NoRecipients`.
  - `A_Cooperative_value_that_is_no_boolean_is_a_problem` — `x` → problem `CooperativeInvalid` on graves.csv row 2.
  - `A_cooperative_grave_keeps_the_recipient_cap` — five recipient links → `TooManyRecipients`.
  - `The_template_lists_the_Cooperative_column`.

- [ ] **Step 2: Run, expect FAIL.** **Step 3: Implement** (the four checks skip graves whose row says `Cooperative`; `CheckLinkedItemPrices` looks the link's grave up). **Step 4: Run `ImportTests`, expect PASS.**

- [ ] **Step 5: Commit** `feat: import cooperative graves from an optional Cooperative column`

### Task 7: The grave editor

**Files:**

- Modify: `frontend/src/components/GraveEditor.vue`, `frontend/src/i18n/de.json`
- Test: `e2e/tests/graves.spec.ts`

**Interfaces:**

- Consumes: Task 5 types.
- Produces: `GraveEditor` props `{ graveId: number | null; initialCooperative?: boolean; successorOf?: GraveDetails | null }` — `successorOf` prefills number, field, site, cemetery, position, items and recipients from the predecessor, flips the flag, asks for the start month, and saves with `succeedsGraveId`.

- [ ] **Step 1: Write the failing E2E tests** (desktop):
  - `a cooperative grave is saved without recipients and items` — tick "Genossenschaftsgrab", fill fields, save via `saveEditor` → detail page shows the tag.
  - `unticking a cooperative grave asks for a recipient` — untick and save → error text for `NoRecipients` shown, drawer stays open.
  - `the box is locked once the grave has invoices` — seeded ordinary grave with an invoice (serial group, current half-year) → checkbox disabled with a hint, the hand-over action offered instead.
  - `a cooperative grave hands over from a month on` — action "Ab Monat … selbst abrechnen", pick month, editor of the successor opens prefilled, save → overview of that half-year lists the successor "offen", the predecessor's detail shows the end month.

- [ ] **Step 2: Run, expect FAIL** (`cd e2e && npm test -- --project=desktop tests/graves.spec.ts`).
- [ ] **Step 3: Implement:** `form.IsCooperative`; checkbox after "Ende" (`v-model`, disabled when `HasInvoices`); client checks (`AllYearItemRequired`, the billing preview, corrections) only when not cooperative; recipients section editable for a new grave or a cooperative one without invoices; the hand-over action in the drawer footer for an existing grave (month picker dialog → reopen editor with `successorOf`).
- [ ] **Step 4: Run E2E, typecheck, lint, build — PASS / clean.**
- [ ] **Step 5: Commit** `feat: tick, untick and hand over a cooperative grave in the editor`

### Task 8: Overview, detail page and item dialog show the tag

**Files:**

- Modify: `frontend/src/pages/GravesPage.vue` (status Tag and `statusOptions` gain `cooperative` → "Genossenschaft", grey `secondary`), `frontend/src/pages/GraveDetailPage.vue` (tag beside the number; current half-year tag "Genossenschaft"), `frontend/src/components/ItemDialog.vue` (tag after the grave number), `frontend/src/i18n/de.json`
- Test: `e2e/tests/graves.spec.ts`, `e2e/tests/masterdata.spec.ts` (item dialog)

- [ ] **Step 1: Write the failing E2E tests:**
  - `the overview shows a cooperative grave with its tag and filters by it` (serial group) — status filter "Genossenschaft" leaves only it; "Offen" hides it.
  - `the detail page names a cooperative grave` — tag and current half-year "Genossenschaft".
  - `the item dialog marks cooperative graves`.
- [ ] **Step 2: Run, FAIL. Step 3: Implement. Step 4: PASS + typecheck/lint/build.**
- [ ] **Step 5: Commit** `feat: mark cooperative graves in the overview, on the detail page and in the item dialog`

### Task 9: The page "Genossenschaftsgräber"

**Files:**

- Create: `frontend/src/pages/CooperativeGravesPage.vue`
- Modify: `frontend/src/router.ts` (`/cooperative-graves`), `frontend/src/components/NavLinks.vue` (after "Gräber", key `menuCooperativeGraves`, icon `pi pi-home`), `frontend/src/i18n/de.json`
- Test: create `e2e/tests/cooperative-graves.spec.ts`; `e2e/tests/layout.spec.ts` gets the page in its phone/tablet checks

**Interfaces:**

- Consumes: `fetchCooperativeGraves` (Task 5), `GraveEditor` with `initialCooperative` (Task 7), the column-filter components the grave overview uses (`ColumnFilterCell`, the filter sheet, URL-query filters, pages of 100).

- [ ] **Step 1: Write the failing E2E tests:**
  - `the list shows active cooperative graves by default` — one active, one ended (end 2025-12) → only the active one; "Alle" shows both; "Beendet" only the ended.
  - `the list filters by column` — number, site, field, cemetery select, recipient text.
  - `a new cooperative grave starts with the box ticked` — button "Neues Genossenschaftsgrab" → editor with the box checked; saved grave appears in the list.
  - `a row opens the grave's detail page`.
  - Layout (phone, tablet): no horizontal scroll, filter sheet opens.
- [ ] **Step 2: FAIL. Step 3: Implement** — "Laufzeit" select values `active` (end empty or ≥ first of the current month), `ended`, `all`, default `active`, stored in the URL query like the other filters.
- [ ] **Step 4: PASS (desktop, phone, tablet) + typecheck/lint/build.**
- [ ] **Step 5: Commit** `feat: list the cooperative graves on a page of their own`

### Task 10: The FriedWin conversion writes cooperative graves

**Files:**

- Modify: `scripts/friedwin/convert.py`, `scripts/friedwin/README.md`

**Interfaces:**

- Consumes: the `Cooperative` column (Task 6).
- Produces: `graves.csv` header `GraveNumber,FieldNumber,SiteName,Cemetery,Position,StartDate,EndDate,Cooperative`; notes `cooperative` ("Als Genossenschaftsgrab übernommen", with per grave: contract `Genossenschaftsvertrag` / `andere Vertragsnummer` / `keiner`, customer record `ja`/`nein`, care price), `empty-record` ("Leerer Grabsatz"), and `no-start` reused with the export year's January; `-nie-abgerechnet.csv` gains the column `Als Genossenschaftsgrab übernommen` (`ja`/`nein`).

- [ ] **Step 1:** In `main()`, an active grave that is `never-billed` or `before-euro` no longer `continue`s into `leave_out`: it goes through the site/cemetery/date checks with the grave record's fields (`g.site or "-"`, field `"-"` when missing), recipient = usable customer record or none, items = `99990` at `g.care_price` when set (count 1), else none; row ends with `true`, ordinary rows with `false`. Contract kind: `GRABSTA1` bytes 250–261 stripped, looked up (as is and zero-filled to 6) among `GENODDAT/u60vert1.DAT` bytes 5–11.
- [ ] **Step 2:** Report: text section and PDF section "Als Genossenschaftsgrab übernommen" (table Grabnummer, Vertrag, Kundensatz, Pflegepreis); "Nie für Grabpflege abgerechnet" now lists ended graves only; the PDF's add-up check still holds.
- [ ] **Step 3: Verify** on the export: `python3 scripts/friedwin/convert.py export/ZWI/20260930-1807 --out <scratchpad>/friedwin-import.zip` → 778 graves, 651 active, 96 with `Cooperative=true` (017630 and 059850 among them with `-`); import the archive into the E2E stack (empty DB, VAT rates 7/19 and the four cemeteries created first) → no problems.
- [ ] **Step 4: Commit** `feat(scripts): import every active FriedWin grave, the unbilled ones as cooperative graves`

### Task 11: Close out

- [ ] **Step 1:** Full backend suite, full E2E suite (all projects), frontend checks — all green; `npm run format:check`.
- [ ] **Step 2:** Ask the owner before resetting the dev database (`scripts/reset-dev-db.sh`); only then restart the dev backend.
- [ ] **Step 3:** Whole-branch review (superpowers:requesting-code-review), fix what it finds, then superpowers:finishing-a-development-branch.
