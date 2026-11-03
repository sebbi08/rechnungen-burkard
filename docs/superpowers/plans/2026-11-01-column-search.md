# Column Search Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the single search field of the invoice, grave, item and recipient lists with a search field per column — a filter row under the headers on wide screens, a filter sheet with chips on phones and tablets — keeping the word-by-word search logic.

**Architecture:** One composable holds a list's filters and mirrors them into the URL query; two components render them (a cell for the DataTable filter row, a button + chips + bottom drawer for narrow screens). Lists the server pages (invoices, recipients) send one custom query option per column and the backend matches each in memory with `SearchWords`; lists that arrive whole (graves, items) are filtered in the browser with the same word rule.

**Tech Stack:** Vue 3, PrimeVue 4 (DataTable `filter-display="row"`, Drawer, Popover), vue-router, TanStack vue-query, odata2ts; ASP.NET Core OData, EF Core/SQLite; Playwright.

**Spec:** `docs/superpowers/specs/2026-11-01-column-search-design.md`

## Global Constraints

- Word rule everywhere: a field leaves the rows in which each of its whitespace-separated words is found in that column's value(s), any order, ignoring case including umlauts (backend `StringComparison.OrdinalIgnoreCase`; frontend `toLocaleLowerCase("de")`). A field of only whitespace is no filter.
- All filled fields apply together (AND).
- The old search field above each of the four lists is removed, with its i18n texts (`searchInvoice`, `searchGrave`, `searchByName`, `searchByNumber`).
- Filter state lives in the URL query (`router.replace`, not push); on the graves page the half-year goes there too. Any change starts on page one.
- Wide screens show the filter row only from the width at which the list shows all its columns: invoices `md`, items `lg`, recipients `lg`, graves `xl`. Below it: the "Filter" button with a count badge, chips "‹Spalte›: ‹Wert› ✕" above the table, and a bottom Drawer with a labelled field per searchable column (hidden columns included) and a "Filter zurücksetzen" button.
- Text filters render as `InputText`, fixed-value filters as `Select` whose empty option is "Alle". Each control's accessible name is `"{header} durchsuchen"` (text) or `"{header} filtern"` (select), in the row and in the sheet alike.
- German UI texts only through `frontend/src/i18n/de.json`. Invoice documents do not change.
- Pages of 100 (`PAGE_SIZE`) and the sticky paginator stay.
- Follow `CLAUDE.md`: backend tests with the backend/obj procedure, E2E only through `cd e2e && npm test`; the phone and tablet projects run only `tests/layout.spec.ts`, so sheet tests live there.

## Review Focus

- A term of only spaces, or with leading/trailing spaces, filters nothing / the same as trimmed — Task 1 and Task 3 tests.
- "müller" finds "MÜLLER" in the server-paged lists (SQLite would not) — Task 1 test.
- A term with `'`, `%`, `+` or `&` reaches the backend intact and matches literally, no 400 — Task 1 test (encoded query) and Task 4 E2E ("O'Brien & Söhne").
- A URL whose select value no longer exists (a deleted cemetery id) or carries unknown keys shows the list as if that filter were "Alle" — Task 3 rule, Task 7 E2E.
- A long chip text on a phone does not make the page scroll sideways — Task 4 layout test.

---

### Task 1: Per-column search in the backend (invoices, recipients)

**Files:**

- Modify: `backend/src/Invoicing.Api/Services/SearchWords.cs`
- Modify: `backend/src/Invoicing.Api/Services/InvoiceSearch.cs`
- Modify: `backend/src/Invoicing.Api/OData/Controllers/InvoicesController.cs:22-100`
- Modify: `backend/src/Invoicing.Api/OData/Controllers/RecipientsController.cs` (`Get()`)
- Modify: `backend/src/Invoicing.Api/Program.cs:17-21` (comment: the custom options, not just `search`)
- Test: `backend/tests/Invoicing.IntegrationTests/InvoiceSearchTests.cs` (rewrite against the new options), Create `backend/tests/Invoicing.IntegrationTests/RecipientSearchTests.cs`

**Interfaces:**

- Produces: `SearchWords.InAny(string? term, params string?[] values): bool` — true when the term has no words; otherwise every word is found (OrdinalIgnoreCase) in at least one non-null value.
- Produces: `InvoiceSearch.Filter(string? Number, string? Recipient, string? Date, string? Total)` record and `InvoiceSearch.Matches(Row row, Filter filter): bool`; `Filter.IsEmpty`.
- Produces HTTP: `GET /odata/Invoices?number=&recipient=&date=&total=` (any subset, alongside `$filter`/`$top`/`$skip`/`$count`); the `search` option is removed.
- Produces HTTP: `GET /odata/Recipients?number=&name=&address=&email=` with the same combination rules.

- [ ] **Step 1: Write the failing tests**

`InvoiceSearchTests` (seed invoices as the file does today):

- `Number_matches_the_number_only` — `number=0007` finds `2026-0007`, not an invoice whose recipient is named "0007er".
- `Recipient_matches_name_and_address_word_by_word` — `recipient=Bauer Hanna` finds "Hanna Bauer"; `recipient=Ahornweg` finds the one living there.
- `Date_matches_the_shown_date` — `date=05.2025` finds an invoice of 19.05.2025, not one of 19.06.2025.
- `Total_matches_with_comma_or_point` — a 1.234,56 € invoice is found by `total=1.234`, `total=1234,56` and `total=1234.56`; `total=12,5` finds 12,50 €.
- `Columns_combine_with_and` — `number=…&recipient=…` where only one invoice satisfies both.
- `Terms_of_only_spaces_filter_nothing` — `recipient=%20%20` returns every invoice of the filter.
- `Special_characters_arrive_intact` — recipient "O'Brien & Söhne", queried as `recipient=O'Brien%20%26%20s%C3%B6hne`, is found; status 200.

`RecipientSearchTests`:

- `Each_column_searches_its_own_field` — `name=`, `number=`, `address=`, `email=` each find by their field only.
- `Umlauts_fold_case` — `name=müller` finds "MÜLLER Hans".
- `Paging_and_count_apply_to_the_matches` — 3 matches, `$top=2&$count=true` gives 2 rows and count 3.

- [ ] **Step 2: Run them to see them fail**

Run: `scripts/dotnet.sh test Invoicing.sln --filter "FullyQualifiedName~InvoiceSearchTests|FullyQualifiedName~RecipientSearchTests"` (after the backend/obj procedure)
Expected: FAIL — the options are not read yet.

- [ ] **Step 3: Implement**

`SearchWords.InAny` as above. `InvoiceSearch.Matches` checks each non-empty part of the filter against its own column only: number → `InvoiceNumber`; recipient → name and address; date → `dd.MM.yyyy`; total → the word as typed in `GrossTotal` formatted `N2` with de-DE ("1.234,56"), and the word with `.` replaced by `,` in it formatted `0.00` with de-DE ("1234,56"). `InvoicesController.Get` reads the four options (empty = absent) instead of `search`, with the in-memory candidate path it already has. `RecipientsController.Get`: when any of its four options is present, load `(Id, Number, Name, Address, Email)` into memory, keep the ids `InAny` accepts per column, and return `_db.Recipients.Where(r => ids.Contains(r.Id))` so `[EnableQuery]` still pages and counts.

- [ ] **Step 4: Run them to see them pass**

Run: the same command. Expected: PASS. Then the whole solution: `scripts/dotnet.sh test Invoicing.sln`, all green.

- [ ] **Step 5: Commit**

`git commit -m "feat(api): search invoices and recipients column by column"`

### Task 2: Recipients on the grave status rows; drop SearchGraves

**Files:**

- Modify: `backend/src/Invoicing.Api/OData/Controllers/GraveInvoicingStatusController.cs`
- Modify: `backend/src/Invoicing.Api/OData/Controllers/GravesController.cs:103-140` (remove `SearchGraves`)
- Modify: `backend/src/Invoicing.Api/Services/GraveService.cs:273` and `CemeteriesController.cs:141` (comments that cite SearchGraves)
- Test: `backend/tests/Invoicing.IntegrationTests/GraveTermGenerationTests.cs` or the file holding the status tests; remove the SearchGraves cases in `GraveTests.cs:140-215`, `CemeteryTests.cs:~327`, `SaveGraveTests.cs:~764` (assert the same facts on the status rows where they test a stored value, otherwise delete)

**Interfaces:**

- Produces: each `GraveInvoicingStatus` row gains `Recipients: [{ Id: long, Name: string, Address: string }]`, ordered as linked.

- [ ] **Step 1: Write the failing test** — `The_status_rows_carry_the_graves_recipients`: a grave with two recipients yields a row whose `Recipients` holds both ids, names and addresses; a grave whose recipient is renamed shows the new name.
- [ ] **Step 2: Run it to see it fail** — `--filter "FullyQualifiedName~status_rows_carry"`, FAIL (no property).
- [ ] **Step 3: Implement** — one grouped query of `GraveRecipients` joined to `Recipients`, keyed by grave, added to the projected row; delete `SearchGraves`.
- [ ] **Step 4: Run the whole solution** — all green.
- [ ] **Step 5: Commit** — `git commit -m "feat(api): carry a grave's recipients on its status row"`

### Task 3: Filter state, cells and sheet (frontend foundation)

**Files:**

- Create: `frontend/src/lib/searchWords.ts`
- Create: `frontend/src/composables/useColumnFilters.ts`
- Create: `frontend/src/components/ColumnFilterCell.vue`
- Create: `frontend/src/components/ColumnFilterSheet.vue`
- Modify: `frontend/src/main.css` (row visibility classes)
- Modify: `frontend/src/i18n/de.json`

**Interfaces:**

- Produces: `matchesWords(term: string, ...values: (string | null | undefined)[]): boolean` — the Global Constraints word rule.
- Produces: `interface ColumnFilterField { key: string; header: string; kind: "text" | "select"; options?: { value: string; label: string }[] }`.
- Produces: `useColumnFilters<K extends string>(defaults: Record<K, string>): { values: Record<K, string> (reactive, what the controls bind); applied: Readonly<Ref<Record<K, string>>> (debounced 300 ms, what queries use); active: ComputedRef<number> (keys whose value differs from its default); clear(key?: K): void }`. Reads the initial values from `route.query` (unknown keys ignored, missing ones = default), writes every change with `router.replace({ query })`, omitting keys at their default.
- Produces: `<ColumnFilterCell :field v-model />` — the `#filter` slot content.
- Produces: `<ColumnFilterSheet :fields v-model:values :breakpoint="'md' | 'lg' | 'xl'" @clear />` — the button with badge, the chips and the bottom Drawer; the whole thing carries `md:hidden` / `lg:hidden` / `xl:hidden` by `breakpoint` (literal class names).
- Produces CSS: `.column-filters-md`, `.column-filters-lg`, `.column-filters-xl` on a DataTable hide its `.p-datatable-filter-row` below that width (Tailwind widths 48rem, 64rem, 80rem).
- Produces i18n: `filterButton` "Filter", `filterReset` "Filter zurücksetzen", `filterSheetTitle` "Filter", `filterAll` "Alle", `filterSearchColumn` "{0} durchsuchen", `filterSelectColumn` "{0} filtern", `filterRemove` "Filter {0} entfernen" (the chip's ✕).

- [ ] **Step 1: Implement the four files and the CSS**

A select whose stored value is not among its options shows and applies as "Alle" (the page treats an unknown value as the default). For the row, the DataTable needs `filter-display="row"`, an empty `:filters="{}"` and, per Column, `:show-filter-menu="false"` and `:show-clear-button="false"`; the slot ignores PrimeVue's `filterModel`. Verify in the browser that the row renders without a `filters` model error.

- [ ] **Step 2: Verify** — `cd frontend && npm run typecheck && npm run lint` clean. (No frontend unit-test runner exists; Tasks 4–7 exercise these through E2E.)
- [ ] **Step 3: Commit** — `git commit -m "feat(frontend): column filter state, filter row cell and filter sheet"`

### Task 4: Recipients list

**Files:**

- Modify: `frontend/src/pages/RecipientsPage.vue`
- Modify: `frontend/src/api/queries.ts` (`useRecipientsPageQuery`)
- Modify: `e2e/vue.ts` (add the helper below), `e2e/tests/masterdata.spec.ts`, `e2e/tests/layout.spec.ts`

**Interfaces:**

- Consumes: Task 1 HTTP options; Task 3 `useColumnFilters`, `ColumnFilterCell`, `ColumnFilterSheet`.
- Produces: `useRecipientsPageQuery(filters: Readonly<Ref<{ number: string; name: string; address: string; email: string }>>, first: Ref<number>)` — sends the non-empty ones as custom query options (`.execute({ params })`), no OData `$filter` for them.
- Produces (e2e): `searchColumn(page: Page, header: string, term: string, answer?: (url: string) => boolean): Promise<void>` in `e2e/vue.ts` — fills the control named `"{header} durchsuchen"` and, given `answer`, waits for that response like `search()` does; `filterColumn(page, header, option)` picks a select option through `pickOption`.

- [ ] **Step 1: Write the failing E2E tests** — in `masterdata.spec.ts`: `recipients: each column searches its own field` (seed "Erna ‹m›" at "Ahornweg ‹m›" and "Ahorn ‹m›" at "Birkenweg ‹m›"; `Name` "ahorn ‹m›" leaves only the second, `Adresse` "ahornweg" the first; the URL holds `name=`; a reload keeps the filter); `recipients: a filter with special characters` ("O'Brien & Söhne ‹m›" found by `Name` "o'brien söhne"). Adapt the existing recipient tests that typed into "Nach Name, Nummer oder Adresse suchen" to `searchColumn`. In `layout.spec.ts` (phone, tablet): `recipients: the filter sheet` — the row is hidden, "Filter" opens the sheet, filling "Name durchsuchen" shows a chip "Name: …", the badge counts 1, ✕ removes it, and a 60-character term causes no sideways scrolling (`expectNoSidewaysScrolling`).
- [ ] **Step 2: Run them to see them fail** — `cd e2e && npm test -- tests/masterdata.spec.ts tests/layout.spec.ts -g "recipients"`, FAIL.
- [ ] **Step 3: Implement** — filter keys `number`, `name`, `address`, `email` (all text); `column-filters-lg`; the old field removed.
- [ ] **Step 4: Run them to see them pass**, then the full suite `cd e2e && npm test`.
- [ ] **Step 5: Commit** — `git commit -m "feat(frontend): search the recipient list by column"`

### Task 5: Invoices list (with the Betrag column)

**Files:**

- Modify: `frontend/src/pages/InvoicesPage.vue`, `frontend/src/i18n/de.json` (`invoiceTotal` "Betrag")
- Test: `e2e/tests/invoices.spec.ts` (replace the `search=` tests at ~545-565), `e2e/tests/layout.spec.ts` (the invoices columns entry)

**Interfaces:**

- Consumes: Task 1 options `number`, `recipient`, `date`, `total`; Task 3 pieces.
- Filter keys: `number`, `recipient`, `date`, `total` (text), `paid` (select: "" Alle / `unpaid` Nur unbezahlte / `paid` Nur bezahlte; replaces today's payment `Select` and becomes the OData `PaidOn` filter as now).

- [ ] **Step 1: Write the failing E2E tests** — `the list searches each column on its own` (two invoices to "Hanna Bauer ‹m›" and "Karl Weber ‹m›" with different totals: `Empfänger` "bauer hanna" leaves one; `Betrag` with that total's "12,50" form leaves the other; `Datum` with today's `dd.MM.yyyy` keeps both; `Zahlung` "Nur bezahlte" after marking one paid leaves it); `the Betrag column shows the gross total` (cell text equals the detail page's total).
- [ ] **Step 2: Run them to see them fail.**
- [ ] **Step 3: Implement** — new `Betrag` column (`formatAmount(data.GrossTotal)`, `col-num`, visible from `md` like `Datum`); params built from `applied`; `column-filters-md`.
- [ ] **Step 4: Run them to see them pass**, then the full suite.
- [ ] **Step 5: Commit** — `git commit -m "feat(frontend): search the invoice list by column and show its totals"`

### Task 6: Items list

**Files:**

- Modify: `frontend/src/pages/ItemsPage.vue`
- Test: `e2e/tests/masterdata.spec.ts` (the item search tests), `e2e/tests/layout.spec.ts`

**Interfaces:**

- Consumes: Task 3 pieces, `matchesWords`; `useItemsQuery(ref(""))` (the whole catalogue) and `useVatRatesQuery()` from `frontend/src/api/queries.ts`.
- Filter keys: `number`, `description`, `price` (text; price matched against `formatAmount(DefaultPrice)`), `vat` (select of configured rates, value = rate as string), `quarter` (select: "" Alle / `none` ganzjährig / `Spring` / `Summer` / `Autumn` / `Winter` with their German labels).

- [ ] **Step 1: Write the failing E2E test** — `items: each column filters on its own` (an all-year 7 % item and a summer 19 % item sharing a marker: `Quartal` "Sommer" leaves the second, `MwSt` "7 %" the first, `Beschreibung` with both words reversed finds its item).
- [ ] **Step 2: Run it to see it fail.**
- [ ] **Step 3: Implement** — filtering in a `computed` over the loaded items; `column-filters-lg`.
- [ ] **Step 4: Run it to see it pass**, then the full suite.
- [ ] **Step 5: Commit** — `git commit -m "feat(frontend): search the item list by column"`

### Task 7: Graves overview (Empfänger column, half-year in the URL)

**Files:**

- Modify: `frontend/src/pages/GravesPage.vue`, `frontend/src/api/endpoints.ts` (`GraveStatusRow.Recipients`; remove `searchGraves`, `GraveSearchResult`), `frontend/src/i18n/de.json` (`graveRecipientsMany` "{0} Empfänger")
- Test: `e2e/tests/graves.spec.ts` (tests waiting on `SearchGraves?term=` at ~1048, ~1107), `e2e/tests/masterdata.spec.ts:~536`, `e2e/tests/layout.spec.ts`

**Interfaces:**

- Consumes: Task 2 `Recipients` on the rows; Task 3 pieces; `useCemeteriesQuery()`.
- Filter keys: `period` (default `halfYearKey(...currentHalfYear())`, driven by the existing year picker and H1/H2 buttons, not a column), `number`, `site`, `field`, `start`, `end` (text; start/end against `formatMonth`), `recipients` (text against every recipient's name and address), `cemetery` (select of cemetery ids as strings), `status` (select: "" / `open` offen / `invoiced` erstellt), `paid` (select: "" / `unpaid` Nur unbezahlte, today's rule `InvoiceCount > 0 && PaidCount < InvoiceCount`).
- Recipient cell: no recipient — empty; one — its name; several — a text button "{n} Empfänger" that toggles a PrimeVue `Popover` listing the names, one per line.

- [ ] **Step 1: Write the failing E2E tests** — `the overview searches each column on its own` (graves by a marker: `Empfänger` with a recipient's street finds its grave; `Friedhof` select narrows to one cemetery; `Status` "offen"; `Beginn` "01/2026"); `a grave with several recipients lists them in a popover` (two recipients: the cell reads "2 Empfänger", a press shows both names); `coming back from a grave keeps the half-year and the filters` (choose the other half-year and a `Grabnummer` filter, open a grave, go back: both are still set). Replace the `SearchGraves` waits in the existing tests with `searchColumn`. Layout: the graves sheet on tablet shows the select filters and the recipient popover stays within the screen on phone.
- [ ] **Step 2: Run them to see them fail.**
- [ ] **Step 3: Implement** — filtering in a `computed` over the status rows; `column-filters-xl`; the `term`/`searchResult` query and the payment `Select` removed; `first` resets on any change of `applied`.
- [ ] **Step 4: Run them to see them pass**, then the full suite.
- [ ] **Step 5: Commit** — `git commit -m "feat(frontend): search the grave overview by column, with its recipients"`

### Task 8: Spec, leftovers, final check

**Files:**

- Modify: `docs/spec.md` (FR-024, FR-068, FR-068a, FR-076, FR-077, FR-029a and the recipient/item list FRs: per-column search, the filter sheet, the new columns, the URL), `frontend/src/i18n/de.json` (remove the four old search keys if Tasks 4–7 left them)

- [ ] **Step 1: Search for leftovers** — `grep -rn "searchInvoice\|searchGrave\|searchByName\|searchByNumber\|SearchGraves\|search=" frontend/src backend/src e2e/tests docs/spec.md` returns nothing but the design and plan documents.
- [ ] **Step 2: Update `docs/spec.md`.**
- [ ] **Step 3: Full verification** — backend tests; `cd frontend && npm run typecheck && npm run lint && npm run build`; `npx prettier --check e2e frontend docs`; `scripts/csharpier.sh check .`; `cd e2e && npm test` twice, all projects. Everything green.
- [ ] **Step 4: Commit** — `git commit -m "docs: describe searching the lists by column"`
