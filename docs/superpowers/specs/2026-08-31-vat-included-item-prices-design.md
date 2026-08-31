# Design: VAT-Included Item Prices

**Created**: 2026-08-31

**Status**: Approved

**Input**: Add an option marking a price as including VAT. For such a line the
price billed is the entered price minus the VAT, and the option applies to that
line alone. It must work for graves too, including the split across a grave's
recipients.

## Revision — the flag lives on the line, not on the item

The first version of this design put `PriceIncludesVat` on `InvoiceItem`, the
catalogue entry, and it was built that way. That was a misreading: the option
belongs where a price is **entered** — on the line as it is added to an invoice
or to a grave — not on the item's master data. An item is not inherently gross
or net; a price is.

Everything about the conversion survives that correction unchanged. What moves
is where the flag is stored, where it is ticked, and where the three call sites
read it from. Two consequences are worth naming up front, because they invert
claims the first version made:

- **The line now stores the flag**, and must. The earlier design argued a
  snapshot on `InvoiceLine` would be worse than useless, because a flag toggled
  on the item after generation would make display and save disagree. That
  reasoning dissolves when the flag is the line's own data: there is nothing to
  disagree with, and without the column a revision would silently turn a gross
  entry into a net one.
- **`GraveInvoiceSplitter` is no longer untouched.** It has to carry the flag
  from the grave's line onto each recipient's line, because the grave-invoice
  dialog reconstructs from the invoices rather than from the grave's current
  configuration. This is a passed-through field; the split arithmetic does not
  change.

## Context

Every price this application stores is a net price. `InvoiceItem.DefaultPrice`,
the per-grave override `GraveItem.UnitPrice` and `InvoiceLine.UnitPrice` are all
net, and `DocumentDataBuilder` adds the item's VAT rate on top of them to reach
the gross total the customer pays.

That is the wrong end to type at when a price was quoted gross — "Grabpflege,
120 € inklusive". Today the user has to divide by the rate themselves and enter
the result, and the invoice then rounds its way back to something that may not
be the 120 € that was promised.

This design adds a per-line flag saying that the price on this line was entered
gross. Nothing about the invoice document changes: a line is still printed net
with the VAT shown separately, and the gross total is what the customer was
quoted.

## Decisions

Settled before the design and treated as fixed:

1. **The flag is a property of the line.** It is ticked where a price is
   entered — a line of an invoice, an item on a grave — and applies to that line
   alone. The catalogue item carries nothing: no flag, and no default that
   pre-ticks the box.
2. **A ticked line's price field means gross**: the price typed on an invoice
   line, and the price typed for an item on a grave. Only the stored invoice
   line — and therefore the PDF — is net.
3. **The import archive carries the flag on the grave-item link.**
   `mapping.csv`'s `Item` rows gain a column; `items.csv` keeps its original
   four.
4. **The document is unchanged.** No new wording, no marker, no layout change.
5. **The VAT rate still comes from the item.** That is an existing rule of this
   application and this design does not touch it; only the flag is per line.

## Data Model

One migration for this version (Principle IV). The branch has not been released
and no database has ever run the earlier version's migration, so the feature's
single migration is **rewritten** rather than supplemented — one migration per
version, as the constitution requires.

### `InvoiceLine.PriceIncludesVat` (bool, not null, default `false`)

True when this line's price was entered as a gross. The line still stores a net
in `UnitPrice`; the flag records how that net was arrived at, which is what lets
a revision dialog show the gross again and take it back unchanged. Existing
lines are false, which is exactly today's behaviour.

### `GraveItem.PriceIncludesVat` (bool, not null, default `false`)

The same, for the price a grave carries for one of its items — whether that is
the link's own `UnitPrice` or the item's `DefaultPrice` standing in for it.

### `InvoiceItem.PriceIncludesVat` — removed

The catalogue entry keeps no flag. `GravesController`'s hand-written grave
detail projection selects the **link's** flag (`i.PriceIncludesVat`) rather than
the item's.

Neither `InvoiceLinesController` nor `GraveItemsController` needs work:
`GraveItemsController` binds the whole `GraveItem` on POST, and
`ODataConventionModelBuilder` exposes every public property.

## The Conversion

The rule is a pair, unchanged from the first version, but only one half of it
is a backend function. The net direction is a pure helper in
`Invoicing.Domain`, `VatInclusivePrice.NetOf`, unit-tested (Principle III
permits unit tests for pure business logic). The backend never displays a
gross, so it has no `ToGross` counterpart: the gross direction lives in the
frontend instead, as `api.ts`'s `grossFromNet`. The round trip between the two
is pinned by a round-trip test in `VatInclusivePriceTests`, which computes the
gross with an open-coded expression rather than through a shared function:

```
ToNet(gross, rate)  = round(round(gross, 2) / (1 + rate/100), 3, AwayFromZero)
ToGross(net, rate)  = round(net * (1 + rate/100), 2, AwayFromZero)
```

**Three decimals for the net.** That is what `GraveInvoiceSplitter` already
stores and what the document already prints — `InvoiceDocument.UnitPrice`
formats `#,0.00#`, at least two and up to three decimals. The extra digit is not
cosmetic. At two decimals a count of 3 at 100,00 € gross prints a gross total of
299,99 €; at three it prints 300,00 €, because the net 84,034 survives the
per-line rounding that 84,03 does not.

**Two decimals for the gross**, and the entered gross is rounded to the cent
before it is divided. A gross price is an amount of money someone pays, so it is
a cent amount; accepting 120,005 € would only produce a price that changes by
half a cent the first time the dialog is reopened.

**The pair round-trips exactly.** `ToNet` leaves an error of at most 0,0005 from
the exact quotient, so `ToGross` is at most 0,0005 × (1 + rate/100) away from
the entered gross — under the half cent that would move it to a different
2-decimal value for any rate below 900 %. Typing 120,00 € at 19 % gives a net of
100,840 €, and 100,840 € reads back as 120,00 € forever. This is what makes a
revision safe: a dialog can be opened and saved without touching anything, and
the stored net does not move.

A rate of 0 % makes both directions the identity.

## Where the Conversion Applies

A ticked line's gross becomes a net at the three places where a price becomes a
line, and nowhere else. Each reads the flag from what was submitted or stored,
never from the catalogue item:

- `InvoiceService.BuildLinesAsync` — from `InvoiceLineInput.PriceIncludesVat`,
  for an ad-hoc invoice and its revisions. The flag is stored on the line it
  builds.
- `GraveInvoiceRevisionService.BuildGraveLinesAsync` — from the same input, onto
  the `GraveLine`.
- `GraveInvoiceGenerator`, from `GraveItem.PriceIncludesVat`, applied to the
  result of `EffectivePrice` **before** the `GraveLine` is built.

All three resolve the price the same way today (`input.UnitPrice ?? item
.DefaultPrice`), and the flag applies to whichever won, so the conversion wraps
the resolved value rather than each operand.

Converting before the split is what makes graves work. `GraveInvoiceSplitter`
divides the net on purpose — each recipient's document then works out its own
VAT from its own net and adds up on its own — so it must keep receiving a net.
Its arithmetic is unchanged; it gains only a `PriceIncludesVat` on `GraveLine`
that it copies onto every recipient's line, and recovers in `Combine`.
`InvoiceLine.GraveUnitPrice` stays what it is today: the grave's net price
before the split.

## What Does Not Change

`ShareCalculator`, `DocumentDataBuilder`, `InvoiceDocument`, and the split
arithmetic inside `GraveInvoiceSplitter`. Every invoice already generated is
unaffected, and so is every invoice generated from an unticked line.

The detail pages and revision panels also keep showing what they show now: the
invoice's real content, net lines with VAT beside them. Only the **edit
dialogs** display a gross.

## Frontend

### The item dialog is not touched

The catalogue keeps no flag, so `ItemDialog` and the items table stay exactly as
they were before this feature.

### A column of its own

Each dialog that collects a price — the invoice dialog, the grave dialog and the
grave-invoice dialog — gains a narrow column holding a CheckBox per row, bound
two-way to that row's `priceIncludesVat`. A column rather than a control tucked
under the price, for two reasons: the table's own column header labels it, and
the price `Input` stays a **direct child** of its cell.

That second point matters. `sap.m.Table` gives a cell's accessible name from the
column header, and it reaches only a cell's direct child; wrapping the `Input`
in a `VBox` silently destroys the name that existing specs find the field by.
The first version of this design hit exactly that and worked around it with an
explicit `ariaLabelledBy` and a `core:InvisibleText` in three fragments. With
the checkbox in its own column the wrapper is gone, so **that workaround is
reverted** — the automatic labelling does the job again.

### Filling and reading the row

`DraftLine` carries `priceIncludesVat`. It is filled two ways:

- **On a new row**, false — the user ticks it if the price they are about to
  type is a gross one. Picking an item does not change it; the catalogue has no
  opinion.
- **From an existing line**, when a dialog loads one: the line's own stored
  flag. The line stores a net, so the field shows `ToGross(net, rate)` when the
  flag is set — for a grave invoice, of `GraveUnitPrice`, the grave's price
  before the split.

The rate for that display comes from the item catalogue, read once when the
dialog opens and indexed by id, because the backend converts what the dialog
sends using the item's **current** rate. Using the line's stored rate instead
would make an untouched line's price move whenever an item's rate had changed
since the invoice was generated.

On save the dialogs send the field and the flag together. The backend converts.

`grossFromNet` lives in `model/api.ts` next to `lineTotal` and `invoiceTotals`,
which already mirror the backend's rounding for the same reason: a screen must
not disagree with the PDF it links to.

### Grave pages

The grave dialog stores its price as typed and its flag alongside it. The grave
detail item table is read-only and shows the stored price, so it keeps a plain
"inkl. MwSt." marker, bound to the **link's** flag.

## Import

`mapping.csv` gains a fifth column, `PriceIncludesVat`, in
`ImportArchiveLayout.MappingHeader`. It is meaningful on `Item` rows, which
carry a count and now a flag; `Recipient` rows leave it empty, as they already
leave `Count` empty. `items.csv` returns to its original four columns.

The header check is exact and the template download shares this definition, so a
freshly downloaded template validates and an archive from before this change
does not — the accepted cost of carrying the flag at all.

The column is optional in content, not in presence: an empty cell means false.
`true`/`false`, `1`/`0`, `ja`/`nein` and `yes`/`no` are accepted
case-insensitively; anything else is a row problem with a code of its own, like
every other malformed cell.

## Testing

**Unit** (`Invoicing.Domain`): the conversion pair — round-trip stability across
rates and prices, 0 %, a gross with more than two decimals, a net that needs the
third decimal. Plus `GraveInvoiceSplitter` carrying the flag onto every
recipient's line and recovering it in `Combine`.

**Integration** (`Invoicing.IntegrationTests`):

- An ad-hoc invoice with a ticked line stores the net, stores the flag, and
  totals to the entered gross.
- The same at a count above one — the case the third decimal exists for.
- A grave whose item is ticked, split across two recipients: the net is what
  divides, every recipient's line carries the flag, and their grosses sum to the
  grave's gross.
- A grave price override on a ticked item, so the override rather than the
  item's default is what gives up its VAT.
- A revision saved with the values the dialog was given reproduces the identical
  net — the double-deduction trap.
- A mixed invoice: one ticked and one unticked line, with the unticked line
  billed exactly as before.
- Import of an archive carrying the flag on a mapping row, and rejection of one
  whose `mapping.csv` still has the old four-column header.

The gross total is asserted with the integration tests rather than in
`Invoicing.DocumentTests`: it is computed by `DocumentDataBuilder`, which lives
in the API project, and the rendering below it does not know the flag exists.

**E2E**: tick the box on an invoice line, save, and check the totals on the
detail page and that the reopened dialog shows the gross with the box still
ticked; the grave path with two recipients.

## Limits

With a count above one, or a split, the printed gross can land a cent away from
`count × entered gross`. This is the property `GraveInvoiceSplitter` already
documents — every document works out its own VAT from its own net, and a
document that does not add up would be the alternative. Three decimals on the
net make it rare rather than routine.

## Out of Scope

A catalogue-level default that pre-ticks the box; a gross-entry mode for
unticked lines; any change to how the document presents VAT.
