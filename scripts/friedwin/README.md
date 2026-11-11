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
  says `Genossenschaftsgrab`, `normales Grab`, or is empty for a left-out one;
  semicolon-separated for Excel; this one names people).

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

## Where the data comes from

| App            | FriedWin                                                                                                                                                                                                                                                                                                                                                                                                                                                                                             |
| -------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| recipients     | `F01KUNR1.DAT`: number at 3, name at 14, address lines at 54, 94, 178 (each 40 characters), country code, postcode and place at 218/221/226 (3, 5 and 40); salutation code at 134 into `f22anre1.DAT`; e-mail in the contact slot of kind 08 (eight slots of 2 + 100 bytes from 780).                                                                                                                                                                                                                |
| graves         | `GRABSTA1.DAT`: the cemetery code at 3 into `f24pfle1.DAT` (name at 16); the customer number at 5 is the grave number; name of the grave at 72; location text ("27 dGr.a") from 104; care start and end as packed MMYYYY at 236 and 240; care price per half-year packed at 287; the contract number at 250 (11 characters).                                                                                                                                                                         |
| site name      | the text after "GRABSTÄTTE:" on the grave's last invoice, which is the grave's name followed by its cemetery, spelled differently over the years ("FRIEDHOF SPEYER" until 2018, nothing until 2025, "Speyer" since): `CEMETERIES` in convert.py maps each spelling, and each name in the cemetery table, to the app's name. The cemetery is the grave record's; a grave whose invoices name another is noted.                                                                                        |
| items, mapping | FriedWin keeps no list of what a grave gets; its invoices do. `ARCHIVX1.DAT` is the print data of every invoice: 2307-byte records of 92 `;`-separated fields, a header record per page (7 number, 8 grave, 9 date, 21 site, 84–91 address) and one per line (20 article, 22 text, 26 quantity, 27 unit price, 28 unit, 30 total, 31 VAT code; 23 a heading such as "Fruehjahr"; 35/36 with 48/49 the VAT rates of the codes). A grave's items are read off its last care invoice of each half-year. |

The text is Latin-1. The other files of the export (`Rechein1`, `Zahlein1`: invoice
and payment journals; `R0320xx1`: yearly ledgers) are not imported.

## Finding your way in the files again

The layouts above were reconstructed without reading the data, and these tools
are how:

- `schema_profile.py` prints each file's record length, a per-column character
  class map and suggested fields, but no values.
- `anonymized_sample.py` prints a few records per file split at the field
  boundaries, with every word masked unless it is common across the file.
- `locate.py` lists where known values (taken from a printed invoice) sit:
  file, record, offset and encoding (digits, packed decimal, binary).
