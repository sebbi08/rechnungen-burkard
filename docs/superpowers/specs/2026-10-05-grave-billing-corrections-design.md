# Design: Automatic Rounding Corrections for Grave Invoices

**Created**: 2026-10-05

**Status**: Approved design (pre-implementation)

**Source**: the owner's request of 2026-10-05, refined in conversation. It
replaces how `docs/superpowers/specs/2026-10-05-grave-quarters-and-prorated-billing-design.md`
handles a block that does not split evenly: there the save was refused and
per-line price suggestions offered; with prorated and seasonal invoices those
suggestions grew too large. Everything else in that spec stands.

## 1. Recorded Decisions

| Topic               | Decision                                                                                                                                                                 |
| ------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| Instead of refusing | The backend corrects each block (invoice of the term) that does not split evenly, per VAT rate, by raising one line's price                                              |
| Which line          | The first all-year line of that rate in the block; without one, the first line of the rate; if that line cannot fix it, the next line of the rate (all-year lines first) |
| How far             | The smallest raise in cent steps (≥ entered, never down) at which the rate's subtotal splits evenly; at most 1,00 € per line                                             |
| Reach               | 1,00 € everywhere — the correction search, and `GraveShare`'s own suggestion search (still used by the revision check), use 100 cents instead of 400                     |
| Beyond the reach    | The save is refused as before (`PricesDoNotDivide`)                                                                                                                      |
| Marking             | A corrected price is shown orange with "angepasst, eingegeben 40,00" in the preview cards; the PDF prints the billed price plainly                                       |
| Saving              | A save that needs corrections shows them in a confirmation dialog first; the backend stores entered prices and corrections                                               |
| Generation precheck | Before a half-year run the backend recomputes every due grave's corrections; any difference to the stored ones stops the whole run with a list of graves                 |
| Import              | Corrections are computed and stored without confirmation; the summary counts graves with corrections                                                                     |
| Filter              | The graves overview gets a "Nur mit Rundungsanpassungen" switch and an orange "angepasst" tag per grave with corrections                                                 |
| Suggestions         | The per-line "Geht auf mit: …" suggestions in the grave editor are removed                                                                                               |
| Revisions           | Unchanged: `ValidateGraveBilling` still refuses lines that do not divide when an invoice group is revised                                                                |

### Why 1,00 € is enough

Simulated for prices 1,00–200,00 €, 2–4 recipients, counts 1–4, net and gross
at 7 % and 19 %: a net-priced line needs at most 3 cents, a gross-priced line at
most 51 cents (7 %, 4 recipients; 23 cents at 19 %). The one case no reach can
fix — a target line whose count shares a factor with the recipient count (count
2 or 4 with 2 or 4 recipients) against an odd remainder from the other lines —
is handled by moving on to the next line of the rate.

## 2. The Correction Rule

Pure domain code next to `GraveBillingCheck` in `Invoicing.Domain`. For each
example invoice of the term (`HalfYearBilling.Examples`) and each VAT rate in it:

1. Bill the block as today (`HalfYearBilling.Bill`). If `GraveShare` finds the
   rate's group even, there is no correction.
2. Candidate lines, in order: the rate's all-year lines in link order, then its
   quarter lines in link order.
3. For the first candidate, raise its billed price (the price as the block
   bills it — already prorated for a start or end block; for a gross-entered
   line the gross, with the net derived as today) by 0,01, 0,02, … up to 1,00.
   The first price at which the rate's group is even (`GraveShare.IsEven`, so
   both `NotDivisible` and `DoesNotAddUp` count) is the correction. If none
   within 1,00, try the next candidate.
4. No candidate works → the block has a problem as before, and the save is
   refused with `PricesDoNotDivide`.

Only one line per block and rate is ever changed; a correction never lowers a
price. A correction is:

| Field            | Meaning                                                                    |
| ---------------- | -------------------------------------------------------------------------- |
| `Kind`           | `Start`, `StartAndEnd`, `FullFirstHalf`, `FullSecondHalf`, `End`           |
| `Year`, `Half`   | The concrete half-year for start/end blocks; `Half` alone for full ones    |
| `VatRate`        | The rate whose subtotal it fixes                                           |
| `ItemId`         | The line's item                                                            |
| `EnteredPrice`   | The price the block would bill without correction (prorated, entered unit) |
| `CorrectedPrice` | The price it bills instead                                                 |

Worked example (G-102: 3 recipients, Grabpflege 60,00 all year, Frühjahr 45,00,
term 03/2026–10/2028, all 19 %): start H1 2026 bills 40,00 + 45,00 = 85,00 →
Grabpflege 40,00 → 40,02; full H1 105,00 and full H2 60,00 divide; end H2 2028
bills 40,00 → 40,02.

`GraveBillingCheck.Evaluate` returns the corrections with each example and
bills the corrected lines; an example is `Ok` when, after correction, every rate
divides. The per-line `Suggestions` and their search go.

## 3. Data Model

New entity `GraveBillingCorrection` (table `GraveBillingCorrections`):

| Property         | Type                 | Notes                          |
| ---------------- | -------------------- | ------------------------------ |
| `Id`             | `long`               |                                |
| `GraveId`        | `long`               | FK to `Graves`, cascade delete |
| `Kind`           | `BillingExampleKind` | stored by name                 |
| `Year`           | `int?`               | set for start/end blocks       |
| `Half`           | `int`                | 1 or 2                         |
| `VatRate`        | `decimal`            |                                |
| `ItemId`         | `long`               | FK to `InvoiceItems`, restrict |
| `EnteredPrice`   | `decimal`            |                                |
| `CorrectedPrice` | `decimal`            |                                |

Unique index on (`GraveId`, `Kind`, `VatRate`). Not on the EDM as a writable
set; read through the endpoints below. The table is folded into the unreleased
migration `20261002083107_GraveSiteFieldsAndGermanOnly` like the `Quarter`
column; the dev database is reset after the merge (the owner agreed: no
production data exists).

## 4. API

### `PreviewGraveBilling`

Per invoice: `Corrections` (`VatRate`, `ItemId`, `ItemNumber`, `EnteredPrice`,
`CorrectedPrice`). Per line: `Price` is the billed (corrected) price,
`EnteredPrice` the price before correction, `Corrected` a flag. `LineSuggestions`
is removed. `Ok` is false only when a block cannot be corrected within the reach.

### `SaveGrave`

The request gains `Corrections`: the list the user confirmed (same fields as
above plus `Kind`, `Year`, `Half`). The backend recomputes the corrections; when
the confirmed list differs from them (by `Kind`, `Year`, `Half`, `VatRate`,
`ItemId`, `CorrectedPrice`; order-independent; an empty or missing list counts
as "none confirmed"), the save is refused with `CorrectionsChanged`. Otherwise
grave, links and corrections are written in one transaction, the grave's old
corrections replaced. `PricesDoNotDivide` remains for a block beyond the reach.

### Generation

- `POST GenerateGraveInvoices` first runs the precheck synchronously: for every
  grave due in the half-year and not yet invoiced for it, recompute the
  corrections from current data and compare with the stored ones (same
  comparison as `SaveGrave`). Any difference → `400 CorrectionsOutdated` with
  `args` = the grave numbers (sorted), and no job is started.
- `GraveInvoiceGenerator` applies the stored corrections of the matching block
  (start/end by concrete `Year`/`Half`, otherwise the full half) to the lines
  before the split check; the split check stays as the last safeguard.

### Status

`GraveInvoicingStatus` rows gain `CorrectionCount` (stored corrections of the
grave).

### Import

`CheckGraveSplits` computes corrections instead of reporting
`PricesDoNotDivideIn` / `PriceSuggestion*`; the import stores them. A block
beyond the reach is reported with `PricesDoNotDivideIn` as before (label first).
`ImportSummary` gains `CorrectedGraves`.

## 5. Frontend

- **Preview cards** (`GraveBillingPreview.vue`): a corrected line's price is
  orange with the text "angepasst, eingegeben 40,00 €" under it; the card's tag
  reads "Geht auf (angepasst)" in orange (`severity="warn"`) when the card has
  corrections, "Geht auf" in green otherwise, "Geht nicht auf" in red when
  `Ok` is false.
- **LineEditor**: the `suggestions` prop and the suggestion buttons are removed.
- **Grave editor save**: after the preview check, if any invoice has
  corrections, a dialog "Preise werden angepasst" lists them (Rechnung,
  Leistung, MwSt., eingegeben → angepasst) with "Abbrechen" and "Mit Anpassungen
  speichern"; confirming saves with the corrections. On `CorrectionsChanged`
  the editor refreshes the preview and shows a toast; the user saves again.
- **Generate dialog**: `CorrectionsOutdated` shows "Bei diesen Gräbern haben
  sich die Rundungsanpassungen geändert. Bitte öffnen, prüfen und speichern:
  {0}" with the grave numbers.
- **Graves overview**: a switch "Nur mit Rundungsanpassungen" filters rows to
  `CorrectionCount > 0`; such rows carry an orange tag "angepasst".
- **Import page**: the summary shows "davon {0} Gräber mit Rundungsanpassungen"
  when `CorrectedGraves > 0`.

## 6. Testing

- **Domain:** no correction when even; target line order (first all-year, then
  first of the rate, then next line when the first cannot fix it — count 2 with
  2 recipients and an odd remainder); smallest raise; gross line (`DoesNotAddUp`);
  reach of 1,00 €; the G-102 example.
- **API:** preview fields; save refused with `CorrectionsChanged` when
  corrections are needed but not sent, or sent wrong; saved with the right list
  and the table rows present; generation bills the corrected price; precheck
  returns `CorrectionsOutdated` (stored row altered directly) and starts no job;
  status `CorrectionCount`; import stores corrections and counts them.
- **E2E:** a three-recipient grave shows an orange card and the dialog; cancel
  keeps the editor open; confirm saves; the overview filter shows only it;
  the old "refused until a price is changed" test is rewritten; layout of the
  dialog on phone and tablet.

## 7. Documentation

`docs/spec.md`: FR-021a (corrections instead of refusal, 1,00 € reach, one line
per block and rate), the rounding edge case, the generation precheck, the
overview filter, the import summary. The previous design spec gets a note at
its "Suggestions" section that this spec replaces it.

## 8. Out of Scope

- Lowering prices or spreading cents over several lines.
- Showing corrections on the PDF.
- Recomputing or flagging corrections of half-years already invoiced.
