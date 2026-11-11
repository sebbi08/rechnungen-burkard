# Importing data from FriedWin

FriedWin, the program the business used before this app, keeps its data in Micro
Focus COBOL files and ships no record layouts. These scripts read an export of it
(a copy of its `frieddat` and `GENODDAT` folders) and turn it into an archive the
app's import accepts. The export holds personal data: keep it outside the
repository's history (`export/` is ignored) and never paste its content anywhere.

## Converting

```sh
python3 scripts/friedwin/convert.py export/ZWI/<stamp> --out export/friedwin-import.zip
```

It writes four files next to `--out`:

- the import archive (`recipients.csv`, `items.csv`, `graves.csv`, `mapping.csv`);
- `<name>-report.pdf`, in German: how many graves FriedWin holds, how many are
  prepared for the import and how many are left out, each reason explained with
  every grave it leaves out, and the prepared graves that carry a guess;
- `<name>-report.txt`, what was guessed or left out, by grave and item number;
- `<name>-nie-abgerechnet.csv`, the graves FriedWin never billed for care, the
  ended ones left out and the active ones imported (the column `Übernommen als`
  says `Genossenschaftsgrab`, `normales Grab`, or is empty for a left-out one; semicolon-separated for
  Excel; this one names people).

Before importing, configure the VAT rates and create the cemeteries the report lists. The import
inserts only, so it goes into an empty database.

`--positions` takes a CSV of `GraveNumber,Position` to override the position read
off a grave's location text; `--salutation omit` leaves FriedWin's "Herrn",
"Frau", "Familie" out of the names.

## Cooperative graves

`graves.csv` ends in the columns `Cooperative` (`true`/`false`) and
`ContractNumber`. An **active** grave (no care end, or one not before the current
month) is a cooperative grave iff its record holds a contract number (`GRABSTA1.DAT`,
bytes 250-260, stripped) and none of its care invoices is dated in the export's
year (the folder stamp, `20260930-1807` gives 2026). The row then says `true` and
carries the contract number; every other row says `false` and leaves it empty.

A grave FriedWin billed before keeps its invoices' items, recipient and values,
cooperative or not. An **active** grave that FriedWin never billed with a care
line, or whose last care invoice predates the euro, is written from its grave
record alone, cooperative or ordinary:

- site name `-` when the record has no name (note "Leerer
  Grabsatz");
- care start from the record, else January of the export's year (note
  "Pflegebeginn ergänzt");
- recipient: the customer record when it has a name and an address, else none;
- items: one link to `99990` (Grabpflege) at the record's care price, count 1,
  when the record has one, else none.

An ended one of these is still left out. The usual checks (known cemetery code,
care not ending before it starts) still apply.

The report has a section "Als Genossenschaftsgrab übernommen" (grave number,
contract number, whether a customer record exists, care price) and a section
"Ohne Empfänger oder Leistung übernommen": the ordinary graves written from the
record alone that lack a recipient and/or a care price, with what is missing
(note `incomplete`; to be completed in the app).
