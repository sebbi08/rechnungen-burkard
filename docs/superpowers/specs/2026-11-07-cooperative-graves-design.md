# Design: Cooperative Graves ("Genossenschaftsgräber")

**Created**: 2026-11-07

**Status**: Approved design (pre-implementation), approved by the owner on 2026-11-07

**Source**: the owner's answers of 2026-11-07, given while comparing FriedWin's
care list ("Pflegeliste mit Preise.pdf") and address list ("Kundendaten.pdf")
with the FriedWin import archive.

> **Note added 2026-11-11**: at the owner's request a cooperative grave no
> longer appears in the grave overview, which lists only the graves the business
> bills; it is found in its own list "Genossenschaftsgräber" (and on its detail
> page and in the item dialog, as before). The rows "Grave overview" and "Status
> in a past half-year" below are amended accordingly; elsewhere "every list of
> graves" now means every list but the overview.

## 1. Intent

Some graves the business tends are billed by the cemetery gardeners'
cooperative (the "Genossenschaft", typically under a long-term care contract),
not by the business itself. In the owner's words: such a grave has to exist as
a grave so that it shows up in the lists the app will get later (a care list,
"Pflegeliste", and a round list, "Laufliste"), but it is billed separately. It
must stay editable, it must never produce an invoice, and there should be a list
of its own with all cooperative graves.

Today the app cannot hold such a grave: every grave must have a recipient and a
billable all-year item (FR-019, FR-020, FR-058a), and the only grave list (the
half-year overview) shows only graves that are due or already invoiced. The
FriedWin conversion therefore leaves out the 95 active graves FriedWin has never
billed, although FriedWin lists them on its care list with a 2026 care price.

## 2. What FriedWin knows (the data behind the decisions)

FriedWin has no flag "cooperative grave". Its cooperative module (`GENODDAT/`)
holds 118 care contracts (`u60vert1.DAT`) with their services and amounts
(`U60SCHNI.DAT`); a grave refers to a contract by the contract number in its
grave record (`GRABSTA1.DAT` offset 250, the "Vertrag" column of the care list).
None of the signals separates the graves cleanly:

| Active graves on the care list                  | never billed by FriedWin | billed by FriedWin |
| ----------------------------------------------- | -----------------------: | -----------------: |
| all                                             |                       95 |                548 |
| with a contract of the cooperative module       |                       23 |                 15 |
| with a contract number the module does not know |                       26 |                 18 |
| with column "VA" = 2 on the care list           |                       15 |                  3 |
| without any customer record in FriedWin         |                       49 |                 10 |
| with a care price in the grave record           |                       93 |                    |

The owner does not know the rule either. **Decided**: every active grave
FriedWin never billed is imported as a cooperative grave, so the app bills
exactly what FriedWin billed and nothing more; the flag can be changed per grave
in the app afterwards. Grave 062050, active and on the care list but last billed
in 2001 (DM prices), is treated the same way (Claude's call, confirmed by the
owner). The 15 billed graves with a cooperative contract stay ordinary graves.

## 3. Goals, Non-Goals, Anti-Goals

**Goals**

- A grave can be marked as cooperative grave, in the grave editor and in the
  import archive.
- A cooperative grave never gets an invoice from the app, by no route.
- A cooperative grave appears in every list of graves the app has, marked as
  such, and in a new list of its own.
- The FriedWin conversion imports all active graves; the never-billed ones (and 062050) as cooperative graves.

**Non-goals**

- The care list and the round list themselves. They come later; this design
  only makes sure cooperative graves will be there for them.
- Importing the cooperative's contracts, their services or their amounts, or
  the contract number. (Possible later, for the care list.)
- Any accounting with the cooperative (statements, payments).

**Anti-goals** — it would be a failure, even if it technically works, if

- a half-year run, a regeneration or any other path created an invoice for a
  cooperative grave;
- a cooperative grave silently dropped out of the grave overview, so that the
  owner can no longer find it where the other graves are;
- turning a grave into a cooperative grave deleted or changed invoices it
  already has.

**Constraint**: the FriedWin archive is always imported into an empty
database (the owner, 2026-11-07). No path is needed to add cooperative graves
to a database that already holds an earlier conversion.

## 4. Decisions

| Topic                      | Decision                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                            |
| -------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Name                       | German UI: "Genossenschaftsgrab" / "Genossenschaftsgräber". Code: `IsCooperative` on `Grave` (Claude's call).                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                       |
| Model                      | A boolean on the grave, default `false`; not a separate entity (Claude's call). Stored by a column added to the unreleased migration `20261101095804_RecipientEmailAndCemeteries` (constitution IV: one migration per version; v3.1.0 does not contain it).                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                         |
| No invoices                | `GraveBilling.OpenForHalfYearAsync` leaves cooperative graves out, so a half-year run never selects them; the correction precheck (`GraveCorrectionCheck`) skips them; `GraveInvoiceGenerator` refuses one as a last guard. The generation result does not count them as open or skipped.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                           |
| Existing invoices          | Marking a grave as cooperative leaves its existing invoices as they are; they can still be viewed, downloaded, marked paid and revised (a revision only re-issues an invoice that exists).                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                          |
| Editable                   | A cooperative grave is edited in the same editor as any grave, with a checkbox "Genossenschaftsgrab" after the end month.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                           |
| Rules relaxed              | For a cooperative grave: no recipient is required (0–4 allowed), no all-year item is required, and items may lack a price. The usual field rules (FR-018, FR-018a, FR-018c) stay.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                   |
| Recipients                 | FR-020b (recipient set fixed at creation) applies once a grave has an invoice. A cooperative grave that has never been invoiced may change its recipients, so that it can be turned into an ordinary grave later (Claude's call).                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                   |
| Correcting the mark        | The box can be ticked or unticked in place only while the grave has no invoice. That corrects a wrong mark, and means the grave always was (or never was) a cooperative grave: an unticked grave is open for every half-year a run may still reach (FR-025), like any ordinary grave. Unticking applies every rule of an ordinary grave on save (1–4 recipients, a billable all-year item, the rounding correction of FR-021a); the editor says what is missing, as it does today.                                                                                                                                                                                                                                                                                                                                                  |
| Changing from a month on   | When a grave moves between the cooperative and the business from a given month (a care contract ends, or one begins), the grave is not flipped: it ends with the month before, and a new grave with the same number, fields and the other kind starts with that month. FR-018a already allows this, since the terms do not overlap. The editor offers it as one action ("Ab Monat … selbst abrechnen" on a cooperative grave, "Ab Monat … über die Genossenschaft" on an ordinary one), which asks for the month, and opens the new grave's editor prefilled. The billing code needs no date of its own, earlier half-years keep the kind they had, and both graves stay visible with their terms (Claude's call, prompted by the builder check: a boolean flipped in place would make the cooperative's past half-years billable). |
| Status in a past half-year | A cooperative grave cannot hold invoices (the flag is fixed once there are any), and it is not in the overview (note of 2026-11-11), so the question does not arise there.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                          |
| Grave overview             | Lists only the graves the business bills: a cooperative grave is left out (amended 2026-11-11; it first showed with a grey tag "Genossenschaft"). The status filter keeps "offen" and "erstellt".                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                   |
| Grave detail page          | Shows the tag "Genossenschaftsgrab" next to the grave number; the current half-year tag reads "Genossenschaft" instead of "offen". Recipients and items cards show what is there, also when empty.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                  |
| New list                   | A page "Genossenschaftsgräber" in the navigation, after "Gräber": every cooperative grave, not per half-year (see §5).                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                              |
| Item dialog                | The list "graves with this item" (`ItemDialog`) marks cooperative graves with the same tag.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                         |
| Import CSV                 | `graves.csv` gets an optional eighth column `Cooperative` (`true`/`false`, empty = `false`), added the way `Email` and `UnitPrice` were (an optional trailing column: archives without it still import). FR-058a's billability checks are skipped for a row with `true`; the reference checks and the maximum of four recipients stay.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                              |
| Not printed                | Nothing changes on invoice documents.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                               |

## 5. The List "Genossenschaftsgräber"

A page at `/cooperative-graves`, reached from the navigation (icon like
"Gräber"), built like the grave overview but without half-year, invoicing and
payment status (Claude's calls throughout this section):

| Column       | Filter                                                                                                                                    |
| ------------ | ----------------------------------------------------------------------------------------------------------------------------------------- |
| Grabnummer   | Text                                                                                                                                      |
| Grabstätte   | Text                                                                                                                                      |
| Feld         | Text                                                                                                                                      |
| Friedhof     | Select (cemeteries from the settings)                                                                                                     |
| Beginn, Ende | Text, as shown `MM/yyyy`                                                                                                                  |
| Empfänger    | Text (names and addresses), empty when there is none                                                                                      |
| Laufzeit     | Select: Aktiv (default) / Beendet / Alle — active as the FriedWin conversion counts it: no end month, or one not before the current month |

Rows sort by grave number; a row opens the grave's detail page; the list has a
"Neues Genossenschaftsgrab" button that opens the grave editor with the box
ticked. Column filters, the phone filter sheet, paging and the URL query behave
as on the grave overview (design of 2026-11-01, FR-024, FR-068a–c). The data
comes from the server; whether through the `Graves` OData set with a filter or a
route of its own is the builder's call.

## 6. The FriedWin Conversion

`scripts/friedwin/convert.py` (cemetery now from the grave record, commit
f0e21b1):

- An **active** grave that FriedWin never billed for care, and an active grave
  whose last care invoice predates the euro, is written to `graves.csv` with
  `Cooperative=true` instead of being left out. Ended ones stay left out as
  today.
- Its fields come from the grave record: site name (`GRABSTA1` 72, else 32),
  field and position from the location text, cemetery from the code, start and
  end month. Without a start month, the first month of the export's year is
  taken and noted (Claude's call; a cooperative grave's start only matters for
  the overlap rule FR-018a and the lists). Five of the 96 have none.
- Two of them (017630, 059850) are empty records in FriedWin: no site name, no
  field, no start, no price, price group 0, and they show the same way on the
  care list. They are imported with "-" as site name and field number and a
  note "Leerer Grabsatz", so the owner can fill them in or end them in the app
  (Claude's call: they are active, and the owner asked for every active grave).
- Every other check the conversion makes still applies to these graves (an
  unknown cemetery code, an end before the start); with the 30.09.2026 export
  none of the 96 fails one.
- Its items: the care item (`99990`) at the grave record's care price when it
  has one, nothing else; no items from pre-euro invoices.
- Its recipient: the grave's customer record when it is usable (43 of 95), else
  none. No recipient from the cooperative contract.
- The report (text and PDF) gets a section "Als Genossenschaftsgrab übernommen"
  listing each such grave with what FriedWin knows about it: cooperative
  contract / other contract number / none, customer record yes/no, care price.
  The section "Nie für Grabpflege abgerechnet" keeps only the ended graves.
  `-nie-abgerechnet.csv` keeps listing all never-billed graves and gains a
  column saying whether the grave is imported as cooperative grave.
- With the 30.09.2026 export this gives 778 graves, 651 of them active
  (96 cooperative), against 648 on the care list of 02.11.2026; the eight and
  five graves that differ are changes after the export.

## 6a. Migration and the Dev Database

The column goes into the unreleased migration `20261101095804_RecipientEmailAndCemeteries`
(constitution IV). The dev database has already applied that migration, so EF
will not add the column there; it holds no graves and no invoices. It is reset
with `scripts/reset-dev-db.sh` once the migration is changed (it keeps the old
database in `data/backup-<time>/`), after asking the owner, as CLAUDE.md asks
for that step.

## 7. Spec Changes

`docs/spec.md` gets the flag in FR-018, the exceptions in FR-019, FR-020,
FR-020b and FR-058a, the exclusion in FR-025, the column in FR-052, the status
in FR-024/FR-028, and a new requirement for the list (§5) next to FR-024.

## 8. Tests

As constitution III asks: integration tests for the generation leaving
cooperative graves out (including the precheck and a direct generator call),
saving a cooperative grave without recipients and items, switching both ways,
and the import column with and without it; E2E for the editor checkbox, the new
list with its filters, and the overview's tag and status filter. Tests touching
the current half-year go into the serial group in `graves.spec.ts`.

## 9. Left to the Builder

- How the new list gets its data (OData filter or a route of its own).
- The exact German wording of messages and tags beyond the names above, in
  `de.json`.
- Whether `GraveInvoiceGenerator`'s guard is a skip reason or an error, as long
  as no invoice comes out.
