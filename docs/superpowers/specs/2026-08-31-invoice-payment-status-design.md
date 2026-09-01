# Design: Invoice Payment Status

**Created**: 2026-08-31

**Status**: Approved

**Input**: Mark invoices — grave invoices included — as paid, and filter the
invoice and grave lists by what is still unpaid.

## Context

An invoice is a snapshot: once generated it never changes, and a correction
creates a new revision of the same chain. Nothing in the application records
whether the money ever arrived, so the question "who still owes me something"
can only be answered outside it.

This design adds that one fact to an invoice and makes the two overviews able
to filter on it. A grave produces one invoice per recipient per half-year, so
the fact belongs to the individual invoice, not to the grave: one recipient can
have paid while the others have not.

Payment also constrains editing. A paid invoice is settled, and revising it
would move the ground under a payment that already happened, so it is locked
against revision until the payment mark is taken back.

## Decisions

These were settled before the design and are treated as fixed:

1. **Payment is per invoice.** A grave half-year with three recipients has
   three independent payment marks.
2. **The database records a payment date**, the day the invoice was marked
   paid. It is kept for later bookkeeping questions.
3. **The UI never shows that date.** Every screen shows paid or unpaid, and
   nothing else. No date field, no date picker, no date column.
4. **Marking paid is one click**, stamped with the server's current date.
5. **Unmarking asks for confirmation** before it clears the mark.
6. **A paid invoice cannot be revised.** Editing is refused while the mark is
   set; taking the mark back is how the invoice is unlocked.
7. **A grave invoice edit is blocked when any recipient of that half-year has
   paid**, and the refusal names them. It is not partially applied.
8. **"Unpaid" on the grave overview means the half-year selected in the
   toolbar**, like the invoiced/open status next to it.

## Data Model

One migration for this version (Principle IV), adding one column and one index.

### `Invoice.PaidOn` (`DateOnly?`, nullable)

Null means unpaid; a date means paid, and the date is the day the invoice was
marked. Nullability carries the whole meaning, so no second flag column exists
to disagree with it.

`DateOnly` rather than `DateTime`: a payment lands on a day, and the column is
never rendered or compared across time zones. `Grave.StartDate` and
`Grave.EndDate` already use `DateOnly`, and it maps to `Edm.Date` in the OData
model, where `PaidOn eq null` is the filter the lists need.

The migration adds the column as nullable with no default, which leaves every
existing invoice unpaid. Nothing is backfilled: the application never knew
which invoices were settled, and guessing would be worse than the truth.

The existing `(GraveId, IsLatestRevision)` index grows a third column and
becomes `(GraveId, IsLatestRevision, PaidOn)` — the invoice list filters on all
three together, and the grave detail page's query still matches the prefix, so
one index serves both instead of two competing for the same table. The
migration drops and recreates it.

### The value's writers

`PaidOn` has exactly one writer, `SetInvoicePaid` (below). Generation and every
revision path leave it at its default, so a newly created invoice — including a
revision of a previously unpaid one — starts unpaid. Decision 6 makes the case
of revising a _paid_ invoice unreachable, so no revision path has to decide
whether to carry a mark forward.

## Backend API

### Added: `SetInvoicePaid`

An EDM action next to the existing ones in `EdmModelBuilder`, handled by
`InvoiceActionsController`, following deviation D-002 (writes are actions
called through `fetch`):

```
POST /odata/SetInvoicePaid  { "InvoiceId": 42, "Paid": true }
```

`Paid: true` sets `PaidOn` to `IClock.GetLocalNow()`'s date — the local date,
because "today" here is the user's day, not UTC's. `Paid: false` sets it to
null. Both are idempotent: marking an already-paid invoice keeps the existing
date rather than restamping it, so a double click cannot move a recorded date.

It returns the updated invoice from the `Invoices` entity set, and refuses
with a `ValidationException` when:

- `InvoiceNotFound` — no invoice with that id.
- `NotLatestRevision` — the invoice is superseded. Lists show latest revisions
  only, so an older one can only be reached deliberately, and marking it would
  put the payment on a row nothing displays.

### Changed: `InvoiceService.CreateRevisionAsync`

Refuses with `InvoicePaid` ("The invoice is paid and cannot be revised.") when
the edited invoice's `PaidOn` is set. The check sits with the existing
`GraveInvoiceRevisionRequired` guard, before any line is built.

### Changed: `GraveInvoiceRevisionService.CreateAsync`

The service already loads the half-year's siblings to split the grave's lines
across them. After that load and before any write, it refuses with
`GraveInvoicePaid` when any sibling has `PaidOn` set. The message names the
paid recipients:

> The invoices of Anna Meier, Josef Huber are paid and cannot be revised.

Naming them is the point: from the row the user pressed edit on, another
recipient's payment is invisible, and "this cannot be edited" alone would give
them nothing to act on.

### Changed: `GraveInvoicingStatus`

Each row gains two numbers for the requested half-year, counted over
latest-revision invoices of that grave and period:

- `InvoiceCount` — how many exist.
- `PaidCount` — how many of those are paid.

The frontend derives the four states from the pair, so the endpoint stays a
projection of counts rather than a place where display logic lives. The
existing `Invoiced` flag and the "open graves first" ordering are unchanged:
this adds columns to the answer, not a new order.

### Unchanged

`Invoices` gains `PaidOn` as an ordinary EDM property, so `$filter=PaidOn eq
null` works without a new endpoint. No PATCH is opened on the entity set;
`Invoice` stays append-only except through the action above.

## Frontend

`api.ts` gains `setInvoicePaid(invoiceId, paid)`, mapping errors the way the
other writes do. `InvoiceRow` and `GraveStatusRow` gain the new fields.

### Shared controller code

Marking, unmarking and rendering the status appear on the invoice list, the
grave detail list and both detail pages. They live in `Base.controller.ts`:

- `paidStatusText(paidOn)` / `paidStatusState(paidOn)` — "bezahlt" in `Success`,
  "unbezahlt" in `Warning`. Not "offen": the graves page already uses that word
  for a half-year with no invoice, and one word for two states in neighbouring
  columns would be unreadable. Warning rather than the graves page's `Error`
  red: an unpaid invoice that was just written is the normal case, while a
  grave with no invoice for a half-year that is already running is not.
- `togglePaid(invoiceId, paidOn)` — marks straight away when unpaid; when paid,
  opens `MessageBox.confirm` and clears only on confirmation. Returns whether
  anything changed, so each caller refreshes its own model.

### Invoices page

A _Bezahlt_ column between the revision and the actions, and a fifth row button
bound to `togglePaid`: `sap-icon://money-bills` with the "mark paid" tooltip
when unpaid, `sap-icon://decline` with the "mark unpaid" tooltip when paid. The
action column widens to fit five buttons.

The edit button is disabled on a paid row, with a tooltip naming the reason.
The backend refuses regardless; this only keeps the user from opening a dialog
whose save cannot succeed.

A payment `Select` in the toolbar — all / unpaid / paid — composes its clause
onto the list binding's static filter through `changeParameters`:

```
GraveId eq null and IsLatestRevision eq true and PaidOn eq null
```

`changeParameters` is what the search field already uses, and `search` is a
separate query option, so a filtered search stays filtered.

### Graves page

A _Zahlung_ column after the invoicing status, showing one of four states
derived from the counts:

| Condition                   | Text              | State     |
| --------------------------- | ----------------- | --------- |
| `InvoiceCount == 0`         | _(cell empty)_    | —         |
| `PaidCount == 0`            | unbezahlt         | `Warning` |
| `PaidCount < InvoiceCount`  | teilweise bezahlt | `Warning` |
| `PaidCount == InvoiceCount` | bezahlt           | `Success` |

The cell stays empty for a grave with no invoice yet — the status beside it
already reports that in red, and a second column repeating it as a payment
problem would say the same thing twice in different words.

A payment `Select` in the toolbar (all / only unpaid) narrows the rows inside
`applyRows()`, where the search already narrows them, so the two compose and a
half-year change re-reads both. "Only unpaid" keeps rows with
`InvoiceCount > 0 && PaidCount < InvoiceCount` — a grave with nothing invoiced
yet has nothing outstanding, and the invoicing status is where that grave is
already visible.

### Grave detail page

The grave's invoice table gets the same column and the same toggle button per
row, and disables edit on a paid row. This is where per-recipient marking
happens: the Invoices page shows non-grave invoices only.

`refresh()` already reloads the page's model; marking calls it, so the header's
half-year status and the row agree afterwards.

### Invoice and grave-invoice detail pages

An `ObjectStatus` for the payment state in the header block, and a button
beside the existing edit button in `headerContent` that toggles it. The edit
button is disabled while the invoice is paid.

`RevisionPanel` is shared by both pages and lists older revisions. It gains no
payment column: only the latest revision can carry a mark.

## i18n

New keys in `i18n.properties` and `i18n_en.properties`:

`paidStatus`, `statusPaid`, `statusUnpaid`, `statusPartlyPaid`,
`actionMarkPaid`, `actionMarkUnpaid`,
`confirmMarkUnpaid`, `paymentFilter`, `paymentFilterAll`,
`paymentFilterUnpaid`, `paymentFilterPaid`, `editBlockedPaid`, and error
messages for `InvoicePaid`, `GraveInvoicePaid` and `NotLatestRevision`.

## Testing

### Backend integration tests (`Invoicing.IntegrationTests`)

A new `PaymentTests.cs`:

- Marking sets today's local date; unmarking clears it.
- Marking twice keeps the first date.
- Marking a superseded revision is refused with `NotLatestRevision`.
- `CreateRevision` on a paid invoice is refused with `InvoicePaid`, and the
  invoice keeps its single revision.
- `CreateGraveInvoiceRevision` is refused with `GraveInvoicePaid` when one
  sibling is paid, the message names that recipient, and no sibling gained a
  revision.
- The same call succeeds once the mark is taken back.
- `$filter=PaidOn eq null` returns exactly the unpaid invoices.

Extending `GraveTests`/`GraveGenerationTests` fixtures: `GraveInvoicingStatus`
reports `InvoiceCount` and `PaidCount` for the requested half-year, including a
two-recipient grave with one paid, and counts only latest revisions.

### E2E (`e2e/tests`)

In `invoices.spec.ts`: an invoice is created, marked paid, and is gone from the
list under the unpaid filter; unmarking asks for confirmation and brings it
back; the edit button is disabled while it is paid.

In `graves.spec.ts`: a grave with two recipients, invoiced for a half-year,
reads _teilweise bezahlt_ after one recipient is marked and _bezahlt_ after
both; the unpaid filter drops it once both are paid. Both specs call
`waitForDialogOpen` before touching the confirmation dialog, per `e2e/dialog.ts`.

### Migration

A database written before the column is opened by the new build, and every
existing invoice reads as unpaid.

## Documentation updates

- `docs/spec.md`: new functional requirements continuing the FR numbering, for
  the payment mark, the revision lock, and the two filters.
- `CLAUDE.md`: nothing. The feature adds no new local workflow.
- `docs/deviations.md`: nothing. `SetInvoicePaid` is an EDM action and
  `GraveInvoicingStatus` is already recorded under D-001.

## Risks

**A payment mark can be silently lost.** Unmarking clears the date rather than
keeping a history, so an accidental confirmation loses the day the invoice was
paid. The confirmation dialog is the whole mitigation; a payment history is out
of scope.

**The date is written but never shown.** Nothing in the UI can reveal a wrong
date, so a mark made on the wrong day stays wrong until something reads the
column. Accepted deliberately (decision 3): the value is being collected now so
it exists when a report wants it.

**The grave overview costs one more aggregate per half-year.** The status
endpoint gains a grouped count over `Invoices`, which the
`(GraveId, IsLatestRevision)` index already covers.

## Out of Scope

- Editing or displaying the payment date, and any report over it.
- Partial payments, amounts received, or an outstanding balance.
- Payment marks on non-latest revisions.
- Bulk "mark all paid" over a half-year or a filtered list.
- Reminders, due dates, or anything computed from how long an invoice has been
  unpaid.

## Assumptions

- The server's local date is the user's date; the application already runs on
  one machine for one office.
- Payment is binary at invoice granularity: an invoice is either settled or it
  is not.
- Marking is rare enough that one click per invoice is acceptable, and no bulk
  action is needed.
