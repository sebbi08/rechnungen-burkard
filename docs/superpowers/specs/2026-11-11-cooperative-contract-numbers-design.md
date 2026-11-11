# Design: Cooperative Graves by Contract Number

**Created**: 2026-11-11

**Status**: Approved design (pre-implementation), approved by the owner on 2026-11-11

**Source**: the owner's answers of 2026-11-11, after the first import of v3.2.0
onto the test server. It amends `2026-11-07-cooperative-graves-design.md`,
which stays the record of the feature as first built.

## 1. Intent

The first conversion took every active grave FriedWin never billed for a
cooperative grave (96 on the export of 30.09.2026). The owner now gives the
rule FriedWin actually follows: a cooperative grave always carries the
cooperative's contract number, the last column "Vertrag" of the care list
("Pflegeliste mit Preise.pdf"; bytes 250–261 of the grave record in
`GRABSTA1.DAT`). Graves change between the business and the cooperative in
both directions over the years, so the number alone does not decide: a grave
with a contract number that was billed for care in 2026 is an ordinary grave,
one without a care invoice in 2026 is a cooperative grave. Every other grave
is ordinary, including those FriedWin never billed and those without a
usable customer record.

The app has to hold the contract number itself, and requires it for every
cooperative grave, also one created in the app.

## 2. What it gives on the export of 30.09.2026

| Active graves                                                   |         Count | Becomes     |
| --------------------------------------------------------------- | ------------: | ----------- |
| contract number, never billed                                   |            49 | cooperative |
| contract number, billed until 2025, no care invoice in 2026     |            33 | cooperative |
| no contract number, care invoice in 2026                        |           498 | ordinary    |
| no contract number, billed until 2025                           |            25 | ordinary    |
| no contract number, never billed (incl. no usable customer: 32) |            46 | ordinary    |
| 062050 (last care invoice in DM, no contract number)            | 1 (in the 25) | ordinary    |

Expected conversion: 778 graves, 651 active, 82 cooperative, 32 ordinary graves
without a recipient (two of them, 017630 and 059850, also without a care price
and so without any item).

## 3. Decisions

| Topic                            | Decision                                                                                                                                                                                                                                                                                                                                                                                                                                                        |
| -------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Contract number                  | New field `ContractNumber` on `Grave` (text, optional in the data model). Required whenever a grave is saved as cooperative — in the editor, by `SaveGrave` (error `ContractNumberRequired`), and for the successor of a hand-over to the cooperative. An ordinary grave stores none: saving one, or unticking the box, clears it (Claude's call).                                                                                                              |
| Where it shows                   | Grave editor: a field "Vertragsnummer" beside the checkbox, shown and required while the box is ticked. Detail page: listed with the grave's fields for a cooperative grave. List "Genossenschaftsgräber": a column "Vertragsnummer" with a text search.                                                                                                                                                                                                        |
| Migration                        | v3.2.0 is released, so the column gets a migration of its own for the next version (constitution IV: one per version).                                                                                                                                                                                                                                                                                                                                          |
| Ordinary grave without recipient | May be imported. The invoice run skips it (`NoRecipients`), as it already does for graves from before that rule. Likewise an ordinary grave without any item (`NoItems`) (Claude's call, for the two empty records). A grave that has item links must still be billable as today (an all-year item, prices, splits).                                                                                                                                            |
| Recipients changeable            | A grave's recipients may change as long as the grave has no invoice — ordinary or cooperative — so that a recipient can be added to an imported grave. FR-020b's reason is invoices already sent; without any there is nothing to keep consistent (Claude's call). Once a grave has an invoice, the set is fixed as before.                                                                                                                                     |
| Import column                    | `graves.csv` gets a ninth optional column `ContractNumber`. A row with `Cooperative=true` and an empty `ContractNumber` is a problem (`ContractNumberRequired`); a contract number on an ordinary row is not stored. An archive may leave out the trailing optional columns it does not use: seven, eight or nine columns.                                                                                                                                      |
| Classification                   | Active graves only: contract number present (bytes 250–261 stripped, non-empty) and no care invoice dated in the export's year → cooperative; otherwise ordinary. Only care invoices (with a "Grabpflege" line) count, so a flower invoice does not turn a cooperative grave ordinary (Claude's call). The year is the export folder's (`20260930-1807` → 2026). Ended graves are classified as before.                                                         |
| Items of a cooperative grave     | One billed until 2025 keeps the items read off its last invoices (useful for the care list later); one never billed gets the care item at the grave record's price, as now. Prices of a cooperative grave are never billed.                                                                                                                                                                                                                                     |
| Never-billed ordinary graves     | Get the care item `99990` at the grave record's care price (none without one), the usable customer record as recipient (none without one), fields as the cooperative branch reads them today ("-" for an empty site name, January of the export year for a missing start, with notes).                                                                                                                                                                          |
| Report                           | The section "Als Genossenschaftsgrab übernommen" lists grave number, contract number, customer record yes/no and care price; the contract kind (cooperative module or other number) is dropped. A new section "Ohne Empfänger oder Leistung übernommen" lists the ordinary graves the app cannot bill until they are completed. `-nie-abgerechnet.csv`'s last column says what a grave became ("Genossenschaftsgrab", "normales Grab", or empty when left out). |

## 4. Non-goals

- Checking that a contract number exists in the cooperative module or is unique.
- The care list and the round list.

## 5. Left to the builder

- The German wording beyond the names above.
- How the import's optional trailing columns are generalised to two.
