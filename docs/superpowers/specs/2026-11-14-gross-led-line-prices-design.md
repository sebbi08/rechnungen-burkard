# Design: Gross-Led Line Prices

**Created**: 2026-11-14

**Status**: Approved

**Input**: Issue 73. On an ordinary invoice, a line priced 75,00 € including
19 % VAT prints as 75,01 €, unit price and total. A price entered as a gross
must come out on the invoice exactly as entered. In the same change, graves stop
offering "including VAT" altogether: a grave price is always net.

## Intent

The VAT-included flag exists for prices agreed with a customer as a gross —
"Grabpflege, 75 € inklusive". Today the entered gross is converted to a net
rounded to the cent (`VatInclusivePrice.NetOf`), only that net is stored on the
line, and the document turns it back into a gross: 75,00 / 1,19 = 63,0252… →
63,03, and 63,03 × 1,19 = 75,0057 → 75,01. With a count above one it gets worse
(3 × 10,00 € bills 29,99 €). A customer who was promised 75,00 € sees 75,01 €,
and the owner has to explain a cent.

Graves never needed the flag: their prices are kept net, and the gross handling
there (the gross check in `GraveShare`, the entered gross carried through the
split) is complexity without a user. The owner confirmed that no grave in the
real data has the flag set.

**Success** is an ordinary invoice or quote whose VAT-included line prints the
typed gross as its unit price, count × that gross as its total, and a VAT block
that adds up — with no invoice already issued changing by a cent anywhere in the
app.

## Goals

1. An ordinary invoice or quote line entered VAT-included is **gross-led**: the
   entered gross is stored, the line's gross total is count × gross exactly, and
   its VAT and net are derived from that total.
2. The editors' running totals, the PDF, the stored totals the lists show, and a
   revision's dialog all agree with that.
3. Graves lose the flag everywhere: grave editor, grave-invoice revision dialog,
   billing check and correction, price increase, work lists, items' grave
   overview, import archive.

## Non-goals

- No change to how net lines are calculated.
- No change to grave billing arithmetic beyond removing the gross branch.
- No change to the VAT rate source: it still comes from the item.
- No new wording or layout on the document. A VAT-included line already prints
  "19 % Incl." with a gross unit price and gross total (FR-037c); only the
  numbers become right.

## Anti-goals

- **No issued invoice may change.** Not its stored PDF (that is stored and never
  touched), and not the totals the app shows for it in lists or on its detail
  page — a list disagreeing with the PDF by a cent is exactly the kind of thing
  this issue is about.
- A revision opened and saved without touching anything must not move a
  gross-led line's price. _(My call:)_ this binds lines entered on or after this
  version. An old flagged line re-saved in a revision becomes gross-led with
  the gross its dialog showed (the gross its PDF printed per unit); with a
  count above one its total may then differ by a cent from the original's.
  Keeping such a line net-led would need a per-line "untouched" marker in the
  editor payload, which buys nothing for a version that is new anyway.

## Constraints

- Constitution Principle IV: one migration per version. The unreleased version
  after v3.4.0 already ships two (deviation D-005). See "Migration" below.
- Prices are held to the cent wherever they are typed (`InvalidPrice` for a
  third decimal); a gross is a cent amount.
- Rounding is `MidpointRounding.AwayFromZero` throughout, as everywhere else.
- PDFs are stored (`Document.Content`, `Quote.Content`) and never re-rendered
  for an existing version.

## Approaches

Considered:

- **A. A separate column for the gross (chosen).** `InvoiceLine` and
  `QuoteLine` gain a nullable `GrossUnitPrice`. A line that has one is
  gross-led; a line without one is calculated exactly as today. Old lines with
  the flag have no gross stored and keep their old (net-led) arithmetic, so
  nothing issued moves. Cost: the old net-led-with-flag path stays in the
  calculation as a fallback for history.
- **B. Reinterpret `UnitPrice`.** `UnitPrice` holds whatever was entered, the
  flag says whether it is gross, and the migration converts old flagged lines to
  the gross their PDF printed. One price, one rule — but an old flagged line
  with a count above one shifts by a cent (3 × 63,03 net billed 225,02 €; a
  migrated 3 × 75,01 gross bills 225,03 €), so the list and the stored PDF
  disagree. Rejected for violating the first anti-goal.
- (Converting the net at three decimals, as an earlier design did, was not taken
  further: it narrows the drift but cannot remove it for every count and rate,
  and the owner chose approach 2 of the issue — gross stored, VAT taken out of
  it.)

The owner chose **A**.

## Data Model

### `InvoiceLine.GrossUnitPrice` and `QuoteLine.GrossUnitPrice` (decimal?, null)

The gross per unit as entered, two decimals, set exactly when the line was
entered VAT-included **on or after this version**. Null on every net line, on
every grave-invoice line, and on every line that existed before this version.

`PriceIncludesVat` stays as it is: it still says "this line was entered gross"
and drives the "Incl." marker. The invariant for new lines is
`GrossUnitPrice != null ⇔ PriceIncludesVat` on ordinary invoices and quotes.

`UnitPrice` on a gross-led line keeps holding a per-unit net,
`round(gross / (1 + rate/100), 2)`, purely so that every existing reader of
`UnitPrice` still gets a sensible number. The calculation does not use it for a
gross-led line. _(Builder's call whether anything still reads it; if nothing
does, it may be stored the same way regardless.)_

### `GraveItem.PriceIncludesVat` — removed

The column is dropped. The owner confirmed no row has it set; the migration
nevertheless converts defensively before dropping — any row with the flag set
gets `UnitPrice = NetOf(UnitPrice ?? item.DefaultPrice)` — so that a database
that does have one bills exactly what it billed before rather than silently
billing the gross as a net.

`InvoiceLine.PriceIncludesVat` on **grave invoices** stays as a column (it is
shared with ordinary invoices) and keeps whatever old grave-invoice lines
recorded. New grave-invoice lines always store false.

### Migration

One migration adds the two `GrossUnitPrice` columns and drops
`GraveItem.PriceIncludesVat` (with the defensive conversion above).

This is the third migration of the unreleased version, which D-005 allowed for
two. **The owner chose to extend D-005 to three** rather than cut a release
first. Folding it into `DocumentComments` was not an option: the development
database has already recorded that migration (D-005, "Why").

## The Calculation

`DocumentDataBuilder.Calculate` is the one place where a line becomes printed
figures; the stored list totals (`NetTotal`, `VatTotal`, `GrossTotal`) and the
quote list are stamped from the same figures. `LineSource` gains
`GrossUnitPrice`.

For a **gross-led line** (`GrossUnitPrice` is set):

```
lineGross = count × grossUnitPrice                   (exact: whole cents × integer)
lineVat   = round(lineGross × rate / (100 + rate), 2, AwayFromZero)
lineNet   = lineGross − lineVat
```

75,00 € at 19 % → VAT 11,97 €, net 63,03 €, total 75,00 €.
3 × 10,00 € at 19 % → 30,00 €, VAT 4,79 €, net 25,21 €.
At 0 % VAT is 0 and net equals gross.

For **every other line** nothing changes: net = round(count × unitPrice, 2), VAT =
round(net × rate / 100, 2). That includes old lines with the flag but no stored
gross.

The VAT block is unchanged in form: per rate, the sum of the lines' nets and the
sum of the lines' VATs (already summed per line today), gross = net + VAT. A
rate group mixing gross-led and net lines therefore adds up by construction.

`LineData` printed by `InvoiceDocument`:

- unit price: `GrossUnitPrice` for a gross-led line; for an old flagged line the
  existing `round(unitPrice × (1 + rate/100), 2)`; `UnitPrice` otherwise.
- total: `lineGross` for a gross-led line; `LineTotal + VatAmount` for an old
  flagged line (as today); `LineTotal` otherwise.

The comment in `InvoiceDocument` about "3 x 10,00 at 19 % can bill 29,99" is
rewritten: it now only holds for lines from before this version.

`VatInclusivePrice.NetOf` stays for computing the informational `UnitPrice` and
for the migration's defensive conversion. `GrossOf` stays for the work lists'
net → gross conversion (FR-104), minus its gross branch.

## Where Lines Are Built

Every ordinary-invoice and quote line is built by `LineBuilder` and then, in
two places, copied field by field:

- `LineBuilder` (ordinary invoices and quotes, new and revised): for an input
  with `PriceIncludesVat`, store `GrossUnitPrice` = the resolved price (the
  typed one, else the item's default). Reading the default as a gross when the
  box is ticked is today's behaviour (`unitPrice.Value` resolves typed-or-default
  before `NetOf`) and stays. `UnitPrice` as above. The existing two-decimal
  check applies to the gross.
- `QuoteService` copies the built `InvoiceLine`s into `QuoteLine`s (the
  `new QuoteLine { … }` projection): it copies `GrossUnitPrice` too.
- `InvoiceService`'s quote conversion copies the quote's lines "as quoted, not
  through LineBuilder": it copies `GrossUnitPrice` too (FR-100's list of what a
  converted invoice carries gains it).
- Grave paths no longer take a flag (below). A grave invoice cannot be revised
  through the ordinary path (`GraveInvoiceRevisionRequired`), so no grave line
  ever reaches `LineBuilder` or the copies above.

## Revisions and the Editors

- **Invoice and quote editors** (`InvoiceEditor`, the quote editor, via
  `LineEditor` and `lib/lines.ts`): when opening a version, a gross-led line's
  price field shows its stored `GrossUnitPrice` directly; an old flagged line
  shows `grossFromNet(UnitPrice, rate)` as today. Saving sends what the field
  shows, so an untouched gross-led line saves the identical gross. An old
  flagged line re-saved in a revision becomes gross-led with the gross the
  dialog showed; the revision's total may then differ by a cent from the
  original's — that is the fix reaching the new version, and the original stays
  as it was.
- **Running totals** in `LineEditor` follow the calculation above for a ticked
  line (gross × count, VAT out of it), so the estimate matches the saved
  invoice.
- **Detail page and revision panel** show a gross-led line's unit price as the
  document prints it (the gross), alongside the existing "inkl. MwSt." column.
  _(Builder's call on exactly which fields; the rule is "what the PDF prints".)_
- OData: `GrossUnitPrice` is exposed on the line entities like every other
  property; regenerate `src/api/metadata.xml` and `src/api/generated/`.

## Graves: Removing the Flag

Everything below loses its `PriceIncludesVat`, `EnteredGross` or gross branch:

- Domain: `HalfYearBilling` (link record and `NetOf` call), `GraveLine` and
  `GraveInvoiceSplitter` (pass-through and `Combine`), `GraveShare`
  (`PricedGross`, `Owed` — the check of FR-021's last sentence disappears),
  `GraveInvoiceGroupLines`.
- API: `GraveBilling.LinkFor/LineFor`, `GraveService` save input,
  `GravesController` detail projection, `GraveBillingValidationController`,
  `GraveInvoiceRevisionService` (input flag, `NetOf`), `GraveInvoiceGroupController`,
  `ItemGravesController` (group by price only), `ItemPriceIncreaseService`
  (rows no longer distinguished by gross), `WorkListService` (amount is always
  net → gross with the item's rate).
- Import: `mapping.csv` loses the `PriceIncludesVat` column in the template and
  in `ImportArchiveLayout`. _(My call:)_ an archive that still carries the
  column is accepted; a link whose value is true is a problem naming the row
  ("Grabpreise sind immer netto"), false or empty is ignored. That keeps
  archives produced by an older `scripts/friedwin/convert.py` importable — it
  has only ever written `false` there — and `convert.py` stops writing the
  column.
- Frontend: the checkbox column in `GraveEditor` and `GraveInvoiceEditor`,
  `useGraveInvoiceEdit`, `lib/billing.ts`, `lib/priceIncrease.ts`,
  `ItemPriceIncreaseDialog`, `ItemDialog`'s grave overview label,
  `GraveDetailPage`, and the i18n keys that only they use.
- An old grave invoice whose lines carry the flag: its stored PDF stays; a
  revision through the grave-invoice dialog shows and saves the net, flag off.

## Spec and Docs

`docs/spec.md`:

- FR-021: drop the sentence about a rate carrying a gross.
- FR-037c: unchanged wording; add that the gross unit price and total are the
  entered gross and count × gross.
- FR-053 (and FR-052's cross-reference): the mapping has no VAT-included flag;
  describe the tolerated legacy column.
- FR-094 / FR-100: a quote version and a converted invoice also carry the
  entered gross.
- FR-104: "a price entered as gross is taken as entered" goes; every grave
  price is net.
- A new FR (next free number in the invoice section) stating the gross-led rule
  and the arithmetic above, and an edge case: an old invoice keeps its figures;
  its revision takes the fixed arithmetic.

`docs/deviations.md`: D-005 becomes "v3.5.0 ships three migrations", naming
this one (issue 73) with the same reasoning and containment — it adds two
nullable columns and drops one, after a data conversion that touches no row in
the owner's data.

## Tests

- Domain/integration: gross-led line arithmetic (75,00 → 75,00 / 11,97 /
  63,03; 3 × 10,00 → 30,00; 0 %; a rate group mixing gross-led and net lines);
  an old flagged line without a gross keeps today's figures; revision of an
  untouched gross-led line keeps the gross; quote conversion carries the gross;
  item default price read as gross.
- Migration: a flagged `GraveItem` (with and without its own price) bills the
  same net after migration.
- Graves: existing VAT-included grave tests are deleted or rewritten as net;
  import with legacy column false/empty accepted, true refused.
- Document test: the PDF prints 75,00 unit and total for the issue's case.
- E2E: `vat-included.spec.ts` covers the invoice case end to end and loses its
  grave cases; `seed.ts` and grave specs lose the flag.

## Left to the Builder

Names of helpers, where exactly the gross-led branch sits inside `Calculate`,
whether `UnitPrice` on gross-led lines is read anywhere, the exact i18n text of
the import problem, and the order of the plan's tasks.
