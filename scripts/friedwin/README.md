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

It writes three files next to `--out`:

- the import archive (`recipients.csv`, `items.csv`, `graves.csv`, `mapping.csv`);
- `<name>-report.txt`, what was guessed or left out, by grave and item number;
- `<name>-nie-abgerechnet.csv`, the graves left out because FriedWin never billed
  them for care (semicolon-separated for Excel; this one names people).

Before importing, configure the VAT rates the report lists (19 and 7). The import
inserts only, so it goes into an empty database.

`--positions` takes a CSV of `GraveNumber,Position` to override the position read
off a grave's location text; `--salutation omit` leaves FriedWin's "Herrn",
"Frau", "Familie" out of the names.

## Where the data comes from

| App            | FriedWin                                                                                                                                                                                                                                                                                                                                                                                                                                                                                             |
| -------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| recipients     | `F01KUNR1.DAT`: number at 3, name at 14, address lines at 54, 94, 178, postcode and place at 221/226; salutation code at 134 into `f22anre1.DAT`; e-mail in the contact slot of kind 08 (eight slots of 2 + 100 bytes from 780).                                                                                                                                                                                                                                                                     |
| graves         | `GRABSTA1.DAT`: the customer number at 5 is the grave number; name of the grave at 72; location text ("27 dGr.a") from 104; care start and end as packed MMYYYY at 236 and 240; care price per half-year packed at 287.                                                                                                                                                                                                                                                                              |
| site name      | the text after "GRABSTÄTTE:" on the grave's last invoice, which adds the cemetery to the name at 72.                                                                                                                                                                                                                                                                                                                                                                                                 |
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
