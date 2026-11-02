# Design: Searching the Lists by Column

**Created**: 2026-11-01

**Status**: Approved design (pre-implementation)

**Source**: the owner's request of 2026-11-01, refined in conversation with
mockups. It replaces the single search field above each list.

## 1. Intent

Each of the four lists — invoices, graves, items ("Leistungen") and
recipients — has one search field today that looks for every word of a term in
any of several fields. The owner wants to search where the data is: in the
columns of the table. On a wide screen every column gets its own search field
in a row under the column headers; on a phone, where most columns are hidden,
one "Filter" button opens a sheet with a field per column. Every search keeps
the word-by-word logic the app already has (`SearchWords` in the backend,
`searchWords`/`wordsIn` in `frontend/src/api/queries.ts`): a field leaves the
rows in which each of its words is found, in any order, ignoring case, so
"Hanna Bauer" finds "Bauer Hanna".

## 2. Recorded Decisions

| Topic                      | Decision                                                                                                                                                                                                                                                               |
| -------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Lists                      | Invoices, graves, items, recipients — all four                                                                                                                                                                                                                         |
| Old search field           | Removed from all four lists, with its texts                                                                                                                                                                                                                            |
| Wide screens               | A filter row directly under the column headers: a text field for text columns, a select for columns with fixed values                                                                                                                                                  |
| Within a field             | Word by word: every word of the field must be found in that column's value, in any order, ignoring case (umlauts included, as the backend's `OrdinalIgnoreCase` compares)                                                                                              |
| Across fields              | All filled fields apply together (AND)                                                                                                                                                                                                                                 |
| Phone (variant A)          | A "Filter" button with the number of active filters opens a bottom sheet with one field per column, hidden columns included; active filters show as chips above the list, each removable with ✕                                                                        |
| Which layout when          | The sheet wherever the list hides columns, so tablets too; the header row only from the width at which the list shows all its columns                                                                                                                                  |
| Columns without search     | Columns holding only buttons (edit, download, row actions)                                                                                                                                                                                                             |
| Paging                     | The pages of 100 (FR-068a) are cut from the filtered list; any change to a filter starts on page one; the sticky paginator stays                                                                                                                                       |
| Keeping the search         | The filters live in the page's URL query, so leaving a list for a detail page and coming back, reloading, or sharing a link shows the same filtered list; a breadcrumb back to the list leads to it as it was left (the builder's call made in conversation by Claude) |
| Half-year, year            | The graves overview keeps its half-year and year pickers above the table; they are not column filters, but they go into the URL query with the filters, so coming back from a grave shows the half-year that was being viewed (Claude's call)                          |
| Case and umlauts           | Every filter of every list folds case including umlauts ("ö" finds "Ö"). SQLite's `lower()` folds A–Z only, so filters the server applies run in memory, as `InvoicesController`'s search already does (Claude's call)                                                 |
| Invoices: new column       | "Betrag" (gross total), searchable, so the total stays findable                                                                                                                                                                                                        |
| Invoices: recipient column | Shows the name; its filter searches the recipient's name and address                                                                                                                                                                                                   |
| Graves: new column         | "Empfänger": one recipient shows its name; several show "mehrere" (wording below), which on a press opens a small popover listing all names. Its filter searches the names and addresses of all of the grave's recipients                                              |
| Not printed                | Nothing here changes invoice documents                                                                                                                                                                                                                                 |

## 3. The Lists and Their Filters

Columns as they are today (by their German header), with the new ones marked.
"Text" is a text field with the word logic; "Select" is a single-choice select
with "Alle" as its empty value.

### Invoices (`InvoicesPage.vue`, server-side paged)

| Column           | Filter | Matches                                                                            |
| ---------------- | ------ | ---------------------------------------------------------------------------------- |
| Rechnungsnummer  | Text   | the number as shown (incl. a revision's "-R1")                                     |
| Empfänger        | Text   | recipient name and address (snapshot on the invoice)                               |
| Datum            | Text   | the date as shown, `dd.MM.yyyy` ("05.2025" finds May 2025)                         |
| **Betrag** (new) | Text   | the gross total as an amount (FR-068b): "1.234,56 €", "1234,56", "1234.56", "12,5" |
| Zahlung          | Select | Alle / Nur unbezahlte / Nur bezahlte (today's payment filter moves here)           |

The `search` custom query option of `InvoicesController` becomes one option per
column (shape left to the builder), each evaluated with `SearchWords` against
that column only. `InvoiceSearch` already computes the gross total and the
displayed date; it is split per column rather than rewritten.

### Graves (`GravesPage.vue`, the half-year's list arrives whole and is filtered in the browser)

| Column              | Filter | Matches                                                   |
| ------------------- | ------ | --------------------------------------------------------- |
| Grabnummer          | Text   | grave number                                              |
| Grabstätte          | Text   | site name                                                 |
| Friedhof            | Select | the cemeteries from the settings                          |
| Feld                | Text   | field number                                              |
| Beginn / Ende       | Text   | the month as shown, `MM/yyyy`                             |
| **Empfänger** (new) | Text   | names and addresses of all the grave's recipients         |
| Status              | Select | Alle / offen / erstellt (the existing invoicing status)   |
| Zahlung             | Select | Alle / Nur unbezahlte (today's payment filter moves here) |

The status rows (`GraveInvoicingStatus`) gain the grave's recipients (id,
name, address) so the column can show and filter them; `SearchGraves` is then
no longer needed by this page (remove it if nothing else uses it). Filtering in
the browser must fold case the way the backend does, so "ö" finds "Ö".

Recipient cell: one recipient — the name. Two to four — the text
"{n} Empfänger" as a button; pressing it opens a PrimeVue Popover with every
name, one per line. (Wording "{n} Empfänger" is Claude's call for "mehrere";
the owner may want plain "mehrere".)

### Items (`ItemsPage.vue`, read whole)

| Column       | Filter | Matches                                                 |
| ------------ | ------ | ------------------------------------------------------- |
| Nummer       | Text   | item number                                             |
| Beschreibung | Text   | description                                             |
| MwSt         | Select | the configured VAT rates                                |
| Quartal      | Select | Alle / ganzjährig / Frühjahr / Sommer / Herbst / Winter |
| Preis        | Text   | the default price as an amount, matched as "Betrag" is  |

### Recipients (`RecipientsPage.vue`, server-side paged)

| Column  | Filter | Matches             |
| ------- | ------ | ------------------- |
| Nummer  | Text   | recipient number    |
| Name    | Text   | name                |
| Adresse | Text   | address (all lines) |
| E-Mail  | Text   | e-mail              |

Filtered on the server, which pages it (thousands of rows), but in memory with
`SearchWords` and `OrdinalIgnoreCase` per column, through custom query options
the way `InvoicesController` handles `search` — not with OData `tolower` +
`contains`, which SQLite folds for A–Z only. The pickers in the editors keep
their own search unchanged.

## 4. Phone and Tablet

Below the width at which a list shows all its columns (the widest
`*:table-cell` breakpoint its columns use: invoices `md`, items and recipients
`lg`, graves `xl`), the filter row is not shown. Instead, above the table:

- a button "Filter" with a badge counting the filled fields;
- the active filters as chips ("Grabstätte: bauer ✕"); ✕ empties that field;
- the button opens a PrimeVue Drawer from the bottom with a labelled field per
  searchable column, in column order, hidden columns included, the same
  text/select kinds as the row, and a button to clear all.

The sheet and the row edit the same filter state; switching width keeps it.
Filtering applies as one types, as today (debounced like the current fields),
in the sheet as in the row.

Mobile layout constraints already in `e2e/tests/layout.spec.ts` (no sideways
scrolling, overlays within the screen) apply to the chips, the sheet and the
recipient popover.

## 5. Goals, Non-Goals, Anti-Goals

- Goal: find a row by what one sees in its column, on any device, without
  learning a search syntax.
- Non-goal: sorting by column, saved filters, filters on the detail pages,
  the item and recipient pickers in the editors (they keep their own search).
- Anti-goal: a phone screen that scrolls sideways or a filter row that eats the
  table on a tablet; a search that finds less than today's for the same words
  in the same column.

## 6. Left to the Builder

- The API shape of the per-column invoice search and of the recipients in the
  status rows.
- How the filter state maps to the URL query (names, encoding) and is shared
  between row and sheet (a composable is the obvious home).
- Debounce timing, empty-state texts, the clear-all button's place.
- Updating `docs/spec.md` (FR-068, FR-068a, FR-024, the payment-filter FRs and
  the grave overview FRs) and the tests: backend integration tests for the
  per-column invoice search and the status rows' recipients, E2E tests per list
  for the row (desktop) and the sheet with chips (phone project), and the
  existing specs that use the removed search fields (they locate them by
  placeholder texts such as "Nach Name oder Nummer suchen").
