# Gross-Led Line Prices Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A VAT-included line on an ordinary invoice or quote stores the entered gross and prints exactly that, and graves lose the VAT-included option entirely (issue 73).

**Architecture:** `InvoiceLine` and `QuoteLine` gain a nullable `GrossUnitPrice`; `DocumentDataBuilder.Calculate` treats a line with one as gross-led (total = count × gross, VAT taken out of the total, net = the rest) and leaves every other line, old flagged ones included, on today's arithmetic. `GraveItem.PriceIncludesVat` is dropped and every gross branch on the grave side goes with it. One EF migration carries both schema changes.

**Tech Stack:** .NET 11 / EF Core 11 / SQLite, QuestPDF, xUnit + PdfPig; Vue 3 + PrimeVue; Playwright.

**Spec:** `docs/superpowers/specs/2026-11-14-gross-led-line-prices-design.md` (read it first; it is the authority). Requirements live in `docs/spec.md`.

## Global Constraints

- Exactly one new EF migration, `GrossLinePrices`, created in Task 1 via `scripts/ef.sh migrations add GrossLinePrices --project src/Invoicing.Api` (from `backend/`). If a later task needs schema, regenerate that same migration (remove + add) rather than adding a second. Existing migrations stay untouched.
- Never start the dev stack from a worktree and never point anything at port 8080; E2E runs `cd e2e && npm test` (own stack, port 8081).
- Rounding is `MidpointRounding.AwayFromZero` everywhere; a typed price (gross or net) has at most two decimals (`InvalidPrice`).
- Gross-led line: `lineGross = count × GrossUnitPrice`; `lineVat = round(lineGross × rate / (100 + rate), 2)`; `lineNet = lineGross − lineVat`. Pinned values at 19 %: 75,00 → VAT 11,97, net 63,03, total 75,00; 3 × 10,00 → 30,00, VAT 4,79, net 25,21. At 0 % VAT 0.
- A line without `GrossUnitPrice` — every net line and every flagged line from before this version — is calculated exactly as today.
- `GrossUnitPrice` is set iff `PriceIncludesVat` on new ordinary-invoice and quote lines; always null on grave-invoice lines.
- Grave paths ignore `InvoiceLineInput.PriceIncludesVat` (as they already ignore `Comment`); grave prices are always net.
- Backend tests: `scripts/dotnet.sh test Invoicing.sln` (see CLAUDE.md about `backend/obj` if a dev container shares it; a worktree has its own). Frontend: `cd frontend && npm run typecheck && npm run lint && npm run build`, all clean. After an EDM change: `npm run gen:odata` against the E2E stack and commit `src/api/metadata.xml` with `src/api/generated/`.
- Conventional Commits; end every message with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- Never read anything under `data/` or `export/`.

## Review Focus

1. A revision of a gross-led line opened and saved untouched must keep the identical gross and total (pinned in Task 2 and in E2E, Task 5).
2. A rate group mixing a gross-led line and a net line on one invoice: the VAT block must equal the sum of the lines (pinned in Task 2).
3. An invoice created before this version with a flagged line, count 3 at 75,00: its list total stays 225,02 after the upgrade (pinned in Task 2 by seeding a flagged line with `GrossUnitPrice = null`).
4. Ticked line without a typed price: the item's default price is read as the gross (pinned in Task 2).
5. An import archive still carrying the `PriceIncludesVat` column with `false` must import; with `true` it must be refused (pinned in Task 1).

---

### Task 1: Graves lose the VAT-included flag; the migration

**Files:**

- Modify: `backend/src/Invoicing.Domain/Entities/Grave.cs` (drop `GraveItem.PriceIncludesVat`), `Entities/Invoice.cs` and `Entities/Quote.cs` (add `public decimal? GrossUnitPrice { get; set; }` to `InvoiceLine` and `QuoteLine`, with a doc comment per the spec's Data Model section)
- Modify (remove flag / `EnteredGross` / gross branch): `backend/src/Invoicing.Domain/HalfYearBilling.cs` (`BillableLink.PriceIncludesVat`, `BillAt`), `GraveInvoiceSplitter.cs` (`GraveLine.PriceIncludesVat`, `GraveLine.EnteredGross`, pass-through and `Combine`), `GraveShare.cs` (`PricedGross`, `Owed`, the `DoesNotAddUp` branch and enum member), `GraveInvoiceGroupLines.cs`
- Modify: `backend/src/Invoicing.Api/Services/GraveBilling.cs` (`LinkFor`/`LineFor` lose the `priceIncludesVat` parameter), `GraveService.cs` (`GraveItemInput`), `GraveInvoiceGenerator.cs`, `GraveInvoiceRevisionService.cs` (ignore `input.PriceIncludesVat`, no `NetOf`), `ItemPriceIncreaseService.cs` (`PriceIncreaseRowInput.PriceIncludesVat` and row matching), `WorkListService.cs` (amount = `GrossOf(price, rate, false)`; drop the `priceIncludesVat` parameter from `GrossOf` if nothing else passes true), `OData/Controllers/GravesController.cs`, `GraveBillingValidationController.cs`, `GraveInvoiceGroupController.cs`, `ItemGravesController.cs` (group by price only)
- Modify: `backend/src/Invoicing.Api/Services/Import/ImportArchiveLayout.cs`, `ImportValidator.cs`, `ImportService.cs`, `ImportModel.cs`; `scripts/friedwin/convert.py` (stop writing the column)
- Create: `backend/src/Invoicing.Api/Migrations/<timestamp>_GrossLinePrices.cs` (+ Designer, snapshot)
- Test: delete `backend/tests/Invoicing.IntegrationTests/VatIncludedGraveTests.cs` and `VatIncludedGraveRevisionTests.cs`; adjust every other test that sets the grave flag (`SaveGraveTests`, `GraveBillingPreviewTests`, `GraveTermGenerationTests`, `GraveBillingValidationTests`, `WorkListTests`, `CooperativeGraveTests`, `ItemPriceIncreaseTests`, `ItemGravesTests`, `GraveTests`, `GraveInvoiceSplitterTests`, `ImportTests`, `DocumentTests/GraveShareDocumentTests`); create `GrossLinePricesMigrationTests.cs`
- Docs: `docs/spec.md` FR-021 (drop the sentence "Where a rate carries a price entered as a gross …"), FR-052's cross-reference, FR-053, FR-104 ("a price entered as gross is taken as entered" goes); `docs/deviations.md` D-005 → "v3.5.0 ships three migrations", adding `GrossLinePrices` (issue 73) with the spec's containment wording

**Interfaces:**

- Produces: `InvoiceLine.GrossUnitPrice`, `QuoteLine.GrossUnitPrice` (`decimal?`, unused until Task 2); `GraveBilling.LinkFor(InvoiceItem item, int count, decimal? unitPrice)` and `LineFor(...)` without the flag; `GraveLine(ItemId, ItemNumber, Description, Count, UnitPrice, VatRate)`; `GraveShareProblemKind` with `NotDivisible` only.
- `InvoiceLine.PriceIncludesVat` stays (shared with ordinary invoices); grave generation and revision always write `false`.

- [ ] **Step 1: Write the failing migration test** `GrossLinePricesMigrationTests` in the style of `GraveMonthMigrationTests`, `PreviousMigration = "20261112212917_DocumentComments"`. Seed raw SQL: an item with `VatRate` 19 and `DefaultPrice` 120.00; grave A linking it with `UnitPrice` 75.00 and `PriceIncludesVat` 1; grave B linking it with `UnitPrice` NULL and `PriceIncludesVat` 1; grave C with `UnitPrice` 50.00 and `PriceIncludesVat` 0; an invoice with one line `UnitPrice` 63.03, `PriceIncludesVat` 1. After `MigrateAsync()`:
  - `Upgrading_bills_a_gross_grave_price_at_the_net_it_billed_before`: A's `UnitPrice == 63.03m`, B's `== 100.84m`, C's `== 50.00m`.
  - `Upgrading_leaves_existing_invoice_lines_without_a_gross`: the line's `GrossUnitPrice` is null, `UnitPrice == 63.03m`, `PriceIncludesVat` true.
- [ ] **Step 2: Remove the flag from the entity and every grave path listed above**, then `scripts/ef.sh migrations add GrossLinePrices --project src/Invoicing.Api`. Edit the generated `Up` so that, **before** the `DropColumn`, it runs a `migrationBuilder.Sql` update converting each flagged `GraveItems` row: `UnitPrice = net of COALESCE(UnitPrice, item DefaultPrice)` at the item's `VatRate`, rounded to two decimals, written as EF's decimal text (e.g. `printf('%.2f', …)`; check the stored format of an existing row in the snapshot/tests). `Down` re-adds the column with default false and the two drops.
- [ ] **Step 3: Import.** `ImportArchiveLayout`'s mapping header loses `PriceIncludesVat`; the validator accepts the column as optional (like `UnitPrice`): absent, empty or false is fine; a true value is a problem with code `GravePriceIncludesVat` naming the row (frontend text in Task 4). Tests in `ImportTests`: `A_mapping_with_the_legacy_vat_column_set_false_imports` and `A_mapping_with_the_legacy_vat_column_set_true_is_refused` (the problem names row and code); the template download no longer contains the column. `convert.py` drops the column from its header and rows.
- [ ] **Step 4: Fix the remaining tests**: delete the two `VatIncludedGrave*` files; rewrite cases elsewhere that ticked the grave flag as net prices or delete them where they only tested the gross branch (e.g. `DoesNotAddUp`). No test may still construct `GraveLine` with a flag or `EnteredGross`.
- [ ] **Step 5: Run** `scripts/dotnet.sh test Invoicing.sln` — all green, migration tests included.
- [ ] **Step 6: Spec and deviation edits** listed under Docs.
- [ ] **Step 7: Commit** `refactor(graves): drop VAT-included grave prices`, body naming issue 73, the migration and D-005. (Not marked breaking: the owner has no grave with the flag set.)

### Task 2: Gross-led lines on ordinary invoices and quotes (backend)

**Files:**

- Modify: `backend/src/Invoicing.Api/Services/LineBuilder.cs`, `QuoteService.cs` (the `new QuoteLine { … }` projection), `InvoiceService.cs` (the quote conversion copy "as quoted, not through LineBuilder", and the revision copy if it copies lines), `DocumentDataBuilder.cs` (`LineSource` gains `decimal? GrossUnitPrice`; gross-led branch in `Calculate`), `backend/src/Invoicing.Documents/InvoiceDocumentData.cs` (`LineData` gains `decimal? GrossUnitPrice = null`), `InvoiceDocument.cs` (unit price = `GrossUnitPrice` when set; rewrite the "29,99" comment to say it only holds for lines from before this version)
- Test: rewrite `backend/tests/Invoicing.IntegrationTests/VatIncludedInvoiceTests.cs`; extend `QuoteConversionTests.cs`, `QuoteTests.cs`; `backend/tests/Invoicing.DocumentTests/InvoiceLayoutTests.cs`
- Docs: `docs/spec.md` — FR-037c (the gross unit price and total are the entered gross and count × gross), FR-094 and FR-100 (a quote version and a converted invoice also carry the entered gross), a new FR after the last invoice FR stating the gross-led rule with the Global Constraints formula, and an edge case: an invoice from before keeps its figures, a revision of it takes the gross its dialog showed.

**Interfaces:**

- Consumes: `InvoiceLine.GrossUnitPrice`, `QuoteLine.GrossUnitPrice` from Task 1.
- Produces: `LineData.GrossUnitPrice`; the OData line entities expose `GrossUnitPrice` (Task 3 regenerates the client); stored list totals of a gross-led invoice equal its printed totals.

- [ ] **Step 1: Write the failing tests** in `VatIncludedInvoiceTests` (keep its seeding helper; item `V-75` at 19 % with `DefaultPrice` 75.00 added):
  - `A_ticked_line_bills_the_entered_gross_exactly`: line count 1, typed 75.00, ticked → stored `GrossUnitPrice == 75.00m`, `UnitPrice == 63.03m`; invoice `GrossTotal == 75.00m`, `VatTotal == 11.97m`, `NetTotal == 63.03m` (read through the list endpoint that stamps totals).
  - `A_count_above_one_bills_count_times_the_gross`: 3 × 10.00 ticked → `GrossTotal 30.00m`, `VatTotal 4.79m`, `NetTotal 25.21m`.
  - `An_untyped_ticked_line_reads_the_default_as_gross`: ticked, `UnitPrice` null, item default 75.00 → `GrossUnitPrice == 75.00m`.
  - `A_gross_line_and_a_net_line_at_one_rate_add_up`: ticked 75.00 plus net 100.00 at 19 % → `NetTotal 163.03m`, `VatTotal 30.97m`, `GrossTotal 194.00m`.
  - `A_rate_of_zero_takes_no_vat_out`: item at 0 %, ticked 50.00 → net 50.00, VAT 0.
  - `A_revision_that_returns_the_gross_keeps_it`: revise with the same line (75.00 ticked) → new version `GrossUnitPrice 75.00m`, totals unchanged.
  - `A_flagged_line_from_before_keeps_its_old_figures`: seed via `AppDbContext` an invoice line `Count 3`, `UnitPrice 63.03m`, `PriceIncludesVat true`, `GrossUnitPrice null` → list totals `NetTotal 189.09m`, `VatTotal 35.93m`, `GrossTotal 225.02m`.
  - `A_third_decimal_on_a_gross_is_refused`: ticked 75.005 → 400 `InvalidPrice`.
  - In `QuoteTests`: `A_ticked_quote_line_stores_its_gross` (quote line `GrossUnitPrice 75.00m`, quote list `GrossTotal 75.00m`). In `QuoteConversionTests`: `Conversion_carries_the_gross` (invoice line `GrossUnitPrice 75.00m`, `GrossTotal 75.00m`).
  - In `InvoiceLayoutTests`: `A_gross_led_line_prints_the_entered_gross` — `LineData` with `UnitPrice 63.03m`, `GrossUnitPrice 75.00m`, `LineTotal 63.03m`, `VatAmount 11.97m`, `PriceIncludesVat true` → the PDF text contains `75,00` as unit price and total and no `75,01`.
- [ ] **Step 2: Run** `scripts/dotnet.sh test Invoicing.sln --filter "VatIncludedInvoiceTests|QuoteTests|QuoteConversionTests|InvoiceLayoutTests"` — the new cases fail.
- [ ] **Step 3: Implement.** `LineBuilder`: for a ticked input, `GrossUnitPrice` = resolved price (typed, else item default); `UnitPrice = VatInclusivePrice.NetOf(resolved, rate, true)`; unticked: `GrossUnitPrice = null`. Copy `GrossUnitPrice` in both projections. `Calculate`: gross-led branch per Global Constraints, else unchanged; pass `GrossUnitPrice` into `LineData`. `InvoiceDocument`: unit price `line.GrossUnitPrice ?? (flagged ? round(UnitPrice × (1+rate/100), 2) : UnitPrice)`; total for any flagged line stays `LineTotal + VatAmount` (equals `lineGross` for a gross-led line).
- [ ] **Step 4: Run** the full `scripts/dotnet.sh test Invoicing.sln` — green.
- [ ] **Step 5: Spec edits** listed under Docs.
- [ ] **Step 6: Commit** `fix(invoices): bill a VAT-included price at exactly the entered gross`, body referencing issue 73. No `Closes #73` trailer: issues are closed only on the owner's request.

### Task 3: Invoice and quote editors work in the stored gross (frontend)

**Files:**

- Modify: `frontend/src/api/metadata.xml`, `frontend/src/api/generated/` (regenerate: bring the E2E stack up from this tree — `docker compose -f docker-compose.e2e.yml -p invoicing-e2e up -d --build` from the repository root (the same project `e2e/stack.ts` uses, port 8081) — then `npm run gen:odata` in `frontend/`; `cd e2e && npm run stack:down` afterwards), `frontend/src/api/endpoints.ts`/`queries.ts` (line types gain `GrossUnitPrice: number | null`), `frontend/src/components/InvoiceEditor.vue`, `frontend/src/components/LineEditor.vue`, `frontend/src/lib/lines.ts`, `frontend/src/lib/money.ts`, `frontend/src/lib/revisions.ts`, `frontend/src/components/RevisionPanel.vue`

**Interfaces:**

- Consumes: `GrossUnitPrice` on invoice and quote lines (Task 2).
- Produces: `LineEditor` prop `ordinary?: boolean` replacing `withComments` (shows the comment field **and** the VAT-included checkbox column); `toLineInputs(lines, { ordinary })` sends `comment` and `priceIncludesVat` only when `ordinary`, else `priceIncludesVat: false`; `money.ts` exports `printedUnitPrice(line: { UnitPrice: number; GrossUnitPrice: number | null; PriceIncludesVat: boolean; VatRate: number }): number` — the unit price the PDF prints (gross-led: the gross; old flagged: `grossFromNet(UnitPrice, VatRate)`; net: `UnitPrice`) — and `splitGross(lineGross, rate)`, the split of a gross-led line into VAT and net. The revision panel's line rows carry a net and a gross column: a gross-led line's net is `splitGross(count × gross).net` and its gross is count × gross; the other lines' gross comes from `grossPerLine`.

- [ ] **Step 1:** Opening a version in `InvoiceEditor`: a line's price field shows `line.GrossUnitPrice` when set, else today's `priceFieldValue(...)`.
- [ ] **Step 2:** `LineEditor`'s running totals: a ticked line contributes `lineGross = count × price` (via `lineNet(count, price)`), `vat = roundCents(lineGross × rate / (100 + rate))`, `net = lineGross − vat`; an unticked line as today. Update the doc comment.
- [ ] **Step 3:** Rename `withComments` → `ordinary` in `LineEditor` and `toLineInputs`; `InvoiceEditor` passes it (invoices and quotes). The checkbox column header and cell render only with `ordinary`; the grid template drops the 8rem column without it.
- [ ] **Step 4:** `revisions.ts`/`RevisionPanel.vue`: `RevisionLineView` carries `GrossUnitPrice`; the panel shows `printedUnitPrice`, the per-line net, and a gross column that is count × gross for a gross-led line (`grossPerLine` runs over the remaining lines only).
- [ ] **Step 5: Run** `npm run typecheck && npm run lint && npm run build` in `frontend/` — clean.
- [ ] **Step 6: Commit** `feat(frontend): edit and show a VAT-included line in its stored gross`.

### Task 4: Graves lose the checkbox (frontend)

**Files:**

- Modify: `frontend/src/components/GraveEditor.vue`, `GraveInvoiceEditor.vue`, `frontend/src/composables/useGraveInvoiceEdit.ts`, `frontend/src/lib/billing.ts`, `frontend/src/lib/priceIncrease.ts`, `frontend/src/components/ItemPriceIncreaseDialog.vue`, `ItemDialog.vue` (grave overview label without "(inkl. MwSt.)"), `frontend/src/pages/GraveDetailPage.vue`, `frontend/src/api/endpoints.ts` (grave inputs, `Kind: "NotDivisible"` only), `frontend/src/i18n/de.json` (drop keys only graves used; rename `importProblemPriceIncludesVatInvalid` → `importProblemGravePriceIncludesVat`: „Zeile {0}: Grabpreise sind immer netto – „PriceIncludesVat“ darf nicht „true“ sein.“ — match the argument order the backend sends)

**Interfaces:**

- Consumes: Task 1's grave API without the flag; Task 3's `LineEditor` `ordinary` prop (graves omit it).

- [ ] **Step 1:** Remove every grave-side `priceIncludesVat`/`PriceIncludesVat` and gross display; grave editors use `LineEditor` without `ordinary` and `toLineInputs(lines)`.
- [ ] **Step 2:** `grep -rn -i "priceIncludesVat" frontend/src --exclude-dir=generated` lists only the ordinary-invoice/quote paths, `money.ts`, `revisions.ts`, `RevisionPanel.vue` and `de.json`'s `priceIncludesVatShort`/`priceIncludesVatColumn`.
- [ ] **Step 3: Run** `npm run typecheck && npm run lint && npm run build` — clean.
- [ ] **Step 4: Commit** `refactor(frontend): drop the VAT-included checkbox from graves`.

### Task 5: End to end

**Files:**

- Modify: `e2e/tests/vat-included.spec.ts`, `e2e/tests/graves.spec.ts` (the two VAT-included grave tests at "a grave splits a VAT-included price …" and "an invoice works each rate's gross out …": delete, or rewrite the second as an ordinary invoice if it still tests something the backend tests do not), `e2e/seed.ts` (no grave flag), `e2e/tests/import-export.spec.ts`, `e2e/tests/quotes.spec.ts`, `e2e/tests/comments.spec.ts` (only where they set the grave flag or depend on `withComments`)

- [ ] **Step 1:** In `vat-included.spec.ts`: `a line ticked as VAT-included bills the price that was entered` asserts 75,00 € typed → the invoice list total 75,00 € (and the PDF via the existing download helper, if the spec already reads PDFs); `a ticked line reopens on its gross and re-saves unchanged` asserts the editor shows 75,00 on reopening and the revision's total is 75,00 €; new `the grave editor offers no VAT-included checkbox` asserts the grave editor has no "inkl. MwSt." control. Use `e2e/vue.ts` helpers (`waitForOverlayOpen`, `pickItem`, `fillNumber`, `saveEditor`).
- [ ] **Step 2: Run** `cd e2e && npm test` — green (all projects).
- [ ] **Step 3: Commit** `test(e2e): cover gross-led invoice lines and graves without the VAT option`.
