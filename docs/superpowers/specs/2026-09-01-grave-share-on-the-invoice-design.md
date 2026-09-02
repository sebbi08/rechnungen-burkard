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
2. **The split must never collect less than the grave's bill.** Adding a few
   cents is acceptable, losing them is not — and "the bill" means the gross,
   which is what a VAT-included price states directly.
3. **Every price on the page has two decimals.** Dividing a line was what
   produced three of them — a recipient's third of 24,95 is 8,317 — and nothing
   is divided any more. A VAT-included item's net is rounded to the cent too,
   and raised where rounding down would undercharge; the Limits section records
   what that costs.
4. **The invoice shows the whole grave, then the share.** Lines at 100 %, the
   net sum, the recipient's share, and the VAT worked out from the share.
5. **Non-grave invoices do not change** in any way.

## The rule

For one grave and half-year, with `n` recipients:

1. Take every line at 100 %, exactly as configured: count and unit price
   unchanged.
2. Group the lines by VAT rate and total each group.
3. Add whole cents to unit prices until the group's total both divides by `n`
   and leaves the `n` shares — each carrying its own rounded VAT — collecting
   at least the gross the group was asked for, measured before any cent was
   added. The fewest cents that satisfy both win. Where two combinations cost
   the same, the one whose sorted list of bumped line numbers is
   lexicographically smallest wins — a rule decided entirely by our own code,
   not by any library's unspecified iteration order. Revisions must reproduce
   the split exactly, so the tie-break is part of the rule rather than an
   implementation detail.

   Divisibility alone is not enough, because the VAT is rounded once per
   recipient instead of once for the grave. A subtotal of 84,24 at 19 % over
   two recipients divides exactly — 42,12 each — but each recipient's VAT
   rounds 8,0028 down to 8,00 where the grave's own 16,0056 rounds up to 16,01,
   so the two of them collect 100,24 against a bill of 100,25. The target is
   fixed before the raising starts: recomputed from the raised subtotal it
   would climb with every cent added and never be caught.

4. Every recipient's invoice carries those same 100 % lines. The recipient's
   share of each VAT group is that group's total divided by `n`, which is now
   exact.
5. The VAT is worked out per rate from the share, not from the 100 % figures.

Each recipient's total is therefore `share + VAT(share)`, identical for all of
them, and the shares sum to the printed net sum exactly.

### Why step 3 always terminates

Adding one cent to a line's unit price moves its group total by `count` cents.
Let `g = gcd(count₁ … countₖ, n)`. Because `g` divides every count, it divides
the group total too, and the reachable additions are exactly the multiples of
`g`. The residue we need is therefore always reachable, and a shortest-path
search over the `n` residues finds the cheapest way there.

**Round-robin does not work**, and it is worth recording why, because it is the
obvious first guess. With three recipients and two lines of count 1 and 2, one
full lap adds 3 cents — which is 0 modulo 3 — so every lap returns to the same
residue and only two of the three residues are ever reachable. Cycling through
the lines can loop forever; _choosing_ the line cannot. Verified exhaustively:
over the same 308 945 configurations, round-robin failed on 18 748 of them and
choosing failed on none.

### What it costs

Measured over the same exhaustive sweep, the cents added to a grave's bill:

- 28 % of graves need none — the subtotals already divide.
- Mean 4 cents, and that sweep deliberately included awkward counts (7, 10) and
  prices as small as one cent.
- The worst case found was 60 cents, on a grave whose every line had a high
  count. A grave with any count-1 line needs at most `n − 1` cents.

The addition lands on the grave's bill, never on one recipient. All `n`
invoices move together and stay identical.

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
> invoice. The grave's bill may be raised by a few cents so that its per-rate
> subtotals divide evenly and the shares together still collect what the grave
> was asked for; it is never reduced.

## Limits

The percentages displayed sum to 99,99 % for three recipients. The amounts are
exact and the net sum on the page proves it, but the percentage is a rounded
label.

A grave's printed line price can be a cent above what the grave record holds —
that is the mechanism, and the grave detail page should show the adjusted
figures so the two agree.

**A VAT-included price is not always exactly reachable.** A two-decimal net
times a count puts line totals on a lattice of `count` cents, so a gross entered
for a line cannot always be hit exactly, and the line lands on the first value
above it. No whole-cent net bills 119,00 € at 7 %, for instance: 111,21 bills
118,99 and 111,22 bills 119,01, so entering 119,00 shows 119,01 afterwards.

Measured over every gross from 0,01 € to 40,00 € at 7 % and 19 %: exact for 88 %
of prices at count 1 and never more than a cent over, rising to at most eleven
cents over at count 10, where one cent on the unit price moves the line by ten.
The overshoot is always upward — decision 2 forbids the other direction — and it
is the price of decision 3. Three-decimal nets would land exactly; they were
what this application stored until it was decided that every printed price has
two decimals.

**The gross a report recomputes may differ by a cent or two.** Each invoice
rounds its own VAT once per rate, so the `n` grosses need not sum to the VAT of
the 100 % net. The raising rule guarantees the direction — the shares always
collect at least the grave's bill — but not equality. No document prints that
figure, so nothing on paper disagrees.

## Out of scope

Changing how non-grave invoices are calculated or rendered; changing the
recipients of a grave; recalculating invoices generated before this change.
