# Design: VAT-Included Item Prices

**Created**: 2026-08-31

**Status**: Approved

**Input**: Add an option on an invoice item that marks its price as including
VAT. For such an item the price billed is the entered price minus the VAT, and
the option applies to that item alone. It must work for graves too, including
the split across a grave's recipients.

## Context

Every price this application stores is a net price. `InvoiceItem.DefaultPrice`,
the per-grave override `GraveItem.UnitPrice` and `InvoiceLine.UnitPrice` are all
net, and `DocumentDataBuilder` adds the item's VAT rate on top of them to reach
the gross total the customer pays.

That is the wrong end to type at when a price was quoted gross — "Grabpflege,
120 € inklusive". Today the user has to divide by the rate themselves and enter
the result, and the invoice then rounds its way back to something that may not
be the 120 € that was promised.

This design adds a per-item flag saying that prices for this item are entered
gross. Nothing about the invoice document changes: a line is still printed net
with the VAT shown separately, and the gross total is what the customer was
quoted.

## Decisions

Settled before the design and treated as fixed:

1. **The flag is a property of the item.** It applies to that item wherever its
   price is entered, and to no other item on the same invoice.
2. **Every price field for a flagged item means gross**: the item's default
   price, a grave's price override, and the price column of the invoice and
   grave-invoice dialogs. Only the stored invoice line — and therefore the PDF —
   is net.
3. **The import archive carries the flag.** `items.csv` gains a column;
   existing archives must be regenerated.
4. **The document is unchanged.** No new wording, no marker, no layout change.

## Data Model

One migration for this version (Principle IV), adding one column.

### `InvoiceItem.PriceIncludesVat` (bool, not null, default `false`)

True when prices entered for this item are gross. Existing items are false,
which is exactly today's behaviour.

The column needs no controller work: `InvoiceItemsController` binds the whole
entity on POST and applies a `Delta<InvoiceItem>` on PATCH, and
`ODataConventionModelBuilder` exposes every public property, so the flag reaches
OData for free. The one hand-written projection that must be extended is the
grave detail query in `GravesController`, which selects the item's fields
explicitly.

Nothing is stored on `InvoiceLine`. A snapshot of the flag would be worse than
useless: if the flag were toggled after generation, the revision dialog would
display a gross computed with the old flag and save it with the new one, and an
untouched line's price would jump. Display and save must both read the item's
current flag.

## The Conversion

A pure helper in `Invoicing.Domain`, unit-tested (Principle III permits unit
tests for pure business logic):

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

A flagged item's gross becomes a net at the three places where a price becomes a
line, and nowhere else:

- `InvoiceService.BuildLinesAsync` — an ad-hoc invoice and its revisions.
- `GraveInvoiceRevisionService.BuildGraveLinesAsync` — a grave invoice revision.
- `GraveInvoiceGenerator`, on the result of `EffectivePrice`, **before** the
  `GraveLine` is built.

All three resolve the price the same way today (`input.UnitPrice ?? item
.DefaultPrice`), and both operands are gross for a flagged item, so the
conversion wraps the resolved value rather than each operand.

Converting before the split is what makes graves work. `GraveInvoiceSplitter`
divides the net on purpose — each recipient's document then works out its own
VAT from its own net and adds up on its own — so it must keep receiving a net,
and it does not change. `InvoiceLine.GraveUnitPrice` therefore stays what it is
today: the grave's net price before the split.

## What Does Not Change

`GraveInvoiceSplitter`, `ShareCalculator`, `DocumentDataBuilder`,
`InvoiceDocument` and the shape of `InvoiceLine`. Every invoice already
generated is unaffected, and so is every invoice generated from an unflagged
item.

The detail pages and revision panels also keep showing what they show now: the
invoice's real content, net lines with VAT beside them. Only the **edit
dialogs** display a gross.

## Frontend

### Item dialog and item list

`ItemDialog` gains a CheckBox under the default price, bound to
`itemDraft>/priceIncludesVat`. Ticking it does not rewrite the number in the
field — it changes what that number means. A user who had entered a net price
and now wants a gross one retypes it, which is the only reading that does not
guess at intent.

The items table marks flagged rows so the price column is not ambiguous.

### The price columns

Every dialog that collects a price gets a per-row marker — a short "inkl.
MwSt." next to the price — driven by the row's own item, so a table can hold
gross and net rows at once and say which is which.

`DraftLine` gains `priceIncludesVat`, filled in two ways. It needs no `vatRate`
of its own: the rate is looked up from the item catalogue map (see below) at
the point a price is converted, rather than carried on the row.

- **From the item**, in the `selectionChange` handlers that already copy the
  item's number, description and default price into the row. The default price
  is stored gross, so it is pre-filled unconverted.
- **From an existing line**, when a revision dialog loads it. The line stores a
  net, so the field shows `ToGross(net, rate)` — for a grave invoice, of
  `GraveUnitPrice`, the grave's price before the split.

For the second case the dialog needs the item's _current_ flag and rate. Both
revision dialogs read the item catalogue once when they open and index it by id;
they already load the whole catalogue into their item ComboBox, so this adds a
request, not a volume of data.

On save the dialogs send what is in the field. The backend converts.

The same `ToNet`/`ToGross` pair is needed in `model/api.ts`, next to `lineTotal`
and `invoiceTotals`, which already mirror the backend's rounding for the same
reason: a screen must not disagree with the PDF it links to.

### Grave pages

The grave dialog stores its override as typed, so it needs the marker and
nothing else. The grave detail item table shows the stored price and needs the
marker too.

## Import

`items.csv` gains a fifth column, `PriceIncludesVat`, in
`ImportArchiveLayout.ItemsHeader`. The header check is exact and the template
download shares this definition, so a freshly downloaded template validates and
an archive from before this change does not — which is the accepted cost of the
decision.

The column is optional in content, not in presence: an empty cell means false.
`true`/`false`, `1`/`0`, `ja`/`nein` and `yes`/`no` are accepted
case-insensitively; anything else is a row problem with a code of its own, like
every other malformed cell.

## Testing

**Unit** (`Invoicing.Domain`): the conversion pair — round-trip stability across
rates and prices, 0 %, a gross with more than two decimals, a net that needs the
third decimal.

**Integration** (`Invoicing.IntegrationTests`):

- An ad-hoc invoice with a flagged item stores the net and totals to the entered
  gross.
- A grave with a flagged item and two recipients splits the net; the recipients'
  grosses sum to the grave's gross.
- A revision saved with the values the dialog was given reproduces the identical
  net — the double-deduction trap, and the reason the display uses the current
  flag.
- A mixed invoice: one flagged and one unflagged line, with the unflagged line
  billed exactly as before.
- Import of an archive with the new column, and rejection of one without it.

The gross total is asserted with the integration tests rather than in
`Invoicing.DocumentTests`: it is computed by `DocumentDataBuilder`, which lives
in the API project, and the rendering below it does not know the flag exists.

**E2E**: create a VAT-included item, invoice it, and check the totals on the
detail page; the grave path with two recipients.

## Limits

With a count above one, or a split, the printed gross can land a cent away from
`count × entered gross`. This is the property `GraveInvoiceSplitter` already
documents — every document works out its own VAT from its own net, and a
document that does not add up would be the alternative. Three decimals on the
net make it rare rather than routine.

## Out of Scope

Per-invoice or per-grave overriding of the flag; a gross-entry mode for
unflagged items; any change to how the document presents VAT.
