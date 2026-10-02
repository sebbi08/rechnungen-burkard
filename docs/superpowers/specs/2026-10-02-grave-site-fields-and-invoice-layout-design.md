# Design: Grave Site Fields, Month Dates and the Scanned Invoice Layout

**Created**: 2026-10-02

**Status**: Approved design (pre-implementation)

**Source**: the owner's request of 2026-10-02 with two scans of invoices
written by the old system (`2026-10-02-08-56-17.pdf`, two grave invoices for
"Schineller Bäcke"; `2026-09-28_091387.pdf`, two offers). The new invoice
layout follows those scans, with the changes listed in section 5.

## 1. Recorded Decisions

| Topic                | Decision                                                                                                        |
| -------------------- | --------------------------------------------------------------------------------------------------------------- |
| Date precision       | A grave's start and end are a month and a year, not a day                                                       |
| Storage of a month   | `DateOnly`, always the first of the month; the API and import take `yyyy-MM`                                    |
| End date             | Optional; no end means the grave runs indefinitely                                                              |
| New grave fields     | Field number, site name, position — all three required when a grave is saved                                    |
| Position             | One of six: double, single or urn grave, each inside or outside                                                 |
| Uniqueness           | The grave number only: two graves with the same number must not overlap in time. The field number may repeat    |
| Billing              | Unchanged: start and end do not affect half-year generation                                                     |
| Customer number      | Always the recipient's number; grave invoices add a "Laufweg-Nr." line below it with the grave number           |
| VAT key column       | The rate itself ("19 %"), not a numbered key; a VAT-included line says "Incl." instead of the scan's "i"        |
| Share on the invoice | Variant A: the VAT block carries the recipient's share; one line above it names the net sum total and the share |
| Out of scope for now | Season groups (Frühjahr/Sommer), per-item additional text, offers ("Angebot")                                   |
| Release              | `feat!`: an import archive in the old three-column `graves.csv` format is no longer accepted                    |

## 2. Data Model

### Grave

| Property      | Type                    | Required | Notes                                                            |
| ------------- | ----------------------- | -------- | ---------------------------------------------------------------- |
| `GraveNumber` | `string`                | yes      | Printed as "Laufweg-Nr." on the invoice; label in UI unchanged   |
| `FieldNumber` | `string`                | yes      | new; "Grab-Feld-Nr."                                             |
| `SiteName`    | `string`                | yes      | new; "Grabstättenname", e.g. "Schineller Bäcke"                  |
| `Position`    | `GravePosition?` (enum) | yes      | new; nullable in the schema only for graves migrated without one |
| `StartDate`   | `DateOnly`              | yes      | always day 1                                                     |
| `EndDate`     | `DateOnly?`             | no       | always day 1 when set; now nullable                              |

`GravePosition` lives in `Invoicing.Domain` and is stored as its name (string
conversion), so the database stays readable and the order of the members can
change:

| Member          | German label     | Short form on the invoice |
| --------------- | ---------------- | ------------------------- |
| `DoubleInside`  | Doppelgrab innen | dGr.i                     |
| `DoubleOutside` | Doppelgrab außen | dGr.a                     |
| `SingleInside`  | Einzelgrab innen | eGr.i                     |
| `SingleOutside` | Einzelgrab außen | eGr.a                     |
| `UrnInside`     | Urnengrab innen  | uGr.i                     |
| `UrnOutside`    | Urnengrab außen  | uGr.a                     |

The short forms are part of the document texts; the scan is the only reference
for them.

### Invoice snapshot

Constitution Principle II: a document must be reproducible from persisted
data, and FR-017 already requires the recipient's name and address to keep the
values an invoice was issued with. The new values printed on the invoice are
snapshotted the same way, all nullable:

| Property           | Filled from         | On                |
| ------------------ | ------------------- | ----------------- |
| `RecipientNumber`  | `Recipient.Number`  | every new invoice |
| `GraveNumber`      | `Grave.GraveNumber` | grave invoices    |
| `GraveSiteName`    | `Grave.SiteName`    | grave invoices    |
| `GraveFieldNumber` | `Grave.FieldNumber` | grave invoices    |
| `GravePosition`    | `Grave.Position`    | grave invoices    |

They are written wherever `RecipientName` is written today: `InvoiceService`
(create and revision), `GraveInvoiceGenerator`, `GraveInvoiceRevisionService`.
A revision takes the current values, as it does for the address. An invoice
generated before this change has them all null, and the document leaves out
whatever is missing.

### Migration

Principle IV allows one migration per version, and `main` already carries an
unreleased one: `20261002064856_RemoveDocumentLanguage` (from the German-only
change, not in any release tag; the latest is `v2.1.0`). This change therefore
does not add a second migration. It removes that one and generates a single
migration in its place (`scripts/ef.sh`), named for both changes, that drops the
`Language` columns from `Invoices` and `GlobalSettings` as before and also:

1. Truncate `Graves.StartDate` and `Graves.EndDate` to the first of their
   month (`UPDATE … SET StartDate = substr(StartDate, 1, 8) || '01'`, same for
   the end). 2040-12-31 becomes 2040-12-01, read as "12/2040".
2. Make `Graves.EndDate` nullable.
3. Add `Graves.FieldNumber`, `Graves.SiteName` (`TEXT NOT NULL DEFAULT ''`) and
   `Graves.Position` (`TEXT NULL`).
4. Add the five invoice snapshot columns, all nullable.

Existing graves keep empty site fields until someone edits them; the editor
then requires them. Generation does not check them: a grave invoice of a grave
without a site name simply has no "GRABSTÄTTE" line.

A migration test seeds a database at the previous migration with day-precise
dates, migrates it, and checks the truncation and the nullable end date.

## 3. Saving a Grave

### Request

`POST /odata/SaveGrave` (deviation D-001, unchanged route) takes:

```json
{
  "graveId": null,
  "graveNumber": "1234",
  "fieldNumber": "27",
  "siteName": "Schineller Bäcke",
  "position": "DoubleOutside",
  "startDate": "2026-03",
  "endDate": null,
  "items": [ … ],
  "recipientIds": [ … ]
}
```

Dates are parsed with the invariant culture as `yyyy-MM`; `yyyy-MM-dd` is
accepted too and truncated to its month, so an older client fails loudly only
on what is actually wrong.

### Validation

In `GravesController.Save` and `GraveService.ValidateAsync`, with codes the
frontend maps to i18n texts:

| Code                  | When                                                            |
| --------------------- | --------------------------------------------------------------- |
| `InvalidDate`         | start missing or unparseable; end present but unparseable       |
| `DateRangeInvalid`    | end month before start month (same month is allowed)            |
| `FieldNumberRequired` | field number blank                                              |
| `SiteNameRequired`    | site name blank                                                 |
| `PositionRequired`    | position missing or not one of the six names                    |
| `GraveNumberInUse`    | another grave with the same number overlaps in time (see below) |

The existing codes (`GraveNumberRequired`, the item and recipient checks) stay.

**Overlap.** A grave covers the months from its start through its end, both
inclusive; no end means no upper bound. Two graves overlap when
`a.Start <= (b.End ?? ∞) && b.Start <= (a.End ?? ∞)`. Numbers are compared
trimmed and case-insensitively, like the import's existing check. The grave
being saved is excluded by id. The error carries the other grave's number,
start and end as arguments, so the message can say
"Grabnummer 1234 ist von 01/2020 bis 12/2030 bereits vergeben". A grave ending
12/2030 and one starting 01/2031 do not overlap.

Existing data may already contain duplicate numbers (nothing enforced it
before). The migration does not touch them; the check applies when one of them
is next saved.

### Read side

- `$metadata`: `Grave` gains `FieldNumber`, `SiteName`, `Position` (an OData
  enum type `GravePosition`); `EndDate` becomes nullable. Then
  `npm run gen:odata` and commit `metadata.xml` with `src/api/generated/`.
- `GravesController.Get(key)` adds the three fields to its hand-built JSON.
- `GraveInvoicingStatus` returns `SiteName` and `FieldNumber` per row.
- `SearchGraves` also matches site name and field number.

## 4. Frontend

**`GraveEditor.vue`**, fields in this order:

1. Grave number (`InputText`, as today)
2. Field number (`InputText`, `id="graveFieldNumber"`)
3. Site name (`InputText`, `id="graveSiteName"`)
4. Position (`Select` with the six labels from i18n,
   `input-id="gravePosition"`)
5. Start (`DatePicker`, `view="month"`, `date-format="mm/yy"`)
6. End (same, with `show-clear`; empty is valid)

`toIsoDate`/`parseIsoDate` get month counterparts in `lib/format.ts`
(`toIsoMonth`: `Date` → `"yyyy-MM"`; `formatMonth`: `"yyyy-MM-dd"` → `"MM/yyyy"`).
`endpoints.ts`' `SaveGraveInput` gets the new fields and `endDate: string | null`.
i18n gains the field labels, the six position labels, and the four new error
texts, in `de.json` (the only catalogue since the UI became German only).

**`GravesPage.vue`**: columns for site name and field number; start and end as
`MM/yyyy`, a missing end as "–".

**`GraveDetailPage.vue`**: the card shows field number, site name and position;
start and end as `MM/yyyy`.

The UI keeps calling the grave number "Grabnummer"; "Laufweg-Nr." appears only
on the document.

## 5. Invoice Document

The layout follows the scans for every invoice, grave or not. Changes against
the scans: the VAT key column shows the rate instead of a numbered key, "Incl."
replaces "i" (so the scan's "i = MWSt inclusive" legend is dropped), and the VAT
block's column heads are the rates ("19,00 %") instead of "MWSt-1 19,00 %".

### First page head

- Left: the recipient's address in the envelope window, as today.
- Right, beside it, a label/value block:
  - `**** RECHNUNG ****`
  - `NUMMER:` invoice number
  - `KUNDEN-NR:` recipient number (omitted if the snapshot is null)
  - `LAUFWEG-NR:` grave number (grave invoices only)
  - `DATUM:` generation date
  - `SEITE:` "1 von 2" (current page / total pages)
- The large "Rechnung …" title below the address goes away, and so does the
  centred "Seite x von y" page footer.

### Continuation pages

One line: `Rechnung 2026-0412 · Kunden-Nr. 067200 · Seite 2 von 2` (the
customer number part left out when unknown). `LetterheadTests`' one-line
assertion for continuation pages keeps holding.

### Grave site lines

On grave invoices, bold, above the first line item:

```
GRABSTÄTTE: Schineller Bäcke
FELD-GRAB-NR: 27 dGr.a
```

Each line is left out when its value is missing; "FELD-GRAB-NR" prints with
whatever of field number and position exists.

### Line table

| Column                         | Content                                       |
| ------------------------------ | --------------------------------------------- |
| (no heading)                   | item number                                   |
| Artikel / Leistungsbezeichnung | description                                   |
| Menge                          | count                                         |
| E-Preis €                      | unit price                                    |
| Gesamt €                       | line total                                    |
| ST                             | `19 %`, or `7 % Incl.` on a VAT-included line |

On a VAT-included line, unit price and line total are the gross amounts — the
price that was entered, as the second offer scan does (`1.300,00 2 i`). The
gross unit price is `round(UnitPrice × (1 + rate/100), 2)`, which is exact,
since saving refuses a gross that cannot be billed as a whole-cent net. The
VAT block keeps computing from the net, so it is unchanged in value.
`LineData` gains `PriceIncludesVat`.

### Ending text and VAT block

The ending text follows the line table. The VAT block sits at the bottom of
the last page (the content column's last item extends to the page's remaining
height and aligns to its bottom); when it does not fit, it moves to the next
page as a whole.

```
                7,00 %     19,00 %                         €
Netto €          50,00       64,00       Nettobetrag   114,00
MWSt €            3,50       12,16       MWSt           15,66
                                         Bruttobetrag  129,66
```

The left part has one column per rate, ordered by rate. On an invoice with a
share (`RecipientCount` set), the block shows the recipient's share values
(`ShareData`) and one line above it reads
`Nettosumme gesamt 114,00 € – davon Ihr Anteil 50 %`. The line items stay at
100 %. An old grave invoice without `RecipientCount` prints its lines and the
block as they are (its lines are already the share); its `ShareText` note
stays above the table.

### Texts

Every new string goes into the static, German-only `DocumentTexts` (e.g.
"RECHNUNG", "NUMMER", "KUNDEN-NR", "LAUFWEG-NR", "DATUM", "SEITE",
"GRABSTÄTTE", "FELD-GRAB-NR", "Nettobetrag", "MWSt", "Bruttobetrag", "Incl.").
Strings that go out of
use (`NetForRate`, `VatForRate`, `VatForRateFromNet`, `NetTotal`, `VatTotal`,
`GrossTotal`, `SharePart`, the page footer) are removed with their callers.

### Letterhead

`Letterhead` is not changed. The new head block lives in the page header like
the address does today and must fit inside the content area. The checks from
CLAUDE.md apply: look at page two of a multi-page invoice.

## 6. Import

`graves.csv`'s header becomes
`GraveNumber,FieldNumber,SiteName,Position,StartDate,EndDate`.

- `StartDate` required, `EndDate` optional, both `YYYY-MM` (`YYYY-MM-DD`
  accepted and truncated); `DateInvalid`'s message names the new format.
- `FieldNumber`, `SiteName`, `Position` required; `Position` must be one of the
  six member names, else a new `PositionInvalid` problem.
- Grave numbers stay strictly unique within the archive and against existing
  graves (`DuplicateGraveNumber`, `GraveExists`), because `mapping.csv`
  references graves by number.
- The template download follows `ImportArchiveLayout`, so it changes with it.

An archive with the old three-column `graves.csv` is refused by the header
check. That is the breaking change the `feat!` stands for.

## 7. Tests

**Integration (`Invoicing.IntegrationTests`)**

- `SaveGraveTests`: month dates round-trip as the first of the month; missing
  end accepted; each new required field; `DateRangeInvalid` by month;
  `GraveNumberInUse` for overlapping, open-ended and same-number-different-case
  graves; adjacent periods accepted; same field number on two graves accepted;
  updating a grave does not collide with itself.
- Snapshot: a generated grave invoice and a revision carry the five values; an
  ordinary invoice carries the recipient number.
- Migration: day-precise dates are truncated, `EndDate` becomes nullable.
- `ImportTests`: new columns, optional end, invalid position, old header refused.

**Documents (`Invoicing.DocumentTests`, PdfPig)**

- Head block with and without "Laufweg-Nr.", and with no recipient number.
- Grave site lines present, partly missing, absent on a non-grave invoice.
- Item number column; "19 %" and "7 % Incl." in the ST column; gross unit price
  and line total on a VAT-included line.
- VAT block for one rate, two rates, and a share (Variant A), and for an old
  grave invoice without `RecipientCount`.
- Page two of a multi-page invoice: letterhead complete, continuation line, VAT
  block on the last page.
- Existing tests that assert on removed texts are rewritten to the new ones;
  `DeterminismTests` stays green.

**E2E (`e2e/`)**

- `seedGrave` gets defaults for the new fields and month dates.
- `graves.spec.ts`: create a grave with the new fields and month pickers; reopen
  shows them; an empty end saves; a second grave with an overlapping number is
  refused with the message; the list shows site name and field number.
- `import-export.spec.ts`: the archive fixtures get the new `graves.csv` columns.

## 8. Documentation

- `docs/spec.md`: FR-018 becomes "A grave MUST have a grave number, a field
  number, a site name, a position and a start month; the end month is
  optional." Add the overlap rule for grave numbers, and describe the invoice
  head, site lines and VAT block where the document is specified.
- No new deviation.
