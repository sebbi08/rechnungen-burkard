# Design: Quarter Items, Prorated Half-Years and a Live Billing Preview

**Created**: 2026-10-05

**Status**: Approved design (pre-implementation)

**Source**: the owner's request of 2026-10-05 ("Grab-Rechnungen werden doch noch
einmal komplizierter"), refined in conversation. It picks up the "season groups"
that the grave site design (2026-10-02) left out of scope.

## 1. Recorded Decisions

| Topic                 | Decision                                                                                                                     |
| --------------------- | ---------------------------------------------------------------------------------------------------------------------------- |
| Quarters              | Frühling = Jan–Mar, Sommer = Apr–Jun (both H1); Herbst = Jul–Sep, Winter = Oct–Dec (both H2)                                 |
| Where a quarter lives | On the invoice item (master data), one quarter or none; it applies to every grave billing the item                           |
| No quarter            | The item is billed all year                                                                                                  |
| Quarter item's price  | The full price, once, in the half-year that holds its quarter — if the invoice's months touch that quarter at all            |
| All-year item's price | The price is per half-year. A half-year the term covers only partly bills price × n ÷ 6, rounded to the cent                 |
| Partial months        | Start and end month each count as a whole month                                                                              |
| On the invoice        | The prorated unit price, with "anteilig 05–06/2026 (2 von 6 Monaten)" appended to the description; the count is unchanged    |
| Outside the term      | A half-year with no month inside the term is not billed at all (until now generation ignored the term)                       |
| Even split            | Every invoice the term actually produces must divide evenly across the recipients; the save is refused otherwise             |
| Live check            | The grave editor shows the example invoices and their check while the user types                                             |
| Suggestions           | Per item line: the nearest prices below and above at which every invoice divides, other lines unchanged; a click applies one |
| Recipients            | At most 4 per grave                                                                                                          |
| Quarter change        | Locked once a grave bills the item, like the VAT rate (FR-009a)                                                              |
| Existing invoices     | Unchanged; the new rule applies from the next generation                                                                     |
| Release               | `feat!`: an import archive whose `items.csv` lacks the `Quarter` column is no longer accepted                                |

## 2. The Billing Rule

All of it lives in one pure function in `Invoicing.Domain`, `HalfYearBilling`.
It takes the grave's term, a half-year and the grave's item links (with their
items' quarters and prices) and returns the lines billed for that half-year.
Saving a grave, the editor's check, the import and generation all call it, for
the reason `GraveBilling` already gives: two copies of the rule would be free to
disagree, and that shows up as a grave that saves and then cannot be invoiced.

For a grave with term `start`–`end` (`end` may be missing, meaning unbounded)
and a half-year `H` of year `Y` (H1 = months 1–6, H2 = months 7–12):

- **n** is the number of months of `H` inside the term, both ends inclusive:
  `0 ≤ n ≤ 6`. The months themselves are `first`–`last`.
- **n = 0** — nothing is billed for `H`.
- **All-year item** (no quarter) — billed when `n > 0`. With `n = 6` the unit
  price is the entered price. With `n < 6` it is
  `round(entered × n / 6, 2, AwayFromZero)`, and the description gets
  `", anteilig MM–MM/YYYY (n von 6 Monaten)"`, or
  `", anteilig MM/YYYY (1 von 6 Monaten)"` for a single month. A price entered as a gross is prorated as a gross,
  and its net is derived from the prorated gross exactly as today
  (`VatInclusivePrice.NetOf`); the prorated gross is the line's `EnteredGross`.
- **Quarter item** — billed only when its quarter lies in `H` and at least one
  of `first`–`last` falls in the quarter. Then at its full price, with no
  suffix.
- An item that is not billed in `H` is left off the invoice. If no line
  remains, there is no invoice for `H`.
- The count is never changed; only the unit price is prorated.

The price fallback stays as it is: the link's own price, otherwise the item's
default price.

### Worked example

Term 05/2026–08/2030. Care (all year) 60,00; summer planting (Sommer) 40,00;
autumn planting (Herbst) 30,00.

| Invoice         | n   | Care                       | Summer                    | Autumn                   |
| --------------- | --- | -------------------------- | ------------------------- | ------------------------ |
| H1 2026 (start) | 2   | 20,00, anteilig 05–06/2026 | 40,00 (May/June touch Q2) | — (H2 item)              |
| any full H1     | 6   | 60,00                      | 40,00                     | —                        |
| any full H2     | 6   | 60,00                      | —                         | 30,00                    |
| H2 2030 (end)   | 2   | 20,00, anteilig 07–08/2030 | —                         | 30,00 (Jul/Aug touch Q3) |

With a start in 03/2026 the H1 2026 invoice would bill a Frühling item in full:
the term covers March. 50,00 over 5 months is 41,666… and bills 41,67.

### Which invoices a term produces

`HalfYearBilling.Examples(start, end)` lists the distinct invoice shapes the
term will actually produce, in this order:

| Kind             | When it exists                                                            | Label in the UI                         |
| ---------------- | ------------------------------------------------------------------------- | --------------------------------------- |
| `Start`          | always, once a start month is set; `n = 6` when the start is Jan or Jul   | "Anfang · H1 2026 · 2 von 6 Monaten"    |
| `StartAndEnd`    | instead of `Start` and `End` when both fall in the same half-year         | "Anfang und Ende · H1 2026 · 3 von 6 …" |
| `FullFirstHalf`  | when at least one H1 lies entirely inside the term                        | "Volles H1"                             |
| `FullSecondHalf` | when at least one H2 lies entirely inside the term                        | "Volles H2"                             |
| `End`            | when an end month is set and it falls in a later half-year than the start | "Ende · H2 2030 · 2 von 6 Monaten"      |

Without a start month (the editor while the user is still typing) only
`FullFirstHalf` and `FullSecondHalf` are listed.

A grave is valid when every one of its examples passes `GraveShare.Problems`
with its own billed lines. That is the check the save, the live preview and the
import make. Generation checks only the invoice it is about to create.

## 3. Data Model

### Invoice item

| Property  | Type                     | Required | Notes                                       |
| --------- | ------------------------ | -------- | ------------------------------------------- |
| `Quarter` | `BillingQuarter?` (enum) | no       | new; null means the item is billed all year |

`BillingQuarter` lives in `Invoicing.Domain` with the members `Spring`,
`Summer`, `Autumn`, `Winter`, in calendar-quarter order. It is stored as its
name (string conversion), like `GravePosition`. German labels: Frühling,
Sommer, Herbst, Winter; "Ganzjährig" for null.

The property goes into the EDM; afterwards `npm run gen:odata` regenerates
`frontend/src/api/metadata.xml` and `src/api/generated/`.

### Migration

The only migration since v2.1.0 is the consolidated
`20261002083107_GraveSiteFieldsAndGermanOnly`, and it is unreleased. The
`Quarter` column is folded into it rather than added in a new migration, the same
way the grave site fields were folded in. The dev database is reset with
`scripts/reset-dev-db.sh` afterwards — ask first, commit before.

### Quarter lock

`Quarter` must not change once a grave bills the item: it moves which invoices
carry the item for every linked grave at once, and a split that divides today
could stop dividing. The item update refuses it with `ItemQuarterFixed`, the
counterpart of the VAT rate's lock, and the item dialog disables the field with
the existing hint extended to name both ("MwSt.-Satz und Quartal sind fest,
sobald ein Grab den Artikel abrechnet.").

### Recipients

`SaveGrave` refuses more than four recipients with `TooManyRecipients`. No
existing grave has more (the dev data holds one grave with two), and a grave's
recipients are fixed once it is saved, so nothing already stored is affected.

### Invoices

No schema change. The prorated unit price and the suffixed description are
written into `InvoiceLine` at generation, so the document, the PDF and the
bulk download need no change and stay reproducible from persisted data
(Constitution Principle II).

## 4. API

### `POST /odata/ValidateGraveBilling`

The request gains the term:

```json
{
  "Recipients": 3,
  "StartMonth": "2026-05",
  "EndMonth": "2030-08",
  "Lines": [{ "ItemId": 1, "Count": 1, "UnitPrice": 60.0, "PriceIncludesVat": false }]
}
```

`StartMonth` and `EndMonth` are optional (`yyyy-MM`, read with
`GraveTerm.TryParseMonth`); an end before the start is `DateRangeInvalid`.
`Recipients` must be 1–4 (`NoRecipients`, `TooManyRecipients`). The other
refusals stay as they are (`InvalidRequest`, `InvalidCount`, `ItemNotFound`,
`PriceRequired`, `InvalidPrice`).

The response:

```json
{
  "Ok": false,
  "Invoices": [
    {
      "Kind": "Start",
      "Year": 2026,
      "Half": 1,
      "Months": 2,
      "Ok": false,
      "NetTotal": 60.0,
      "GrossTotal": null,
      "PerRecipient": null,
      "Problems": [{ "VatRate": 19, "Kind": "NotDivisible", "Amount": 60.0, "Suggestions": [] }],
      "Lines": [
        {
          "LineIndex": 0,
          "ItemNumber": "100",
          "Description": "Grabpflege, anteilig 05–06/2026 (2 von 6 Monaten)",
          "Count": 1,
          "VatRate": 19,
          "PriceIncludesVat": false,
          "UnitPrice": 20.0,
          "NetTotal": 20.0
        }
      ]
    }
  ],
  "LineSuggestions": [{ "LineIndex": 1, "Suggestions": [39.98, 40.02] }]
}
```

- `Year` is null for `FullFirstHalf`/`FullSecondHalf`.
- `GrossTotal` and `PerRecipient` are what the recipients collect between them
  and each (`GraveShare.CollectedCents`), and null for an invoice that does not
  divide.
- `LineIndex` refers to the request's `Lines`, so the editor can put a
  suggestion on the row it came from.
- `Problems` keep today's shape; their `Suggestions` stay empty (the per-line
  suggestions replace them).

### Suggestions

For each request line that is billed on at least one failing invoice, the
nearest entered price below and above is searched in cent steps, up to 400 cents
away (today's reach), at which **every** example invoice divides while all other
lines keep their prices. A gross-entered line is moved in its gross, as
`GraveShare` does today. A line for which no price within reach works gets no
entry, and its invoice shows the existing "Bitte einen Preis mit diesem Satz
anpassen" sentence. The search lives next to `GraveShare` in the domain, where
it can be tested without the API.

### `SaveGrave`

Refuses with `PricesDoNotDivide` when any example fails; the message names the
first failing invoice by its label. Refuses more than four recipients with
`TooManyRecipients`.

### Item update

Refuses a changed `Quarter` on a linked item with `ItemQuarterFixed`.

### `GraveInvoicingStatus` and generation

- The status lists as open only graves whose term touches the selected
  half-year, and returns a flag `InTerm` per grave for the detail page.
- The generation job selects only graves whose term touches the half-year; the
  others are neither generated nor counted as skipped.
- `GraveInvoiceGenerator` builds its lines from `HalfYearBilling` for the
  concrete year and half. A new skip reason `NothingToBill` covers a half-year
  that leaves no line (only quarter items whose quarter is not touched).
  `PricesDoNotDivide` stays for an invoice that does not divide.
- Generating a half-year outside the term is no longer possible. This replaces
  the edge case in `docs/spec.md` that says the term does not affect
  generation.

### Revisions

`GraveInvoiceRevisionService` today takes a line's description from the item,
which would drop the "anteilig …" suffix on every edit. It now keeps the
description of the edited revision's line for the same item (matched by item id
in line order, each old line used once); a newly added item takes the item's
description. The unit price is whatever the edit dialog sends, which opens on the
prorated price as invoiced.

## 5. Import and Export

- `items.csv` gets `Quarter` as its last column. Allowed values: empty,
  `Spring`, `Summer`, `Autumn`, `Winter` (the enum names, as `Position` takes
  them). Anything else is `QuarterInvalid` with its German message.
- The header still has to match exactly, so an archive without the column is
  refused with `HeaderInvalid`. That breaks the import format and goes into the
  next major release together with the grave site fields' `feat(import)!`,
  which is also unreleased.
- A grave with more than four recipient links is refused with
  `TooManyRecipients`.
- The import's price check runs the same example check as `SaveGrave`. Each
  failing invoice and rate is reported with the existing
  `importProblemPricesDoNotDivide0` text, prefixed by the invoice's label
  ("Anfang H1 2026: MwSt. 19 %: …"). The figure variants `…1` and `…2` are
  dropped: they suggested amounts for one invoice that may break another. In
  their place each line suggestion becomes its own problem,
  `importProblemPriceSuggestion` — "Leistung {0}: Möglich wären {1} oder {2}."
  (or "Möglich wäre {1}." with one).
- The export writes the column; the empty template carries the new header.

## 6. Frontend

### Item dialog and item list

- A "Quartal" select with "Ganzjährig", Frühling, Sommer, Herbst, Winter;
  disabled with the extended hint while a grave bills the item.
- A "Quartal" column in the item list.

### Grave editor

```
Leistungen
┌ Nr ▾ | Anz | Preis   | inkl. | MwSt. | Quartal   ┐
│ 100  |  1  | 60,00   |  ☐    | 19 %  | Ganzjährig│
│ 210  |  1  | 40,00 ⚠ |  ☐    | 19 %  | Sommer    │
│   Geht auf mit: [39,98] [40,02]                   │
└───────────────────────────────────────────────────┘
Beispielrechnungen
┌ Anfang · H1 2026 · 2 von 6 Mon. ✗ ┐ ┌ Volles H2            ✓ ┐
│ 100 Grabpflege, anteilig … 20,00  │ │ 100 Grabpflege  60,00  │
│ 210 Sommerbepflanzung      40,00  │ │ 300 Herbst      30,00  │
│ Netto 60,00                       │ │ Netto 90,00            │
│ ✗ MwSt. 19 %: 60,00 lässt sich    │ │ Brutto 107,10          │
│   nicht gleichmäßig auf 3 …       │ │ je Empfänger (1/3) …   │
└───────────────────────────────────┘ └────────────────────────┘
```

- **`LineEditor`** gets two read-only columns from the item master, "MwSt." and
  "Quartal", and an optional `suggestions` prop keyed by row. A row with a
  suggestion is marked as faulty and shows "Geht auf mit:" followed by one
  button per price; a click sets that row's price. The invoice editor, which
  shares `LineEditor`, passes no suggestions and shows the two new columns too.
- **`GraveBillingPreview`** (new component) renders one card per example: the
  label, a status as text and icon (not colour alone), the billed lines with
  their VAT rate, net total, collected gross and the amount per recipient with
  its share, and for a failing card a red border with the problem sentences.
  Two columns from `sm`, stacked on a phone.
- **Live check** — `useGraveBillingPreview` sends the complete lines (item chosen
  and a price available, own or default), the recipient count (at least 1; with
  none the cards say they assume one recipient), start and end, debounced by
  300 ms after any change. A stale answer is dropped; while one is pending the
  last result stays with a small spinner. With no complete line the section shows
  a hint instead of cards. If the check cannot be reached the section says
  "Prüfung gerade nicht möglich"; the save still validates.
- **Recipients** — the add button gives way to a hint once four are linked.
- **Save** — the modal "Preise gehen nicht auf" dialog is gone. The save asks the
  check first as today; if an example fails it shows a toast and moves focus to
  the first failing card.

### Grave detail page

`GraveBillingPreview` is shown read-only below the "Leistungen" card, fed from
the grave's stored links and term. The current-half-year status reads "Außerhalb
der Laufzeit" when `InTerm` is false.

### Texts

All new UI strings go into `frontend/src/i18n/de.json`. The "anteilig …" suffix
is formatted by `HalfYearBilling` itself (the application is German only), so the
preview, the save message and generation all print the same text, and it is
stored in the line's description at generation.

## 7. Testing

**Domain** (`HalfYearBilling`, suggestions)

- n at the start and at the end; start in January or July; start and end in the
  same half-year; no end.
- Quarter touched and not touched (start 03/2026 with a Frühling item, start
  05/2026 with a Frühling item), a quarter item in the other half-year.
- Rounding to the cent (50,00 × 5/6 = 41,67); a gross price prorated as a gross.
- Which examples a term produces (table in section 2), including a term with no
  full half-year.
- A found suggestion makes every example divide; none within reach gives none.

**API**

- `ValidateGraveBilling`: response shape, `LineIndex` mapping, 1–4 recipients,
  `DateRangeInvalid`.
- `SaveGrave`: refuses five recipients; refuses a grave whose start invoice does
  not divide although its full half-years do.
- Generation: prorated price and suffix on the start and end invoice; a
  half-year outside the term is not generated and not counted as skipped;
  `NothingToBill`.
- Status: a grave outside the term is not listed as open; `InTerm`.
- Item update: `ItemQuarterFixed` when linked, allowed when not.
- Revision keeps the "anteilig" description.
- Import: `Quarter` read and written; `QuarterInvalid`; `TooManyRecipients`;
  `HeaderInvalid` without the column; the price check names the invoice.

**E2E** (own stack, through `e2e/vue.ts`)

- Create an item with a quarter; its select is disabled once a grave bills it.
- Grave editor: the cards update while typing; a failing card; clicking a
  suggestion fixes it and the save goes through.
- The add-recipient button is gone at four recipients.
- `layout.spec.ts`: the card grid on phone and tablet.
- `seedGrave` and the seeded items gain the quarter.

## 8. Documentation

`docs/spec.md`: User Story 6 and its scenarios, FR-009a (quarter lock), FR-019
(quarter, proration), FR-020 (at most four recipients), FR-021a (every invoice of
the term divides), FR-025ff (term bounds generation), the edge cases on the term
and on rounding, and the import columns. `docs/deviations.md` needs no entry.

## 9. Out of Scope

- More than one quarter per item.
- Per-grave quarter overrides.
- Proration by days.
- Re-billing already generated half-years under the new rule.
