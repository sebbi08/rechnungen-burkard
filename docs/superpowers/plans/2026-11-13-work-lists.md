# Work Lists Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Two printable PDF work lists (care list per year in three variants, planting list per quarter), grouped by cemetery, downloaded from a new page "Listen".

**Architecture:** Pure rules (periods, grave-number order, gross amounts) go in `Invoicing.Domain`. A QuestPDF document in `Invoicing.Documents` renders a plain data object. An API service gathers and sorts the graves into that object, and two plain GET routes under `odata` return the PDF. A Vue page downloads it.

**Tech Stack:** .NET 11, ASP.NET Core OData (plain attribute routes, not in the EDM), EF Core SQLite, QuestPDF, xUnit + PdfPig, Vue 3 + PrimeVue + vue-i18n, Playwright.

**Spec:** `docs/superpowers/specs/2026-11-13-work-lists-design.md` (the design). `docs/spec.md` is updated in Task 3.

## Global Constraints

- Code, identifiers and comments in English. Everything the user or the PDF shows is German. Frontend strings live in `frontend/src/i18n/de.json` (constitution VII).
- Document generation is deterministic: the same data and the same `MadeOn` date give byte-identical PDFs (constitution II). No `DateTime.Now` in `Invoicing.Documents`. The API takes "today" from `IClock`.
- Months print as `MM/yyyy`. A grave without an end prints `–`. Money prints as `N2` in de-DE plus ` €` ("48,15 €"). A missing amount prints `–`.
- Grave position text = field number and `DocumentTexts.PositionShortForm(position)` joined by one space, with missing parts left out ("27 dGr.a", "27"). No "FELD-GRAB-NR:" label.
- Start and end are bold when the month lies in the list's period (the year for the care list, the quarter for the planting list).
- Each cemetery starts a new page. "Seite n von m" counts within the cemetery.
- A4 portrait, no letterhead, no logo, no fold marks. Text is never cut off: it may wrap or use smaller type.
- File names: `Pflegeliste-{year}.pdf`, `Pflanzliste-{year}-{Fruehling|Sommer|Herbst|Winter}.pdf`.
- Routes: `GET odata/CareList?year=&services=none|care|all`, `GET odata/PlantingList?year=&quarter=Spring|Summer|Autumn|Winter`. Both are plain `[HttpGet]` routes on an `ODataController` with `[Route("odata")]`, not in the EDM, and need no `gen:odata`.
- Refusals are 400 `{error:{code,message,args?}}`. `ValidationFailed` covers a missing or bad parameter, or a year outside 2000–2100. `NoGravesOnList` has args `[year]` for the care list and `[year, quarterName]` for the planting list, where `quarterName` is the German "Frühling".
- Commits: Conventional Commits, ending with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`. No body line starts with `word:`.
- Backend tests: `scripts/dotnet.sh test Invoicing.sln`. C# formatting: `scripts/csharpier.sh`; the pre-commit hook runs it.
- E2E only against its own stack (`cd e2e && npm test`, port 8081), never the dev stack on 8080.

## Review Focus

1. Grave numbers with leading zeros, no digits at all, or digit runs longer than a `long`. The order must not throw and must stay total. Test: Task 1 `GraveNumberOrder_handles_zeros_letters_and_huge_numbers`.
2. A cemetery with more graves than one page holds. Its pages read "Seite 1 von 2", "Seite 2 von 2", and the next cemetery starts again at "Seite 1 von 1". Test: Task 2 `Page_numbers_restart_for_each_cemetery`.
3. A long site name in the two-column variant. It must appear whole in the PDF text, not clipped. Test: Task 2 `A_long_site_name_is_printed_whole`.
4. A service with no price and no default (possible on a cooperative grave). It prints `–`, not `0,00 €`, and does not crash. Tests: Task 2 `A_line_without_amount_prints_a_dash`, Task 3 `A_service_without_any_price_has_no_amount`.
5. Bad query values (`year=abc`, `quarter=spring` in lower case, `services` missing). Lower-case quarter names are accepted, the rest gets 400 `ValidationFailed` and never a 500. Test: Task 3 `Bad_parameters_are_refused`.

---

### Task 1: Period, order and gross rules (Domain)

**Files:**

- Create: `backend/src/Invoicing.Domain/WorkListPeriod.cs`
- Create: `backend/src/Invoicing.Domain/GraveNumberOrder.cs`
- Modify: `backend/src/Invoicing.Domain/VatInclusivePrice.cs` (add `GrossOf`)
- Test: `backend/tests/Invoicing.IntegrationTests/WorkListRulesTests.cs` (pure tests live in this project, like `HalfYearBillingTests.cs`)

**Interfaces:**

- Produces:
  - `public readonly record struct MonthRange(DateOnly First, DateOnly Last)`. Both ends are the first of their month, inclusive.
  - `public static class WorkListPeriod`:
    - `MonthRange Year(int year)`: 01-01 to 12-01.
    - `MonthRange Quarter(int year, BillingQuarter quarter)`: Spring is months 1–3, Summer 4–6, Autumn 7–9, Winter 10–12.
    - `bool Runs(DateOnly start, DateOnly? end, MonthRange range)`: `start <= range.Last && (end is null || end >= range.First)`.
    - `bool Contains(MonthRange range, DateOnly month)`.
  - `public sealed class GraveNumberOrder : IComparer<string>` with `public static GraveNumberOrder Instance`.
  - `public static decimal VatInclusivePrice.GrossOf(decimal price, decimal vatRate, bool priceIncludesVat)`. A gross price is rounded to the cent and returned. A net price becomes `Round(price × (1 + rate/100), 2, AwayFromZero)`, which is the rounding `InvoiceDocument` uses for its gross column.

- [ ] **Step 1: Write the failing tests** in `WorkListRulesTests.cs`:

```csharp
[Fact] public void Year_spans_january_to_december()
  => Assert.Equal(new MonthRange(new(2027,1,1), new(2027,12,1)), WorkListPeriod.Year(2027));
[Theory] // decimal-free, so InlineData is fine
[InlineData(BillingQuarter.Spring, 1, 3)] [InlineData(BillingQuarter.Summer, 4, 6)]
[InlineData(BillingQuarter.Autumn, 7, 9)] [InlineData(BillingQuarter.Winter, 10, 12)]
public void Quarter_spans_its_three_months(BillingQuarter q, int first, int last) { ... }
[Fact] public void A_grave_runs_in_a_range_it_touches_by_one_month()
  // start 2020-01, end 2027-01 runs in Year(2027); start 2027-12 runs in Year(2027);
  // end 2026-12 does not run in Year(2027); start 2028-01 does not; no end runs.
[Fact] public void A_quarter_touched_by_one_month_counts()
  // end 2027-01 runs in Quarter(2027, Spring) and not in Quarter(2027, Summer)
[Fact] public void GraveNumberOrder_compares_digit_runs_as_numbers()
  // sorted ["A-10","A-2","a-3","B-1"] == ["A-2","a-3","A-10","B-1"]; "9a" < "10"
[Fact] public void GraveNumberOrder_handles_zeros_letters_and_huge_numbers()
  // "A-02" vs "A-2": not 0 (ordinal tie-break) and consistent both ways;
  // "ohne" vs "Ohne" vs "OHNE" give a total, repeatable order;
  // "1" + 30 × "0" > "999" without throwing
[Fact] public void GrossOf_keeps_a_gross_price_and_converts_a_net_one()
  // GrossOf(48.15m, 7m, true) == 48.15m; GrossOf(45.00m, 7m, false) == 48.15m;
  // GrossOf(10.00m, 19m, false) == 11.90m; GrossOf(0.125m, 0m, true) == 0.13m
```

- [ ] **Step 2: Run them and see them fail.** `scripts/dotnet.sh test Invoicing.sln --filter WorkListRulesTests` should fail to compile because the types are missing.

- [ ] **Step 3: Implement the three files.** `GraveNumberOrder`:
  - Split both strings into runs of digits and runs of non-digits, and compare run by run.
  - Two digit runs: drop the leading zeros, then the shorter one is smaller; at equal length compare ordinally. Never parse to a number.
  - Otherwise compare with `string.Compare(a, b, CultureInfo.GetCultureInfo("de-DE"), CompareOptions.IgnoreCase)`.
  - If everything is equal, fall back to `string.CompareOrdinal` on the whole strings, so the order is total.

- [ ] **Step 4: Run the tests and see them pass.** Same filter, all green.

- [ ] **Step 5: Commit.** `feat(domain): periods, grave-number order and gross amounts for the work lists`

---

### Task 2: The work list document (QuestPDF)

**Files:**

- Create: `backend/src/Invoicing.Documents/WorkListData.cs`
- Create: `backend/src/Invoicing.Documents/WorkListDocument.cs` (internal, `IDocument`)
- Create: `backend/src/Invoicing.Documents/WorkListDocuments.cs` (public static entry)
- Modify: `backend/src/Invoicing.Documents/DocumentTexts.cs` (add the helpers below)
- Test: `backend/tests/Invoicing.DocumentTests/WorkListDocumentTests.cs`

**Interfaces:**

- Consumes: `GravePosition`, `BillingQuarter` (Domain); `DocumentTexts.PositionShortForm(string?)`.
- Produces (namespace `Burkard.Invoicing.Documents`):

```csharp
public enum WorkListKind { CareGravesOnly, CareWithServices, Planting }
public sealed record WorkListData(
    WorkListKind Kind, int Year, BillingQuarter? Quarter, DateOnly MadeOn,
    IReadOnlyList<WorkListCemetery> Cemeteries);
public sealed record WorkListCemetery(string Name, IReadOnlyList<WorkListGrave> Graves);
public sealed record WorkListGrave(
    string GraveNumber, string SiteName, string? FieldNumber, GravePosition? Position,
    DateOnly Start, DateOnly? End, bool StartInPeriod, bool EndInPeriod,
    string? ContractNumber, IReadOnlyList<WorkListLine> Lines);
public sealed record WorkListLine(
    int Count, string ItemNumber, string Description, BillingQuarter? Quarter, decimal? Amount);

public static class WorkListDocuments { public static byte[] Generate(WorkListData data); }
```

The `DocumentTexts` additions:

- `string QuarterName(BillingQuarter q)`: "Frühling", "Sommer", "Herbst", "Winter".
- `string QuarterFileWord(BillingQuarter q)`: "Fruehling", "Sommer", "Herbst", "Winter".
- `string CareListTitle(int year)`: "Pflegeliste 2027".
- `string PlantingListTitle(BillingQuarter q, int year)`: "Pflanzliste Frühling 2027".
- `string GravePositionText(string? fieldNumber, GravePosition? position)`.
- `string Month(DateOnly? month)`: "MM/yyyy" or "–".
- `string Amount(decimal? amount)`: "48,15 €" or "–".

The document renders `data` in the order given. Sorting belongs to Task 3.

- [ ] **Step 1: Write the failing tests.** Build `WorkListData` by hand. Read the text with PdfPig as `GermanDocumentTests.ExtractText` does, per page where needed (`document.GetPage(n).Text`). Tests:

```csharp
Each_cemetery_starts_on_its_own_page        // 2 cemeteries, 1 grave each → 2 pages; page 1 text has
                                            // "Waldfriedhof" and not "Bergfriedhof", page 2 the reverse
The_heading_names_list_cemetery_date_and_page // "Pflegeliste 2027", "Waldfriedhof", "Stand: 13.11.2026",
                                            // "Seite 1 von 1"; planting: "Pflanzliste Frühling 2027"
Page_numbers_restart_for_each_cemetery      // cemetery A with 120 graves (≥ 2 pages), then B with 1:
                                            // A's last page has "Seite 2 von 2" or more; B's page "Seite 1 von 1"
The_grave_details_are_printed               // "A-12", "Müller", "27 dGr.i", "04/2015", "12/2030", "4711"
The_position_without_a_position_is_the_field_number // FieldNumber "31", Position null → "31", no "dGr"
A_grave_without_end_prints_a_dash           // End null → "–"
Start_and_end_in_the_period_are_bold        // letters of "11/2027" (StartInPeriod) and "03/2027" (EndInPeriod)
                                            // have a FontName containing "Bold"; "04/2015" letters do not
The_graves_only_variant_has_two_columns     // 2 graves on one page: the first grave's number and the
                                            // last's sit at clearly different X (> 70 mm apart) on the page
A_long_site_name_is_printed_whole           // 60-char SiteName in CareGravesOnly appears whole in the text
                                            // (compare with whitespace removed, wrapping may split it)
The_services_variant_lists_lines_with_amounts // "1 × 1001", "Jahrespflege", "120,00 €";
                                            // "Betrag je Halbjahr" in the heading of the services column
A_seasonal_line_names_its_quarter_on_the_care_list // Quarter Spring → "(Frühling)"; on the planting
                                            // list the same line has no "(Frühling)"
A_line_without_amount_prints_a_dash         // Amount null → "–" and no "0,00 €"
The_planting_list_prints_grave_details_once_per_grave // grave with 2 lines: "A-12" occurs once
The_properties_name_the_list                // Information.Title == "Pflegeliste 2027", Author "Blumen Burkard"
The_same_data_gives_the_same_bytes          // Generate twice → equal byte arrays
```

- [ ] **Step 2: Run them and see them fail.** `scripts/dotnet.sh test Invoicing.sln --filter WorkListDocumentTests` should fail to compile.

- [ ] **Step 3: Implement.**
  - **Pages:** one `container.Page(...)` per cemetery. Wrap each cemetery's content in `.Section($"cemetery-{index}")`, and write the page numbers with `text.PageNumberWithinSection(name)` / `text.TotalPagesWithinSection(name)`.
  - **Header and metadata:** the header is a `page.Header()` with the title and the cemetery on the left and "Stand: dd.MM.yyyy · Seite n von m" on the right. `GetMetadata` sets Title (the list title), Author and Creator "Blumen Burkard", and Creation/ModifiedDate = `MadeOn` at midnight UTC.
  - **CareGravesOnly:** a two-column flow (QuestPDF `MultiColumn` with 2 columns). Each column has the header "Nr. · Grabstätte · Pos. · Beginn · Ende · Vertrag".
  - **CareWithServices:** one column. A detail line per grave: number, site, position, "Beginn …", "Ende …", "Vertrag …" when there is one. Under it, indented, one line per `WorkListLine`: "{Count} × {ItemNumber} {Description}", plus " ({QuarterName})" when `Quarter` is set, and the amount right-aligned. The column heading "Leistung · Betrag je Halbjahr" stands once per page under the page header.
  - **Planting:** a table with the columns "Nr. · Grabstätte · Pos. · Leistung · Betrag · Vertrag · Beginn · Ende". The grave's cells are filled on its first line only.
  - **Bold:** start and end are `.Bold()` when their flag is set.
  - **Sizes:** default font size 9 (8 in the two-column flow if needed to fit).
  - **Entry point:** `WorkListDocuments.Generate` sets the Community license if none is set, as `DocumentService` does.

- [ ] **Step 4: Run the tests and see them pass.** Then run the whole `Invoicing.DocumentTests` project; it must stay green.

- [ ] **Step 5: Commit.** `feat(documents): render the care and planting lists`

---

### Task 3: Gathering the graves, the routes, the spec

**Files:**

- Create: `backend/src/Invoicing.Api/Services/WorkListService.cs`
- Create: `backend/src/Invoicing.Api/OData/Controllers/WorkListController.cs`
- Modify: `backend/src/Invoicing.Api/Program.cs` (register `WorkListService` scoped, next to the other services)
- Modify: `docs/spec.md`
- Test: `backend/tests/Invoicing.IntegrationTests/WorkListTests.cs`

**Interfaces:**

- Consumes: Task 1 (`WorkListPeriod`, `GraveNumberOrder`, `VatInclusivePrice.GrossOf`); Task 2 (`WorkListData` and friends, `WorkListDocuments.Generate`, `DocumentTexts.QuarterName`/`QuarterFileWord`).
- Produces:
  - `public enum CareListServices { None, Care, All }`
  - `public sealed class WorkListService(AppDbContext db, IClock clock)`:
    - `Task<WorkListData?> CareListAsync(int year, CareListServices services)`: null when no grave is on the list.
    - `Task<WorkListData?> PlantingListAsync(int year, BillingQuarter quarter)`: null likewise.
  - Routes as in Global Constraints. 200 is `application/pdf` with `Content-Disposition: attachment` and the file name.

**Rules the service implements (design 4.1–4.3, 4.6):**

- **Graves:** `DeletedAt == null`, cooperative or not.
- **Care list:** `WorkListPeriod.Runs(start, end, Year(y))`.
- **Planting list:** `Runs(start, end, Quarter(y, q))` and at least one link whose `Item.Quarter == q`.
- **Lines:**
  - `None`: no lines (kind `CareGravesOnly`).
  - `Care`: links with `Item.Quarter == null`.
  - `All`: links with `Item.Quarter == null`, plus those with `Item.Quarter = q'` where `Runs(start, end, Quarter(y, q'))`.
  - Planting: links with `Item.Quarter == q`.
  - Always in link-id order.
- **Amount:** `price = link.UnitPrice ?? link.Item.DefaultPrice`; null gives a null amount. Otherwise `GrossOf(Round(price, 2), Item.VatRate, link.PriceIncludesVat) × Count`.
- **Bold flags:**
  - `StartInPeriod = Contains(range, start)`.
  - `EndInPeriod = end is not null && Contains(range, end)`.
  - The range is the year (care list) or the quarter (planting list).
- **Order:** cemeteries by `Name` with `StringComparer.Create(de-DE, ignoreCase: true)`; graves by `GraveNumberOrder.Instance`, then by `StartDate`, so a handed-over grave's old term comes first. Cemeteries without graves are left out.
- **Date:** `MadeOn = DateOnly.FromDateTime(clock.GetLocalNow())`.

**Controller:**

- **`year`:** required, an int from 2000 to 2100.
- **`services`:** required, one of `none|care|all`, ignoring case.
- **`quarter`:** required, an enum name ignoring case. Reject numeric strings, so check `Enum.IsDefined` after parsing.
- **Any failure:** `ValidationFailed` with an English message naming the parameter.
- **Null from the service:** `NoGravesOnList`, args `[year.ToString()]` for the care list and `[year.ToString(), DocumentTexts.QuarterName(q)]` for the planting list. The English message is "No grave is on this list."

- [ ] **Step 1: Write the failing tests.** For speed, seed graves straight through `AppDbContext` from `host.Services.CreateScope()`: `Grave` with `CemeteryId`, `Position`, `StartDate`/`EndDate` on the first of the month, `Items` linked to `InvoiceItem`s. The cemetery is `TestCemetery.Id`, plus one more named "Altfriedhof" for ordering. Call the service from a scope for selection tests, and use `host.CreateClient()` for the HTTP tests. Every test gets its own `new ApiTestHost()`. Tests:

```csharp
The_care_list_holds_the_graves_running_in_the_year
  // year 2027: ends 2027-01 in; starts 2027-12 in; ended 2026-12 out; starts 2028-01 out;
  // no end in; deleted out; cooperative in (with its ContractNumber carried)
The_planting_list_holds_graves_with_a_service_of_the_quarter
  // Spring 2027: grave with spring item running 2027-03..: in; grave with only autumn item: out;
  // grave with spring item that ended 2026-12: out; grave ending 2027-01 with spring item: in
Cemeteries_and_graves_are_in_order
  // "Bergfriedhof" and "Altfriedhof": Altfriedhof first; graves "A-10","A-2" → "A-2","A-10";
  // same number handed over (ends 2027-05, new starts 2027-06): old first
The_lines_follow_the_variant
  // grave with care item (no quarter), spring item, autumn item, ending 2027-03:
  // None → 0 lines; Care → care only; All → care + spring (not autumn); planting Spring → spring only
Amounts_are_count_times_gross
  // link UnitPrice 45.00 net @7 %, Count 2 → 96.30; gross link 48.15 incl. → 48.15;
  // no link price, item default 10.00 @19 % → 11.90
A_service_without_any_price_has_no_amount // link and item without price → Amount null
Start_and_end_in_the_period_are_flagged
  // care 2027: start 2027-11 → StartInPeriod; end 2027-03 → EndInPeriod; start 2015 → not
  // planting Spring 2027: end 2027-05 → not EndInPeriod (outside the quarter)
An_empty_list_is_refused
  // GET /odata/CareList?year=2000&services=none → 400, code NoGravesOnList, args ["2000"];
  // GET /odata/PlantingList?year=2000&quarter=Spring → args ["2000","Frühling"]
The_lists_download_as_pdf_with_their_names
  // with one grave: 200, application/pdf, body starts with "%PDF",
  // Content-Disposition file name "Pflegeliste-2027.pdf" / "Pflanzliste-2027-Fruehling.pdf"
Bad_parameters_are_refused
  // year missing, year=abc, year=1999, year=2101, services missing, services=some,
  // quarter=5, quarter=Fall → 400 ValidationFailed; quarter=spring → not 400 ValidationFailed
```

- [ ] **Step 2: Run them and see them fail.** `scripts/dotnet.sh test Invoicing.sln --filter WorkListTests`

- [ ] **Step 3: Implement the service, the controller and the registration.**

- [ ] **Step 4: Run the tests and see them pass.** Then run the whole solution: `scripts/dotnet.sh test Invoicing.sln`, all green.

- [ ] **Step 5: Update `docs/spec.md`.**
  - Add **User Story 17 – Print work lists (Priority: P3)** after User Story 16, with acceptance scenarios:
    - the care list for a year in its three variants;
    - the planting list for a quarter;
    - a cemetery per page;
    - start and end in bold;
    - the refusal for an empty list.
  - Add an edge-case bullet for each of these:
    - a grave without a position;
    - a service without a price;
    - a grave starting or ending inside the period;
    - a grave handed over inside the period;
    - a cooperative grave without services.
  - Add a new requirements group **Work lists** after **Quotes**' free-text subsection, FR-102 to FR-108, one requirement per design section:
    - FR-102: which graves (4.1)
    - FR-103: care and extra services (4.2)
    - FR-104: amounts (4.3)
    - FR-105: the care list (4.4)
    - FR-106: the planting list (4.5)
    - FR-107: common rules (4.6)
    - FR-108: the page and the API (4.7, 4.8)

  Write it in the spec's style: "The system MUST …".

- [ ] **Step 6: Commit.** `feat(api): download the care and planting lists`. Include `docs/spec.md` in this commit, or commit it separately as `docs(spec): …`.

---

### Task 4: The page "Listen" and its E2E spec

**Files:**

- Create: `frontend/src/pages/ListsPage.vue`
- Modify: `frontend/src/router.ts` (route `/lists`, `meta.titleKey: "menuLists"`, above the "add new routes" comment)
- Modify: `frontend/src/components/NavLinks.vue` (entry `menuLists`, icon `pi pi-list`, right after `menuInactiveGraves`)
- Modify: `frontend/src/api/endpoints.ts`
- Modify: `frontend/src/i18n/de.json`
- Create: `e2e/tests/lists.spec.ts`

**Interfaces:**

- Consumes: the routes and refusals of Task 3.
- Produces, in `endpoints.ts`:
  - `downloadCareList(year: number, services: "none" | "care" | "all"): Promise<{ blob: Blob; fileName: string }>`
  - `downloadPlantingList(year: number, quarter: "Spring" | "Summer" | "Autumn" | "Winter"): Promise<{ blob: Blob; fileName: string }>`

  Both read the file name from `Content-Disposition`, as `downloadBackup` does. Pull that parsing into one shared helper used by all three. A failed request throws `ApiError`, as `getBlob` does.

**Page** (template: `ImportExportPage.vue`, with `PageHeader` and `Card`s):

- **Card "Pflegeliste":**
  - year: `DatePicker view="year" date-format="yy"`, as on `GravesPage.vue`;
  - `SelectButton` "Nur Gräber" / "Mit Pflege-Leistungen" / "Mit allen Leistungen";
  - button "PDF herunterladen" (`pi pi-download`, `:loading`).
- **Card "Pflanzliste":**
  - year;
  - `SelectButton` over the quarters, with the labels from the existing `quarterSpring` … `quarterWinter` keys;
  - the same button.
- **Defaults:** the current year, the current quarter (from the month), variant "Nur Gräber".
- **Errors:** go to `useErrorToast().showError`. Success calls `downloadBlob(blob, fileName)`.
- **Labels on the controls:** give each control an accessible name, so the E2E spec can find it by role. The year pickers are "Jahr der Pflegeliste" / "Jahr der Pflanzliste", the variant group "Umfang", the quarter group "Quartal". Each button gets an `aria-label` naming its list.

**i18n keys** (German text exactly):

- `menuLists` "Listen"
- `careList` "Pflegeliste"
- `plantingList` "Pflanzliste"
- `careListHint` "Alle Gräber, die im gewählten Jahr laufen, je Friedhof ab einer neuen Seite."
- `plantingListHint` "Alle Gräber mit einer Zusatzleistung im gewählten Quartal."
- `careListServicesNone` "Nur Gräber", `careListServicesCare` "Mit Pflege-Leistungen", `careListServicesAll` "Mit allen Leistungen"
- `downloadPdf` "PDF herunterladen" (reuse it if it already exists)
- `errorNoGravesOnList1` "Für {0} gibt es keine Gräber."
- `errorNoGravesOnList2` "Für {1} {0} gibt es keine Gräber mit Zusatzleistungen."

**E2E** (`e2e/tests/lists.spec.ts`; seed through `e2e/seed.ts`, names from `unique()`):

- **The care list downloads:**
  - seed one grave running this year (`seedGrave`);
  - go to `/lists`;
  - click the care list's download button;
  - `page.waitForEvent("download")` gives the suggested name `Pflegeliste-<this year>.pdf`;
  - the saved file starts with `%PDF`.
- **The planting list downloads:**
  - seed an item with `Quarter` = the current quarter;
  - seed a grave billing it, running through this year;
  - the download is named `Pflanzliste-<year>-<Fruehling|Sommer|Herbst|Winter>.pdf`, the word matching the current quarter.
- **An empty list says so:**
  - set the care list's year to 2000;
  - download;
  - the toast reads "Für 2000 gibt es keine Gräber.";
  - for the planting list in 2000 with "Frühling" selected: "Für Frühling 2000 gibt es keine Gräber mit Zusatzleistungen.".
  - Use 2000, not a year far out: graves without an end are on every later year, and the specs share one stack.
- **Driving the controls:** use `e2e/vue.ts` helpers wherever they apply. Wait for the overlay of a DatePicker as other specs do. If changing the year through the picker is awkward, type it into the input and blur, the way `GravesPage` specs do.
- **Other spec files:** if `tests/layout.spec.ts` iterates over every page route, add `/lists` there.

- [ ] **Step 1: Write the E2E spec** (`lists.spec.ts`) as above.
- [ ] **Step 2: Run it and see it fail.** `cd e2e && npx playwright test tests/lists.spec.ts --project=desktop` (via `npm test -- tests/lists.spec.ts --project=desktop`, which starts the E2E stack) should fail because there is no page.
- [ ] **Step 3: Implement** the endpoints, the page, the route, the menu entry and the i18n keys.
- [ ] **Step 4: Verify the frontend.** `cd frontend && npm run typecheck && npm run lint && npm run build` must all be clean. Then run the spec again; it must pass. Then run the full E2E suite (`cd e2e && npm test`); it must be green.
- [ ] **Step 5: Commit.** `feat(frontend): a page to download the care and planting lists`
