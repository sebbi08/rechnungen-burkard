# Work lists: care list per year, planting list per quarter (issue 64)

**Source**: issue 64 ("Listen") and the owner's answers of 2026-11-13.

## 1. Why

The staff need printed sheets that tell them which graves need work, and how
much: the **care list** (Pflegeliste) for a year, to check whether a grave has
to be looked after, and the **planting list** (Pflanzliste) for a quarter, to
see which graves get a seasonal planting and of what size. Both are working
papers for the people doing the work. They are not invoices, and nobody signs
or files them. The amount of money printed next to a service shows its scope:
a planting worth 80 € is more work than one worth 40 €.

FriedWin, the old program, printed such lists ("Pflegeliste mit Preise"), and
cooperative graves were brought into this app so that they would show up on
them (design 2026-11-07, cooperative graves).

## 2. Goals, non-goals, anti-goals

Goals

- Two PDF lists, downloaded from a new page "Listen".
- Both grouped by cemetery, each cemetery starting on a new page, graves
  sorted by grave number within it.
- Each grave starts with its details: grave number, site name (Grabstätte) and
  grave position (Grabposition). The owner's "Grabposition" is the field
  number together with the position's short form, as the grave invoice prints
  it ("27 dGr.a", FR-037b).
- The care list for a chosen year, in three variants: graves only, with the
  care services and their prices, or with all services and their prices.
- The planting list for a chosen quarter of a chosen year, one line per
  seasonal service with its amount.
- Start month, end month and the cooperative's contract number on both
  lists. The start and the end are each set in bold when they fall inside the
  list's period.

Non-goals

- No editing from the lists, no ticking off work done, no record of what was
  printed.
- No new property on items. Which service counts as care and which as an
  extra follows from what items already carry (section 4.2).
- No round list (Laufliste). The cooperative design mentions one as a later
  idea; it is not part of issue 64.
- No Excel or CSV export.

Anti-goals

- A sheet that tells a worker to look after a grave whose term has ended, or
  leaves out one that is still running. Which graves are on a list follows the
  term exactly (section 4.1).
- A list that shows different amounts than the ones on the grave. The amount
  comes from the grave's own prices, the same ones the editor shows.

## 3. Approaches

Three ways were considered. The owner picked the first.

1. **PDF rendered on the server with QuestPDF** (picked). This is the engine
   the invoices already use. A page break per cemetery, the two-column flow,
   the header on every page and bold text are under exact control, and the
   sheet prints the same from every device. The cost: a layout change needs a
   build.
2. **A print view in the browser** (a page printed with Ctrl+P). It is quicker
   to change and visible before printing. But page breaks, a two-column flow
   and running headers print differently from browser to browser, and printing
   from a phone is awkward.
3. **An Excel file.** The staff could filter it themselves. But a page per
   cemetery, bold ends and two columns are practically lost, and it reads as a
   table rather than a work sheet.

The owner wants sheets to take along with a firm page break per cemetery, and
the PDF delivers that reliably with technology the project already has.

## 4. Behaviour

### 4.1 Which graves are on a list

- **Care list, year Y**: every grave whose term has at least one month in Y.
  A grave from 01/2020 to 03/Y is on it, and so is one starting in 11/Y.
  Cooperative graves are on it too. Deleted graves are not.
- **Planting list, quarter Q of year Y**: every grave that has at least one
  service billed in Q and whose term has at least one month in Q. This is the
  same rule invoicing uses (a quarter service is billed in full once any month
  of its quarter lies in the term; `HalfYearBilling.Touches`). Cooperative
  graves are on it if they carry such a service. Deleted graves are not.
- A cemetery with no grave on the list gets no page.
- If no grave at all is on the list, there is no PDF. The page says so
  instead, for example "Für 2027 gibt es keine Gräber." or "Für Frühling 2027
  gibt es keine Gräber mit Zusatzleistungen.", just as the invoice ZIP does
  when it would be empty.

### 4.2 Care services and extra services

An item without a quarter is billed all year. Every billed grave must have
one (FR-019/FR-020), and that is its care. An item with a quarter (Frühling,
Sommer, Herbst, Winter) is a seasonal service: the planting.

- **Care service** (Grabpflege-Leistung) = an item without a quarter.
- **Extra service** (Zusatzleistung) = an item with a quarter. The planting
  list for Q shows the grave's items whose quarter is Q.

### 4.3 Amounts

The amount printed for a service on a grave is **count × unit price including
VAT**, in euros with two decimals ("48,15 €"). The count is printed in front
of the service ("2 × Schalen"), because it shows the scope too.

- The unit price is the grave's own price, or the item's default when the
  grave has none (as `GraveBilling.LinkFor` resolves it).
- A price entered as gross is taken as entered. A net price is turned into a
  gross with the item's current VAT rate, rounded to the cent, as the invoice
  PDF does for its gross column. The line amount is that unit gross × count.
- The price is the one entered on the grave (owner's answer): **per
  half-year** for a care service, since that is what an all-year price means
  (spec line 743), and per occurrence for a seasonal service. The care list
  is not doubled to a yearly amount; its amount column is headed "Betrag je
  Halbjahr" for care services, and a seasonal service in the "all services"
  variant carries its quarter, which says it is once a year. The list does not prorate partial half-years and
  ignores rounding corrections (FR-021a): those move a bill by cents for one
  invoice, while the list shows scope.
- A service without any price (no grave price, no default; possible on a
  cooperative grave) prints "–" instead of an amount.

### 4.4 The care list

Heading line on every page: "Pflegeliste Y", the cemetery, "Stand: dd.MM.yyyy"
(the date it was made) and "Seite n von m", counted **per cemetery** (my
call: each cemetery's pages are a stack of their own that a worker takes
along, so "Seite 2 von 3" refers to that stack).

**Variant "Nur Gräber"**: one line per grave, in **two columns**: the left
column of a page fills first, then the right one. Per grave: grave number,
site name, grave position, start month, end month, contract number.

```
Pflegeliste 2027 · Waldfriedhof                       Stand: 13.11.2026 · Seite 1 von 3
Nr.   Grabstätte  Pos.     Beginn   Ende     Vertr. │ Nr.   Grabstätte  Pos.     Beginn       Ende
A-12  Müller      27 dGr.i 04/2015  12/2030  4711   │ A-20  Weber       31 uGr.i 01/2019      –
A-14  Schmidt     27 uGr.a 01/2020  –               │ A-21  Klein       31 eGr   **11/2027**  –
```

**Variant "Mit Pflege-Leistungen"** and **variant "Mit allen Leistungen"**:
one column. Each grave's detail line is followed by one indented line per
service: count, item number, description, amount. "Mit allen Leistungen" also
names the quarter of a seasonal service ("Frühling"). The services stand in
the grave's own order (link order).

Which services: "Mit Pflege-Leistungen" lists the grave's care services.
"Mit allen Leistungen" lists its care services and those seasonal services
whose quarter has at least one month of the grave's term in Y (the
`Touches` rule of 4.1). A grave ending 03/Y does not list its autumn
planting (my call, from the first anti-goal). A grave with no service to
list still gets its detail line.

```
A-12  Müller   27 dGr.i              Beginn 04/2015   Ende 12/2030   Vertrag 4711
      1 × 1001  Jahrespflege                                             120,00 €
      1 × 2010  Frühjahrsbepflanzung (Frühling)                           48,15 €
```

### 4.5 The planting list

Heading line on every page: "Pflanzliste <Quartal> Y", the cemetery,
"Stand: …" and "Seite n von m" (per cemetery, as on the care list). One
column. One line per extra service of that quarter. The grave's details,
start and end month and contract number stand on the grave's first line
only, so that a grave reads as one block.

```
Pflanzliste Frühling 2027 · Waldfriedhof                   Stand: 13.11.2026 · Seite 1 von 2
Nr.   Grabstätte  Pos.      Leistung                   Betrag   Vertrag  Beginn   Ende
A-12  Müller      27 dGr.i  1 × Frühjahrsbepflanzung  48,15 €  4711     04/2015  12/2030
                            2 × Schalen                21,40 €
A-14  Schmidt     27 uGr.a  1 × Frühjahrsbepflanzung  32,10 €           01/2020  **03/2027**
```

A grave handed over during the period (FR-018d: the old grave ends, a new
one starts under the same number) appears twice, once with its bold end and
once with its bold start. That is correct and is what the bold months are
for.

### 4.6 Common rules

- **Start month** as "MM/yyyy", always printed, next to the end month. It is
  **bold** when it falls inside the list's period: in Y on the care list, in
  Q of Y on the planting list (owner's answer).
- **End month** as "MM/yyyy", or "–" for a grave without an end, bold by the
  same rule.
- **Contract number** only on cooperative graves (only they have one), empty
  otherwise.
- **Grave position** (Grabposition) is the field number followed by the
  position's short form, exactly as the grave invoice composes it
  (`DocumentTexts.PositionShortForm`, FR-037b): "27 dGr.a"; kinds without an
  abbreviation are spelled out ("27 Rasengrab außen"); a part that is missing
  is left out, so a migrated grave without a position shows the field number
  alone. The invoice's "FELD-GRAB-NR:" label is not printed; the column
  heading says "Grabposition" (or "Pos." where space is short).
- **Sorting**: cemeteries by name, German order ignoring case. Graves by grave
  number in human order: runs of digits compare as numbers, everything else
  ignoring case, so "A-2" comes before "A-10".
- **Page**: A4 portrait, no letterhead, no logo, no fold marks. A plain
  working sheet.
- **File names**: "Pflegeliste-2027.pdf", "Pflanzliste-2027-Fruehling.pdf".

### 4.7 The page "Listen"

A new menu entry "Listen" (icon `pi-list`) after "Inaktive Gräber", route
`/lists`. Two cards:

- **Pflegeliste**: year (the year picker the graves page uses), variant (a
  SelectButton: "Nur Gräber" / "Mit Pflege-Leistungen" / "Mit allen
  Leistungen"), button "PDF herunterladen".
- **Pflanzliste**: year and quarter (SelectButton: Frühling, Sommer, Herbst,
  Winter), button "PDF herunterladen".

The year starts at the current year, the quarter at the current quarter, the
variant at "Nur Gräber". The button shows it is busy while the PDF is made, as
the invoice ZIP button does. A refusal (nothing on the list) shows as an error
toast with the server's message.

### 4.8 API

Two plain routes under `odata`, outside the EDM, like the other file
downloads (`BulkDownloadDocuments`, `Quotes({key})/Pdf`), so no `gen:odata`:

- `GET odata/CareList?year=2027&services=none|care|all` → `application/pdf`,
  `Content-Disposition` with the file name.
- `GET odata/PlantingList?year=2027&quarter=Spring` → the same.

A refusal is 400 with `{error:{code,message}}`, codes `NoGravesOnList`
(nothing to list) and `ValidationFailed` for a bad year, quarter or variant.
The year must be between 2000 and 2100.

## 5. Spec changes

- A new user story, "Print work lists", with acceptance scenarios for both
  lists, the bold end, a cemetery per page and the empty refusal.
- A new group **Work lists** under Functional Requirements, FR-102 onwards:
  which graves (4.1), care and extra services (4.2), amounts (4.3), the two
  lists and the variants (4.4, 4.5), common rules (4.6), the page (4.7).
- Edge cases: a grave without a position; a service without a price; a grave
  starting or ending inside the period; a grave handed over inside the
  period; a cooperative grave without services.

## 6. Tests

- **Integration (API)**: which graves are on each list, at the boundaries of
  the term (ends in January of Y, starts in December of Y, ended in Y−1,
  starts in Y+1; a quarter touched by one month only), deleted graves out,
  cooperative graves in, the empty refusal, bad parameters.
- **Document tests (PdfPig)**: one cemetery per page, sorted order including
  "A-2" before "A-10", the two columns of the graves-only variant, the
  services per variant, the amounts (gross entered, net converted, count, no
  price), the grave position ("27 dGr.a", field number alone without a
  position), the bold start and end (font name or weight of the letters),
  the heading line with page numbers counted per cemetery, determinism for
  the same input and date.
- **E2E**: the page "Listen" downloads both PDFs with their file names, and
  shows the refusal toast for a period without graves. That period must be an
  early year such as 2000: graves without an end are on every later year, and
  the specs share one stack, so a year far out always has someone's graves.

## 7. Left to the builder

- Font sizes and column widths, as long as the two-column variant fits A4
  portrait (smaller type, or a wrapped site name, is fine; cutting text off is
  not).
- Where the list data is gathered (a service in the API that hands a plain
  data object to a document class in `Invoicing.Documents`, as invoices do).
- The exact German wording of the refusals and of the page's hints.
