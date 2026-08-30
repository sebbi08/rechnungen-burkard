# Design: Invoice and Grave Detail Pages

**Created**: 2026-08-29

**Status**: Approved

**Input**: Rework the invoice overview around non-grave invoices and a search,
move grave invoicing entirely behind the grave pages, and add detail pages for
invoices, graves and grave invoices — including editing a grave invoice for all
of its recipients at once.

## Context

Today the invoice overview lists every invoice — grave and non-grave, every
revision as its own row — filtered by a half-year select, with bulk downloads
for normal documents and for copies. The grave overview lists graves with a
plain-text invoicing status for one selected half-year and an edit button.
Neither object has a detail view, so a grave's invoicing history and an
invoice's revision history are not visible anywhere.

This design splits the two overviews along their real subjects: the invoice
page becomes the place for invoices that are not tied to a grave, and
everything about grave invoicing — status, history, downloads, editing — moves
onto the grave pages.

## Decisions

These were settled before the design and are treated as fixed:

1. **Grave edits are not versioned.** Editing a grave changes the grave record.
   Invoices already generated are snapshots and never change. The edit dialog
   states this.
2. **Editing a grave invoice affects every recipient.** One edit creates a new
   revision for each recipient invoice of that grave and half-year.
3. **The edit dialog shows the grave as it was in that half-year**, reconstructed
   from the invoices of that half-year — never from the grave's current items.
4. **Lists show the latest revision only.** Older revisions live on the detail
   pages.
5. **Invoice search covers** invoice number, recipient name, recipient address,
   the invoice date as displayed, and the invoice total. Not line items.
6. **The grave mass download** covers the normal documents of the half-year
   selected in the toolbar, across all graves.
7. **Three detail views**: grave, invoice, grave invoice.
8. **Grave rows carry icon buttons**, like invoice rows; no row navigation.
9. **Status colours**: green for invoiced, red for open.
10. **A grave's recipient list is fixed at creation** and can never be changed.

## Data Model

One migration for this version (Principle IV), adding two columns.

### `Invoice.IsLatestRevision` (bool, not null, default `true`)

An invoice is the latest revision of its chain when no other invoice shares its
`RootId` with a higher `RevisionNumber`. The value is maintained on write:

- `InvoiceService.CreateAsync` and `GraveInvoiceGenerator` set `true`.
- Every revision path sets `true` on the new row and `false` on the predecessor,
  inside the transaction that already wraps those writes.

Only `InvoiceService` and `GraveInvoiceGenerator` create invoices, so the flag
has two writers.

An index on `(GraveId, IsLatestRevision)` backs the two list queries that use it.

The migration backfills existing rows: `true` where no sibling with a higher
`RevisionNumber` shares the `RootId`, `false` otherwise.

### `InvoiceLine.GraveUnitPrice` (decimal, nullable)

On a grave invoice, the line's price **before** it was split across the
recipients — the price the grave was invoiced at. `null` on non-grave invoices,
and `null` on grave invoices generated before this migration.

It exists because the split is not losslessly reversible. Each recipient's line
stores a unit price rounded to three decimals, and recomputing the gross from
that rounded price does not always return the share it came from. For a grave
line of 2.60 EUR at 19% VAT across three recipients, reconstruction returns
2.62. Storing the grave-level price removes the arithmetic from the read path
entirely: what the edit dialog shows is what was invoiced.

The migration adds the column without a backfill. Historical rows stay `null`
and fall back to reconstruction (see below), which is best-effort; everything
generated from here on is exact.

**Why a stored column rather than a query.** "No sibling with a higher revision"
cannot be expressed in an OData `$filter`, so without the column the invoice
list would lose server-side filtering and paging, or would need a second entity
set over the same CLR type — a known source of ambiguous entity-set resolution
in the OData model builder. The column also fixes the bulk download, which today
zips superseded revisions.

## Domain: a reversible split

The even split across a grave's recipients lives inside
`GraveInvoiceGenerator.BuildLines` today. Editing a grave invoice needs the same
arithmetic in both directions, so it moves into `Invoicing.Domain` as a pure,
unit-testable pair.

```
GraveLine(ItemId, ItemNumber, Description, Count, UnitPrice, VatRate)

GraveInvoiceSplitter.Split(IReadOnlyList<GraveLine> lines, int recipients)
    -> IReadOnlyList<IReadOnlyList<InvoiceLine>>

GraveInvoiceSplitter.Combine(IReadOnlyList<IReadOnlyList<InvoiceLine>> perRecipient)
    -> IReadOnlyList<GraveLine>
```

`Split` keeps the current rules unchanged: per grave line, gross cents are
computed as `round(round(count × unitPrice × 100) × (100 + vat) / 100)`, split
with `ShareCalculator.SplitCents`, and each recipient's line derives its net and
a unit price rounded to three decimals from its share.

`Combine` is the best-effort inverse, used **only** for grave invoices
generated before `GraveUnitPrice` existed. It sums each line's gross cents
across the recipients and derives the grave-level net and unit price from that
sum. It is exact for ordinary amounts and can land a cent or two off for very
small ones, which is why it is a fallback for historical rows rather than the
read path.

`GraveInvoiceGenerator` is refactored onto `Split`, so one implementation of the
rounding rules serves both generation and editing.

**Testing**: `Combine(Split(lines, n)) == lines` for one, two, three and five
recipients, including counts and prices that force remainders, and a test that
pins the known lossy case so the limitation is documented rather than
discovered.

## Backend API

### Changed

**`InvoicesController.Get([FromQuery] string? search)`** — a custom query option
on the existing entity set. When `search` is set, the controller projects
`Id`, `InvoiceNumber`, `RecipientName`, `RecipientAddress`, `GeneratedAt` and
the lines' `Count`/`UnitPrice`/`VatRate`, matches in memory, and returns
`_db.Invoices.Where(i => matchedIds.Contains(i.Id))`. `EnableQuery` then applies
`$filter`, `$orderby`, `$skip` and `$top` on top, so paging survives.

Matching is `StringComparison.OrdinalIgnoreCase` in memory rather than SQL
`LIKE`, for the same reason `SearchGraves` does it: SQLite folds case for ASCII
only, which would miss `Ö` against `ö` in German names.

A term matches an invoice when it is contained in the invoice number, the
recipient name, the recipient address, the generation date rendered as
`dd.MM.yyyy`, or the invoice gross total. The total is computed from the
projected lines with the same rounding as the documents and rendered with two
decimals and an invariant `.` separator; a `,` in the search term is normalised
to `.` first, so both `1234,50` and `1234.50` match.

**`BulkDownloadController.Bulk`** additionally filters `IsLatestRevision`, so a
bulk download never contains a superseded revision.

**`GravesController.Save`** rejects a request whose recipient set differs from
the stored one when `GraveId` is set, with the new code
`GraveRecipientsImmutable`. Creation is unaffected.

**`GraveRecipientsController`** rejects `POST` and `DELETE` for a grave that
already has a `GraveInvoiceRun`, with the same code. Links on never-invoiced
graves stay writable: that is the path the E2E suite seeds through, and it
cannot corrupt an existing split.

### Added

**`GraveInvoiceRuns`** — a read-only entity set over the existing table, so the
grave detail page can show which half-years were invoiced without loading every
grave's status.

**`CreateGraveInvoiceRevision`** — an EDM action next to `CreateInvoice` and
`CreateRevision`.

```
POST /odata/CreateGraveInvoiceRevision
{ "invoiceId": 42, "lines": [ { "itemId": 7, "count": 2, "unitPrice": 120.00 } ] }
```

`lines` are **grave-level**: the full count and full price, as if the grave were
invoiced to a single recipient. The action:

1. Loads the invoice and requires `GraveId`, `PeriodYear` and `PeriodHalf` to be
   set; otherwise `NotAGraveInvoice`.
2. Resolves the sibling group: all invoices with the same `GraveId`,
   `PeriodYear` and `PeriodHalf` where `IsLatestRevision` is true.
3. Validates the submitted lines the same way `InvoiceService.BuildLinesAsync`
   does — item exists, count positive, a price available — producing
   `GraveLine`s.
4. Splits them across the group with `GraveInvoiceSplitter.Split`.
5. Creates one revision per sibling: same `RootId`, `Year` and `Sequence`;
   `RevisionNumber` = the chain's maximum + 1; `InvoiceNumber` = base number
   plus the revision marker; `GraveId`, `PeriodYear` and `PeriodHalf` carried
   over; `SharePercent` = 100 / N; `EndingText`, `StampWord` and `Language` from
   the current settings; recipient name and address re-snapshotted from the
   current recipient record. Normal and copy documents are generated for each.
6. Flips `IsLatestRevision` on the predecessors.

All of it runs in one transaction, so a grave and half-year is never left with
some recipients revised and others not.

Because a grave's recipient list is fixed (decision 10), the sibling group's
size always equals the grave's recipient count. The action asserts this rather
than compensating for a mismatch.

Recipient name and address are re-snapshotted deliberately: a revision is a
newly generated invoice, and FR-017 says recipient changes apply to invoices
generated after them.

**`GET /odata/GraveInvoiceGroup?invoiceId=`** — a projection route under the
`/odata` prefix, extending D-001.

```json
{
  "GraveId": 3, "GraveNumber": "A-17", "PeriodYear": 2026, "PeriodHalf": 1,
  "Recipients": [ { "Id": 8, "Name": "Anna Beispiel" }, ... ],
  "Lines": [ { "ItemId": 7, "ItemNumber": "1001", "Description": "Grabpflege",
               "Count": 2, "UnitPrice": 120.0, "VatRate": 7.0 } ]
}
```

`Lines` come from the stored `GraveUnitPrice` of any one sibling's lines, whose
`Count` and `VatRate` are already the grave's. When those are `null` — a grave
invoice generated before the migration — it falls back to
`GraveInvoiceSplitter.Combine` over the whole sibling group. Either way the edit
dialog opens on the grave as it was invoiced for that half-year, which is what
keeps a later grave edit from leaking into a revision of an older period.

It is a route rather than an EDM function because its payload is a report about
a group of invoices, not an entity — the same reasoning D-001 already records
for `GraveInvoicingStatus`.

## Frontend

### Routing

```
invoices/{rootId}                   -> InvoiceDetail
graves/{graveId}                    -> GraveDetail
graves/{graveId}/invoices/{rootId}  -> GraveInvoiceDetail
```

The route parameter is the chain's `RootId`, which every list row carries. A
detail controller queries `RootId eq {id}`; if that is empty — someone pasted a
revision's id — it falls back to reading that invoice and re-querying by its
`RootId`.

Back navigation goes to the parent screen: invoice detail to the invoice list,
grave detail to the grave list, grave-invoice detail to its grave.

All three are `sap.m` `Page`s built from `Panel`s. No new library dependency.

### Shared controller code

Three screens now open a document and format the same values, so
`Base.controller` absorbs `formatDate`, `openDocument(invoiceId, isCopy)`,
`formatInvoiceNumber` and `formatRevisionBadge`, which are duplicated across the
Invoices and Graves controllers today.

Two dialogs are each opened from two screens, so each gets an abstract
controller between `Base` and its users:

- `InvoiceEdit.controller` owns the invoice dialog and the recipient
  quick-create — extended by `Invoices` (list) and `InvoiceDetail`.
- `GraveInvoiceEdit.controller` owns the grave-invoice dialog — extended by
  `GraveDetail` and `GraveInvoiceDetail`.

Without this, the dialog handlers (`onAddLine`, `onLineItemSelected`,
`onInvoiceDialogSave`, the quick-create pair) would be copied into the detail
controllers.

### Invoices page

- The list binds `GraveId eq null and IsLatestRevision eq true`, ordered by
  `Id desc`. Grave invoices no longer appear, and each chain appears once, as its
  newest revision.
- The toolbar holds a `SearchField` and "Rechnung erstellen". The half-year
  `Select` and both bulk-download buttons are removed. Searching calls
  `changeParameters({ search: term })` on the list binding; an empty term clears
  it.
- Each row carries four icon-only buttons, each with a tooltip that doubles as
  its accessible name:

  | Action     | Icon                        | Behaviour                                |
  | ---------- | --------------------------- | ---------------------------------------- |
  | Details    | `sap-icon://detail-view`    | navigates to `invoices/{RootId}`         |
  | Rechnung   | `sap-icon://pdf-attachment` | opens the normal document                |
  | Kopie      | `sap-icon://copy`           | opens the copy                           |
  | Bearbeiten | `sap-icon://edit`           | opens the invoice dialog on this invoice |

  The buttons sit in the same wrapping `HBox` as today, so they drop below the
  row in the phone pop-in.

### Invoice detail page

A header block with the base invoice number, recipient name and address, the
latest revision's date, and its gross total. Below it one panel per revision,
newest first, titled "Original" for revision 0 and "Revision N" otherwise, each
holding:

- that revision's invoice number and generation date,
- its lines (number, description, count, unit price, VAT rate, line total),
- its net, VAT and gross totals,
- its own "Rechnung" and "Kopie" download buttons.

An edit action in the header opens the existing invoice dialog on the latest
revision, which creates a further revision through `CreateRevision`.

Data comes from
`Invoices?$filter=RootId eq {id}&$orderby=RevisionNumber desc&$expand=Lines,Documents($select=Id,FileName,Kind)`.
The `$select` on the expand keeps the PDF blobs out of the response.

### Graves page

- The status cell becomes an `ObjectStatus`: state `Success` with
  `sap-icon://accept` for "erstellt", state `Error` with `sap-icon://error` for
  "offen".
- The toolbar gains "Rechnungen (ZIP)", which bulk-downloads the normal
  documents of the half-year selected in the toolbar, across all graves. It
  carries `OverflowToolbarLayoutData priority="Low"` so it moves into the
  overflow menu on a phone instead of clipping the toolbar.
- Rows carry two icon buttons — details (`sap-icon://detail-view`, navigating to
  `graves/{Id}`) and edit (`sap-icon://edit`). The text "Bearbeiten" button is
  removed.

### Grave detail page

Header: grave number, start date, end date, creation date.

Then the current half-year's invoicing status, derived from today's date, as the
same colour-coded `ObjectStatus`, labelled with the half-year it describes
("Halbjahr H1 2026"). It reads from `GraveInvoiceRuns?$filter=GraveId eq {id}`;
a run exists exactly when the grave was invoiced for that period.

Three panels:

- **Leistungen** — the grave's items with count, effective price (the link's
  price, else the item's default) and VAT rate.
- **Empfänger** — number, name and address. Read-only, always: a grave's
  recipients are fixed.
- **Rechnungen** — every latest-revision invoice of the grave, across all
  half-years and recipients, ordered by `PeriodYear desc, PeriodHalf desc,
RecipientName`. Columns: half-year ("H1 2026"), recipient, invoice number,
  date, revision badge, gross total, and the same four icon actions as the
  invoice list — details navigating to `graves/{graveId}/invoices/{RootId}`,
  the two downloads, and edit opening the grave-invoice dialog.

A "Grab bearbeiten" button opens the existing grave dialog.

Data:
`Invoices?$filter=GraveId eq {id} and IsLatestRevision eq true&$orderby=PeriodYear desc,PeriodHalf desc,RecipientName&$expand=Lines,Documents($select=Id,FileName,Kind)`.

### Grave-invoice detail page

The revision history exactly as on the invoice detail page, plus:

- a grave block: grave number (linking back to the grave), half-year, and the
  share percentage this invoice represents,
- the other recipients of the same grave and half-year, each linking to their own
  grave-invoice detail page.

Editing from here opens the same grave-invoice dialog as the grave detail page.

### Grave-invoice edit dialog

A new fragment, `GraveInvoiceDialog.fragment.xml`.

At the top a `MessageStrip` of type `Warning`, naming what will happen
concretely rather than in the abstract:

> Die Änderung betrifft alle Empfänger dieses Grabs im Halbjahr H1 2026
> (Anna Beispiel, Bernd Beispiel). Für jeden wird eine neue Revision erstellt.

Below it the grave-level lines — item, description, count, price — prefilled
from `GraveInvoiceGroup`, with the same add-line and item-selection behaviour as
the invoice dialog.

Saving posts `CreateGraveInvoiceRevision` behind the standard blocking
`BusyDialog` (Principle VIII): documents are rendered for every recipient, so
the interface stays blocked until it finishes, and a re-entrancy guard covers
the moment before the dialog is on screen.

### Grave dialog

Two changes:

- A `MessageStrip` of type `Warning`: changes apply only to invoices generated
  from now on; invoices already generated stay unchanged.
- The recipients panel is editable only while a grave is being created. Once the
  grave exists, it renders the recipients as read-only text — no add, no delete,
  no quick-create — with a note that a grave's recipients cannot be changed.

The items panel, the dates and the prices stay editable in both cases.

## i18n

New keys in `i18n.properties` (German, the fallback bundle) and
`i18n_en.properties`:

- Row actions: `actionDetails`, `actionDownloadNormal`, `actionDownloadCopy`,
  `actionEdit` — used as tooltips, so they are also the accessible names.
- Invoice list: `searchInvoice`.
- Grave list: `bulkGraveInvoices`.
- Detail pages: `invoiceDetailTitle`, `graveDetailTitle`,
  `graveInvoiceDetailTitle`, `revisionOriginal`, `revisionLabel`, `lineTotal`,
  `netTotal`, `vatTotal`, `grossTotal`, `graveCreatedAt`,
  `currentHalfYearStatus`, `graveInvoices`, `sharePercent`, `otherRecipients`.
- Dialogs: `graveEditWarning`, `graveRecipientsFixed`, `graveInvoiceEditWarning`.
- Errors: `errorGraveRecipientsImmutable`, `errorNotAGraveInvoice`.

Removed: `allInvoices`, `bulkCopy`. `bulkNormal` is replaced by
`bulkGraveInvoices` on the grave page.

## Testing

Following Principle III, the weight is on integration and E2E tests; the unit
tests are confined to the pure split arithmetic.

**Domain unit tests**, alongside the existing `ShareCalculatorTests` in
`Invoicing.IntegrationTests`:

- `Combine(Split(lines, n)) == lines` for n of 1, 2, 3 and 5, including cases
  whose cents do not divide evenly.
- The remainder goes to the first recipients, and shares always sum to the total.

**Backend integration tests**:

- `search` matches an invoice by number, recipient name, recipient address,
  displayed date and total, and matches `Ö` against a term typed as `ö`.
- `search` composes with `$filter` and `$top`.
- `IsLatestRevision` is true on creation, moves to the new row on
  `CreateRevision`, and is backfilled correctly by the migration.
- A bulk download excludes superseded revisions.
- `CreateGraveInvoiceRevision` creates exactly one revision per recipient, their
  shares sum back to the edited grave totals, predecessors are no longer latest,
  and each revision has a normal and a copy document.
- `CreateGraveInvoiceRevision` rejects a non-grave invoice.
- `SaveGrave` rejects a changed recipient set on an existing grave and accepts an
  unchanged one.
- `GraveRecipients` POST and DELETE are rejected once the grave has a run.
- `GraveInvoiceGroup` returns the grave's lines as invoiced, unaffected by a
  subsequent grave edit.

**E2E** (`cd e2e && npm test`, against the suite's own stack — never the dev
stack):

- The invoice list shows no grave invoices and one row per chain.
- Searching by recipient name narrows the list; clearing restores it.
- The four row actions are present and the details action reaches the detail
  page, which lists both revisions after an edit.
- The grave list renders the status with its colour and offers the mass
  download.
- The grave detail page lists the grave's invoices grouped by half-year.
- Editing a grave invoice from the grave detail page bumps **both** recipients'
  invoices to `-R1`.
- `layout.spec.ts` sweeps the detail pages it can reach on a small screen — the
  invoice and grave detail pages. The grave-invoice page needs a generated
  invoice, which only the grave spec may produce, and shares its markup with
  the invoice detail page.

## Documentation updates

`docs/spec.md`:

- **US8 / US9** — the copy bulk download and half-year browsing on the invoice
  page are replaced by the invoice search, the grave-page mass download, and the
  detail pages.
- **FR-045 – FR-048** — bulk download is redefined as normal grave invoices for
  a half-year; the separate copy bulk download is removed, single copy downloads
  stay.
- **SC-006 / SC-012** — restated accordingly.
- **FR-020** — gains a companion requirement: a grave's recipient set is fixed at
  creation and cannot be changed afterwards. US5 gains an acceptance scenario for
  the rejected change.
- New requirements for the three detail pages, the invoice search fields, and
  grave-invoice revisions across all recipients.

`docs/deviations.md`: D-001's list gains `GraveInvoiceGroup`.

## Risks

**The `search` custom query option must survive the V4 list binding.** The
design assumes `ODataListBinding.changeParameters({ search: term })` sends
`search=` as a custom query option and that `EnableQuery` passes it through
untouched. Both are expected — OData V4 reserves only `$`- and `@`-prefixed
names — but the whole invoice search rests on it, so the implementation plan
verifies it against the running stack before the search UI is built. If it does
not hold, the fallback is a `SearchInvoices` route returning matching ids that
the binding applies as an `Id` filter, which costs one more D-001 route and caps
how many matches a single search can carry.

**The existing generator loses cents on very small grave amounts.** Splitting
2.60 EUR at 19% VAT across three recipients produces three invoices of 1.04 EUR
— 3.12 EUR against a grave total of 3.09 EUR, which SC-004 says must agree
within the smallest currency unit. The cause is that the split happens on gross
cents while each line then stores a rounded net unit price the document re-grosses
from. This is pre-existing behaviour, not something this work introduces, and
`GraveInvoiceSplitter.Split` reproduces it exactly so the refactor changes no
generated document. **Fixing it is out of scope here** — it would change the
totals of documents already sent out — but it is worth a follow-up, and the
splitter's tests pin the current behaviour so a later fix is a deliberate change
rather than an accident.

## Out of Scope

- Versioned graves. Grave edits are not effective-dated; invoices already
  generated are the only historical record, which decision 1 accepts.
- A combined list of grave and non-grave invoices. Grave invoices are reachable
  through their grave. Re-adding a combined view later is a filter change, not a
  rebuild.
- Searching invoices by line item. Decision 5 excludes it, which keeps the
  search on the invoice's own fields.
- Deleting invoices, which FR-036 forbids.

## Assumptions

- Invoice volume stays in the low thousands, so the search's in-memory match
  over projected rows is cheap enough. It runs on the search event, not per
  keystroke.
- The current half-year on the grave detail page is derived from the system date,
  matching the rest of the application.
- A grave that was skipped during generation has no `GraveInvoiceRun`, so it
  reads as "offen" — the same meaning the grave list already gives it.
