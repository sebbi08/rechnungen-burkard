# Quarter Items, Prorated Half-Years and a Live Billing Preview — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Items carry an optional quarter, a grave's first and last half-year bill all-year items pro rata, quarter items appear only in the invoices whose months touch their quarter, every invoice a grave's term produces must split evenly across at most four recipients, and the grave editor shows those invoices live with per-line price suggestions.

**Architecture:** One pure rule in `Invoicing.Domain` (`HalfYearBilling` for periods and billed lines, `GraveBillingCheck` for the example invoices, their split check and the suggestions). `SaveGrave`, the import, generation and a new `PreviewGraveBilling` action all call it; `ValidateGraveBilling` stays untouched for the grave invoice editor. The Vue grave editor calls `PreviewGraveBilling` debounced and renders the result in a new `GraveBillingPreview` component; `LineEditor` shows rate and quarter under the item and offers the suggestions.

**Tech Stack:** .NET 10, EF Core (SQLite), ASP.NET Core OData, xUnit; Vue 3, PrimeVue 4, TanStack Query, vue-i18n; Playwright.

**Spec:** `docs/superpowers/specs/2026-10-05-grave-quarters-and-prorated-billing-design.md`

## Global Constraints

- Quarters: `Spring` = Jan–Mar, `Summer` = Apr–Jun (H1), `Autumn` = Jul–Sep, `Winter` = Oct–Dec (H2). An item has one quarter or none (none = all year).
- An all-year item's price is per half-year; a partly covered half-year bills `round(entered × n / 6, 2, AwayFromZero)` with `n` = months of the half-year inside the term, start and end month counted whole.
- Description suffix, verbatim: `", anteilig MM–MM/YYYY (n von 6 Monaten)"`, or `", anteilig MM/YYYY (1 von 6 Monaten)"` for one month. The dash is U+2013.
- A quarter item is billed at its full price when the invoice's months touch its quarter, never prorated, never suffixed.
- At most 4 recipients per grave (`TooManyRecipients`).
- `Quarter` is stored by name (`HasConversion<string>()`), folded into the unreleased migration `20261002083107_GraveSiteFieldsAndGermanOnly` — no new migration.
- Error codes: `QuarterImmutable`, `TooManyRecipients`, `QuarterInvalid`, skip reasons `NothingToBill`, `OutsideTerm`; import message keys `PricesDoNotDivideIn`, `PriceSuggestion1`, `PriceSuggestion2`.
- Suggestions search ±400 cents in cent steps, holding every other line fixed; they are in the unit the price was entered in.
- UI texts are German and live in `frontend/src/i18n/de.json`; C# messages are English except the document suffix and the example labels ("Anfang H1 2026", "Anfang und Ende H1 2026", "Ende H2 2030", "Volles H1", "Volles H2").
- Never run E2E or probes against the dev stack on port 8080 (see `CLAUDE.md`).
- Commits follow Conventional Commits; lint-staged formats on commit (CSharpier for `backend/`, Prettier elsewhere).

## Review Focus

1. **A grave saved before this change whose start invoice no longer divides** — editing it must show the failing card and refuse the save, not crash; generation must skip it with `PricesDoNotDivide`. Pinned in Task 4 (`Saving_refuses_a_grave_whose_start_invoice_does_not_divide`).
2. **A term entirely inside one half-year (start and end in the same half)** — exactly one `StartAndEnd` invoice, prorated on both sides. Pinned in Task 2 (`Examples_*` theory) and Task 6 (`Generation_prorates_a_term_inside_one_half_year`).
3. **An end month before the start month while typing** — the editor must not show "Prüfung gerade nicht möglich"; it sends no preview, and the save says `DateRangeInvalid`. Pinned in Task 5 (`Preview_refuses_an_end_before_the_start`) and Task 11 (preview input is null).
4. **A half-year that bills nothing (only quarter items, none touched)** — generation skips with `NothingToBill`; the preview card says so instead of rendering an empty table. Pinned in Task 6 (`A_half_year_with_nothing_to_bill_is_skipped`) and Task 10 (empty-card text).
5. **Seeded test graves with a day in their end date (`2040-12-31`)** — month arithmetic must treat them as their month. Pinned in Task 2 (`PeriodIn_ignores_the_day`).

---

## How to run things

- **Backend tests** (from the repository root). The dev container shares `backend/obj`, so:

  ```sh
  docker compose stop backend 2>/dev/null || true
  find backend -type d \( -name bin -o -name obj \) -prune -exec rm -rf {} +
  scripts/dotnet.sh test Invoicing.sln --filter "FullyQualifiedName~<TestClass>"
  ```

  Delete `bin`/`obj` only when you see `NETSDK1064`; repeated runs within one task do not need it.
  Afterwards `docker compose start backend` if the dev stack was running.

- **Frontend** (in `frontend/`): `npm run typecheck && npm run lint && npm run build`.
- **E2E** (in `e2e/`): `npm test -- tests/graves.spec.ts --project=desktop`. It builds and starts its own stack on 8081.
- **OData client** after an EDM change: from the repo root `docker compose -f docker-compose.e2e.yml -p invoicing-e2e up -d --build`, wait until `curl -sf http://localhost:8081/odata/\$metadata` answers, run `cd frontend && npm run gen:odata`, then `cd e2e && npm run stack:down`.

---

### Task 1: Items carry a quarter

**Files:**

- Create: `backend/src/Invoicing.Domain/Entities/BillingQuarter.cs`
- Modify: `backend/src/Invoicing.Domain/Entities/InvoiceItem.cs`
- Modify: `backend/src/Invoicing.Api/Persistence/AppDbContext.cs:66`
- Modify: `backend/src/Invoicing.Api/Migrations/20261002083107_GraveSiteFieldsAndGermanOnly.cs` (Up and Down)
- Modify: `backend/src/Invoicing.Api/Migrations/20261002083107_GraveSiteFieldsAndGermanOnly.Designer.cs` (InvoiceItem entity)
- Modify: `backend/src/Invoicing.Api/Migrations/AppDbContextModelSnapshot.cs:278-304`
- Modify: `backend/src/Invoicing.Api/OData/Controllers/InvoiceItemsController.cs` (Patch)
- Modify: `backend/src/Invoicing.Api/OData/Controllers/GravesController.cs` (`Get(key)` payload)
- Test: `backend/tests/Invoicing.IntegrationTests/InvoiceItemTests.cs`

**Interfaces:**

- Produces: `enum BillingQuarter { Spring = 1, Summer = 2, Autumn = 3, Winter = 4 }` in `Burkard.Invoicing.Domain.Entities`; `InvoiceItem.Quarter : BillingQuarter?`; OData serializes it as its name (`"Summer"`); `Graves(id)` returns `Items[].Item.Quarter` as a name or null; PATCH answers `QuarterImmutable`.

- [ ] **Step 1: Write the failing tests** — append to `InvoiceItemTests.cs`, next to `Patch_vat_rate_of_a_linked_item_is_rejected`, reusing its helpers (`SeedVatRateAsync`, `CreateItemAsync`, `LinkGraveAsync`, `PatchAsync`, `GetCreatedIdAsync`):

```csharp
    [Fact]
    public async Task An_item_keeps_its_quarter_by_name()
    {
        using var client = _host.CreateClient();
        await SeedVatRateAsync(client);
        var created = await client.PostAsync(
            "/odata/InvoiceItems",
            ODataJson.Body(
                new
                {
                    Number = "3101",
                    Description = "Sommerbepflanzung",
                    VatRate = 7.0m,
                    DefaultPrice = 40.00m,
                    Quarter = "Summer",
                }
            )
        );
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = await GetCreatedIdAsync(created);

        using var json = await ODataJson.ReadAsync(await client.GetAsync($"/odata/InvoiceItems({id})"));
        Assert.Equal("Summer", json.RootElement.GetProperty("Quarter").GetString());
    }

    [Fact]
    public async Task An_item_without_a_quarter_is_billed_all_year()
    {
        using var client = _host.CreateClient();
        await SeedVatRateAsync(client);
        var id = await CreateItemAsync(client, "3102", 60.00m);

        using var json = await ODataJson.ReadAsync(await client.GetAsync($"/odata/InvoiceItems({id})"));
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("Quarter").ValueKind);
    }

    // Like the VAT rate: the quarter decides which invoices carry the item, for
    // every linked grave at once, so a split that divides today could stop.
    [Fact]
    public async Task Patch_quarter_of_a_linked_item_is_rejected()
    {
        using var client = _host.CreateClient();
        await SeedVatRateAsync(client);
        var itemId = await CreateItemAsync(client, "3103", 100.00m);
        await LinkGraveAsync(client, itemId, "I-3103", null);

        var response = await PatchAsync(client, itemId, new { Quarter = "Spring" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        Assert.Equal(
            "QuarterImmutable",
            json.RootElement.GetProperty("error").GetProperty("code").GetString()
        );
    }

    [Fact]
    public async Task Patch_quarter_of_an_unlinked_item_is_allowed()
    {
        using var client = _host.CreateClient();
        await SeedVatRateAsync(client);
        var itemId = await CreateItemAsync(client, "3104", 100.00m);

        var response = await PatchAsync(client, itemId, new { Quarter = "Autumn" });

        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
    }
```

Add `using System.Text.Json;` at the top if it is missing. Check `CreateItemAsync`'s VAT rate matches what `SeedVatRateAsync` configures (read both helpers first; the existing tests use 7 %).

- [ ] **Step 2: Run them, expect failure** — `scripts/dotnet.sh test Invoicing.sln --filter "FullyQualifiedName~InvoiceItemTests"`. Expected: the four new tests fail (unknown property `Quarter` / missing code).

- [ ] **Step 3: Add the enum and the property**

`backend/src/Invoicing.Domain/Entities/BillingQuarter.cs`:

```csharp
namespace Burkard.Invoicing.Domain.Entities;

/// <summary>The quarter an item is billed in, in calendar order: Spring is
/// January to March and Summer April to June (both in the first half-year),
/// Autumn July to September and Winter October to December (both in the
/// second). An item without one is billed all year. Stored by name, like
/// <see cref="GravePosition"/>, so the members can be reordered.</summary>
public enum BillingQuarter
{
    Spring = 1,
    Summer = 2,
    Autumn = 3,
    Winter = 4,
}
```

`InvoiceItem.cs`, after `DefaultPrice`:

```csharp
    /// <summary>The quarter this item is billed in; null bills it all year.
    /// Fixed once a grave bills the item, like <see cref="VatRate"/>.</summary>
    public BillingQuarter? Quarter { get; set; }
```

`AppDbContext.cs`, next to the `Grave.Position` conversion:

```csharp
        mb.Entity<InvoiceItem>().Property(i => i.Quarter).HasConversion<string>();
```

- [ ] **Step 4: Fold the column into the unreleased migration**

In `20261002083107_GraveSiteFieldsAndGermanOnly.cs`, `Up`, after the `SiteName` column and before the `UPDATE Graves` SQL:

```csharp
            migrationBuilder.AddColumn<string>(
                name: "Quarter",
                table: "InvoiceItems",
                type: "TEXT",
                nullable: true
            );
```

In `Down`, first statement:

```csharp
            migrationBuilder.DropColumn(name: "Quarter", table: "InvoiceItems");
```

In both `20261002083107_GraveSiteFieldsAndGermanOnly.Designer.cs` and `AppDbContextModelSnapshot.cs`, inside the `Burkard.Invoicing.Domain.Entities.InvoiceItem` entity, between `Number` and `VatRate` (properties are alphabetical):

```csharp
                    b.Property<string>("Quarter")
                        .HasColumnType("TEXT");
```

- [ ] **Step 5: Lock the quarter in `InvoiceItemsController.Patch`** — capture `var previousQuarter = item.Quarter;` next to `previousRate`, and after the `VatRateImmutable` block add:

```csharp
        // The quarter decides which half-year invoices carry the item. Read
        // from the item at generation like the rate, so changing it would move
        // every linked grave's invoices at once.
        if (linked && item.Quarter != previousQuarter)
        {
            return BadRequest(
                new
                {
                    error = new
                    {
                        code = "QuarterImmutable",
                        message = "The quarter of an item that graves bill cannot be changed.",
                    },
                }
            );
        }
```

- [ ] **Step 6: Expose the quarter on `Graves(id)`** — in `GravesController.Get(long key)`, the anonymous `Item = new { … }` gains `Quarter = i.Item.Quarter?.ToString(),` (by name, like `Position`).

- [ ] **Step 7: Run the tests, expect them to pass** — same filter; then the whole suite once (`scripts/dotnet.sh test Invoicing.sln`) to catch migration-related failures (`GraveMonthMigrationTests`, `SeedTests`). Expected: all green.

- [ ] **Step 8: Commit**

```sh
git add backend/src backend/tests
git commit -m "feat(items): give an item an optional billing quarter, fixed once a grave bills it"
```

---

### Task 2: The half-year rule

**Files:**

- Create: `backend/src/Invoicing.Domain/HalfYearBilling.cs`
- Test: `backend/tests/Invoicing.IntegrationTests/HalfYearBillingTests.cs`

**Interfaces:**

- Consumes: `GraveLine` (`Invoicing.Domain/GraveInvoiceSplitter.cs`), `VatInclusivePrice.NetOf(decimal price, decimal vatRate, bool priceIncludesVat)`, `BillingQuarter` (Task 1).
- Produces (namespace `Burkard.Invoicing.Domain`):
  - `record BillableLink(long ItemId, string ItemNumber, string Description, decimal VatRate, BillingQuarter? Quarter, int Count, decimal EnteredPrice, bool PriceIncludesVat)`
  - `record BillingPeriod(int? Year, int Half, int FirstMonth, int LastMonth)` with `int Months`, `bool IsFull`, `static BillingPeriod Full(int half)`
  - `enum BillingExampleKind { Start, StartAndEnd, FullFirstHalf, FullSecondHalf, End }`
  - `record BillingExample(BillingExampleKind Kind, BillingPeriod Period)`
  - `record BilledLine(int LinkIndex, decimal Price, GraveLine Line)` — `Price` is the (prorated) entered price
  - `static class HalfYearBilling`: `BillingPeriod? PeriodIn(DateOnly start, DateOnly? end, int year, int half)`, `IReadOnlyList<BilledLine> Bill(IReadOnlyList<BillableLink>, BillingPeriod)`, `IReadOnlyList<GraveLine> Lines(IReadOnlyList<BillableLink>, BillingPeriod)`, `bool Touches(BillingQuarter, BillingPeriod)`, `IReadOnlyList<BillingExample> Examples(DateOnly? start, DateOnly? end)`, `string Label(BillingExample)`, `string ProrationNote(BillingPeriod)`

- [ ] **Step 1: Write the failing tests** — `HalfYearBillingTests.cs`:

```csharp
using System;
using System.Linq;
using Burkard.Invoicing.Domain;
using Burkard.Invoicing.Domain.Entities;
using Xunit;

namespace Burkard.Invoicing.IntegrationTests;

/// <summary>Which months of a half-year a grave's term covers, and what that
/// bills: all-year items pro rata, quarter items in full when their quarter is
/// touched.</summary>
public class HalfYearBillingTests
{
    private static DateOnly M(int year, int month) => new(year, month, 1);

    private static BillableLink Care(decimal price = 60m, bool gross = false) =>
        new(1, "100", "Grabpflege", 19m, null, 1, price, gross);

    private static BillableLink Seasonal(BillingQuarter quarter, decimal price = 40m) =>
        new(2, "210", $"Bepflanzung {quarter}", 19m, quarter, 1, price, false);

    [Fact]
    public void PeriodIn_cuts_the_first_half_year_at_the_start()
    {
        Assert.Equal(new BillingPeriod(2026, 1, 5, 6), HalfYearBilling.PeriodIn(M(2026, 5), null, 2026, 1));
        Assert.Equal(new BillingPeriod(2026, 2, 7, 12), HalfYearBilling.PeriodIn(M(2026, 5), null, 2026, 2));
        Assert.Null(HalfYearBilling.PeriodIn(M(2026, 5), null, 2025, 2));
    }

    [Fact]
    public void PeriodIn_cuts_the_last_half_year_at_the_end()
    {
        Assert.Equal(new BillingPeriod(2030, 2, 7, 8), HalfYearBilling.PeriodIn(M(2026, 5), M(2030, 8), 2030, 2));
        Assert.Null(HalfYearBilling.PeriodIn(M(2026, 5), M(2030, 8), 2031, 1));
    }

    [Fact]
    public void PeriodIn_ignores_the_day()
    {
        // tests seed ends like 2040-12-31; the term is counted in months
        Assert.Equal(
            new BillingPeriod(2040, 2, 7, 12),
            HalfYearBilling.PeriodIn(new DateOnly(2020, 1, 1), new DateOnly(2040, 12, 31), 2040, 2)
        );
        Assert.Equal(
            new BillingPeriod(2026, 1, 6, 6),
            HalfYearBilling.PeriodIn(new DateOnly(2026, 6, 15), null, 2026, 1)
        );
    }

    [Fact]
    public void An_all_year_item_is_prorated_with_a_note()
    {
        var line = Assert.Single(HalfYearBilling.Lines([Care()], new BillingPeriod(2026, 1, 5, 6)));

        Assert.Equal(20.00m, line.UnitPrice);
        Assert.Equal("Grabpflege, anteilig 05–06/2026 (2 von 6 Monaten)", line.Description);
        Assert.Equal(1, line.Count);
    }

    [Fact]
    public void A_single_month_is_named_once()
    {
        var line = Assert.Single(HalfYearBilling.Lines([Care()], new BillingPeriod(2026, 1, 6, 6)));

        Assert.Equal(10.00m, line.UnitPrice);
        Assert.Equal("Grabpflege, anteilig 06/2026 (1 von 6 Monaten)", line.Description);
    }

    [Fact]
    public void A_full_half_year_bills_the_price_without_a_note()
    {
        var line = Assert.Single(HalfYearBilling.Lines([Care()], BillingPeriod.Full(2)));

        Assert.Equal(60.00m, line.UnitPrice);
        Assert.Equal("Grabpflege", line.Description);
    }

    [Fact]
    public void A_prorated_price_is_rounded_to_the_cent()
    {
        // 50,00 over five months is 41,666…
        var line = Assert.Single(HalfYearBilling.Lines([Care(50m)], new BillingPeriod(2026, 1, 2, 6)));
        Assert.Equal(41.67m, line.UnitPrice);
    }

    [Fact]
    public void A_gross_price_is_prorated_as_a_gross()
    {
        var billed = Assert.Single(HalfYearBilling.Bill([Care(119m, gross: true)], new BillingPeriod(2026, 1, 4, 6)));

        Assert.Equal(59.50m, billed.Price);
        Assert.Equal(59.50m, billed.Line.EnteredGross);
        Assert.Equal(VatInclusivePrice.NetOf(59.50m, 19m, true), billed.Line.UnitPrice);
    }

    [Theory]
    [InlineData(BillingQuarter.Summer, 5, true)] // May/June touch Apr–Jun
    [InlineData(BillingQuarter.Spring, 5, false)] // Jan–Mar is over by May
    [InlineData(BillingQuarter.Spring, 3, true)] // March is enough
    [InlineData(BillingQuarter.Autumn, 1, false)] // the other half-year
    public void A_quarter_item_is_billed_in_full_when_its_quarter_is_touched(
        BillingQuarter quarter,
        int startMonth,
        bool billed
    )
    {
        var lines = HalfYearBilling.Lines([Seasonal(quarter)], new BillingPeriod(2026, 1, startMonth, 6));

        if (billed)
        {
            var line = Assert.Single(lines);
            Assert.Equal(40.00m, line.UnitPrice);
            Assert.Equal($"Bepflanzung {quarter}", line.Description);
        }
        else
        {
            Assert.Empty(lines);
        }
    }

    [Fact]
    public void The_end_half_year_bills_the_quarters_it_touches()
    {
        var period = HalfYearBilling.PeriodIn(M(2026, 5), M(2030, 8), 2030, 2)!;
        var lines = HalfYearBilling.Bill(
            [Care(), Seasonal(BillingQuarter.Autumn, 30m), Seasonal(BillingQuarter.Winter, 25m)],
            period
        );

        Assert.Equal(new[] { 0, 1 }, lines.Select(l => l.LinkIndex));
        Assert.Equal(new[] { 20.00m, 30.00m }, lines.Select(l => l.Line.UnitPrice));
    }

    [Fact]
    public void Without_a_start_only_the_full_half_years_are_examples()
    {
        Assert.Equal(
            new[] { BillingExampleKind.FullFirstHalf, BillingExampleKind.FullSecondHalf },
            HalfYearBilling.Examples(null, null).Select(e => e.Kind)
        );
    }

    [Fact]
    public void Examples_of_an_open_ended_term()
    {
        var examples = HalfYearBilling.Examples(M(2026, 5), null);

        Assert.Equal(
            new[] { BillingExampleKind.Start, BillingExampleKind.FullFirstHalf, BillingExampleKind.FullSecondHalf },
            examples.Select(e => e.Kind)
        );
        Assert.Equal(new BillingPeriod(2026, 1, 5, 6), examples[0].Period);
    }

    [Fact]
    public void Examples_of_a_term_with_an_end()
    {
        var examples = HalfYearBilling.Examples(M(2026, 5), M(2030, 8));

        Assert.Equal(
            new[]
            {
                BillingExampleKind.Start,
                BillingExampleKind.FullFirstHalf,
                BillingExampleKind.FullSecondHalf,
                BillingExampleKind.End,
            },
            examples.Select(e => e.Kind)
        );
        Assert.Equal(new BillingPeriod(2030, 2, 7, 8), examples[3].Period);
    }

    [Fact]
    public void Examples_of_a_term_without_a_full_half_year()
    {
        var examples = HalfYearBilling.Examples(M(2026, 5), M(2026, 8));

        Assert.Equal(new[] { BillingExampleKind.Start, BillingExampleKind.End }, examples.Select(e => e.Kind));
    }

    [Fact]
    public void Examples_of_a_term_inside_one_half_year()
    {
        var example = Assert.Single(HalfYearBilling.Examples(M(2026, 2), M(2026, 4)));

        Assert.Equal(BillingExampleKind.StartAndEnd, example.Kind);
        Assert.Equal(new BillingPeriod(2026, 1, 2, 4), example.Period);
    }

    [Fact]
    public void Examples_list_only_the_full_half_years_that_occur()
    {
        // H2 2026 lies entirely inside, no H1 does
        var examples = HalfYearBilling.Examples(M(2026, 7), M(2027, 3));

        Assert.Equal(
            new[] { BillingExampleKind.Start, BillingExampleKind.FullSecondHalf, BillingExampleKind.End },
            examples.Select(e => e.Kind)
        );
        Assert.True(examples[0].Period.IsFull);
    }

    [Fact]
    public void Labels_name_the_invoice()
    {
        var examples = HalfYearBilling.Examples(M(2026, 5), M(2030, 8));

        Assert.Equal(
            new[] { "Anfang H1 2026", "Volles H1", "Volles H2", "Ende H2 2030" },
            examples.Select(HalfYearBilling.Label)
        );
        Assert.Equal(
            "Anfang und Ende H1 2026",
            HalfYearBilling.Label(HalfYearBilling.Examples(M(2026, 2), M(2026, 4))[0])
        );
    }

    [Fact]
    public void An_end_before_the_start_is_refused()
    {
        Assert.Throws<ArgumentException>(() => HalfYearBilling.Examples(M(2026, 5), M(2026, 4)));
    }
}
```

- [ ] **Step 2: Run them, expect a compile failure** — `--filter "FullyQualifiedName~HalfYearBillingTests"`. Expected: build error, `HalfYearBilling` not found.

- [ ] **Step 3: Implement** `backend/src/Invoicing.Domain/HalfYearBilling.cs`:

```csharp
using System.Globalization;
using Burkard.Invoicing.Domain.Entities;

namespace Burkard.Invoicing.Domain;

/// <summary>One of a grave's item links as the half-year rule sees it: the
/// item's facts, the grave's count, and the price billed for a full half-year
/// in the unit it was entered in — the gross when <paramref name="PriceIncludesVat"/>.</summary>
public sealed record BillableLink(
    long ItemId,
    string ItemNumber,
    string Description,
    decimal VatRate,
    BillingQuarter? Quarter,
    int Count,
    decimal EnteredPrice,
    bool PriceIncludesVat
);

/// <summary>The months of one half-year an invoice covers, both ends inclusive,
/// as month numbers 1–12. <see cref="Year"/> is null for the generic full
/// half-years the editor previews; a prorated period always has one.</summary>
public sealed record BillingPeriod(int? Year, int Half, int FirstMonth, int LastMonth)
{
    public int Months => LastMonth - FirstMonth + 1;

    public bool IsFull => Months == 6;

    public static BillingPeriod Full(int half) =>
        half == 1 ? new(null, 1, 1, 6) : new(null, 2, 7, 12);
}

public enum BillingExampleKind
{
    Start,
    StartAndEnd,
    FullFirstHalf,
    FullSecondHalf,
    End,
}

/// <summary>One of the invoices a grave's term produces.</summary>
public sealed record BillingExample(BillingExampleKind Kind, BillingPeriod Period);

/// <summary>A billed line, the link it came from, and its price in the unit it
/// was entered in (prorated where the period is).</summary>
public sealed record BilledLine(int LinkIndex, decimal Price, GraveLine Line);

/// <summary>What a grave bills for one half-year.
///
/// A grave's term is counted in months. Only the months of the half-year that
/// fall inside it are billed: an all-year item at price × n ÷ 6, a quarter item
/// in full when one of its months is among them, and nothing at all when none
/// is. Saving a grave, the import, the editor's preview and generation all ask
/// here, so they cannot disagree about what a half-year costs.</summary>
public static class HalfYearBilling
{
    /// <summary>The months of the half-year inside the term, or null when there
    /// are none. Days are ignored: a term runs from its start month through its
    /// end month.</summary>
    public static BillingPeriod? PeriodIn(DateOnly start, DateOnly? end, int year, int half)
    {
        var halfFirst = year * 12 + (half == 1 ? 0 : 6);
        var halfLast = halfFirst + 5;
        var first = Math.Max(Index(start), halfFirst);
        var last = Math.Min(end is { } until ? Index(until) : int.MaxValue, halfLast);
        return first > last
            ? null
            : new BillingPeriod(year, half, first % 12 + 1, last % 12 + 1);
    }

    /// <summary>The lines billed for a period, each with the index of the link
    /// it came from. Links billed nothing in the period are left out.</summary>
    public static IReadOnlyList<BilledLine> Bill(
        IReadOnlyList<BillableLink> links,
        BillingPeriod period
    )
    {
        var billed = new List<BilledLine>(links.Count);
        for (var index = 0; index < links.Count; index++)
        {
            var link = links[index];
            if (link.Quarter is { } quarter)
            {
                // a quarter's work is done or not: in full once it is touched
                if (Touches(quarter, period))
                {
                    billed.Add(Billed(index, link, link.EnteredPrice, link.Description));
                }

                continue;
            }

            if (period.IsFull)
            {
                billed.Add(Billed(index, link, link.EnteredPrice, link.Description));
                continue;
            }

            var prorated = decimal.Round(
                link.EnteredPrice * period.Months / 6m,
                2,
                MidpointRounding.AwayFromZero
            );
            billed.Add(Billed(index, link, prorated, link.Description + ProrationNote(period)));
        }

        return billed;
    }

    public static IReadOnlyList<GraveLine> Lines(
        IReadOnlyList<BillableLink> links,
        BillingPeriod period
    ) => Bill(links, period).Select(b => b.Line).ToList();

    /// <summary>Whether at least one month of the quarter is in the period.</summary>
    public static bool Touches(BillingQuarter quarter, BillingPeriod period)
    {
        var first = ((int)quarter - 1) * 3 + 1;
        return first <= period.LastMonth && period.FirstMonth <= first + 2;
    }

    /// <summary>The invoices the term produces, in order: the start (or the
    /// start and end in one), a full H1 and a full H2 where one lies entirely
    /// inside the term, and the end. Without a start, only the two full ones.</summary>
    public static IReadOnlyList<BillingExample> Examples(DateOnly? start, DateOnly? end)
    {
        var fullFirst = new BillingExample(BillingExampleKind.FullFirstHalf, BillingPeriod.Full(1));
        var fullSecond = new BillingExample(BillingExampleKind.FullSecondHalf, BillingPeriod.Full(2));
        if (start is not { } from)
        {
            return [fullFirst, fullSecond];
        }

        if (end is { } until && Index(until) < Index(from))
        {
            throw new ArgumentException("The end month must not be before the start month.", nameof(end));
        }

        var (startYear, startHalf) = HalfOf(from);
        (int Year, int Half)? endHalf = end is { } last ? HalfOf(last) : null;
        var sameHalf = endHalf == (startYear, startHalf);

        var examples = new List<BillingExample>
        {
            new(
                sameHalf ? BillingExampleKind.StartAndEnd : BillingExampleKind.Start,
                PeriodIn(from, end, startYear, startHalf)!
            ),
        };

        var (hasFirst, hasSecond) = FullHalves(from, end);
        if (hasFirst)
        {
            examples.Add(fullFirst);
        }

        if (hasSecond)
        {
            examples.Add(fullSecond);
        }

        if (endHalf is { } final && !sameHalf)
        {
            examples.Add(
                new(BillingExampleKind.End, PeriodIn(from, end, final.Year, final.Half)!)
            );
        }

        return examples;
    }

    /// <summary>How messages name an invoice: "Anfang H1 2026", "Volles H2".</summary>
    public static string Label(BillingExample example)
    {
        var period = example.Period;
        return example.Kind switch
        {
            BillingExampleKind.Start => Invariant($"Anfang H{period.Half} {period.Year}"),
            BillingExampleKind.StartAndEnd => Invariant(
                $"Anfang und Ende H{period.Half} {period.Year}"
            ),
            BillingExampleKind.End => Invariant($"Ende H{period.Half} {period.Year}"),
            BillingExampleKind.FullFirstHalf => "Volles H1",
            _ => "Volles H2",
        };
    }

    /// <summary>", anteilig 05–06/2026 (2 von 6 Monaten)" — appended to a
    /// prorated line's description and stored with it.</summary>
    public static string ProrationNote(BillingPeriod period)
    {
        var year =
            period.Year
            ?? throw new ArgumentException("A prorated period needs its year.", nameof(period));
        var months =
            period.Months == 1
                ? Invariant($"{period.FirstMonth:00}/{year}")
                : Invariant($"{period.FirstMonth:00}–{period.LastMonth:00}/{year}");
        return Invariant($", anteilig {months} ({period.Months} von 6 Monaten)");
    }

    private static (bool First, bool Second) FullHalves(DateOnly start, DateOnly? end)
    {
        if (end is null)
        {
            return (true, true);
        }

        var first = false;
        var second = false;
        var (year, half) = HalfOf(start);
        var (lastYear, lastHalf) = HalfOf(end.Value);
        while (year * 2 + half <= lastYear * 2 + lastHalf && !(first && second))
        {
            if (PeriodIn(start, end, year, half) is { IsFull: true })
            {
                if (half == 1)
                {
                    first = true;
                }
                else
                {
                    second = true;
                }
            }

            (year, half) = half == 1 ? (year, 2) : (year + 1, 1);
        }

        return (first, second);
    }

    private static BilledLine Billed(
        int index,
        BillableLink link,
        decimal price,
        string description
    ) =>
        new(
            index,
            price,
            new GraveLine(
                link.ItemId,
                link.ItemNumber,
                description,
                link.Count,
                VatInclusivePrice.NetOf(price, link.VatRate, link.PriceIncludesVat),
                link.VatRate,
                link.PriceIncludesVat,
                link.PriceIncludesVat ? price : null
            )
        );

    private static (int Year, int Half) HalfOf(DateOnly month) =>
        (month.Year, month.Month <= 6 ? 1 : 2);

    private static int Index(DateOnly month) => month.Year * 12 + month.Month - 1;

    private static string Invariant(FormattableString text) =>
        text.ToString(CultureInfo.InvariantCulture);
}
```

- [ ] **Step 4: Run the tests, expect them to pass.**

- [ ] **Step 5: Commit**

```sh
git add backend/src/Invoicing.Domain/HalfYearBilling.cs backend/tests/Invoicing.IntegrationTests/HalfYearBillingTests.cs
git commit -m "feat(billing): bill a half-year by the months of the term and the items' quarters"
```

---

### Task 3: Checking every invoice of a term, with suggestions

**Files:**

- Modify: `backend/src/Invoicing.Domain/GraveShare.cs` (add `IsEven`)
- Create: `backend/src/Invoicing.Domain/GraveBillingCheck.cs`
- Test: `backend/tests/Invoicing.IntegrationTests/GraveBillingCheckTests.cs`

**Interfaces:**

- Consumes: everything from Task 2; `GraveShare.Problems`.
- Produces:
  - `GraveShare.IsEven(IReadOnlyList<GraveLine> lines, int recipients) : bool` — same verdict as `Problems(...).Count == 0`, without searching suggestions.
  - `record BillingExampleResult(BillingExample Example, IReadOnlyList<BilledLine> Lines, IReadOnlyList<GraveShareProblem> Problems)` with `bool Ok`, `IReadOnlyList<GraveLine> GraveLines`
  - `record GraveBillingEvaluation(IReadOnlyList<BillingExampleResult> Invoices, IReadOnlyDictionary<int, IReadOnlyList<decimal>> Suggestions)` with `bool Ok`, `BillingExampleResult? FirstFailing`
  - `GraveBillingCheck.Evaluate(IReadOnlyList<BillableLink> links, DateOnly? start, DateOnly? end, int recipients, bool withSuggestions = true)`

- [ ] **Step 1: Write the failing tests** — `GraveBillingCheckTests.cs`:

```csharp
using System;
using System.Linq;
using Burkard.Invoicing.Domain;
using Burkard.Invoicing.Domain.Entities;
using Xunit;

namespace Burkard.Invoicing.IntegrationTests;

/// <summary>A grave is valid when every invoice its term produces splits evenly,
/// and a suggestion is a price at which all of them do.</summary>
public class GraveBillingCheckTests
{
    private static readonly DateOnly May2026 = new(2026, 5, 1);

    private static BillableLink Care(decimal price) =>
        new(1, "100", "Grabpflege", 19m, null, 1, price, false);

    [Fact]
    public void A_grave_whose_every_invoice_divides_is_ok()
    {
        // 60,00 full, 20,00 for May/June: both even over two
        var evaluation = GraveBillingCheck.Evaluate([Care(60m)], May2026, null, 2);

        Assert.True(evaluation.Ok);
        Assert.Equal(3, evaluation.Invoices.Count);
        Assert.Empty(evaluation.Suggestions);
    }

    [Fact]
    public void The_start_invoice_can_fail_while_the_full_ones_divide()
    {
        // 60,02 is 6 002 cents, even; two months of it are 20,01, odd
        var evaluation = GraveBillingCheck.Evaluate([Care(60.02m)], May2026, null, 2);

        Assert.False(evaluation.Ok);
        var failing = evaluation.FirstFailing!;
        Assert.Equal(BillingExampleKind.Start, failing.Example.Kind);
        Assert.Equal(2001, Assert.Single(failing.Problems).Amount);
        Assert.True(evaluation.Invoices.Skip(1).All(i => i.Ok));
    }

    [Fact]
    public void A_suggestion_makes_every_invoice_divide()
    {
        // 60,01: the full ones are odd. 60,00 works everywhere; 60,02 and 60,04
        // fix the full ones but leave May/June odd (20,01); 60,06 works.
        var evaluation = GraveBillingCheck.Evaluate([Care(60.01m)], May2026, null, 2);

        Assert.Equal(new[] { 60.00m, 60.06m }, evaluation.Suggestions[0]);
        foreach (var price in evaluation.Suggestions[0])
        {
            Assert.True(GraveBillingCheck.Evaluate([Care(price)], May2026, null, 2).Ok);
        }
    }

    [Fact]
    public void A_line_that_cannot_fix_its_invoice_gets_no_suggestion()
    {
        // both full half-years fail on the 19 % care line. The 7 % Herbst line
        // sits on the failing H2 too, but no price of its own can fix a 19 %
        // subtotal, so it gets no entry
        var links = new[]
        {
            Care(60.01m),
            new BillableLink(2, "300", "Herbst", 7m, BillingQuarter.Autumn, 1, 30m, false),
        };

        var evaluation = GraveBillingCheck.Evaluate(links, null, null, 2);

        Assert.Equal(new[] { 0 }, evaluation.Suggestions.Keys);
    }

    [Fact]
    public void Suggestions_can_be_left_out()
    {
        var evaluation = GraveBillingCheck.Evaluate([Care(60.01m)], May2026, null, 2, withSuggestions: false);

        Assert.False(evaluation.Ok);
        Assert.Empty(evaluation.Suggestions);
    }

    [Fact]
    public void An_invoice_with_nothing_to_bill_is_ok()
    {
        var spring = new BillableLink(2, "200", "Frühling", 19m, BillingQuarter.Spring, 1, 40.01m, false);

        // start in May: H1 2026 bills nothing, the full H1 bills 40,01 (odd)
        var evaluation = GraveBillingCheck.Evaluate([spring], May2026, null, 2);

        Assert.True(evaluation.Invoices[0].Ok);
        Assert.Empty(evaluation.Invoices[0].Lines);
        Assert.False(evaluation.Invoices[1].Ok);
    }

    [Fact]
    public void IsEven_agrees_with_Problems()
    {
        var lines = HalfYearBilling.Lines([Care(47.80m)], BillingPeriod.Full(1));

        Assert.False(GraveShare.IsEven(lines, 3));
        Assert.True(GraveShare.IsEven(HalfYearBilling.Lines([Care(47.79m)], BillingPeriod.Full(1)), 3));
    }
}
```

- [ ] **Step 2: Run them, expect a compile failure.**

- [ ] **Step 3: Add `IsEven` to `GraveShare`** (after `Problems`):

```csharp
    /// <summary>Whether these lines split evenly — the verdict of
    /// <see cref="Problems"/> without the search for suggestions, which the
    /// per-line suggestion search would otherwise run thousands of times.</summary>
    public static bool IsEven(IReadOnlyList<GraveLine> lines, int recipients) =>
        lines
            .Select(l => l.VatRate)
            .Distinct()
            .All(rate => KindOf(lines.Where(l => l.VatRate == rate).ToList(), recipients, rate) is null);
```

- [ ] **Step 4: Implement** `backend/src/Invoicing.Domain/GraveBillingCheck.cs`:

```csharp
namespace Burkard.Invoicing.Domain;

/// <summary>One invoice of a grave's term, what it bills and what stops it
/// splitting evenly.</summary>
public sealed record BillingExampleResult(
    BillingExample Example,
    IReadOnlyList<BilledLine> Lines,
    IReadOnlyList<GraveShareProblem> Problems
)
{
    public bool Ok => Problems.Count == 0;

    public IReadOnlyList<GraveLine> GraveLines => Lines.Select(l => l.Line).ToList();
}

/// <summary>Every invoice of a grave's term, and per link index the nearest
/// prices below and above at which all of them divide.</summary>
public sealed record GraveBillingEvaluation(
    IReadOnlyList<BillingExampleResult> Invoices,
    IReadOnlyDictionary<int, IReadOnlyList<decimal>> Suggestions
)
{
    public bool Ok => Invoices.All(i => i.Ok);

    public BillingExampleResult? FirstFailing => Invoices.FirstOrDefault(i => !i.Ok);
}

/// <summary>Whether a grave can be billed as configured: each invoice its term
/// produces has to split evenly on its own, since the first and last half-year
/// bill different amounts than the full ones.
///
/// A suggestion moves one line's entered price and keeps every other line, and
/// is only offered when it fixes every invoice at once — a price that rescues
/// the full half-years but breaks the first one is no help.</summary>
public static class GraveBillingCheck
{
    /// <summary>How far, in cents, a suggestion may lie from the entered price —
    /// the reach <see cref="GraveShare"/> has always searched.</summary>
    public const int SuggestionReachCents = 400;

    public static GraveBillingEvaluation Evaluate(
        IReadOnlyList<BillableLink> links,
        DateOnly? start,
        DateOnly? end,
        int recipients,
        bool withSuggestions = true
    )
    {
        var examples = HalfYearBilling.Examples(start, end);
        var invoices = examples
            .Select(example =>
            {
                var billed = HalfYearBilling.Bill(links, example.Period);
                var problems =
                    billed.Count == 0
                        ? []
                        : GraveShare.Problems(billed.Select(b => b.Line).ToList(), recipients);
                return new BillingExampleResult(example, billed, problems);
            })
            .ToList();

        var suggestions =
            withSuggestions && invoices.Any(i => !i.Ok)
                ? Suggest(links, examples, invoices, recipients)
                : new Dictionary<int, IReadOnlyList<decimal>>();
        return new GraveBillingEvaluation(invoices, suggestions);
    }

    private static Dictionary<int, IReadOnlyList<decimal>> Suggest(
        IReadOnlyList<BillableLink> links,
        IReadOnlyList<BillingExample> examples,
        IReadOnlyList<BillingExampleResult> invoices,
        int recipients
    )
    {
        var candidates = invoices
            .Where(i => !i.Ok)
            .SelectMany(i => i.Lines.Select(l => l.LinkIndex))
            .Distinct()
            .OrderBy(index => index);

        var found = new Dictionary<int, IReadOnlyList<decimal>>();
        foreach (var index in candidates)
        {
            var price = links[index].EnteredPrice;
            decimal? below = null;
            decimal? above = null;
            for (var cents = 1; cents <= SuggestionReachCents && (below is null || above is null); cents++)
            {
                var step = cents / 100m;
                if (below is null && price - step > 0 && AllEven(With(links, index, price - step), examples, recipients))
                {
                    below = price - step;
                }

                if (above is null && AllEven(With(links, index, price + step), examples, recipients))
                {
                    above = price + step;
                }
            }

            var prices = new[] { below, above }.Where(p => p is not null).Select(p => p!.Value).ToList();
            if (prices.Count > 0)
            {
                found[index] = prices;
            }
        }

        return found;
    }

    private static bool AllEven(
        IReadOnlyList<BillableLink> links,
        IReadOnlyList<BillingExample> examples,
        int recipients
    ) =>
        examples.All(example =>
        {
            var lines = HalfYearBilling.Lines(links, example.Period);
            return lines.Count == 0 || GraveShare.IsEven(lines, recipients);
        });

    private static List<BillableLink> With(IReadOnlyList<BillableLink> links, int index, decimal price)
    {
        var copy = links.ToList();
        copy[index] = copy[index] with { EnteredPrice = price };
        return copy;
    }
}
```

- [ ] **Step 5: Run `GraveBillingCheckTests` and `GraveShareTests`, expect them to pass.**

- [ ] **Step 6: Commit** — `git commit -m "feat(billing): check every invoice of a grave's term and suggest prices that fix all of them"`

---

### Task 4: `SaveGrave` checks the term's invoices and caps recipients

**Files:**

- Modify: `backend/src/Invoicing.Api/Services/GraveBilling.cs`
- Modify: `backend/src/Invoicing.Api/Services/GraveService.cs` (`SaveAsync`, `ValidateAsync`, `GraveShareValidationException`)
- Modify: `backend/src/Invoicing.Api/OData/Controllers/GravesController.cs` (catch of `GraveShareValidationException`)
- Test: `backend/tests/Invoicing.IntegrationTests/SaveGraveTests.cs`

**Interfaces:**

- Consumes: `GraveBillingCheck.Evaluate`, `HalfYearBilling.Label`.
- Produces: `GraveBilling.MaxRecipients = 4`; `GraveBilling.LinkFor(InvoiceItem item, int count, decimal? unitPrice, bool priceIncludesVat) : BillableLink`; `GraveShareValidationException(problems, recipients, string? invoice = null)` with `Invoice`; the SaveGrave refusal body carries `args: [label]` when the invoice is known.

- [ ] **Step 1: Write the failing tests** — add to `SaveGraveTests.cs`. Read the file's existing save helper first (search for `"/odata/SaveGrave"`); the tests below post the body directly so they do not depend on it:

```csharp
    private static object GraveBody(
        string number,
        string start,
        string? end,
        long itemId,
        decimal price,
        IEnumerable<long> recipientIds
    ) =>
        new
        {
            graveId = (long?)null,
            graveNumber = number,
            fieldNumber = "1",
            siteName = "Schineller Bäcke",
            position = "SingleInside",
            startDate = start,
            endDate = end,
            items = new[]
            {
                new
                {
                    itemId,
                    count = 1,
                    unitPrice = (decimal?)price,
                    priceIncludesVat = false,
                },
            },
            recipientIds,
        };

    private static async Task<long[]> RecipientsAsync(HttpClient client, int count)
    {
        var ids = new long[count];
        for (var i = 0; i < count; i++)
        {
            ids[i] = await ReadIdAsync(
                client.PostAsync(
                    "/odata/Recipients",
                    ODataJson.Body(new { Name = $"Empfänger {i}", Address = $"Weg {i}" })
                )
            );
        }

        return ids;
    }

    [Fact]
    public async Task Saving_refuses_more_than_four_recipients()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var (priced, _, _, _) = await SeedAsync(client);
        var recipients = await RecipientsAsync(client, 5);

        // 120,00 divides by five, so only the cap can refuse it
        var response = await client.PostAsync(
            "/odata/SaveGrave",
            ODataJson.Body(GraveBody("TM-1", "2026-01", null, priced, 120.00m, recipients))
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        Assert.Equal("TooManyRecipients", json.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Saving_refuses_a_grave_whose_start_invoice_does_not_divide()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var (priced, _, first, second) = await SeedAsync(client);

        // 60,02 divides by two; May and June of it are 20,01, which does not
        var response = await client.PostAsync(
            "/odata/SaveGrave",
            ODataJson.Body(GraveBody("SD-1", "2026-05", null, priced, 60.02m, new[] { first, second }))
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        var error = json.RootElement.GetProperty("error");
        Assert.Equal("PricesDoNotDivide", error.GetProperty("code").GetString());
        Assert.Equal("Anfang H1 2026", Assert.Single(error.GetProperty("args").EnumerateArray().ToArray()).GetString());
    }

    [Fact]
    public async Task Saving_accepts_a_grave_whose_every_invoice_divides()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var (priced, _, first, second) = await SeedAsync(client);

        var response = await client.PostAsync(
            "/odata/SaveGrave",
            ODataJson.Body(GraveBody("SD-2", "2026-05", "2030-08", priced, 60.00m, new[] { first, second }))
        );

        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
    }
```

(If `SeedAsync` is an instance method, keep calling it as one; `ReadIdAsync` is static.)

- [ ] **Step 2: Run them, expect failure** (`TooManyRecipients` unknown; the 60,02 grave saves).

- [ ] **Step 3: Extend `GraveBilling`** (add `using Burkard.Invoicing.Domain;` already present):

```csharp
    /// <summary>How many recipients a grave may split its bill across.</summary>
    public const int MaxRecipients = 4;

    /// <summary>A link as the half-year rule sees it: the link's own price, or
    /// the item's default standing in for it, rounded to the cent like
    /// <see cref="LineFor"/>.</summary>
    public static BillableLink LinkFor(
        InvoiceItem item,
        int count,
        decimal? unitPrice,
        bool priceIncludesVat
    ) =>
        new(
            item.Id,
            item.Number,
            item.Description,
            item.VatRate,
            item.Quarter,
            count,
            decimal.Round(unitPrice ?? item.DefaultPrice ?? 0m, 2, MidpointRounding.AwayFromZero),
            priceIncludesVat
        );
```

Update the class summary's first paragraph: "Turns a grave's item links into what the half-year rule bills."

- [ ] **Step 4: Rewrite the split check in `GraveService`**

`SaveAsync` calls `await ValidateAsync(items, recipientIds, fields, ct);`. In `ValidateAsync(IReadOnlyList<GraveItemInput> items, IReadOnlyList<long> recipientIds, GraveFields fields, CancellationToken ct)`, after the duplicate-recipient check:

```csharp
        if (recipientIds.Count > GraveBilling.MaxRecipients)
        {
            throw new ValidationException(
                "TooManyRecipients",
                $"A grave can have at most {GraveBilling.MaxRecipients} recipients."
            );
        }
```

Replace the `if (recipientIds.Count > 0 && items.Count > 0) { … }` block with:

```csharp
        if (recipientIds.Count > 0 && items.Count > 0)
        {
            var links = items
                .Select(input =>
                    GraveBilling.LinkFor(
                        invoiceItems[input.ItemId],
                        input.Count,
                        input.UnitPrice,
                        input.PriceIncludesVat
                    )
                )
                .ToList();

            // every invoice the term produces has to divide on its own: the
            // first and last half-year bill other amounts than the full ones
            var evaluation = GraveBillingCheck.Evaluate(
                links,
                fields.StartDate,
                fields.EndDate,
                recipientIds.Count,
                withSuggestions: false
            );
            if (evaluation.FirstFailing is { } failing)
            {
                throw new GraveShareValidationException(
                    failing.Problems,
                    recipientIds.Count,
                    HalfYearBilling.Label(failing.Example)
                );
            }
        }
```

`ValidateFieldsAsync` runs first and already refuses an end before the start, so `Examples` cannot throw here.

`GraveShareValidationException` gains the invoice:

```csharp
    public GraveShareValidationException(
        IReadOnlyList<GraveShareProblem> problems,
        int recipients,
        string? invoice = null
    )
        : base(
            "PricesDoNotDivide",
            invoice is null
                ? "The prices cannot be split evenly across the recipients."
                : $"The invoice '{invoice}' cannot be split evenly across the recipients."
        )
    {
        Problems = problems;
        Recipients = recipients;
        Invoice = invoice;
    }

    /// <summary>The label of the invoice that does not divide ("Anfang H1
    /// 2026"); null where only one invoice is in question, as in a revision.</summary>
    public string? Invoice { get; }
```

- [ ] **Step 5: Send the label** — in `GravesController.Save`'s `catch (GraveShareValidationException ex)`, add to the `error` object: `args = ex.Invoice is null ? null : new[] { ex.Invoice },`.

- [ ] **Step 6: Run `SaveGraveTests`, `GraveTests`, `VatIncludedGraveTests`, `GraveRecipientImmutabilityTests`, expect green.** Existing graves in these tests start in January with no or a December end, so their start and end invoices are full and nothing changes for them.

- [ ] **Step 7: Commit** — `git commit -m "feat(graves): refuse a grave unless every invoice of its term divides, and cap it at four recipients"`

---

### Task 5: `PreviewGraveBilling`

**Files:**

- Modify: `backend/src/Invoicing.Api/OData/Controllers/GraveBillingValidationController.cs`
- Test: `backend/tests/Invoicing.IntegrationTests/GraveBillingPreviewTests.cs`

**Interfaces:**

- Consumes: `GraveBilling.LinkFor`, `GraveBilling.MaxRecipients`, `GraveBillingCheck.Evaluate`, `GraveShare.CollectedCents`, `GraveTerm.TryParseMonth`.
- Produces: `POST /odata/PreviewGraveBilling`, request `{ Recipients, StartMonth?, EndMonth?, Lines: [{ ItemId, Count, UnitPrice?, PriceIncludesVat }] }`, response (PascalCase):

```text
{ Ok, Recipients,
  Invoices: [{ Kind, Year|null, Half, Months, Ok, NetTotal, GrossTotal|null, PerRecipient|null,
               Problems: [{ VatRate, Kind, Amount, Suggestions: [] }],
               Lines: [{ LineIndex, ItemNumber, Description, Count, VatRate, PriceIncludesVat, Price, UnitPrice, NetTotal }] }],
  LineSuggestions: [{ LineIndex, Suggestions: [decimal] }] }
```

- [ ] **Step 1: Write the failing tests** — `GraveBillingPreviewTests.cs`, seeding like `GraveBillingValidationTests.SeedAsync`:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Domain.Entities;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Burkard.Invoicing.IntegrationTests;

/// <summary>The grave editor asks for every invoice the grave's term will
/// produce while the user types: what each bills, whether it divides, and which
/// price on which line would fix it.</summary>
public class GraveBillingPreviewTests
{
    private static async Task<(long Care, long Summer)> SeedAsync(ApiTestHost host)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.VatRates.Add(new VatRate { Rate = 19m });
        var care = new InvoiceItem { Number = "100", Description = "Grabpflege", VatRate = 19m, DefaultPrice = 60m };
        var summer = new InvoiceItem
        {
            Number = "210",
            Description = "Sommerbepflanzung",
            VatRate = 19m,
            DefaultPrice = 40m,
            Quarter = BillingQuarter.Summer,
        };
        db.InvoiceItems.AddRange(care, summer);
        await db.SaveChangesAsync();
        return (care.Id, summer.Id);
    }

    private static object Line(long itemId, decimal? price) =>
        new { ItemId = itemId, Count = 1, UnitPrice = price, PriceIncludesVat = false };

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, object body) =>
        await client.PostAsJsonAsync("/odata/PreviewGraveBilling", body);

    private static async Task<JsonElement> PreviewAsync(HttpClient client, object body)
    {
        var response = await PostAsync(client, body);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    }

    [Fact]
    public async Task The_preview_lists_the_terms_invoices_with_their_lines()
    {
        using var host = new ApiTestHost();
        var (care, summer) = await SeedAsync(host);
        using var client = host.CreateClient();

        var body = await PreviewAsync(
            client,
            new
            {
                Recipients = 2,
                StartMonth = "2026-05",
                EndMonth = "2030-08",
                Lines = new[] { Line(care, null), Line(summer, null) },
            }
        );

        Assert.True(body.GetProperty("Ok").GetBoolean());
        Assert.Equal(2, body.GetProperty("Recipients").GetInt32());
        var invoices = body.GetProperty("Invoices").EnumerateArray().ToArray();
        Assert.Equal(
            new[] { "Start", "FullFirstHalf", "FullSecondHalf", "End" },
            invoices.Select(i => i.GetProperty("Kind").GetString())
        );

        var start = invoices[0];
        Assert.Equal(2026, start.GetProperty("Year").GetInt32());
        Assert.Equal(1, start.GetProperty("Half").GetInt32());
        Assert.Equal(2, start.GetProperty("Months").GetInt32());
        Assert.Equal(60.00m, start.GetProperty("NetTotal").GetDecimal());
        var lines = start.GetProperty("Lines").EnumerateArray().ToArray();
        Assert.Equal(new[] { 0, 1 }, lines.Select(l => l.GetProperty("LineIndex").GetInt32()));
        Assert.Equal(
            "Grabpflege, anteilig 05–06/2026 (2 von 6 Monaten)",
            lines[0].GetProperty("Description").GetString()
        );
        Assert.Equal(20.00m, lines[0].GetProperty("Price").GetDecimal());

        // the full H2 carries no summer item
        Assert.Equal(JsonValueKind.Null, invoices[2].GetProperty("Year").ValueKind);
        Assert.Single(invoices[2].GetProperty("Lines").EnumerateArray());

        // 60,00 net at 19 % over two: 30,00 + 5,70 each
        Assert.Equal(71.40m, invoices[2].GetProperty("GrossTotal").GetDecimal());
        Assert.Equal(35.70m, invoices[2].GetProperty("PerRecipient").GetDecimal());
    }

    [Fact]
    public async Task A_failing_invoice_puts_its_suggestion_on_the_line()
    {
        using var host = new ApiTestHost();
        var (care, _) = await SeedAsync(host);
        using var client = host.CreateClient();

        var body = await PreviewAsync(
            client,
            new { Recipients = 2, StartMonth = "2026-05", Lines = new[] { Line(care, 60.01m) } }
        );

        Assert.False(body.GetProperty("Ok").GetBoolean());
        var suggestion = Assert.Single(body.GetProperty("LineSuggestions").EnumerateArray().ToArray());
        Assert.Equal(0, suggestion.GetProperty("LineIndex").GetInt32());
        Assert.Equal(
            new[] { 60.00m, 60.06m },
            suggestion.GetProperty("Suggestions").EnumerateArray().Select(v => v.GetDecimal())
        );
        var full = body.GetProperty("Invoices")[1];
        Assert.False(full.GetProperty("Ok").GetBoolean());
        Assert.Equal(JsonValueKind.Null, full.GetProperty("GrossTotal").ValueKind);
        Assert.Empty(full.GetProperty("Problems")[0].GetProperty("Suggestions").EnumerateArray());
    }

    [Theory]
    [InlineData(0, "NoRecipients")]
    [InlineData(5, "TooManyRecipients")]
    public async Task Preview_refuses_a_recipient_count_out_of_range(int recipients, string code)
    {
        using var host = new ApiTestHost();
        var (care, _) = await SeedAsync(host);
        using var client = host.CreateClient();

        var response = await PostAsync(client, new { Recipients = recipients, Lines = new[] { Line(care, null) } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(code, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Preview_refuses_an_end_before_the_start()
    {
        using var host = new ApiTestHost();
        var (care, _) = await SeedAsync(host);
        using var client = host.CreateClient();

        var response = await PostAsync(
            client,
            new { Recipients = 1, StartMonth = "2026-05", EndMonth = "2026-04", Lines = new[] { Line(care, null) } }
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("DateRangeInvalid", await response.Content.ReadAsStringAsync());
    }
}
```

- [ ] **Step 2: Run, expect 404s / failures.**

- [ ] **Step 3: Implement the action** in `GraveBillingValidationController`. Factor the item loading and line validation that `Validate` does (items dictionary, `ItemNotFound`, `PriceRequired`, `InvalidPrice`, `InvalidCount`) into a private method both actions call:

```csharp
    /// <summary>The items the lines name, or the refusal that stops the check.</summary>
    private async Task<(Dictionary<long, InvoiceItem>? Items, IActionResult? Refusal)> LoadItemsAsync(
        List<ValidateGraveBillingLine> lines
    )
    {
        if (lines.Any(l => l.Count <= 0))
        {
            return (null, BadRequest(Error("InvalidCount", "Every line's count must be positive.")));
        }

        var itemIds = lines.Select(l => l.ItemId).Distinct().ToList();
        var items = await _db
            .InvoiceItems.AsNoTracking()
            .Where(i => itemIds.Contains(i.Id))
            .ToDictionaryAsync(i => i.Id, HttpContext.RequestAborted);

        if (itemIds.Any(id => !items.ContainsKey(id)))
        {
            return (null, BadRequest(Error("ItemNotFound", "A line refers to an item that does not exist.")));
        }

        var unpriced = lines.FirstOrDefault(l => (l.UnitPrice ?? items[l.ItemId].DefaultPrice) is null);
        if (unpriced is not null)
        {
            return (
                null,
                BadRequest(Error("PriceRequired", $"A price is required for item '{items[unpriced.ItemId].Number}'."))
            );
        }

        var beyondCents = lines.FirstOrDefault(l => l.UnitPrice is { } price && !GraveBilling.IsWholeCents(price));
        if (beyondCents is not null)
        {
            var number = items[beyondCents.ItemId].Number;
            return (
                null,
                BadRequest(Error("InvalidPrice", $"The price for item '{number}' has more than two decimals."))
            );
        }

        return (items, null);
    }
```

Rewrite `Validate` to call it (`var (items, refusal) = await LoadItemsAsync(request.Lines); if (refusal is not null) return refusal;`) without changing its response. Then add:

```csharp
    /// <summary>Every invoice the grave's term will produce, as the editor shows
    /// them while the grave is being typed: the lines each bills, whether it
    /// divides, and per line the prices that would make all of them divide.
    /// The same rule refuses the save, so the preview cannot promise what the
    /// save then rejects.</summary>
    [HttpPost("PreviewGraveBilling")]
    public async Task<IActionResult> Preview([FromBody] PreviewGraveBillingRequest? request)
    {
        if (request is null || request.Lines.Count == 0)
        {
            return BadRequest(Error("InvalidRequest", "The request body must carry at least one line."));
        }

        if (request.Recipients <= 0)
        {
            return BadRequest(Error("NoRecipients", "A grave without recipients has nothing to divide."));
        }

        if (request.Recipients > GraveBilling.MaxRecipients)
        {
            return BadRequest(
                Error("TooManyRecipients", $"A grave can have at most {GraveBilling.MaxRecipients} recipients.")
            );
        }

        DateOnly? start = null;
        DateOnly? end = null;
        if (!string.IsNullOrWhiteSpace(request.StartMonth))
        {
            if (!GraveTerm.TryParseMonth(request.StartMonth, out var parsed))
            {
                return BadRequest(Error("InvalidDate", "Start and end are months (yyyy-MM)."));
            }

            start = parsed;
        }

        if (!string.IsNullOrWhiteSpace(request.EndMonth))
        {
            if (!GraveTerm.TryParseMonth(request.EndMonth, out var parsed))
            {
                return BadRequest(Error("InvalidDate", "Start and end are months (yyyy-MM)."));
            }

            end = parsed;
        }

        if (start is not null && end < start)
        {
            return BadRequest(Error("DateRangeInvalid", "The end month must not be before the start month."));
        }

        var (items, refusal) = await LoadItemsAsync(request.Lines);
        if (refusal is not null)
        {
            return refusal;
        }

        var links = request
            .Lines.Select(l => GraveBilling.LinkFor(items![l.ItemId], l.Count, l.UnitPrice, l.PriceIncludesVat))
            .ToList();
        var evaluation = GraveBillingCheck.Evaluate(links, start, end, request.Recipients);

        return new JsonResult(
            new
            {
                evaluation.Ok,
                request.Recipients,
                Invoices = evaluation
                    .Invoices.Select(invoice =>
                    {
                        var collected =
                            invoice.Ok && invoice.Lines.Count > 0
                                ? GraveShare.CollectedCents(invoice.GraveLines, request.Recipients) / 100m
                                : (decimal?)null;
                        return new
                        {
                            Kind = invoice.Example.Kind.ToString(),
                            invoice.Example.Period.Year,
                            invoice.Example.Period.Half,
                            invoice.Example.Period.Months,
                            invoice.Ok,
                            NetTotal = invoice.Lines.Sum(b => NetTotal(b.Line)),
                            GrossTotal = collected,
                            PerRecipient = collected / request.Recipients,
                            Problems = invoice
                                .Problems.Select(p => new
                                {
                                    p.VatRate,
                                    Kind = p.Kind.ToString(),
                                    Amount = p.Amount / 100m,
                                    // the per-line suggestions replace these: a
                                    // figure for one invoice may break another
                                    Suggestions = Array.Empty<decimal>(),
                                })
                                .ToList(),
                            Lines = invoice
                                .Lines.Select(b => new
                                {
                                    LineIndex = b.LinkIndex,
                                    b.Line.ItemNumber,
                                    b.Line.Description,
                                    b.Line.Count,
                                    b.Line.VatRate,
                                    b.Line.PriceIncludesVat,
                                    b.Price,
                                    b.Line.UnitPrice,
                                    NetTotal = NetTotal(b.Line),
                                })
                                .ToList(),
                        };
                    })
                    .ToList(),
                LineSuggestions = evaluation
                    .Suggestions.OrderBy(s => s.Key)
                    .Select(s => new { LineIndex = s.Key, Suggestions = s.Value })
                    .ToList(),
            },
            ODataJsonDefaults.PascalCase
        );
    }

    private static decimal NetTotal(GraveLine line) =>
        decimal.Round(line.Count * line.UnitPrice, 2, MidpointRounding.AwayFromZero);

    public sealed class PreviewGraveBillingRequest
    {
        public int Recipients { get; set; }

        /// <summary>"yyyy-MM"; without it only the full half-years are shown.</summary>
        public string? StartMonth { get; set; }

        /// <summary>"yyyy-MM"; without it the term runs indefinitely.</summary>
        public string? EndMonth { get; set; }

        public List<ValidateGraveBillingLine> Lines { get; set; } = new();
    }
```

Add `using Burkard.Invoicing.Domain.Entities;` for `InvoiceItem`.

- [ ] **Step 4: Run `GraveBillingPreviewTests` and `GraveBillingValidationTests`, expect green.**

- [ ] **Step 5: Commit** — `git commit -m "feat(graves): preview every invoice of a grave's term with per-line price suggestions"`

---

### Task 6: Generation follows the term

**Files:**

- Modify: `backend/src/Invoicing.Api/Services/GraveInvoiceGenerator.cs`
- Modify: `backend/src/Invoicing.Api/Jobs/GraveInvoicesJobHandler.cs`
- Modify: `backend/src/Invoicing.Api/OData/Controllers/GraveInvoicingStatusController.cs`
- Test: `backend/tests/Invoicing.IntegrationTests/GraveTermGenerationTests.cs`

**Interfaces:**

- Consumes: `HalfYearBilling.PeriodIn`, `HalfYearBilling.Lines`, `GraveBilling.LinkFor`.
- Produces: skip reasons `NothingToBill`, `OutsideTerm`; the job only processes graves whose term touches the half-year; the status leaves out graves outside the term that were not invoiced for it.

- [ ] **Step 1: Write the failing tests** — `GraveTermGenerationTests.cs`:

```csharp
using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Burkard.Invoicing.IntegrationTests;

/// <summary>A half-year bills the months of the grave's term it covers, and a
/// half-year outside the term bills nothing.</summary>
public class GraveTermGenerationTests
{
    private static async Task<long> SeedAsync(
        ApiTestHost host,
        DateOnly start,
        DateOnly? end,
        params (string Number, BillingQuarter? Quarter, decimal Price)[] items
    )
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (!await db.VatRates.AnyAsync())
        {
            db.VatRates.Add(new VatRate { Rate = 19m });
        }

        var recipient = new Recipient { Number = $"E-{Guid.NewGuid():N}", Name = "Anna", Address = "Weg 1" };
        var grave = new Grave
        {
            GraveNumber = $"T-{Guid.NewGuid():N}",
            StartDate = start,
            EndDate = end,
            CreatedAt = DateOnly.FromDateTime(DateTime.UtcNow),
        };
        db.Recipients.Add(recipient);
        db.Graves.Add(grave);
        var entities = items
            .Select(i => new InvoiceItem
            {
                Number = $"{i.Number}-{Guid.NewGuid():N}",
                Description = i.Number,
                VatRate = 19m,
                DefaultPrice = i.Price,
                Quarter = i.Quarter,
            })
            .ToList();
        db.InvoiceItems.AddRange(entities);
        await db.SaveChangesAsync();

        foreach (var item in entities)
        {
            db.GraveItems.Add(new GraveItem { GraveId = grave.Id, ItemId = item.Id, Count = 1 });
        }

        db.GraveRecipients.Add(new GraveRecipient { GraveId = grave.Id, RecipientId = recipient.Id });
        await db.SaveChangesAsync();
        return grave.Id;
    }

    /// <summary>Runs a generation and returns the job's result.</summary>
    private static async Task<JsonElement> GenerateAsync(HttpClient client, int year, int half)
    {
        var start = await client.PostAsync("/odata/GenerateGraveInvoices", ODataJson.Body(new { year, half }));
        Assert.Equal(HttpStatusCode.Accepted, start.StatusCode);
        using var startJson = await ODataJson.ReadAsync(start);
        var jobId = startJson.RootElement.GetProperty("Id").GetInt64();

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(20);
        while (DateTime.UtcNow < deadline)
        {
            using var json = await ODataJson.ReadAsync(await client.GetAsync($"/odata/Jobs({jobId})"));
            var status = json.RootElement.GetProperty("Status").GetString();
            Assert.NotEqual("Failed", status);
            if (status == "Succeeded")
            {
                return JsonDocument.Parse(json.RootElement.GetProperty("ResultJson").GetString()!).RootElement.Clone();
            }

            await Task.Delay(100);
        }

        throw new TimeoutException("generation did not finish");
    }

    private static async Task<InvoiceLine[]> LinesAsync(ApiTestHost host, long graveId, int year, int half)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db
            .Invoices.Where(i => i.GraveId == graveId && i.PeriodYear == year && i.PeriodHalf == half)
            .SelectMany(i => i.Lines)
            .OrderBy(l => l.LineNumber)
            .ToArrayAsync();
    }

    [Fact]
    public async Task Generation_prorates_the_start_and_bills_the_touched_quarter()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var graveId = await SeedAsync(
            host,
            new DateOnly(2026, 5, 1),
            null,
            ("Pflege", null, 60m),
            ("Sommer", BillingQuarter.Summer, 40m),
            ("Fruehling", BillingQuarter.Spring, 30m)
        );

        await GenerateAsync(client, 2026, 1);

        var lines = await LinesAsync(host, graveId, 2026, 1);
        Assert.Equal(2, lines.Length);
        Assert.Equal("Pflege, anteilig 05–06/2026 (2 von 6 Monaten)", lines[0].Description);
        Assert.Equal(20.00m, lines[0].UnitPrice);
        Assert.Equal("Sommer", lines[1].Description);
        Assert.Equal(40.00m, lines[1].UnitPrice);
    }

    [Fact]
    public async Task Generation_prorates_a_term_inside_one_half_year()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var graveId = await SeedAsync(host, new DateOnly(2026, 2, 1), new DateOnly(2026, 4, 1), ("Pflege", null, 60m));

        await GenerateAsync(client, 2026, 1);

        var line = Assert.Single(await LinesAsync(host, graveId, 2026, 1));
        Assert.Equal(30.00m, line.UnitPrice);
        Assert.Equal("Pflege, anteilig 02–04/2026 (3 von 6 Monaten)", line.Description);
    }

    [Fact]
    public async Task A_half_year_outside_the_term_is_neither_billed_nor_skipped()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var graveId = await SeedAsync(host, new DateOnly(2026, 5, 1), null, ("Pflege", null, 60m));

        var result = await GenerateAsync(client, 2025, 2);

        Assert.Empty(await LinesAsync(host, graveId, 2025, 2));
        Assert.Equal(0, result.GetProperty("InvoicedGraves").GetInt32());
        Assert.Empty(result.GetProperty("SkippedGraves").EnumerateArray());
    }

    [Fact]
    public async Task A_half_year_with_nothing_to_bill_is_skipped()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        await SeedAsync(host, new DateOnly(2026, 1, 1), null, ("Herbst", BillingQuarter.Autumn, 30m));

        var result = await GenerateAsync(client, 2026, 1);

        var skip = Assert.Single(result.GetProperty("SkippedGraves").EnumerateArray().ToArray());
        Assert.Equal("NothingToBill", skip.GetProperty("Reason").GetString());
    }

    [Fact]
    public async Task The_status_leaves_out_graves_outside_the_term()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var graveId = await SeedAsync(host, new DateOnly(2026, 5, 1), null, ("Pflege", null, 60m));

        using var before = await ODataJson.ReadAsync(await client.GetAsync("/odata/GraveInvoicingStatus?year=2025&half=2"));
        using var during = await ODataJson.ReadAsync(await client.GetAsync("/odata/GraveInvoicingStatus?year=2026&half=1"));

        Assert.DoesNotContain(
            before.RootElement.GetProperty("value").EnumerateArray(),
            g => g.GetProperty("Id").GetInt64() == graveId
        );
        Assert.Contains(
            during.RootElement.GetProperty("value").EnumerateArray(),
            g => g.GetProperty("Id").GetInt64() == graveId
        );
    }
}
```

Check how `Jobs(id)` exposes `ResultJson` (a string property on `Job`; if it is not in the payload, read the job from the DbContext instead) and how `GraveSkip` serializes (`Number`, `Reason`).

- [ ] **Step 2: Run, expect failures** (generation bills 60,00 in full and the outside-term half-year bills too).

- [ ] **Step 3: Generator** — in `GenerateForGraveAsync`, replace the `var graveLines = graveItems.Select(…).ToList();` block (keep the three skip checks before it):

```csharp
        // only the months of the half-year inside the term are billed; the job
        // never hands over a grave outside it, but a direct caller might
        var period = HalfYearBilling.PeriodIn(grave.StartDate, grave.EndDate, year, half);
        if (period is null)
        {
            return new GraveGenerationResult(null, "OutsideTerm");
        }

        var links = graveItems
            .Select(item =>
                GraveBilling.LinkFor(item.Item, item.Count, item.UnitPrice, item.PriceIncludesVat)
            )
            .ToList();
        var graveLines = HalfYearBilling.Lines(links, period);
        if (graveLines.Count == 0)
        {
            return new GraveGenerationResult(null, "NothingToBill");
        }
```

`graveLines` was a `List<GraveLine>`; it is now `IReadOnlyList<GraveLine>`, which `GraveShare.Problems` and the `.Select((line, index) => …)` below accept unchanged. Update the method's summary to mention the two new skip reasons.

- [ ] **Step 4: Job** — in `GraveInvoicesJobHandler.ExecuteAsync`:

```csharp
        // a grave outside its term for this half-year has nothing to be billed
        // for; it is not "skipped", it is simply not due
        var todo = graves
            .Where(g =>
                !invoiced.Contains(g.Id)
                && HalfYearBilling.PeriodIn(g.StartDate, g.EndDate, payload.Year, payload.Half)
                    is not null
            )
            .ToList();
```

Add `using Burkard.Invoicing.Domain;`.

- [ ] **Step 5: Status** — in `GraveInvoicingStatusController.Status`, filter `graves` before projecting `value`:

```csharp
        // a grave whose term does not reach this half-year is not due; one that
        // was invoiced for it anyway (before the term counted) stays listed
        var value = graves
            .Where(g =>
                invoicedIds.Contains(g.Id)
                || HalfYearBilling.PeriodIn(g.StartDate, g.EndDate, year, half) is not null
            )
            .Select(g => new { … unchanged … });
```

Add `using Burkard.Invoicing.Domain;`.

- [ ] **Step 6: Run `GraveTermGenerationTests` and every `*Generation*`, `GraveShareGenerationTests`, `JobTests`, `BulkDownloadTests`, expect green.**

- [ ] **Step 7: Commit** — `git commit -m "feat(generation): bill only the months of a grave's term and the quarters they touch"`

---

### Task 7: A revision keeps the prorated description

**Files:**

- Modify: `backend/src/Invoicing.Api/Services/GraveInvoiceRevisionService.cs` (`CreateAsync`, `BuildGraveLinesAsync`)
- Test: `backend/tests/Invoicing.IntegrationTests/GraveInvoiceRevisionTests.cs`

- [ ] **Step 1: Write the failing test** — append to `GraveInvoiceRevisionTests.cs`. It seeds a grave starting 2026-05 directly (copy the seeding pattern used at the top of the file, with `StartDate = new DateOnly(2026, 5, 1)`, one recipient, one item priced 60,00 without a quarter), generates 2026/1 with `GraveGenerationHelper.RunAsync(client, 2026, 1)`, reads the invoice id, then posts a revision with the same item at 21,00:

```csharp
    [Fact]
    public async Task A_revision_keeps_the_prorated_description()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var (graveId, itemId) = await SeedProratedGraveAsync(host); // StartDate 2026-05, item 60,00
        await GraveGenerationHelper.RunAsync(client, 2026, 1);
        var invoiceId = await FirstInvoiceIdAsync(host, graveId);

        var response = await client.PostAsync(
            "/odata/CreateGraveInvoiceRevision",
            ODataJson.Body(
                new
                {
                    invoiceId,
                    lines = new[] { new { itemId, count = 1, unitPrice = 21.00m, priceIncludesVat = false } },
                }
            )
        );
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var line = await db
            .Invoices.Where(i => i.GraveId == graveId && i.IsLatestRevision)
            .SelectMany(i => i.Lines)
            .SingleAsync();
        Assert.Equal("Grabpflege, anteilig 05–06/2026 (2 von 6 Monaten)", line.Description);
        Assert.Equal(21.00m, line.UnitPrice);
    }
```

Write `SeedProratedGraveAsync` and `FirstInvoiceIdAsync` as private helpers in the class, modelled on the file's existing seeding (read it; match the request body shape the file's other `CreateGraveInvoiceRevision` calls use, including property casing).

- [ ] **Step 2: Run, expect the description to be "Grabpflege".**

- [ ] **Step 3: Implement** — in `CreateAsync`, before `BuildGraveLinesAsync`:

```csharp
        // the edited revision's descriptions carry what generation wrote, such
        // as ", anteilig 05–06/2026 (2 von 6 Monaten)"; the item's own text
        // would drop it on every edit
        var previous = await _db
            .InvoiceLines.AsNoTracking()
            .Where(l => l.InvoiceId == edited.Id)
            .OrderBy(l => l.LineNumber)
            .ToListAsync(ct);
        var graveLines = await BuildGraveLinesAsync(lineInputs, previous, ct);
```

(Check the FK property name on `InvoiceLine`; if it is not `InvoiceId`, use `_db.Invoices.Where(i => i.Id == edited.Id).SelectMany(i => i.Lines)`.)

In `BuildGraveLinesAsync(IReadOnlyList<InvoiceLineInput> inputs, IReadOnlyList<InvoiceLine> previous, CancellationToken ct)`, before the loop `var unused = previous.ToList();`, and inside it, before `lines.Add`:

```csharp
            // matched by item in line order, each earlier line used once; a
            // newly added item takes its own description
            var earlier = unused.FirstOrDefault(l => l.ItemId == item.Id);
            if (earlier is not null)
            {
                unused.Remove(earlier);
            }
```

and pass `earlier?.Description ?? item.Description` as the `GraveLine`'s description.

- [ ] **Step 4: Run all `*Revision*` tests, expect green.**

- [ ] **Step 5: Commit** — `git commit -m "fix(revisions): keep a grave invoice line's prorated description when it is edited"`

---

### Task 8: Import reads the quarter, caps recipients and checks the term's invoices

**Files:**

- Modify: `backend/src/Invoicing.Api/Services/Import/ImportArchiveLayout.cs` (`ItemsHeader`)
- Modify: `backend/src/Invoicing.Api/Services/Import/ImportModel.cs` (`ItemRow`)
- Modify: `backend/src/Invoicing.Api/Services/Import/ImportValidator.cs` (`ReadItems`, `CheckGraveRecipients`, `CheckGraveSplits`, `Describe`)
- Modify: `backend/src/Invoicing.Api/Services/Import/ImportService.cs:70-80`
- Test: `backend/tests/Invoicing.IntegrationTests/ImportTests.cs`
- Modify: `e2e/tests/import-export.spec.ts` (`ITEMS_HEADER` and its item rows)

**Interfaces:**

- Produces: items.csv header `ItemNumber,Description,VatRate,Price,Quarter`; problems `QuarterInvalid` (args `[raw, "Spring, Summer, Autumn, Winter"]`), `TooManyRecipients` (args `["4"]`), `PricesDoNotDivide` with key `PricesDoNotDivideIn` (args `[label, rate, amount, recipients]`) and `PriceSuggestion` problems with keys `PriceSuggestion1`/`PriceSuggestion2` (args `[itemNumber, price…]`), all on the grave's row in `graves.csv`.

- [ ] **Step 1: Update the fixtures** — in `ImportTests.cs` set `ItemsHeader = "ItemNumber,Description,VatRate,Price,Quarter\r\n"` and give every items.csv data row a fifth, empty column (append `,` before `\r\n`, e.g. `"9001,Grabpflege,19,120.01,\r\n"`). Do the same for `ITEMS_HEADER` and the item rows in `e2e/tests/import-export.spec.ts`. Run `ImportTests` and confirm that only the split test now fails (header change alone breaks nothing else once rows have five columns).

- [ ] **Step 2: Write the failing tests** — change `A_grave_whose_prices_do_not_divide_is_refused_naming_what_would_work` to the new report (grave IMP-1 starts 2026-01, so its first failing invoice is the full start half-year):

```csharp
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problems = await ProblemsAsync(response);
        Assert.All(problems, p => Assert.Equal(("graves.csv", 2), (p.File, p.Row)));

        var split = Assert.Single(problems, p => p.Code == "PricesDoNotDivide");
        Assert.Equal(
            "Anfang H1 2026: VAT 19 %: 120.01 does not split evenly across 2 recipients. "
                + "Adjust a price at this VAT rate.",
            split.Message
        );
        var suggestion = Assert.Single(problems, p => p.Code == "PriceSuggestion");
        Assert.Equal("Item 9001: 120.00 or 120.02 would work.", suggestion.Message);
```

(Check the record `ProblemsAsync` returns; if it exposes `MessageKey`/`MessageArgs`, also assert `"PricesDoNotDivideIn"` with args `["Anfang H1 2026", "19", "120.01", "2"]` and `"PriceSuggestion2"` with `["9001", "120.00", "120.02"]`.)

Add, using the file's `Zip`, `Upload`, `ProblemsAsync` and header constants:

```csharp
    [Fact]
    public async Task An_items_quarter_is_imported()
    {
        using var host = new ApiTestHost();
        await ConfigureVatAsync(host, 19m);
        using var client = host.CreateClient();
        var archive = Zip(
            ("recipients.csv", RecipientsHeader),
            ("items.csv", ItemsHeader + "9001,Sommerbepflanzung,19,40,Summer\r\n9002,Grabpflege,19,60,\r\n"),
            ("graves.csv", GravesHeader),
            ("mapping.csv", MappingHeader)
        );

        var response = await client.PostAsync("/odata/ImportArchive", Upload(archive));

        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(BillingQuarter.Summer, (await db.InvoiceItems.SingleAsync(i => i.Number == "9001")).Quarter);
        Assert.Null((await db.InvoiceItems.SingleAsync(i => i.Number == "9002")).Quarter);
    }

    [Fact]
    public async Task An_unknown_quarter_is_refused()
    {
        using var host = new ApiTestHost();
        await ConfigureVatAsync(host, 19m);
        using var client = host.CreateClient();
        var archive = Zip(
            ("recipients.csv", RecipientsHeader),
            ("items.csv", ItemsHeader + "9001,Sommerbepflanzung,19,40,Sommer\r\n"),
            ("graves.csv", GravesHeader),
            ("mapping.csv", MappingHeader)
        );

        var response = await client.PostAsync("/odata/ImportArchive", Upload(archive));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(
            await ProblemsAsync(response),
            p => p.File == "items.csv" && p.Row == 2 && p.Code == "QuarterInvalid"
        );
    }

    [Fact]
    public async Task A_grave_with_five_recipients_is_refused()
    {
        using var host = new ApiTestHost();
        await ConfigureVatAsync(host, 19m);
        using var client = host.CreateClient();
        var recipients = string.Concat(Enumerable.Range(0, 5).Select(i => $"E-{i},Name {i},Weg {i}\r\n"));
        var links = string.Concat(Enumerable.Range(0, 5).Select(i => $"Recipient,IMP-5,E-{i},,\r\n"));
        var archive = Zip(
            ("recipients.csv", RecipientsHeader + recipients),
            ("items.csv", ItemsHeader + "9001,Grabpflege,19,120.00,\r\n"),
            ("graves.csv", GravesHeader + "IMP-5,27,Grabstätte,DoubleOutside,2026-01,\r\n"),
            ("mapping.csv", MappingHeader + "Item,IMP-5,9001,1,\r\n" + links)
        );

        var response = await client.PostAsync("/odata/ImportArchive", Upload(archive));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(
            await ProblemsAsync(response),
            p => p.File == "graves.csv" && p.Row == 2 && p.Code == "TooManyRecipients"
        );
    }
```

Also extend the template test that asserts the template's headers (search `downloadImportTemplate`/`ImportTemplate` in `ImportTests.cs`) if it compares the items header literally.

- [ ] **Step 3: Run, expect failures.**

- [ ] **Step 4: Implement**

`ImportArchiveLayout.ItemsHeader` gains `"Quarter"` as the fifth entry.

`ImportModel.ItemRow` gains a last parameter `BillingQuarter? Quarter`.

`ReadItems`: `HasColumns(row, 5, …)`; read `var rawQuarter = row[4].Trim();` and, after the price block and before the duplicate check:

```csharp
            BillingQuarter? quarter = null;
            if (rawQuarter.Length > 0)
            {
                // a member name only, as for a grave's position: Enum.TryParse
                // alone would also take "2"
                if (!Enum.GetNames<BillingQuarter>().Contains(rawQuarter)
                    || !Enum.TryParse(rawQuarter, out BillingQuarter parsedQuarter))
                {
                    var allowed = string.Join(", ", Enum.GetNames<BillingQuarter>());
                    problems.Add(
                        new ImportProblem(
                            file,
                            line,
                            "QuarterInvalid",
                            $"'{rawQuarter}' is not a quarter (expected one of {allowed}).",
                            "QuarterInvalid",
                            [rawQuarter, allowed]
                        )
                    );
                    continue;
                }

                quarter = parsedQuarter;
            }
```

and pass `quarter` to the `ItemRow`.

`ImportService`: the `InvoiceItem` initializer gains `Quarter = row.Quarter,`.

`CheckGraveRecipients`: after the "no recipients" loop, count links per grave and report graves with more than `GraveBilling.MaxRecipients`:

```csharp
        var counts = data
            .RecipientLinks.GroupBy(link => link.GraveNumber, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
        foreach (var grave in data.Graves)
        {
            if (counts.TryGetValue(grave.Number, out var count) && count > GraveBilling.MaxRecipients)
            {
                var max = GraveBilling.MaxRecipients.ToString(CultureInfo.InvariantCulture);
                problems.Add(
                    new ImportProblem(
                        ImportArchiveLayout.GravesFile,
                        grave.Row,
                        "TooManyRecipients",
                        $"A grave can have at most {max} recipients.",
                        "TooManyRecipients",
                        [max]
                    )
                );
            }
        }
```

`CheckGraveSplits`: build links with `GraveBilling.LinkFor(new InvoiceItem { Number = item.Number, Description = item.Description, VatRate = item.VatRate, DefaultPrice = item.Price, Quarter = item.Quarter }, link.Count, null, link.PriceIncludesVat)`; skip a grave whose recipient count exceeds the cap (already reported); then:

```csharp
            var evaluation = GraveBillingCheck.Evaluate(links, grave.StartDate, grave.EndDate, count);
            if (evaluation.FirstFailing is not { } failing)
            {
                continue;
            }

            var label = HalfYearBilling.Label(failing.Example);
            foreach (var problem in failing.Problems)
            {
                problems.Add(Describe(ImportArchiveLayout.GravesFile, grave.Row, label, problem, count));
            }

            foreach (var (index, prices) in evaluation.Suggestions.OrderBy(s => s.Key))
            {
                problems.Add(Suggest(ImportArchiveLayout.GravesFile, grave.Row, links[index].ItemNumber, prices));
            }
```

Rewrite `Describe` (drop the figure variants) and add `Suggest`:

```csharp
    /// <summary>One rate of the grave's first failing invoice, named by its
    /// label. No figures: an amount that fixes this invoice may break another,
    /// so the figures come per line, from <see cref="Suggest"/>.</summary>
    private static ImportProblem Describe(
        string file,
        int row,
        string invoice,
        GraveShareProblem problem,
        int recipients
    )
    {
        var rate = problem.VatRate.ToString("0.##", CultureInfo.InvariantCulture);
        var across = recipients == 1 ? "1 recipient" : $"{recipients} recipients";
        return new ImportProblem(
            file,
            row,
            "PricesDoNotDivide",
            $"{invoice}: VAT {rate} %: {Euros(problem.Amount)} does not split evenly across {across}. "
                + "Adjust a price at this VAT rate.",
            "PricesDoNotDivideIn",
            [invoice, rate, Euros(problem.Amount), recipients.ToString(CultureInfo.InvariantCulture)]
        );
    }

    /// <summary>The prices at which one item would let every invoice divide.</summary>
    private static ImportProblem Suggest(string file, int row, string item, IReadOnlyList<decimal> prices)
    {
        var texts = prices.Select(p => p.ToString("0.00", CultureInfo.InvariantCulture)).ToList();
        return new ImportProblem(
            file,
            row,
            "PriceSuggestion",
            $"Item {item}: {string.Join(" or ", texts)} would work.",
            $"PriceSuggestion{texts.Count}",
            [item, .. texts]
        );
    }
```

Update both methods' doc comments; remove the stale duplicated `<summary>` above the old `Describe`.

- [ ] **Step 5: Run `ImportTests`, expect green.**

- [ ] **Step 6: Commit** — `git commit -m "feat(import)!: read an item's quarter, cap a grave at four recipients and check every invoice of its term" -m "BREAKING CHANGE: items.csv needs the Quarter column."`

---

### Task 9: Frontend data layer — generated client, quarters, preview endpoint

**Files:**

- Regenerate: `frontend/src/api/metadata.xml`, `frontend/src/api/generated/**`
- Create: `frontend/src/lib/quarter.ts`
- Modify: `frontend/src/lib/halfYear.ts`
- Modify: `frontend/src/lib/billing.ts`
- Modify: `frontend/src/api/endpoints.ts`

**Interfaces:**

- Produces:
  - `QUARTERS = ["Spring","Summer","Autumn","Winter"] as const`, `type QuarterName`, `quarterLabelKey(quarter: string | null | undefined): string` (`"quarterSpring"`… or `"quarterNone"`)
  - `halfYearInTerm(start: string, end: string | null, year: number, half: number): boolean`
  - `MAX_RECIPIENTS = 4`; `previewLines(lines: DraftLine[], defaultPrice: (itemId: number) => number | null): { inputs: InvoiceLineInput[]; keys: number[] }`
  - `GraveItemLink.Item.Quarter: QuarterName | null`
  - `previewGraveBilling(input: PreviewRequest): Promise<BillingPreview>` with `PreviewRequest { recipients: number; startMonth: string | null; endMonth: string | null; lines: InvoiceLineInput[] }` and the types `BillingExampleKind`, `BillingPreviewLine`, `BillingPreviewInvoice`, `BillingPreview`

- [ ] **Step 1: Regenerate the OData client** — from the repo root `docker compose -f docker-compose.e2e.yml -p invoicing-e2e up -d --build`, wait for `curl -sf http://localhost:8081/odata/\$metadata >/dev/null`, then `cd frontend && npm run gen:odata`, then `cd ../e2e && npm run stack:down`. Expected: `metadata.xml` gains `BillingQuarter` and `InvoiceItem.Quarter`; `src/api/generated/…/billing-quarter/BillingQuarter.ts` appears.

- [ ] **Step 2: `lib/quarter.ts`**

```ts
import type { BillingQuarter } from "@/api/generated";

/** The backend's BillingQuarter members, in calendar order. */
export const QUARTERS = ["Spring", "Summer", "Autumn", "Winter"] as const;

export type QuarterName = (typeof QUARTERS)[number];

/** The i18n key naming a quarter; an item without one is billed all year. */
export function quarterLabelKey(quarter: string | null | undefined): string {
  return quarter ? `quarter${quarter}` : "quarterNone";
}

/** A quarter as the editor's select holds it; anything else is "all year". */
export function toQuarterName(value: string | null | undefined): QuarterName | null {
  return QUARTERS.find((quarter) => quarter === value) ?? null;
}

/** The generated client's enum for the same name. A string enum does not take
 *  a literal union directly, so it goes through string. */
export function toBillingQuarter(name: QuarterName | null): BillingQuarter | null {
  return name == null ? null : (name as string as BillingQuarter);
}
```

If `BillingQuarter` is not re-exported from `@/api/generated`, import it from its generated file the way `InvoiceItem` is imported elsewhere.

- [ ] **Step 3: `lib/halfYear.ts`** — append:

```ts
/** Whether a grave's term ("yyyy-MM…" strings, end optional) has a month in
 *  the half-year. Strings of the same shape compare like their months. */
export function halfYearInTerm(
  start: string,
  end: string | null,
  year: number,
  half: number,
): boolean {
  const first = `${year}-${half === 1 ? "01" : "07"}`;
  const last = `${year}-${half === 1 ? "06" : "12"}`;
  return start.slice(0, 7) <= last && (end == null || end.slice(0, 7) >= first);
}
```

- [ ] **Step 4: `lib/billing.ts`** — append (add `import type { DraftLine } from "./lines";` and `import type { InvoiceLineInput } from "@/api/endpoints";` to the existing type import):

```ts
/** How many recipients a grave may split its bill across. */
export const MAX_RECIPIENTS = 4;

function isWholeCents(value: number): boolean {
  return Math.abs(value * 100 - Math.round(value * 100)) < 1e-6;
}

/** The rows a billing preview can judge: an item, a whole count above zero and
 *  a price in whole cents, its own or the item's default. A row still being
 *  typed is left out rather than sent to be refused, which would read as the
 *  check being down. `keys` maps each input back to its row. */
export function previewLines(
  lines: DraftLine[],
  defaultPrice: (itemId: number) => number | null,
): { inputs: InvoiceLineInput[]; keys: number[] } {
  const inputs: InvoiceLineInput[] = [];
  const keys: number[] = [];
  for (const line of lines) {
    if (line.itemId == null || line.count == null) {
      continue;
    }
    if (!Number.isInteger(line.count) || line.count <= 0) {
      continue;
    }
    const price = line.unitPrice ?? defaultPrice(line.itemId);
    if (price == null || !isWholeCents(price)) {
      continue;
    }
    inputs.push({
      itemId: line.itemId,
      count: line.count,
      unitPrice: line.unitPrice,
      priceIncludesVat: line.priceIncludesVat,
    });
    keys.push(line.key);
  }
  return { inputs, keys };
}
```

- [ ] **Step 5: `api/endpoints.ts`** — in `GraveItemLink.Item` add `Quarter: QuarterName | null;` (import the type from `@/lib/quarter`). After `checkGraveBilling` add:

```ts
export type BillingExampleKind =
  "Start" | "StartAndEnd" | "FullFirstHalf" | "FullSecondHalf" | "End";

export interface BillingPreviewLine {
  LineIndex: number;
  ItemNumber: string;
  /** With ", anteilig …" where the invoice is prorated. */
  Description: string;
  Count: number;
  VatRate: number;
  PriceIncludesVat: boolean;
  /** In the unit it was entered in: the gross on a VAT-included line. */
  Price: number;
  UnitPrice: number;
  NetTotal: number;
}

export interface BillingPreviewInvoice {
  Kind: BillingExampleKind;
  /** Null for the generic full half-years. */
  Year: number | null;
  Half: 1 | 2;
  Months: number;
  Ok: boolean;
  NetTotal: number;
  /** What the recipients collect between them; null when it does not divide. */
  GrossTotal: number | null;
  PerRecipient: number | null;
  Problems: GraveBillingProblem[];
  Lines: BillingPreviewLine[];
}

export interface BillingPreview {
  Ok: boolean;
  Recipients: number;
  Invoices: BillingPreviewInvoice[];
  LineSuggestions: { LineIndex: number; Suggestions: number[] }[];
}

export interface PreviewRequest {
  recipients: number;
  /** "yyyy-MM" */
  startMonth: string | null;
  /** "yyyy-MM"; null for a grave without an end */
  endMonth: string | null;
  lines: InvoiceLineInput[];
}

/** Every invoice the grave's term will produce, checked like the save checks
 *  them, with per-line prices that would make all of them divide. */
export function previewGraveBilling(input: PreviewRequest): Promise<BillingPreview> {
  return postJson<BillingPreview>("/odata/PreviewGraveBilling", {
    Recipients: input.recipients,
    StartMonth: input.startMonth,
    EndMonth: input.endMonth,
    Lines: input.lines.map((line) => ({
      ItemId: line.itemId,
      Count: line.count,
      UnitPrice: line.unitPrice,
      PriceIncludesVat: line.priceIncludesVat,
    })),
  });
}
```

- [ ] **Step 6: Verify** — `cd frontend && npm run typecheck && npm run lint && npm run build`. Expected: clean.

- [ ] **Step 7: Commit** — `git commit -m "feat(frontend): read item quarters and the grave billing preview"`

---

### Task 10: Quarter in the item dialog, list and line caption; the preview component

**Files:**

- Modify: `frontend/src/components/ItemDialog.vue`
- Modify: `frontend/src/pages/ItemsPage.vue`
- Modify: `frontend/src/components/LineEditor.vue`
- Create: `frontend/src/components/GraveBillingPreview.vue`
- Modify: `frontend/src/i18n/de.json`

**Interfaces:**

- Consumes: Task 9.
- Produces: `LineEditor` prop `suggestions?: Map<number, number[]>` keyed by `DraftLine.key`; `GraveBillingPreview` props `{ preview: BillingPreview | null; pending: boolean; failed: boolean; assumesOneRecipient?: boolean }`; failing cards carry `data-billing-invalid` and `tabindex="-1"`; each card has `role="group"` and `aria-label` = its title.

- [ ] **Step 1: i18n** — add to `de.json` (keep the file's ordering style: append near related keys):

```json
  "itemQuarter": "Quartal",
  "quarterNone": "Ganzjährig",
  "quarterSpring": "Frühling",
  "quarterSummer": "Sommer",
  "quarterAutumn": "Herbst",
  "quarterWinter": "Winter",
  "errorQuarterImmutable": "Das Quartal ist festgelegt, solange Gräber die Leistung verwenden.",
  "errorTooManyRecipients": "Ein Grab kann höchstens 4 Empfänger haben.",
  "errorPricesDoNotDivide1": "Die Rechnung „{0}“ lässt sich nicht gleichmäßig auf die Empfänger aufteilen.",
  "graveRecipientsMax": "Ein Grab hat höchstens 4 Empfänger.",
  "billingPreviewTitle": "Beispielrechnungen",
  "billingPreviewHint": "Sobald eine Leistung mit Preis eingetragen ist, stehen hier die Rechnungen, die dieses Grab erzeugt.",
  "billingPreviewUnavailable": "Prüfung gerade nicht möglich. Beim Speichern wird erneut geprüft.",
  "billingPreviewAssumesOne": "Noch kein Empfänger: gerechnet wird mit einem.",
  "billingPreviewChecking": "Wird geprüft",
  "billingKindStart": "Anfang · {0}",
  "billingKindStartAndEnd": "Anfang und Ende · {0}",
  "billingKindEnd": "Ende · {0}",
  "billingKindFullFirstHalf": "Volles H1",
  "billingKindFullSecondHalf": "Volles H2",
  "billingMonths": "{0} von 6 Monaten",
  "billingOk": "Geht auf",
  "billingNotOk": "Geht nicht auf",
  "billingNothing": "In diesem Halbjahr wird keine Leistung abgerechnet.",
  "billingNetLabel": "Netto",
  "billingGrossLabel": "Brutto",
  "billingPerRecipientLabel": "je Empfänger ({0})",
  "lineSuggestion": "Geht auf mit:",
  "applySuggestedPrice": "Preis {0} für Zeile {1} übernehmen"
```

and change `itemVatRateFixed` to `"MwSt.-Satz und Quartal sind festgelegt, solange Gräber die Leistung verwenden."`.

- [ ] **Step 2: `ItemDialog.vue`** — import `QUARTERS, type QuarterName, quarterLabelKey, toBillingQuarter, toQuarterName` from `@/lib/quarter`. Add `Quarter: null as QuarterName | null` to `form`, fill it on open (`Quarter: toQuarterName(props.item?.Quarter)`), send `Quarter: toBillingQuarter(form.Quarter)` in `payload`. Options:

```ts
const quarterOptions = computed(() => [
  { value: null, label: t("quarterNone") },
  ...QUARTERS.map((value) => ({ value, label: t(quarterLabelKey(value)) })),
]);
```

Template, after the VAT rate block (the existing `<small v-if="linked">` hint moves below the quarter so it covers both):

```vue
<div class="flex flex-col gap-2">
        <label for="itemQuarter" class="font-medium">{{ t("itemQuarter") }}</label>
        <Select
          v-model="form.Quarter"
          input-id="itemQuarter"
          :options="quarterOptions"
          option-value="value"
          option-label="label"
          :disabled="rateLocked"
        />
        <small v-if="linked" class="text-muted-color">{{ t("itemVatRateFixed") }}</small>
      </div>
```

- [ ] **Step 3: `ItemsPage.vue`** — after the VAT column add (import `quarterLabelKey`):

```vue
<Column
  :header="t('itemQuarter')"
  header-class="hidden md:table-cell"
  body-class="hidden md:table-cell"
>
          <template #body="{ data }">{{ t(quarterLabelKey(data.Quarter)) }}</template>
        </Column>
```

- [ ] **Step 4: `LineEditor.vue`**

Script additions (imports: `formatVatRate` from `@/lib/format`, `quarterLabelKey` from `@/lib/quarter`):

```ts
const props = withDefaults(defineProps<{ suggestions?: Map<number, number[]> }>(), {
  suggestions: () => new Map(),
});

/** The item's description, rate and quarter: what decides where and at which
 *  rate the line is billed, without two more columns the drawer has no room
 *  for on a tablet. */
function caption(line: DraftLine): string {
  const picked = item(line);
  if (!picked) {
    return "";
  }
  return [
    picked.Description,
    `${formatVatRate(picked.VatRate)} %`,
    t(quarterLabelKey(picked.Quarter)),
  ].join(" · ");
}

function suggestionsFor(line: DraftLine): number[] {
  return props.suggestions.get(line.key) ?? [];
}
```

Template: the caption `<small v-if="item(line)" …>{{ item(line)?.Description }}</small>` becomes `{{ caption(line) }}`. The price `InputNumber` gets `:invalid="suggestionsFor(line).length > 0"`. After the delete button, still inside the row:

```vue
<div
  v-if="suggestionsFor(line).length > 0"
  class="col-span-2 flex flex-wrap items-center gap-2 text-sm md:col-span-5"
>
        <i class="pi pi-exclamation-triangle text-red-600 dark:text-red-400" aria-hidden="true" />
        <span>{{ t("lineSuggestion") }}</span>
        <Button
          v-for="price in suggestionsFor(line)"
          :key="price"
          :label="formatAmount(price)"
          :aria-label="t('applySuggestedPrice', [formatAmount(price), index + 1])"
          size="small"
          outlined
          @click="line.unitPrice = price"
        />
      </div>
```

- [ ] **Step 5: `GraveBillingPreview.vue`**

```vue
<script setup lang="ts">
import Message from "primevue/message";
import ProgressSpinner from "primevue/progressspinner";
import Tag from "primevue/tag";
import { computed } from "vue";
import { useI18n } from "vue-i18n";
import type { BillingPreview, BillingPreviewInvoice, GraveBillingProblem } from "@/api/endpoints";
import { formatAmount, formatVatRate } from "@/lib/format";
import { halfYearText } from "@/lib/halfYear";

const props = defineProps<{
  preview: BillingPreview | null;
  pending: boolean;
  failed: boolean;
  assumesOneRecipient?: boolean;
}>();

const { t } = useI18n();

const suggested = computed(
  () => new Set((props.preview?.LineSuggestions ?? []).map((entry) => entry.LineIndex)),
);

function title(invoice: BillingPreviewInvoice): string {
  const key = `billingKind${invoice.Kind}`;
  return invoice.Year == null ? t(key) : t(key, [halfYearText(invoice.Year, invoice.Half)]);
}

function problemText(problem: GraveBillingProblem): string {
  const recipients = props.preview?.Recipients ?? 1;
  return t(recipients === 1 ? "pricesDoNotDivideLineOne" : "pricesDoNotDivideLine", [
    formatVatRate(problem.VatRate),
    formatAmount(problem.Amount),
    recipients,
  ]);
}

/** Without a suggestion on any of its lines, the card says what to do itself. */
function needsAdvice(invoice: BillingPreviewInvoice): boolean {
  return !invoice.Ok && !invoice.Lines.some((line) => suggested.value.has(line.LineIndex));
}
</script>

<template>
  <section class="flex flex-col gap-3" :aria-busy="pending">
    <div class="flex items-center gap-2">
      <h3 class="m-0 text-base font-semibold">{{ t("billingPreviewTitle") }}</h3>
      <ProgressSpinner
        v-if="pending"
        style="width: 1.25rem; height: 1.25rem"
        stroke-width="6"
        :aria-label="t('billingPreviewChecking')"
      />
    </div>
    <Message v-if="failed" severity="warn" size="small">
      {{ t("billingPreviewUnavailable") }}
    </Message>
    <Message v-if="preview && assumesOneRecipient" severity="info" size="small">
      {{ t("billingPreviewAssumesOne") }}
    </Message>
    <p v-if="!preview && !failed" class="m-0 text-sm text-muted-color">
      {{ t("billingPreviewHint") }}
    </p>
    <div v-if="preview" class="grid gap-3 sm:grid-cols-2">
      <article
        v-for="(invoice, index) in preview.Invoices"
        :key="`${invoice.Kind}-${index}`"
        role="group"
        :aria-label="title(invoice)"
        class="flex min-w-0 flex-col gap-2 rounded-lg border p-3"
        :class="invoice.Ok ? 'border-surface' : 'border-red-500'"
        :data-billing-invalid="invoice.Ok ? undefined : ''"
        :tabindex="invoice.Ok ? undefined : -1"
      >
        <header class="flex flex-wrap items-start justify-between gap-2">
          <div class="flex min-w-0 flex-col">
            <span class="font-medium">{{ title(invoice) }}</span>
            <small v-if="invoice.Months < 6" class="text-muted-color">
              {{ t("billingMonths", [invoice.Months]) }}
            </small>
          </div>
          <Tag
            :value="invoice.Ok ? t('billingOk') : t('billingNotOk')"
            :severity="invoice.Ok ? 'success' : 'danger'"
            :icon="invoice.Ok ? 'pi pi-check' : 'pi pi-times'"
          />
        </header>
        <p v-if="invoice.Lines.length === 0" class="m-0 text-sm text-muted-color">
          {{ t("billingNothing") }}
        </p>
        <table v-else class="w-full text-sm">
          <tbody>
            <tr v-for="line in invoice.Lines" :key="line.LineIndex">
              <td class="py-0.5 pr-2 align-top whitespace-nowrap">{{ line.ItemNumber }}</td>
              <td class="py-0.5 pr-2 align-top break-words">
                {{ line.Description }}
                <span class="text-muted-color">· {{ formatVatRate(line.VatRate) }} %</span>
              </td>
              <td class="py-0.5 pr-2 text-right align-top tabular-nums whitespace-nowrap">
                {{ line.Count }} ×
              </td>
              <td class="py-0.5 text-right align-top tabular-nums whitespace-nowrap">
                {{ formatAmount(line.Price) }}
              </td>
            </tr>
          </tbody>
        </table>
        <dl
          v-if="invoice.Lines.length > 0"
          class="m-0 grid grid-cols-[1fr_auto] gap-x-3 text-sm tabular-nums"
        >
          <dt class="text-muted-color">{{ t("billingNetLabel") }}</dt>
          <dd class="m-0 text-right">{{ formatAmount(invoice.NetTotal) }}</dd>
          <template v-if="invoice.GrossTotal != null">
            <dt class="text-muted-color">{{ t("billingGrossLabel") }}</dt>
            <dd class="m-0 text-right">{{ formatAmount(invoice.GrossTotal) }}</dd>
            <dt class="text-muted-color">
              {{ t("billingPerRecipientLabel", [`1/${preview.Recipients}`]) }}
            </dt>
            <dd class="m-0 text-right">{{ formatAmount(invoice.PerRecipient) }}</dd>
          </template>
        </dl>
        <ul
          v-if="!invoice.Ok"
          class="m-0 flex list-none flex-col gap-1 p-0 text-sm text-red-700 dark:text-red-400"
        >
          <li v-for="problem in invoice.Problems" :key="problem.VatRate">
            {{ problemText(problem) }}
          </li>
          <li v-if="needsAdvice(invoice)">{{ t("pricesAdjustAPrice") }}</li>
        </ul>
      </article>
    </div>
  </section>
</template>
```

Check that `pricesAdjustAPrice`, `pricesDoNotDivideLine` and `pricesDoNotDivideLineOne` exist in `de.json` (they are used by `lib/billing.ts`) and that `formatAmount` accepts `number | null`.

- [ ] **Step 6: Verify** — `npm run typecheck && npm run lint && npm run build`.

- [ ] **Step 7: Commit** — `git commit -m "feat(frontend): show an item's quarter and rate, offer price suggestions on a line, render billing previews"`

---

### Task 11: The grave editor previews live; the detail page shows it too

**Files:**

- Create: `frontend/src/composables/useGraveBillingPreview.ts`
- Modify: `frontend/src/components/GraveEditor.vue`
- Modify: `frontend/src/pages/GraveDetailPage.vue`
- Modify: `frontend/src/i18n/de.json` (status, skip reasons, import keys)

**Interfaces:**

- Consumes: `previewGraveBilling`, `PreviewRequest`, `BillingPreview` (Task 9); `GraveBillingPreview`, `LineEditor.suggestions` (Task 10); `useDebounced`.
- Produces: `useGraveBillingPreview(input: Ref<PreviewInput | null>)` returning `{ preview: Ref<BillingPreview | null>; answered: Ref<PreviewInput | null>; pending: Ref<boolean>; failed: Ref<boolean>; check: () => Promise<BillingPreview | null> }`, with `PreviewInput = PreviewRequest & { keys: number[] }`.

- [ ] **Step 1: The composable** — `useGraveBillingPreview.ts`:

```ts
import { computed, ref, type Ref, watch } from "vue";
import { type BillingPreview, previewGraveBilling, type PreviewRequest } from "@/api/endpoints";
import { useDebounced } from "./useDebounced";

/** A preview request plus, per line, the editor row it came from. */
export interface PreviewInput extends PreviewRequest {
  keys: number[];
}

/** Asks for the grave's billing preview once the input has been still for
 *  300 ms. An answer to an older input is dropped, and the last answer stays
 *  on screen while the next one is on its way. `answered` is the input the
 *  current preview belongs to, so its line indexes map to the right rows even
 *  while the user keeps typing. */
export function useGraveBillingPreview(input: Ref<PreviewInput | null>) {
  const preview = ref<BillingPreview | null>(null);
  const answered = ref<PreviewInput | null>(null);
  const pending = ref(false);
  const failed = ref(false);
  const key = computed(() => JSON.stringify(input.value));
  const debounced = useDebounced(key, 300);
  let latest = 0;

  async function run(raw: string): Promise<BillingPreview | null> {
    const current = ++latest;
    const value = JSON.parse(raw) as PreviewInput | null;
    if (value == null) {
      preview.value = null;
      answered.value = null;
      pending.value = false;
      failed.value = false;
      return null;
    }
    pending.value = true;
    try {
      const answer = await previewGraveBilling(value);
      if (current === latest) {
        preview.value = answer;
        answered.value = value;
        failed.value = false;
      }
      return answer;
    } catch {
      if (current === latest) {
        failed.value = true;
      }
      return null;
    } finally {
      if (current === latest) {
        pending.value = false;
      }
    }
  }

  watch(debounced, (raw) => void run(raw), { immediate: true });

  /** Checks the current input now, without waiting for the debounce. */
  function check(): Promise<BillingPreview | null> {
    return run(key.value);
  }

  return { preview, answered, pending, failed, check };
}
```

- [ ] **Step 2: `GraveEditor.vue`** — replace the `checkGraveBilling`/`billingProblemText`/`showMessage` imports and the `billingProblems` function. Add:

```ts
import { useItemsQuery, useRecipientsQuery } from "@/api/queries";
import { type PreviewInput, useGraveBillingPreview } from "@/composables/useGraveBillingPreview";
import { MAX_RECIPIENTS, previewLines } from "@/lib/billing";
import GraveBillingPreview from "./GraveBillingPreview.vue";

const items = useItemsQuery(ref(""));
const defaultPrices = computed(
  () => new Map((items.data.value ?? []).map((item) => [item.Id, item.DefaultPrice])),
);
const previewInput = computed<PreviewInput | null>(() => {
  if (!visible.value || loading.value) {
    return null;
  }
  // an end before the start is the save's to refuse; a preview of it would
  // only read as the check being down
  if (form.StartDate && form.EndDate && form.EndDate.getTime() < form.StartDate.getTime()) {
    return null;
  }
  const { inputs, keys } = previewLines(
    lines.value,
    (itemId) => defaultPrices.value.get(itemId) ?? null,
  );
  if (inputs.length === 0) {
    return null;
  }
  return {
    recipients: Math.max(1, recipients.value.length),
    startMonth: toIsoMonth(form.StartDate),
    endMonth: toIsoMonth(form.EndDate),
    lines: inputs,
    keys,
  };
});
const billing = useGraveBillingPreview(previewInput);
const suggestions = computed(() => {
  const map = new Map<number, number[]>();
  const keys = billing.answered.value?.keys ?? [];
  for (const entry of billing.preview.value?.LineSuggestions ?? []) {
    const key = keys[entry.LineIndex];
    if (key != null) {
      map.set(key, entry.Suggestions);
    }
  }
  return map;
});

/** Moves focus to the first invoice that does not divide, once it is drawn. */
async function focusFailingInvoice(): Promise<void> {
  await nextTick();
  formEl.value?.querySelector<HTMLElement>("[data-billing-invalid]")?.focus();
}
```

(`nextTick` from `vue`.) In `save()`, replace the `billingProblems` call:

```ts
if (previewInput.value) {
  const check = await billing.check();
  if (check && !check.Ok) {
    showError(new ApiError("PricesDoNotDivide"));
    await focusFailingInvoice();
    return;
  }
}
```

Template: `<LineEditor v-model="lines" :suggestions="suggestions" />`; after the items `<section>`:

```vue
<GraveBillingPreview
  :preview="billing.preview.value"
  :pending="billing.pending.value"
  :failed="billing.failed.value"
  :assumes-one-recipient="recipients.length === 0"
/>
```

The recipient picker row's `v-if="graveId == null"` becomes `v-if="graveId == null && recipients.length < MAX_RECIPIENTS"`, followed by:

```vue
<Message v-if="graveId == null && recipients.length >= MAX_RECIPIENTS" severity="info" size="small">
          {{ t("graveRecipientsMax") }}
        </Message>
```

Remove the now unused `translate` constant if nothing else uses it.

- [ ] **Step 3: `GraveDetailPage.vue`** — import `GraveBillingPreview`, `useGraveBillingPreview`, `type PreviewInput`, and `halfYearInTerm`. Add:

```ts
const inTerm = computed(() =>
  grave.value
    ? halfYearInTerm(grave.value.StartDate, grave.value.EndDate, currentYear, currentHalf)
    : true,
);
const previewInput = computed<PreviewInput | null>(() => {
  const current = grave.value;
  if (!current || current.Items.length === 0) {
    return null;
  }
  return {
    recipients: Math.max(1, current.Recipients.length),
    startMonth: current.StartDate.slice(0, 7),
    endMonth: current.EndDate?.slice(0, 7) ?? null,
    lines: current.Items.map((link) => ({
      itemId: link.Item.Id,
      count: link.Count,
      unitPrice: link.UnitPrice,
      priceIncludesVat: link.PriceIncludesVat,
    })),
    keys: current.Items.map((_, index) => index),
  };
});
const billing = useGraveBillingPreview(previewInput);
```

The status `Tag` becomes:

```vue
<Tag
  :value="
    detail.data.value.currentInvoiced
      ? t('statusInvoiced')
      : inTerm
        ? t('statusOpen')
        : t('statusOutsideTerm')
  "
  :severity="detail.data.value.currentInvoiced ? 'success' : inTerm ? 'danger' : 'secondary'"
  :aria-label="
    detail.data.value.currentInvoiced
      ? t('statusInvoicedAnnouncement')
      : inTerm
        ? t('statusOpenAnnouncement')
        : t('statusOutsideTermAnnouncement')
  "
/>
```

After the "Leistungen" `Card`, add:

```vue
<Card>
      <template #content>
        <GraveBillingPreview
          :preview="billing.preview.value"
          :pending="billing.pending.value"
          :failed="billing.failed.value"
        />
      </template>
    </Card>
```

- [ ] **Step 4: Remaining texts in `de.json`**

```json
  "statusOutsideTerm": "außerhalb der Laufzeit",
  "statusOutsideTermAnnouncement": "Das aktuelle Halbjahr liegt außerhalb der Laufzeit dieses Grabs.",
  "skipReasonNothingToBill": "In diesem Halbjahr keine Leistung",
  "skipReasonOutsideTerm": "Außerhalb der Laufzeit",
  "importProblemQuarterInvalid": "„{0}“ ist kein Quartal; erlaubt sind: {1}.",
  "importProblemTooManyRecipients": "Ein Grab kann höchstens {0} Empfänger haben.",
  "importProblemPricesDoNotDivideIn": "{0}: MwSt. {1} %: {2} lässt sich nicht gleichmäßig auf {3} Empfänger aufteilen. Bitte einen Preis mit diesem Satz anpassen.",
  "importProblemPriceSuggestion1": "Leistung {0}: Möglich wäre {1}.",
  "importProblemPriceSuggestion2": "Leistung {0}: Möglich wären {1} oder {2}."
```

Remove `importProblemPricesDoNotDivide0`, `…1`, `…2`. Check how `ImportExportPage.vue` formats amounts in args (`120.01` arrives invariant); if it reformats `PricesDoNotDivide*` args by key, extend that to the new keys (search the page for `PricesDoNotDivide`).

- [ ] **Step 5: Verify** — `npm run typecheck && npm run lint && npm run build`. Then a manual probe against the E2E stack, never the dev stack: start it (`docker compose -f docker-compose.e2e.yml -p invoicing-e2e up -d --build`), run `VITE_PROXY_TARGET=http://localhost:8081 npm run dev -- --port 5173`, create an item 60,01 and a grave starting 05/2026 with two recipients, and check the cards, the suggestion buttons and that clicking one turns every card green. Stop the stack afterwards.

- [ ] **Step 6: Commit** — `git commit -m "feat(frontend): preview a grave's invoices while it is edited and on its page"`

---

### Task 12: E2E coverage

**Files:**

- Modify: `e2e/tests/graves.spec.ts`
- Modify: `e2e/tests/masterdata.spec.ts`
- Modify: `e2e/tests/layout.spec.ts`

- [ ] **Step 1: Rewrite the refusal test** — `"a grave whose prices do not divide is refused until a price is changed"` no longer sees a dialog. Replace the part from `await editor.getByRole("button", { name: "Speichern" }).click();` to the end with:

```ts
// the preview says so before anything is saved: the full half-years cannot
// be split, and the line offers the nearest prices that can
const failing = editor.getByRole("group", { name: "Volles H1" });
await expect(failing).toContainText("Geht nicht auf");
await expect(failing).toContainText("47,80");
// formatAmount writes "47,79 €"
const fix = editor.getByRole("button", { name: "Preis 47,79 € für Zeile 1 übernehmen" });
await expect(fix).toBeVisible();

// saving anyway is refused and lands on the failing invoice
await editor.getByRole("button", { name: "Speichern" }).click();
await expect(toasts(page)).toContainText("nicht gleichmäßig");
await expect(editor.locator("[data-billing-invalid]").first()).toBeFocused();

await fix.click();
await expect(editor.getByRole("spinbutton", { name: "Preis 1" })).toHaveValue("47,79");
await expect(editor.getByRole("group", { name: "Volles H1" })).toContainText("Geht auf");
await saveEditor(editor);

await expect(editor).toHaveCount(0);
await expect(page.getByRole("table")).toContainText(marker);
```

`startNewGrave` fills 01/2026 to 12/2040, so all four invoices are full half-years and the suggestions stay 47,79 and 47,82, as before.

- [ ] **Step 2: A prorated, seasonal grave** — new test in `graves.spec.ts`:

```ts
test("the grave editor previews a prorated start and a seasonal item", async ({ page }) => {
  const marker = `GQ-${Date.now()}`;
  await ensureRow(page, "/odata/VatRates", { Rate: 19 });
  await createRow(page, "/odata/InvoiceItems", {
    Number: `${marker}-pflege`,
    Description: "Grabpflege",
    VatRate: 19,
    DefaultPrice: 60,
  });
  await createRow(page, "/odata/InvoiceItems", {
    Number: `${marker}-sommer`,
    Description: "Sommerbepflanzung",
    VatRate: 19,
    DefaultPrice: 40,
    Quarter: "Summer",
  });

  await page.goto("/graves");
  const editor = await startNewGrave(page, marker);
  await fillDate(page, editor.getByLabel("Beginn"), "05/2026");
  await editor.getByRole("button", { name: "Zeile hinzufügen" }).click();
  await pickItem(page, 1, `${marker}-pflege`);
  await editor.getByRole("button", { name: "Zeile hinzufügen" }).click();
  await pickItem(page, 2, `${marker}-sommer`);

  // rate and quarter under the item
  await expect(editor).toContainText("Sommerbepflanzung · 19 % · Sommer");

  const start = editor.getByRole("group", { name: "Anfang · H1 2026" });
  await expect(start).toContainText("2 von 6 Monaten");
  await expect(start).toContainText("anteilig 05–06/2026");
  await expect(start).toContainText("20,00");
  await expect(start).toContainText("Sommerbepflanzung");
  await expect(editor.getByRole("group", { name: "Volles H2" })).not.toContainText(
    "Sommerbepflanzung",
  );
  await expect(editor).toContainText("Noch kein Empfänger");
});
```

`startNewGrave` fills 01/2026 to 12/2040; the `fillDate` call above overrides the start.

- [ ] **Step 3: The recipient cap** — new test: seed five recipients with `createRow`, open a new grave, add four with the file's `addRecipient` helper, and assert `editor.getByRole("button", { name: "Hinzufügen", exact: true })` has count 0 and the editor contains "Ein Grab hat höchstens 4 Empfänger."

- [ ] **Step 4: The item dialog** — in `masterdata.spec.ts`, a test that creates an item with "Quartal" = "Herbst" through the dialog (use `pickOption` on `#itemQuarter`), reopens it and expects "Herbst"; and that an item linked to a grave (`seedGrave`) shows the quarter select disabled.

- [ ] **Step 5: Layout** — in `layout.spec.ts` `"graves: the editor spends its width on the fields"`, after adding the recipient: `await expect(editor.getByRole("group", { name: "Volles H1" })).toBeVisible();` before `expectNoSidewaysScrolling(page)`, so the cards are on screen when sideways scrolling is checked on phone and tablet.

- [ ] **Step 6: Run** — `cd e2e && npm test -- tests/graves.spec.ts tests/masterdata.spec.ts tests/import-export.spec.ts --project=desktop`, then `npm test -- tests/layout.spec.ts`. Expected: green (the phone test marked `test.fail` stays "expected to fail"). A red spec in a full run may be cross-spec interference: re-run it alone before blaming the change.

- [ ] **Step 7: Commit** — `git commit -m "test(e2e): cover the billing preview, price suggestions, quarters and the recipient cap"`

---

### Task 13: Documentation and the dev database

**Files:**

- Modify: `docs/spec.md`

- [ ] **Step 1: Update `docs/spec.md`**
  - User Story 6: a scenario for a grave starting 05/2026 billed 2/6 of its all-year items and its Sommer item in full for H1 2026; a scenario for a half-year outside the term not being generated.
  - FR-009a: the quarter is fixed alongside the VAT rate.
  - FR-019: an item may carry one quarter (Frühling Jan–Mär, Sommer Apr–Jun, Herbst Jul–Sep, Winter Okt–Dez); all-year items are prorated by months ÷ 6 in the first and last half-year; quarter items are billed in full when touched.
  - FR-020: one to four recipients.
  - FR-021a: every invoice the term produces must divide, not only a full half-year.
  - FR-025: generation bills only half-years with a month inside the term.
  - Edge cases: replace "What happens when a grave's end month lies before the selected half-year?" with the new answer (not generated, not listed as open); replace the stale rounding case ("price is raised by whole cents") with the refusal-and-suggestion behaviour.
  - Import section: the `Quarter` column and its values.

- [ ] **Step 2: Format check** — `npm run format:check` at the root.

- [ ] **Step 3: Commit** — `git commit -m "docs: state quarter items, prorated half-years and the recipient cap in the spec"`

- [ ] **Step 4: Dev database** — the `Quarter` column was folded into a migration the dev database has already applied, so the dev backend will fail on `InvoiceItems.Quarter`. **Ask the user before** running `scripts/reset-dev-db.sh`; commit everything first.

- [ ] **Step 5: Full verification** — whole backend suite, frontend typecheck/lint/build, the full E2E suite (`cd e2e && npm test`), `scripts/test-release-scripts.sh`, and `scripts/next-version.sh` (expected: a major bump because of `feat(import)!`).
