# Design: The Grave Share on the Invoice

**Created**: 2026-09-01

**Status**: Draft — awaiting review

**Input**: Every recipient of a grave must pay exactly the same amount, and the
split must not lose cents. Print each line at 100 %, then the net sum, then the
recipient's share, and work out the VAT from the share. Every price has two
decimals.

## The problem

A grave's half-yearly bill is split across its recipients. Today
`GraveInvoiceSplitter` divides each line's **net** across them and
`ShareCalculator` hands the remainder out one cent at a time to whoever comes
first, so the recipients pay different amounts:

| grave                       | recipients | today                     |
| --------------------------- | ---------- | ------------------------- |
| 100,00 @ 7 %                | 3          | 35,67 / 35,66 / 35,66     |
| 100,00 @ 7 % + 45,50 @ 19 % | 3          | 53,72 / 53,71 / **53,70** |
| 33,33 @ 19 %                | 2          | 19,84 / 19,83             |

Two cents of spread on one grave, decided by nothing more than sort order. The
recipients are equal parties to the same bill and should pay the same.

The unequal shares also buy less than they appear to. SC-004 promises the
recipients' **net** shares sum to the grave's net exactly, and they do — but
each invoice then rounds its own VAT, so the **gross** totals drift anyway. In
the multi-line row above, today's unequal split and an equal one both bill
161,13 against a 161,15 rate card. The inequality achieves nothing there.

Splitting per line also forces three-decimal unit prices — a recipient's third
of 24,95 is 8,317 — which are legal to print but read badly, and which only
reproduce their line totals because the splitter derives them from a cent
target rather than by dividing.

## Decisions

Settled with the user before this design:

1. **Every recipient pays exactly the same.** This is the requirement; the rest
   follows from it.
2. **The split collects exactly the grave's bill.** Not a cent more or less.
   Where the prices cannot do that, the grave is refused rather than adjusted.
3. **Every price on the page has two decimals**, and every price shown is one
   that was entered. Dividing a line was what produced three decimals — a
   recipient's third of 24,95 is 8,317 — and nothing is divided any more.
4. **The invoice shows the whole grave, then the share.** Lines at 100 %, the
   net sum, the recipient's share, and the VAT worked out from the share.
5. **Non-grave invoices keep their layout and their split-free arithmetic.**
   One thing does reach them: `VatInclusivePrice.NetOf` is shared, so a
   VAT-included line on an ordinary invoice is now stored at two decimals and
   raised where rounding down would undercharge, exactly as on a grave. That
   follows from decision 3 applying to every price the application prints, not
   only to the ones a grave produces.

## The rule

For one grave and half-year, with `n` recipients:

1. Every recipient's invoice carries the grave's own lines, at the count and
   price configured. Nothing is divided and nothing is adjusted.
2. The recipient's share of each VAT rate is that rate's net subtotal divided by
   `n`, and the VAT is worked out from the share.

That only works when the numbers divide, so **the prices have to be chosen for
it**. Saving a grave is refused unless, for each VAT rate:

- the net subtotal in cents divides evenly by `n`; and
- where the rate carries a price entered as a gross, what the recipients collect
  between them comes to exactly the gross that was typed.

The refusal names the rate, the subtotal, and the nearest subtotals that would
work. A net-priced line is not held to the second test: a net price says what
the net is, and the gross follows from it — only a price entered as a gross is a
promise about what the customer pays.

### Why nothing is adjusted automatically

The first version of this design raised prices by whole cents until the
subtotals divided. It was abandoned. Raising kept the arithmetic out of sight,
but it put figures on the page that nobody had typed, and it could not resolve
what the grave was then worth: adding VAT to the grave's own net gave one
number, and the recipients' rounded shares another, a cent or two apart, with no
honest way to show both. Refusing instead moves the choice to the moment a price
is entered, where it can be changed.

### What it costs to satisfy

Measured over every price from 0,01 € to 200,00 € at 7 % and 19 %:

| recipients                | 2    | 3    | 4    | 5    | 6    | 7     |
| ------------------------- | ---- | ---- | ---- | ---- | ---- | ----- |
| net prices that work      | 50 % | 33 % | 25 % | 20 % | 16 % | 14 %  |
| gross prices that work    | 24 % | 10 % | 6 %  | 3 %  | 2 %  | 1 %   |
| nearest valid gross, mean | 1,6c | 3,4c | 5,4c | 7,4c | 9,4c | 13,4c |

A net price needs at most `n − 1` cents of adjustment. A gross price is rarer,
which is why the refusal has to name the nearest figure that works — at seven
recipients it can be 45 cents away, and nobody would find it by trying.

## The invoice

Grave A-17, four lines, three recipients. The 19 % subtotal is 97,70, which
does not divide by three, so one cent goes onto the count-1 line — 47,80
becomes 47,81 — and the subtotal becomes 97,71.

```
  Pos Nr.    Bezeichnung                 Anz    Preis  MwSt    Betrag
  --------------------------------------------------------------------
    1 4001   Grabpflege Jahrespauschale    1   100,00    7%    100,00
    2 4002   Blumenschmuck                 2    24,95   19%     49,90
    3 4003   Grabstein reinigen            1    47,81   19%     47,81
    4 4004   Gießdienst                    5     3,70    7%     18,50
  --------------------------------------------------------------------
                              Nettosumme gesamt       216,21
                              Ihr Anteil (33,33 %)     72,07
                                 MwSt  7% aus    39,50 =    2,77
                                 MwSt 19% aus    32,57 =    6,19
                              GESAMTBETRAG             81,03
```

Every price here has two decimals and every line multiplies out exactly — true
of this example, and of every grave whose items are priced net, which is the
common case. A VAT-included item is the exception; see Limits. The three shares
sum to 216,21, which is the net sum printed directly above them — the document
verifies itself on its own face, which is the property the old layout could not
offer.

The share percentage is shown to two decimals, so three recipients read
33,33 % and the percentages sum to 99,99 %. The amounts are exact; only the
percentage is rounded for display. The document already prints a share line
today and this replaces it.

## Data model

One migration. **`main` already carries two unreleased migrations**
(`InvoicePaymentStatus` and `VatIncludedLinePrices`), and the constitution
allows one per version, so this feature's migration must not ship in the same
release as those. That is a release-sequencing constraint, not a design one,
but it has to be planned for.

### `Invoice.RecipientCount` (int, null)

The number of recipients the grave bill was divided by. Null on every invoice
that is not a grave invoice, and on every grave invoice generated before this
change.

It is stored rather than derived. `SharePercent` is a rounded percentage and
inverting it to recover `n` is the kind of cleverness that survives review and
then breaks on the first grave with seven recipients.

**Null is also the discriminator that keeps old documents reproducible.** The
constitution requires generated documents to be reproducible from persisted
data (Principle II). An invoice generated before this change stores lines that
are _already_ that recipient's share; rendering it with the new layout would
divide them a second time. So `RecipientCount is null` means "the lines are the
share, render the old way", and non-null means "the lines are the grave, render
the share block". Both paths stay in `DocumentDataBuilder` permanently.

`InvoiceLine.GraveUnitPrice` becomes redundant for new invoices — the line's
own `UnitPrice` is now the grave's price — but it stays for the old ones.

## What is deleted

`GraveInvoiceSplitter.Split` and `ShareCalculator` have no remaining caller: no
line is divided any more. `ShareCalculator` and its tests go.

`GraveInvoiceSplitter.Combine` stays. It reconstructs a grave's lines from
old-style per-recipient invoices, which is what the grave-invoice edit dialog
needs to open a group generated before this change. Its documentation should
say it is a legacy path.

## Revisions

Revising a grave invoice regenerates the whole group, as it does today. The
edit dialog already opens on the grave's pre-split lines, so what the user sees
does not change; the difference is that those lines are now stored directly
rather than reconstructed.

Revising a group generated **before** this change migrates it: the dialog
reconstructs the grave's lines through `Combine`, and the new revisions are
written in the new form with `RecipientCount` set. The superseded revisions
keep rendering the old way, which is what reproducibility requires.

## The frontend

`api.invoiceTotals` sums an invoice's lines to get its totals, and both the
invoice detail page and the grave detail page use it. Under the new form that
sum is the grave's 100 % net, not what the recipient is billed, so every screen
showing a grave invoice's total would be wrong by a factor of `n`.

The frontend therefore needs the same share-aware arithmetic as the document.
It will not get its own copy of it: `NetTotal`, `VatTotal` and `GrossTotal` are
exposed on the OData `Invoice`, computed by the same code that builds the
document, and `api.invoiceTotals` is deleted. That removes an existing
duplication — it exists today only to mirror `DocumentDataBuilder` — rather
than adding a second one to keep in step.

## Testing

**Unit** (`Invoicing.Domain`): the divisibility rule — that it terminates for
every combination of counts and recipient counts, that it adds the fewest
cents, that it is deterministic for equal-cost choices, and the round-robin
counterexample as a regression guard.

**Integration** (`Invoicing.IntegrationTests`):

- A grave with three recipients bills three identical invoices whose net shares
  sum exactly to the stored net sum.
- A grave whose subtotals already divide gets no adjustment at all.
- A mixed-rate grave adjusts each rate group independently.
- A VAT-included item on a grave still works — the flag applies to the 100 %
  line as it does today.
- A revision reproduces the same amounts.
- Revising a pre-change group produces new-form invoices while the superseded
  ones still render their original amounts.

**Document** (`Invoicing.DocumentTests`): the new block renders, and a
pre-change invoice still renders its original totals — the reproducibility
guarantee, which is the one thing here that cannot be checked any other way.

**E2E**: generate a grave for three recipients, check all three invoices show
the same total, and check the detail pages agree with the documents.

## What SC-004 becomes

> **SC-004**: For graves with multiple recipients, every recipient is billed an
> identical amount, and their shares sum exactly to the net sum printed on the
> invoice. A grave whose prices cannot be split that way cannot be saved: the
> prices are never adjusted on the user's behalf.

## Limits

The percentages displayed sum to 99,99 % for three recipients. The amounts are
exact and the net sum on the page proves it, but the percentage is a rounded
label.

**A grave's worth is what its recipients collect**, not its net plus VAT on that
net. VAT is rounded once per recipient, so the two differ: a grave whose net is
84,24 at 19 % is 100,25 as a single bill and 100,24 as two of 50,12. Only the
second is ever paid, so it is the only gross the application shows. A report
that recomputes VAT from a grave's net will differ by a cent or two.

**Prices saved before this rule existed may not satisfy it.** Editing such a
grave is refused until a price is adjusted, and generation skips it with
`PricesDoNotDivide` rather than billing it wrong. Invoices already produced are
untouched and go on rendering exactly as they did.

## Out of scope

Changing how non-grave invoices are calculated or rendered; changing the
recipients of a grave; recalculating invoices generated before this change.
