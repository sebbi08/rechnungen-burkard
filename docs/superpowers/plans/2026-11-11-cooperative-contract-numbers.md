# Cooperative Graves by Contract Number Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A cooperative grave carries its cooperative contract number (required, stored, shown), ordinary graves may come in without recipient or item, and the FriedWin conversion classifies by contract number and 2026 care invoices.

**Architecture:** A nullable `Grave.ContractNumber` in a new migration; `GraveService` requires it for cooperative graves and relaxes the recipient lock to "no invoices yet"; the import gains a ninth optional column and accepts incomplete ordinary graves; the editor, detail page and cooperative list show the number; `convert.py` reclassifies.

**Tech Stack:** .NET / EF Core (SQLite) / ASP.NET OData, xUnit; Vue 3 + PrimeVue, Playwright; Python 3.

**Spec:** `docs/superpowers/specs/2026-11-11-cooperative-contract-numbers-design.md` (read it first), amending `docs/superpowers/specs/2026-11-07-cooperative-graves-design.md`.

## Global Constraints

- Branch `feat/cooperative-contract-numbers` (checked out). Never push, never touch the dev stack on 8080, never run `scripts/reset-dev-db.sh`, never read `data/`.
- `export/` holds personal data: run the converter over it, print counts, grave numbers and codes only; outputs to the session scratchpad, never into `export/` or the repo.
- Code in English; every UI string in `frontend/src/i18n/de.json`, German only. Names: field label "Vertragsnummer", error code `ContractNumberRequired`, import column `ContractNumber`.
- New EF migration `GraveContractNumber` (v3.2.0 shipped `20261101095804_RecipientEmailAndCemeteries`; that one stays untouched).
- Backend tests: `find backend -type d \( -name bin -o -name obj \) -prune -exec rm -rf {} +` if NETSDK1064, then `scripts/dotnet.sh test Invoicing.sln`; do NOT `docker compose start backend` afterwards.
- Frontend: `npm run typecheck`, `npm run lint`, `npm run build` clean; after the EDM change `npm run gen:odata` against the E2E stack, keep `src/api/metadata.xml` in its pretty-printed layout (Prettier 3 + `@prettier/plugin-xml`, `--tab-width 4 --print-width 100 --xml-whitespace-sensitivity ignore`, plugin not added to package.json).
- E2E only via `cd e2e && npm test …` (own stack on 8081), through `e2e/vue.ts` helpers; `npm run stack:down` afterwards and between runs whose frontend changed.
- No URL the frontend sends may contain a `;` (Traefik splits queries at it).
- Each task updates the `docs/spec.md` requirements it touches. Conventional Commits, each message ending with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>` and `Claude-Session: https://claude.ai/code/session_015EooesU4igSCakatgHZKpJ`.

## Review Focus

1. A cooperative grave saved with a contract number of only spaces must be refused like an empty one (`ContractNumberRequired`), and a stored number is trimmed. Pinned in Task 1.
2. Unticking a cooperative grave must drop its contract number, so the cooperative list never shows a stale number for an ordinary grave. Pinned in Task 1.
3. Adding a recipient to an imported ordinary grave without one must work in the editor and on the server, while a grave with an invoice keeps its set fixed (`GraveRecipientsImmutable`). Pinned in Tasks 1 and 3.
4. An eight-column archive written by the v3.2.0 converter (with `Cooperative=true` rows, no `ContractNumber`) is refused with `ContractNumberRequired` per row, not with a header error. Pinned in Task 2.
5. A grave with item links but no recipient still runs the item checks (an unpriced item stays `PriceRequired`), so "no recipient" does not become a back door for unbillable links. Pinned in Task 2.

---

### Task 1: The contract number on the grave, and recipients until the first invoice

**Files:**

- Modify: `backend/src/Invoicing.Domain/Entities/Grave.cs`, `backend/src/Invoicing.Api/Services/GraveService.cs`, `backend/src/Invoicing.Api/OData/Controllers/GravesController.cs`, `docs/spec.md` (FR-018, FR-020b, FR-018d)
- Create: migration `GraveContractNumber` via `scripts/ef.sh migrations add GraveContractNumber --project src/Invoicing.Api` (from `backend/`)
- Test: `backend/tests/Invoicing.IntegrationTests/CooperativeGraveTests.cs`

**Interfaces:**

- Produces: `Grave.ContractNumber` (`string?`, column `ContractNumber` TEXT NULL); `GraveFields(..., bool IsCooperative, string? ContractNumber)` (new last member); `SaveGraveRequest.ContractNumber` (`string?`); `Graves(key)` JSON gains `ContractNumber`; the `Graves` OData set exposes it; error `ContractNumberRequired`.

- [ ] **Step 1: Failing tests** in `CooperativeGraveTests` (existing cooperative saves in the file need a contract number added, e.g. via the save helpers' default `"V-1"`):
  - `A_cooperative_grave_needs_a_contract_number` — `isCooperative=true`, `contractNumber` null → 400 `ContractNumberRequired`; `"   "` → 400 `ContractNumberRequired`; `" 2023029 "` → 200 and `Graves(id).ContractNumber == "2023029"`.
  - `An_ordinary_grave_stores_no_contract_number` — ordinary save with `contractNumber="X"` → 200, `ContractNumber` null.
  - `Unticking_drops_the_contract_number` — cooperative grave with `"V-9"`, untick with recipient + item 4001 → `ContractNumber` null.
  - `A_successor_to_the_cooperative_needs_a_contract_number` — ordinary predecessor, cooperative successor without number → 400 `ContractNumberRequired`, predecessor's end unchanged; with `"V-2"` → 200.
  - `An_ordinary_grave_without_invoices_may_change_its_recipients` — ordinary grave with Anna, save with Bert → 200 (replaces the old immutability case for uninvoiced graves); after a run for its half-year, a change → 400 `GraveRecipientsImmutable`.
- [ ] **Step 2: Run, expect FAIL** (`--filter "FullyQualifiedName~CooperativeGraveTests"`).
- [ ] **Step 3: Implement** — entity property; migration (AddColumn TEXT nullable; Down drops); `GraveService.SaveAsync`: trim, `ContractNumberRequired` when `IsCooperative` and empty (before the predecessor checks of a hand-over write anything), store null for ordinary; `GraveRecipientsImmutable` only when the stored grave has an invoice (drop the cooperative condition); controller passes `request.ContractNumber`, `Get(key)` projects it. Spec: FR-018 (field, required for cooperative), FR-020b (fixed once the grave has an invoice), FR-018d (successor needs the number).
- [ ] **Step 4: Run the whole backend suite, expect PASS** (existing `GraveRecipientImmutabilityTests` may encode "fixed at creation" for uninvoiced graves — update those cases to the new rule and say so in the report).
- [ ] **Step 5: Commit** `feat: give a cooperative grave its contract number and free recipients until the first invoice`

### Task 2: The import — ContractNumber column and incomplete ordinary graves

**Files:**

- Modify: `backend/src/Invoicing.Api/Services/Import/ImportArchiveLayout.cs` (`GravesHeader` + `"ContractNumber"`, `OptionalTrailingColumns[GravesFile] = 2`, `AcceptedHeaders` yields the header without 0, 1, …, k trailing optional columns), `ImportModel.cs` (`GraveRow` gains `string? ContractNumber`), `ImportValidator.cs`, `ImportService.cs`, `frontend/src/i18n/de.json` (`importProblemContractNumberRequired`), `docs/spec.md` (FR-052, FR-058a)
- Test: `backend/tests/Invoicing.IntegrationTests/ImportTests.cs`

**Interfaces:**

- Consumes: `Grave.ContractNumber` (Task 1).
- Produces: problem code/message key `ContractNumberRequired` (graves.csv row, args `[graveNumber]`); a grave row without recipient links, or without item links, imports for any grave.

- [ ] **Step 1: Failing tests:**
  - `A_cooperative_row_imports_its_contract_number` — 9 columns, `true`, `2023029` → `ContractNumber == "2023029"`.
  - `A_cooperative_row_without_contract_number_is_a_problem` — 9 columns, `true`, empty → `ContractNumberRequired` on graves.csv row 2, nothing imported.
  - `An_eight_column_archive_still_reads` — the v3.2.0 header (…`Cooperative`) with a `false` row imports; with a `true` row → `ContractNumberRequired` (not `HeaderInvalid`).
  - `An_ordinary_grave_without_recipient_imports` — ordinary row, one priced all-year item link, no recipient link → imported, no recipients.
  - `An_ordinary_grave_without_items_imports` — ordinary row, no item and no recipient links → imported.
  - `Item_links_without_a_recipient_are_still_checked` — ordinary row, no recipient, an unpriced item link → `PriceRequired`.
  - The existing "ordinary grave without recipient is refused `NoRecipients`" case (Task 6 of the first plan, `An_archive_without_the_Cooperative_column_imports_ordinary_graves`) changes: it now imports.
- [ ] **Step 2: Run, expect FAIL.**
- [ ] **Step 3: Implement** — read column 8 with `Cell(row, 8)`; `CheckGraveRecipients` keeps only `TooManyRecipients`; `CheckGraveAllYearItems` and `CheckGraveSplits` skip a grave with no item links (and, as now, cooperative graves); the split check needs recipients, so it skips graves without them; `ImportService` stores the trimmed number for cooperative rows only.
- [ ] **Step 4: Run `ImportTests` and the full backend suite, expect PASS.**
- [ ] **Step 5: Commit** `feat: import contract numbers and ordinary graves still to be completed`

### Task 3: Frontend — editor, detail page, cooperative list

**Files:**

- Modify: `frontend/src/api/endpoints.ts`, `frontend/src/api/metadata.xml` + `frontend/src/api/generated/*` (gen:odata), `frontend/src/components/GraveEditor.vue`, `frontend/src/pages/GraveDetailPage.vue`, `frontend/src/pages/CooperativeGravesPage.vue`, `frontend/src/i18n/de.json`, `e2e/seed.ts`, `docs/spec.md` (FR-024a)
- Test: `e2e/tests/graves.spec.ts`, `e2e/tests/cooperative-graves.spec.ts`

**Interfaces:**

- Consumes: `ContractNumber` on `Graves(key)` and the `Graves` set, `SaveGrave.contractNumber`, `ContractNumberRequired`, recipients changeable while `HasInvoices` is false (Tasks 1–2).
- Produces: `SaveGraveInput.contractNumber: string | null`; `GraveDetails.ContractNumber: string | null`; `CooperativeGraveRow.ContractNumber: string | null`; `SeedGraveInput.contractNumber?: string` (seedGrave sends `"V-1"` for a cooperative grave when none is given).

- [ ] **Step 1: Failing E2E tests:**
  - graves.spec `a cooperative grave needs its contract number` — tick the box, leave "Vertragsnummer" empty, save → the `ContractNumberRequired` text, drawer stays open; fill `2023029`, save → detail page shows "Vertragsnummer" `2023029`.
  - graves.spec `a recipient can be added to a grave without invoices` — SaveGrave refuses an ordinary grave without recipients on create, so seed it the way it arises: a new helper `seedIncompleteGrave(page, marker): Promise<number>` in `e2e/seed.ts` posts a one-grave archive to `/odata/ImportArchive` (built with `adm-zip`, already a devDependency; unique item/recipient-free rows named by the marker; ordinary row, one priced all-year item, no recipient link) and returns the grave's id → the editor offers adding a recipient, add one, save → the detail page lists it.
  - graves.spec hand-over to the cooperative: the successor editor shows "Vertragsnummer", saving without it shows the error, with it succeeds.
  - cooperative-graves.spec `the list shows and searches the contract number` — two cooperative graves `V-111`/`V-222` → column "Vertragsnummer" shows them; text search `222` leaves one.
- [ ] **Step 2: Run, expect FAIL.**
- [ ] **Step 3: Implement** — gen:odata; types; editor field (shown and required while ticked, `ContractNumberRequired` client check like `CemeteryRequired`, cleared from the payload when unticked); `recipientsEditable` = new grave or `!HasInvoices`; detail page row "Vertragsnummer" for a cooperative grave; list column + `ColumnFilterCell` text field + phone sheet entry, request URL without `;`.
- [ ] **Step 4: Run E2E desktop graves + cooperative-graves, phone/tablet layout; typecheck/lint/build — PASS / clean.**
- [ ] **Step 5: Commit** `feat: show, search and require the contract number of a cooperative grave`

### Task 4: The FriedWin conversion classifies by contract number

**Files:**

- Modify: `scripts/friedwin/convert.py`, `scripts/friedwin/README.md`

**Interfaces:**

- Consumes: column `ContractNumber` (Task 2).
- Produces: `graves.csv` header `GraveNumber,FieldNumber,SiteName,Cemetery,Position,StartDate,EndDate,Cooperative,ContractNumber`.

- [ ] **Step 1:** Classification in `main()`: an active grave is cooperative iff `g.contract` (bytes 250–261 stripped) is non-empty and none of its care invoices (`invoices[g.customer]`, as `read_invoices` returns them) is dated in `export_year`. A never-billed or DM-only active grave that is not cooperative goes through the former cooperative branch as an ordinary grave (Cooperative `false`, recipient if usable else none, care item at `g.care_price` if set else none). A grave billed before keeps its normal path and items; if it classifies cooperative its row gets `true` and the number. Drop `contract_kind`/`read_contracts` if nothing else uses them.
- [ ] **Step 2:** Report: "Als Genossenschaftsgrab übernommen" with Grabnummer, Vertragsnummer, Kundensatz ja/nein, Pflegepreis (text + PDF); new note/section `incomplete` "Ohne Empfänger oder Leistung übernommen" (Grabnummer, fehlt: Empfänger/Leistung); `-nie-abgerechnet.csv` last column `Übernommen als` (`Genossenschaftsgrab` / `normales Grab` / empty). README: the rule, the column, the report sections.
- [ ] **Step 3: Verify** on `export/ZWI/20260930-1807` into the scratchpad: 778 graves, 651 active, 82 `Cooperative=true` (all with a contract number), 32 ordinary graves without a recipient link, 017630 and 059850 without item links; import into the E2E stack (VAT 7/19, cemeteries Speyer/Dudenhofen/Otterstadt/Harthausen first) → 0 problems, 82 cooperative graves. Print counts only; `npm run stack:down`.
- [ ] **Step 4: Commit** `feat(scripts): classify cooperative graves by contract number and 2026 care invoices`

### Task 5: Close out

- [ ] **Step 1:** Full backend suite, full E2E (all projects), frontend checks, `npm run format:check` (ignore `.claude/settings.local.json`).
- [ ] **Step 2:** Whole-branch review, fixes, then superpowers:finishing-a-development-branch (the owner decides about push and release).
