# Print Grave Invoices Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A "Drucken" button on the grave page opens all grave invoices of the selected half-year as one PDF in a new tab.

**Architecture:** The ZIP route's document selection moves into one shared query. A new GET route joins the selected stored PDFs with QuestPDF's `DocumentOperation` and serves the result inline. The grave page opens that route with the existing `openPdf` helper.

**Tech Stack:** .NET 11, EF Core, QuestPDF 2026.9 (`DocumentOperation`), xUnit + PdfPig, Vue 3 + PrimeVue, Playwright.

**Spec:** `docs/superpowers/specs/2026-11-14-print-grave-invoices-design.md`

## Global Constraints

- Route: `GET odata/GraveInvoicesPdf?year=&half=`, `application/pdf`, `Content-Disposition: inline`, file name `Grabrechnungen-{year}-{half}.pdf`.
- Selection identical to `POST odata/BulkDownloadDocuments`: normal documents, latest revisions, no deleted graves' invoices, `PeriodYear == year && PeriodHalf == half`, ordered by `FileName`.
- Missing/invalid year or half (half not 1 or 2) → 400 with code `InvalidHalfYear`. No documents → 400 with code `NoGraveInvoices`.
- The PDF holds the stored bytes' pages unchanged and in order; no blank pages (single-sided printing).
- Button label `Drucken`, icon `pi pi-print`, placed directly after the ZIP button.
- Never point E2E at the dev stack (port 8080); `cd e2e && npx playwright test …` starts its own stack.
- Conventional Commits, ending with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

## Review Focus

- A half-year with exactly one invoice — the join must still answer a valid PDF (test in Task 1).
- A superseded revision in the half-year — only the latest revision prints, as in the ZIP (covered by the shared query; Task 1 asserts the print holds the same file names as the ZIP).
- A deleted grave's invoice — left out, same reason; covered by the shared query.
- Temp files from the join must not pile up — the join cleans up even when it throws (Task 1 test).
- A refusal must not leave an empty tab open — `openPdf` closes it; already pinned by `e2e/tests/lists.spec.ts` ("a list with nothing on it says so"), so Task 2 reuses the helper unchanged rather than re-testing it.

---

### Task 1: Backend — shared selection, PDF join, print route

**Files:**

- Create: `backend/src/Invoicing.Api/Services/GraveInvoiceDocuments.cs`
- Create: `backend/src/Invoicing.Documents/PdfJoiner.cs`
- Create: `backend/src/Invoicing.Api/OData/Controllers/GraveInvoicePrintController.cs`
- Modify: `backend/src/Invoicing.Api/OData/Controllers/BulkDownloadController.cs` (use the shared query)
- Test: `backend/tests/Invoicing.DocumentTests/PdfJoinerTests.cs`, `backend/tests/Invoicing.IntegrationTests/GraveInvoicePrintTests.cs`
- Modify: `docs/spec.md` (User Story 9 scenario, FR-045a)

**Interfaces:**

- Produces: `static IQueryable<Document> GraveInvoiceDocuments.OfHalfYear(AppDbContext db, int year, int half)` — the ZIP's current `Where`, ordered by `FileName`.
- Produces: `static byte[] PdfJoiner.Join(IReadOnlyList<byte[]> pdfs)` — throws `ArgumentException` on an empty list.
- Produces: the route in Global Constraints; Task 2 calls it.

- [ ] **Step 1: Write the failing tests**

`PdfJoinerTests` (render small PDFs with QuestPDF `Document.Create` in the test, read with PdfPig):

- `Join_keeps_every_page_in_order`: inputs with 1, 2 and 1 pages whose page texts are `A1`, `B1`, `B2`, `C1` → joined has 4 pages with texts in that order.
- `Join_of_one_pdf_is_that_pdf`: one 1-page input → 1 page, its text.
- `Join_leaves_no_temp_files`: the joiner works in per-call subdirectories of `Path.Combine(Path.GetTempPath(), "invoicing-pdf-join")`; the number of subdirectories there is the same before and after a join, and after a join of a non-PDF `byte[]` that throws.
- `Join_of_nothing_is_refused`: empty list → `ArgumentException`.

`GraveInvoicePrintTests` (reuse `BulkDownloadTests.SeedAsync`'s setup — make it `internal static` or copy it):

- `A_half_years_print_holds_the_same_invoices_as_its_zip`: seed (H1 and H2 2026 generated, plus a non-grave invoice); GET `/odata/GraveInvoicesPdf?year=2026&half=2` → 200, `application/pdf`, disposition `inline`, file name `Grabrechnungen-2026-2.pdf`; the PDF's text contains each ZIP entry's invoice number in ZIP order, and its page count equals the sum of the ZIP entries' page counts.
- `A_print_needs_a_whole_half_year` (Theory: no year; year without half; half 3) → 400, code `InvalidHalfYear`.
- `A_half_year_without_invoices_is_refused`: `year=2030&half=1` → 400, code `NoGraveInvoices`.

- [ ] **Step 2: Run the tests and see them fail**

Run: `scripts/dotnet.sh test Invoicing.sln --filter "FullyQualifiedName~PdfJoiner|FullyQualifiedName~GraveInvoicePrint"` (stop the dev backend and remove stale `bin`/`obj` first, see CLAUDE.md).
Expected: compile errors / FAIL for the missing types and route.

- [ ] **Step 3: Implement**

- `GraveInvoiceDocuments.OfHalfYear`: move the query from `BulkDownloadController` unchanged (with its comments), add `.OrderBy(d => d.FileName)`; the ZIP route calls it.
- `PdfJoiner.Join`: write each input to a file in a fresh subdirectory of `Path.Combine(Path.GetTempPath(), "invoicing-pdf-join")`, `DocumentOperation.LoadFile(first)`, `.MergeFile(...)` for the rest, `.Save(output)`, read the output back; delete the directory in `finally`.
- `GraveInvoicePrintController`: `[Route("odata")]`, `[HttpGet("GraveInvoicesPdf")]` with `[FromQuery] int? year, int? half`; refusals in the `{ error = { code, message } }` shape the ZIP route uses; `NoGraveInvoices` message `"There are no grave invoices for this half-year."`; disposition as in `WorkListController.Pdf` (inline, `SetHttpFileName`).
- `docs/spec.md`: User Story 9 gets scenario 4 ("**Given** a half-year is selected on the grave overview, **When** the user prints its invoices, **Then** one PDF of that half-year's normal grave documents, the same as the bulk download's and in its order, opens in a new tab"); add FR-045a with the route, name, refusal codes and selection from Global Constraints.

- [ ] **Step 4: Run the tests and see them pass, then the whole backend suite**

Run: `scripts/dotnet.sh test Invoicing.sln`
Expected: all green, `BulkDownloadTests` unchanged and green.

- [ ] **Step 5: Commit**

```bash
git add backend docs/spec.md
git commit -m "feat(api): print a half-year's grave invoices as one PDF"
```

### Task 2: Frontend — the "Drucken" button

**Files:**

- Modify: `frontend/src/api/endpoints.ts`, `frontend/src/pages/GravesPage.vue`, `frontend/src/i18n/de.json`
- Test: `e2e/tests/graves.spec.ts` (the test "the grave list colours its status and offers the invoice mass download"), `e2e/tests/layout.spec.ts` (existing graves entry covers the wider header)

**Interfaces:**

- Consumes: the route from Task 1; `openPdf(url: string): Promise<void>` from `frontend/src/lib/download.ts`.
- Produces: `graveInvoicesPdfUrl(year: number, half: number): string` in `endpoints.ts`.

- [ ] **Step 1: Write the failing E2E assertions**

In the mass-download test, after the ZIP assertion (same serial group — it relies on the generated current half-year): click `Drucken`, wait for the new page and its request to `/odata/GraveInvoicesPdf` (the `openList` pattern in `e2e/tests/lists.spec.ts`), assert the URL holds `year=${CURRENT_YEAR}` and `half=${CURRENT_HALF}`, close the tab, and GET the URL: `application/pdf`, disposition contains `inline` and `Grabrechnungen-${CURRENT_YEAR}-${CURRENT_HALF}.pdf`, body starts with `%PDF`.

- [ ] **Step 2: Run it and see it fail**

Run: `cd e2e && npx playwright test tests/graves.spec.ts -g "mass download" --project=desktop`
Expected: FAIL, no button `Drucken`.

- [ ] **Step 3: Implement**

- `graveInvoicesPdfUrl` builds `/odata/GraveInvoicesPdf?year=…&half=…` with `URLSearchParams`, like `careListUrl`.
- `GravesPage.vue`: `printing` ref and `printAll()` mirroring `downloadZip` — same empty check with title `t("bulkGraveInvoices")` and text `t("bulkNothingToPrint", [halfYearText(...)])`, then `openPdf(graveInvoicesPdfUrl(...))`, errors to `showError`. Button after the ZIP button: label `t("printGraveInvoices")`, icon `pi pi-print`, `severity="secondary"`, `outlined`, `:loading="printing"`.
- `de.json`: `"printGraveInvoices": "Drucken"`, `"bulkNothingToPrint": "Für {0} gibt es noch keine Grabrechnungen zum Drucken."`, `"errorNoGraveInvoices": "Für dieses Halbjahr gibt es keine Grabrechnungen."`.

- [ ] **Step 4: Verify**

Run: `cd frontend && npm run typecheck && npm run lint && npm run build`, then
`cd e2e && npx playwright test tests/graves.spec.ts tests/layout.spec.ts`
Expected: clean, all green (phone/tablet layout included).

- [ ] **Step 5: Commit**

```bash
git add frontend e2e
git commit -m "feat(frontend): print a half-year's grave invoices from the grave page"
```
