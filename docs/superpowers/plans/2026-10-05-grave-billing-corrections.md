# Automatic Rounding Corrections — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Instead of refusing a grave whose invoices do not split evenly, raise one line per invoice block and VAT rate by the smallest amount (≤ 1,00 €) that makes it split, show it orange, confirm it on save, store it, apply it at generation, and stop a half-year run when stored and recomputed corrections differ.

**Architecture:** The correction rule lives in `Invoicing.Domain` next to `GraveBillingCheck` and is introduced beside the old evaluation first (`EvaluateWithCorrections`), so every task stays green; callers move over one by one (save, preview, generation, import), and the last backend task deletes the old suggestion path and renames the method back to `Evaluate`. Corrections are stored in a new table folded into the unreleased migration. The Vue editor confirms corrections in a dialog before saving.

**Tech Stack:** .NET 10, EF Core (SQLite), ASP.NET Core OData, xUnit; Vue 3, PrimeVue 4, vue-i18n; Playwright.

**Spec:** `docs/superpowers/specs/2026-10-05-grave-billing-corrections-design.md` (builds on `docs/superpowers/specs/2026-10-05-grave-quarters-and-prorated-billing-design.md`)

## Global Constraints

- A correction raises exactly one line per block (invoice of the term) and VAT rate, never lowers, in cent steps, by at most 1,00 € (`CorrectionReachCents = 100`).
- Candidate order per rate: the rate's all-year lines (item `Quarter` null) in link order, then its quarter lines in link order; the first candidate that can fix the rate within the reach wins.
- The raised price is the price as the block bills it (already prorated in a start/end block; the gross for a gross-entered line). Evenness is `GraveShare.IsEven` over that rate's lines.
- `GraveShare`'s own suggestion search (still used by `ValidateGraveBilling` for revisions) also reaches 100 cents instead of 400.
- Correction comparison key: (`Kind`, `Year`, `Half`, `VatRate`, `ItemId`, `CorrectedPrice`), order-independent; `Year` is null for full half-year blocks.
- Error codes: `CorrectionsChanged` (save), `CorrectionsOutdated` (generation precheck, one arg: grave numbers sorted and joined with ", "), `PricesDoNotDivide` only when no candidate fixes a block.
- `ValidateGraveBilling` (revision editor) behaviour is unchanged apart from the 100-cent reach.
- UI texts are German in `frontend/src/i18n/de.json`; the PDF is unchanged (prints the billed price).
- Never run E2E or probes against the dev stack on 8080; port 8082 is the owner's test instance — leave it alone.
- Commits follow Conventional Commits, end with the model's `Co-Authored-By` trailer; body lines must not start with `word:`.

## Review Focus

1. **A grave saved before this change (no stored corrections) whose blocks now need corrections** — generation must stop with `CorrectionsOutdated` naming it, not bill uncorrected or crash. Pinned in Task 4 (`Generation_stops_when_a_grave_has_no_stored_correction_it_needs`).
2. **Count 2 on the all-year line, 2 recipients, an odd cent from another line** — the all-year line cannot fix it; the next line of the rate must. Pinned in Task 1 (`The_next_line_takes_the_correction_when_the_first_cannot`).
3. **The user changes a price after the preview but before confirming** — the save must refuse with `CorrectionsChanged` and the editor must show the new preview, never store unconfirmed corrections. Pinned in Task 2 (`Saving_with_outdated_corrections_is_refused`).
4. **Editing a grave so that it no longer needs a correction** — its old stored corrections must disappear. Pinned in Task 2 (`Saving_replaces_the_stored_corrections`).
5. **A gross-entered line in a block that does not add up** — the correction raises the gross; net derived as today. Pinned in Task 1 (`A_gross_line_is_raised_as_a_gross`).

---

## How to run things

- Backend tests, worktree root: `scripts/dotnet.sh test Invoicing.sln --filter "FullyQualifiedName~<Class>"`; full suite once before committing.
- Frontend, `frontend/`: `npm run typecheck && npm run lint && npm run build`.
- E2E, `e2e/`: `npm test -- tests/<spec>.ts --project=desktop` (own stack on 8081, down afterwards with `npm run stack:down`).

---

### Task 1: The correction rule (domain)

**Files:**

- Modify: `backend/src/Invoicing.Domain/HalfYearBilling.cs` (make the private `Billed` public as `BillAt`)
- Modify: `backend/src/Invoicing.Domain/GraveBillingCheck.cs`
- Modify: `backend/src/Invoicing.Domain/GraveShare.cs` (reach 400 → 100)
- Test: `backend/tests/Invoicing.IntegrationTests/GraveBillingCorrectionTests.cs` (new), `GraveShareTests.cs` (adjust only if a suggestion now lies beyond 100 cents)

**Interfaces:**

- Produces (namespace `Burkard.Invoicing.Domain`):
  - `HalfYearBilling.BillAt(int index, BillableLink link, decimal price, string description) : BilledLine` (the former private `Billed`)
  - `record BillingCorrection(int LinkIndex, long ItemId, string ItemNumber, decimal VatRate, decimal EnteredPrice, decimal CorrectedPrice)`
  - `record CorrectionKey(BillingExampleKind Kind, int? Year, int Half, decimal VatRate, long ItemId, decimal CorrectedPrice)`
  - `BillingExampleResult` gains a 4th positional parameter `IReadOnlyList<BillingCorrection> Corrections` (the old `Evaluate` passes `[]`)
  - `GraveBillingEvaluation.CorrectionKeys : IReadOnlyList<CorrectionKey>` (all invoices' corrections as keys)
  - `GraveBillingCheck.CorrectionReachCents = 100`
  - `GraveBillingCheck.EvaluateWithCorrections(IReadOnlyList<BillableLink> links, DateOnly? start, DateOnly? end, int recipients) : GraveBillingEvaluation` (Suggestions always empty)
  - `GraveBillingCheck.ApplyCorrections(IReadOnlyList<BillableLink> links, IReadOnlyList<BilledLine> billed, IEnumerable<(long ItemId, decimal VatRate, decimal CorrectedPrice)> corrections) : IReadOnlyList<BilledLine>`
  - `GraveBillingCheck.SameCorrections(IEnumerable<CorrectionKey> a, IEnumerable<CorrectionKey> b) : bool`

- [ ] **Step 1: Write the failing tests** — `GraveBillingCorrectionTests.cs`:

```csharp
using System;
using System.Linq;
using Burkard.Invoicing.Domain;
using Burkard.Invoicing.Domain.Entities;
using Xunit;

namespace Burkard.Invoicing.IntegrationTests;

/// <summary>A block that does not split evenly is fixed by raising one line of
/// the failing VAT rate by the smallest amount that works.</summary>
public class GraveBillingCorrectionTests
{
    private static readonly DateOnly March2026 = new(2026, 3, 1);
    private static readonly DateOnly October2028 = new(2028, 10, 1);

    private static BillableLink AllYear(long id, decimal price, int count = 1, decimal rate = 19m, bool gross = false) =>
        new(id, $"{id}", $"Leistung {id}", rate, null, count, price, gross);

    private static BillableLink Seasonal(long id, BillingQuarter quarter, decimal price, decimal rate = 19m) =>
        new(id, $"{id}", $"Leistung {id}", rate, quarter, 1, price, false);

    [Fact]
    public void A_block_that_divides_needs_no_correction()
    {
        var evaluation = GraveBillingCheck.EvaluateWithCorrections([AllYear(1, 60m)], null, null, 2);

        Assert.True(evaluation.Ok);
        Assert.Empty(evaluation.CorrectionKeys);
    }

    [Fact]
    public void The_g102_example_raises_the_care_in_the_start_and_end_blocks()
    {
        // 3 recipients; start H1 2026 bills 40,00 + 45,00 = 85,00 and end H2 2028
        // bills 40,00 — both raised to 40,02; the full halves divide
        var links = new[] { AllYear(100, 60m), Seasonal(200, BillingQuarter.Spring, 45m) };

        var evaluation = GraveBillingCheck.EvaluateWithCorrections(links, March2026, October2028, 3);

        Assert.True(evaluation.Ok);
        Assert.Equal(
            new[]
            {
                new CorrectionKey(BillingExampleKind.Start, 2026, 1, 19m, 100, 40.02m),
                new CorrectionKey(BillingExampleKind.End, 2028, 2, 19m, 100, 40.02m),
            },
            evaluation.CorrectionKeys
        );
        var start = evaluation.Invoices[0];
        var correction = Assert.Single(start.Corrections);
        Assert.Equal(40.00m, correction.EnteredPrice);
        Assert.Equal(40.02m, start.Lines[0].Price);
        Assert.Contains("anteilig 03–06/2026", start.Lines[0].Line.Description);
        Assert.Equal(45.00m, start.Lines[1].Price);
    }

    [Fact]
    public void The_all_year_line_is_raised_before_a_seasonal_one_listed_first()
    {
        // full H1: 40,01 + 60,00 = 100,01 over 2 — the seasonal line comes first
        // in the list, but the all-year line takes the cent
        var links = new[] { Seasonal(1, BillingQuarter.Summer, 40.01m), AllYear(2, 60m) };

        var evaluation = GraveBillingCheck.EvaluateWithCorrections(links, null, null, 2);

        var key = Assert.Single(evaluation.CorrectionKeys);
        Assert.Equal(2, key.ItemId);
        Assert.Equal(60.01m, key.CorrectedPrice);
    }

    [Fact]
    public void The_next_line_takes_the_correction_when_the_first_cannot()
    {
        // all-year line with count 2 moves the subtotal in steps of 2 cents, and
        // the other line leaves an odd cent: over 2 recipients only the second
        // all-year line can fix it
        var links = new[] { AllYear(1, 30m, count: 2), AllYear(2, 10.01m) };

        var evaluation = GraveBillingCheck.EvaluateWithCorrections(links, null, null, 2);

        Assert.True(evaluation.Ok);
        var key = Assert.Single(evaluation.CorrectionKeys.Where(k => k.Kind == BillingExampleKind.FullFirstHalf));
        Assert.Equal(2, key.ItemId);
        Assert.Equal(10.02m, key.CorrectedPrice);
    }

    [Fact]
    public void Each_rate_is_corrected_on_its_own()
    {
        var links = new[] { AllYear(1, 60.01m, rate: 19m), AllYear(2, 25.01m, rate: 7m) };

        var evaluation = GraveBillingCheck.EvaluateWithCorrections(links, null, null, 2);

        var full = evaluation.Invoices[0];
        Assert.Equal(new[] { 7m, 19m }, full.Corrections.Select(c => c.VatRate).OrderBy(r => r));
        Assert.True(full.Ok);
    }

    [Fact]
    public void A_gross_line_is_raised_as_a_gross()
    {
        // 100,25 gross at 19 % over 2 does not add up (100,24 collected)
        var evaluation = GraveBillingCheck.EvaluateWithCorrections([AllYear(1, 100.25m, gross: true)], null, null, 2);

        var correction = Assert.Single(evaluation.Invoices[0].Corrections);
        Assert.True(correction.CorrectedPrice > 100.25m);
        Assert.True(correction.CorrectedPrice - 100.25m <= 1.00m);
        var line = evaluation.Invoices[0].Lines[0].Line;
        Assert.Equal(correction.CorrectedPrice, line.EnteredGross);
        Assert.Equal(VatInclusivePrice.NetOf(correction.CorrectedPrice, 19m, true), line.UnitPrice);
        Assert.True(GraveShare.IsEven([line], 2));
    }

    [Fact]
    public void A_quarter_line_takes_the_correction_when_no_all_year_line_can()
    {
        // full H1: 2 × 10,00 + 2 × 10,00 + 0,01 = 40,01 over 2. Both all-year
        // lines move the subtotal in steps of 2 cents, so only the summer line
        // can take the cent
        var links = new[]
        {
            AllYear(1, 10m, count: 2),
            AllYear(2, 10m, count: 2),
            Seasonal(3, BillingQuarter.Summer, 0.01m),
        };

        var evaluation = GraveBillingCheck.EvaluateWithCorrections(links, null, null, 2);

        var key = Assert.Single(evaluation.CorrectionKeys);
        Assert.Equal(BillingExampleKind.FullFirstHalf, key.Kind);
        Assert.Equal(3, key.ItemId);
        Assert.Equal(0.02m, key.CorrectedPrice);
    }

    [Fact]
    public void ApplyCorrections_bills_the_stored_price_for_the_item_and_rate()
    {
        var links = new[] { AllYear(100, 60m) };
        var billed = HalfYearBilling.Bill(links, new BillingPeriod(2026, 1, 3, 6));

        var corrected = GraveBillingCheck.ApplyCorrections(links, billed, [(100L, 19m, 40.02m)]);

        Assert.Equal(40.02m, corrected[0].Price);
        Assert.Equal(billed[0].Line.Description, corrected[0].Line.Description);
    }

    [Fact]
    public void SameCorrections_ignores_order_and_decimal_scale()
    {
        var a = new[]
        {
            new CorrectionKey(BillingExampleKind.Start, 2026, 1, 19m, 1, 40.02m),
            new CorrectionKey(BillingExampleKind.End, 2028, 2, 19m, 1, 40.02m),
        };
        var b = new[]
        {
            new CorrectionKey(BillingExampleKind.End, 2028, 2, 19.0m, 1, 40.020m),
            new CorrectionKey(BillingExampleKind.Start, 2026, 1, 19m, 1, 40.02m),
        };

        Assert.True(GraveBillingCheck.SameCorrections(a, b));
        Assert.False(GraveBillingCheck.SameCorrections(a, b.Take(1)));
    }
}
```

- [ ] **Step 2: Run them, expect a compile failure** (`EvaluateWithCorrections` missing).

- [ ] **Step 3: Implement**

In `HalfYearBilling.cs`, rename the private `Billed(int index, BillableLink link, decimal price, string description)` to `public static BilledLine BillAt(...)` with a summary ("A billed line at a given price — used by `Bill` and by the rounding correction") and update its callers in `Bill`.

In `GraveShare.cs`, `Suggestions`: `distance <= 400` → `distance <= 100`, and update the summary that names the reach.

In `GraveBillingCheck.cs`:

```csharp
/// <summary>A raised price on one line of a block, so that the line's VAT rate
/// splits evenly. <paramref name="EnteredPrice"/> is what the block billed before
/// (prorated where the block is), in the unit the price was entered in.</summary>
public sealed record BillingCorrection(
    int LinkIndex,
    long ItemId,
    string ItemNumber,
    decimal VatRate,
    decimal EnteredPrice,
    decimal CorrectedPrice
);

/// <summary>What identifies a correction when stored corrections are compared
/// with freshly computed ones.</summary>
public sealed record CorrectionKey(
    BillingExampleKind Kind,
    int? Year,
    int Half,
    decimal VatRate,
    long ItemId,
    decimal CorrectedPrice
);
```

`BillingExampleResult` becomes `(BillingExample Example, IReadOnlyList<BilledLine> Lines, IReadOnlyList<GraveShareProblem> Problems, IReadOnlyList<BillingCorrection> Corrections)`; the old `Evaluate` passes `[]`.

`GraveBillingEvaluation` gains:

```csharp
    public IReadOnlyList<CorrectionKey> CorrectionKeys =>
        Invoices
            .SelectMany(invoice =>
                invoice.Corrections.Select(c => new CorrectionKey(
                    invoice.Example.Kind,
                    invoice.Example.Period.Year,
                    invoice.Example.Period.Half,
                    c.VatRate,
                    c.ItemId,
                    c.CorrectedPrice
                ))
            )
            .ToList();
```

`GraveBillingCheck` gains:

```csharp
    /// <summary>How far, in cents, a correction may raise a line.</summary>
    public const int CorrectionReachCents = 100;

    /// <summary>Every invoice of the term, each block corrected per VAT rate
    /// where it does not split evenly. An invoice still has problems only when
    /// no line of a rate can fix it within <see cref="CorrectionReachCents"/>.</summary>
    public static GraveBillingEvaluation EvaluateWithCorrections(
        IReadOnlyList<BillableLink> links,
        DateOnly? start,
        DateOnly? end,
        int recipients
    )
    {
        var invoices = HalfYearBilling
            .Examples(start, end)
            .Select(example =>
            {
                var (lines, corrections) = Correct(
                    links,
                    HalfYearBilling.Bill(links, example.Period),
                    recipients
                );
                var problems =
                    lines.Count == 0
                        ? []
                        : GraveShare.Problems(lines.Select(b => b.Line).ToList(), recipients);
                return new BillingExampleResult(example, lines, problems, corrections);
            })
            .ToList();
        return new GraveBillingEvaluation(invoices, new Dictionary<int, IReadOnlyList<decimal>>());
    }

    private static (List<BilledLine> Lines, List<BillingCorrection> Corrections) Correct(
        IReadOnlyList<BillableLink> links,
        IReadOnlyList<BilledLine> billed,
        int recipients
    )
    {
        var lines = billed.ToList();
        var corrections = new List<BillingCorrection>();
        foreach (var rate in lines.Select(b => b.Line.VatRate).Distinct().OrderBy(r => r))
        {
            bool Even() =>
                GraveShare.IsEven(
                    lines.Where(b => b.Line.VatRate == rate).Select(b => b.Line).ToList(),
                    recipients
                );

            if (Even())
            {
                continue;
            }

            // all-year lines first: they are in every block, so the cent sits on
            // the same line wherever it can
            var candidates = lines
                .Select((line, position) => (line, position))
                .Where(x => x.line.Line.VatRate == rate)
                .OrderBy(x => links[x.line.LinkIndex].Quarter is null ? 0 : 1)
                .ThenBy(x => x.position)
                .Select(x => x.position)
                .ToList();

            foreach (var position in candidates)
            {
                var original = lines[position];
                var link = links[original.LinkIndex];
                BilledLine? fixedLine = null;
                for (var cents = 1; cents <= CorrectionReachCents; cents++)
                {
                    lines[position] = HalfYearBilling.BillAt(
                        original.LinkIndex,
                        link,
                        original.Price + cents / 100m,
                        original.Line.Description
                    );
                    if (Even())
                    {
                        fixedLine = lines[position];
                        break;
                    }
                }

                if (fixedLine is not null)
                {
                    corrections.Add(
                        new BillingCorrection(
                            original.LinkIndex,
                            original.Line.ItemId,
                            original.Line.ItemNumber,
                            rate,
                            original.Price,
                            fixedLine.Price
                        )
                    );
                    break;
                }

                lines[position] = original;
            }
        }

        return (lines, corrections);
    }

    /// <summary>The lines of one block with stored corrections applied: the line
    /// of each correction's item and rate is billed at its corrected price.</summary>
    public static IReadOnlyList<BilledLine> ApplyCorrections(
        IReadOnlyList<BillableLink> links,
        IReadOnlyList<BilledLine> billed,
        IEnumerable<(long ItemId, decimal VatRate, decimal CorrectedPrice)> corrections
    )
    {
        var lines = billed.ToList();
        foreach (var (itemId, rate, price) in corrections)
        {
            var position = lines.FindIndex(b => b.Line.ItemId == itemId && b.Line.VatRate == rate);
            if (position < 0)
            {
                continue;
            }

            var line = lines[position];
            lines[position] = HalfYearBilling.BillAt(
                line.LinkIndex,
                links[line.LinkIndex],
                price,
                line.Line.Description
            );
        }

        return lines;
    }

    /// <summary>Whether two sets of corrections are the same, in any order.</summary>
    public static bool SameCorrections(IEnumerable<CorrectionKey> a, IEnumerable<CorrectionKey> b)
    {
        var left = a.ToList();
        var right = b.ToList();
        return left.Count == right.Count && new HashSet<CorrectionKey>(left).SetEquals(right);
    }
```

(`decimal` equality and hash ignore scale, so `40.02m` and `40.020m` compare equal inside the record.)

- [ ] **Step 4: Run `GraveBillingCorrectionTests`, `GraveBillingCheckTests`, `GraveShareTests`, `HalfYearBillingTests`; fix only a `GraveShareTests` expectation that relied on a suggestion beyond 100 cents (say which). Full suite.**

- [ ] **Step 5: Commit** — `feat(billing): correct a block that does not split by raising one line per rate`

---

### Task 2: Store corrections and confirm them on save

**Files:**

- Create: `backend/src/Invoicing.Domain/Entities/GraveBillingCorrection.cs`
- Modify: `backend/src/Invoicing.Domain/Entities/Grave.cs` (navigation `Corrections`)
- Modify: `backend/src/Invoicing.Api/Persistence/AppDbContext.cs`
- Modify: migration `20261002083107_GraveSiteFieldsAndGermanOnly.cs`, its `.Designer.cs`, `AppDbContextModelSnapshot.cs`
- Modify: `backend/src/Invoicing.Api/Services/GraveService.cs`
- Modify: `backend/src/Invoicing.Api/OData/Controllers/GravesController.cs` (`SaveGraveRequest.Corrections`)
- Test: `backend/tests/Invoicing.IntegrationTests/SaveGraveTests.cs`

**Interfaces:**

- Consumes: Task 1.
- Produces:
  - entity `GraveBillingCorrection { long Id; long GraveId; BillingExampleKind Kind; int? Year; int Half; decimal VatRate; long ItemId; InvoiceItem Item; decimal EnteredPrice; decimal CorrectedPrice }`, `DbSet<GraveBillingCorrection> GraveBillingCorrections`, `Grave.Corrections`
  - `GraveCorrectionInput { string Kind; int? Year; int Half; decimal VatRate; long ItemId; decimal CorrectedPrice }` (in `GraveService.cs`, next to `GraveItemInput`)
  - `GraveService.SaveAsync(long? graveId, GraveFields fields, IReadOnlyList<GraveItemInput> items, IReadOnlyList<long> recipientIds, IReadOnlyList<GraveCorrectionInput> corrections, CancellationToken ct)`
  - SaveGrave request body gains `corrections: [{ kind, year, half, vatRate, itemId, correctedPrice }]` (camelCase like the rest of the body); refusal code `CorrectionsChanged`

- [ ] **Step 1: Write the failing tests** — in `SaveGraveTests.cs`, reuse the file's `GraveBody`-style helper (from the quarter feature) but with an added `corrections` field, and a seeded all-year item at 19 % (add a small local seed if `SeedAsync`'s item is 7 %; keep VAT rates configured):

```csharp
    // 3 recipients, care 60,00 all year, term 03/2026–10/2028: start H1 2026 and
    // end H2 2028 bill 40,00, raised to 40,02
    [Fact]
    public async Task Saving_without_the_needed_corrections_is_refused()
    // POST SaveGrave with corrections = [] → 400, code "CorrectionsChanged"

    [Fact]
    public async Task Saving_with_outdated_corrections_is_refused()
    // corrections = [{ kind = "Start", year = 2026, half = 1, vatRate = 19, itemId, correctedPrice = 40.01 }, { End … 40.02 }] → 400 "CorrectionsChanged"

    [Fact]
    public async Task Saving_with_the_confirmed_corrections_stores_them()
    // corrections = [Start 2026/1 40.02, End 2028/2 40.02] → success;
    // db.GraveBillingCorrections for the grave: 2 rows, EnteredPrice 40.00, CorrectedPrice 40.02, Kind Start/End

    [Fact]
    public async Task Saving_replaces_the_stored_corrections()
    // save as above, then save the same grave (graveId set) with price 60.03 and corrections = [] → success (60,03 divides by 3, 4/6 = 40,02 divides) → 0 rows

    [Fact]
    public async Task A_grave_needing_no_correction_saves_without_any()
    // 60,00 over 2 recipients → corrections = [] → success, 0 rows
```

Write each body in full in the file's style (`client.PostAsync("/odata/SaveGrave", ODataJson.Body(...))`, `ODataJson.ReadAsync`, a scoped `AppDbContext` for the row checks). The recipients must exist (`RecipientsAsync` helper from the quarter feature).

- [ ] **Step 2: Run them, expect failures** (field unknown; table missing).

- [ ] **Step 3: Entity, context, migration**

`GraveBillingCorrection.cs` in `Burkard.Invoicing.Domain.Entities` (`using Burkard.Invoicing.Domain;` for `BillingExampleKind`):

```csharp
/// <summary>A price the rounding correction raised for one block of a grave's
/// term and one VAT rate, as confirmed when the grave was saved. Generation
/// bills it, and checks before a run that it still matches the rule.</summary>
public sealed class GraveBillingCorrection
{
    public long Id { get; set; }
    public long GraveId { get; set; }
    public BillingExampleKind Kind { get; set; }

    /// <summary>The concrete year of a start or end block; null for a full half.</summary>
    public int? Year { get; set; }
    public int Half { get; set; }
    public decimal VatRate { get; set; }
    public long ItemId { get; set; }
    public InvoiceItem Item { get; set; } = null!;
    public decimal EnteredPrice { get; set; }
    public decimal CorrectedPrice { get; set; }
}
```

`Grave` gains `public List<GraveBillingCorrection> Corrections { get; set; } = new();`.

`AppDbContext`: `DbSet<GraveBillingCorrection> GraveBillingCorrections`; `Kind` `HasConversion<string>()`; unique index on (`GraveId`, `Kind`, `VatRate`); FK to `Graves` cascade, to `InvoiceItems` restrict. Fold `CreateTable("GraveBillingCorrections", …)` with its indexes into `Up` of `20261002083107` (and `DropTable` first in `Down`), and add the entity to the Designer and the snapshot exactly as `dotnet ef` would write it (columns alphabetical, `Kind` as `TEXT`, decimals as `TEXT`, the two FKs and the unique index). Do not add a new migration. Ignore the entity in the EDM (`b.Ignore<GraveBillingCorrection>()` is not needed if it is not reachable from an entity set — `Grave.Corrections` makes it reachable, so add `b.EntityType<Grave>().Ignore(g => g.Corrections);` in `EdmModelBuilder`).

- [ ] **Step 4: Save path**

`GraveService.SaveAsync` gains `IReadOnlyList<GraveCorrectionInput> corrections`. `ValidateAsync` already evaluates the term; switch it to `GraveBillingCheck.EvaluateWithCorrections(links, fields.StartDate, fields.EndDate, recipientIds.Count)` and return the evaluation to `SaveAsync` (or recompute there). Then:

```csharp
        // corrections are a promise the user confirmed: a price the editor did
        // not show is never stored
        var confirmed = corrections.Select(c => new CorrectionKey(
            Enum.TryParse<BillingExampleKind>(c.Kind, out var kind) ? kind : (BillingExampleKind)(-1),
            c.Year,
            c.Half,
            c.VatRate,
            c.ItemId,
            c.CorrectedPrice
        ));
        if (!GraveBillingCheck.SameCorrections(evaluation.CorrectionKeys, confirmed))
        {
            throw new ValidationException(
                "CorrectionsChanged",
                "The rounding corrections differ from the ones confirmed."
            );
        }
```

When no recipients or no items are sent (the evaluation is skipped today), treat the computed set as empty. Keep `PricesDoNotDivide` for `evaluation.FirstFailing`. Inside the transaction, after the links: remove the grave's existing `GraveBillingCorrections` and add one row per computed correction (`Kind`, `Year`, `Half`, `VatRate`, `ItemId`, `EnteredPrice`, `CorrectedPrice` from `evaluation.Invoices[*].Corrections` with the invoice's example). Load existing corrections in `LoadForReplaceAsync` (`.Include(g => g.Corrections)`) and clear them there with the links.

`GravesController.SaveGraveRequest` gains `public List<GraveCorrectionInput>? Corrections { get; set; }`, passed as `request.Corrections ?? new()`.

- [ ] **Step 5: Run `SaveGraveTests`, `GraveTests`, `VatIncludedGraveTests`, `GraveRecipientImmutabilityTests`; existing tests that saved a grave needing no correction keep passing; any existing test that expected `PricesDoNotDivide` for a correctable grave now expects `CorrectionsChanged` — update it and say which. Full suite.**

- [ ] **Step 6: Commit** — `feat(graves): store rounding corrections a save confirmed`

---

### Task 3: Preview returns corrections

**Files:**

- Modify: `backend/src/Invoicing.Api/OData/Controllers/GraveBillingValidationController.cs` (`Preview`)
- Test: `backend/tests/Invoicing.IntegrationTests/GraveBillingPreviewTests.cs`

**Interfaces:**

- Produces: preview response — per invoice `Corrections: [{ Kind, Year, Half, LineIndex, VatRate, ItemId, ItemNumber, EnteredPrice, CorrectedPrice }]`; per line `Price` (billed, corrected), `EnteredPrice`, `Corrected`; top-level `LineSuggestions` removed. `Ok` false only beyond the reach.

- [ ] **Step 1: Tests** — replace `A_failing_invoice_puts_its_suggestion_on_the_line` with `A_block_that_does_not_divide_comes_back_corrected`: 1 line all-year 60,01, 2 recipients, start 2026-05 → `Ok` true; the full H1 invoice has one correction `{ Kind: "FullFirstHalf", Year: null, Half: 1, VatRate: 19, ItemNumber: "100", EnteredPrice: 60.01, CorrectedPrice: 60.02 }`, its line has `Price` 60.02, `EnteredPrice` 60.01, `Corrected` true and `GrossTotal` not null; the start invoice (20,00) has no correction and its line `Corrected` false; the response has no `LineSuggestions` property (`Assert.False(body.TryGetProperty("LineSuggestions", out _))`). Keep the other tests.

- [ ] **Step 2: Run, expect failure.**

- [ ] **Step 3: Implement** — call `EvaluateWithCorrections`; per line: `EnteredPrice = invoice.Corrections.FirstOrDefault(c => c.LinkIndex == b.LinkIndex)?.EnteredPrice ?? b.Price`, `Corrected = invoice.Corrections.Any(c => c.LinkIndex == b.LinkIndex)`; per invoice `Corrections` projected with the example's `Kind.ToString()`, `Period.Year`, `Period.Half` and the correction's `LinkIndex` as `LineIndex`; remove `LineSuggestions`.

- [ ] **Step 4: Run the preview and validation tests; full suite.**

- [ ] **Step 5: Commit** — `feat(graves): preview the rounding corrections of every invoice`

---

### Task 4: Generation applies stored corrections; precheck; status count

**Files:**

- Modify: `backend/src/Invoicing.Domain/HalfYearBilling.cs` (`ExampleFor`)
- Create: `backend/src/Invoicing.Api/Services/GraveCorrectionCheck.cs`
- Modify: `backend/src/Invoicing.Api/Program.cs` (register the service like the other scoped services)
- Modify: `backend/src/Invoicing.Api/Services/GraveInvoiceGenerator.cs`
- Modify: `backend/src/Invoicing.Api/OData/Controllers/InvoiceActionsController.cs` (`GenerateGraveInvoices`)
- Modify: `backend/src/Invoicing.Api/OData/Controllers/GraveInvoicingStatusController.cs`
- Test: `HalfYearBillingTests.cs`, `GraveTermGenerationTests.cs`, `GraveGenerationTests.cs`

**Interfaces:**

- Consumes: Tasks 1–2.
- Produces:
  - `HalfYearBilling.ExampleFor(DateOnly start, DateOnly? end, int year, int half) : BillingExample?` — the block a concrete half-year is: `Start`/`StartAndEnd`/`End` with that year, otherwise `FullFirstHalf`/`FullSecondHalf` with `Year` null; null outside the term
  - `GraveCorrectionCheck.OutdatedGraveNumbersAsync(int year, int half, CancellationToken ct) : Task<IReadOnlyList<string>>`
  - `GenerateGraveInvoices` refuses with `400 { error: { code: "CorrectionsOutdated", message, args: ["G-1, G-2"] } }`
  - status rows gain `CorrectionCount`

- [ ] **Step 1: Tests**
  - `HalfYearBillingTests.ExampleFor_names_the_block_of_a_concrete_half_year`: start 2026-05, end 2030-08 → (2026,1) Start with Year 2026; (2027,2) FullSecondHalf with Year null; (2030,2) End; (2025,2) null; start 2026-02/end 2026-04 → (2026,1) StartAndEnd.
  - `GraveTermGenerationTests.Generation_bills_the_stored_correction`: seed (direct DbContext, like the file's `SeedAsync`) a grave starting 2026-03 with care 60,00 all-year and 3 recipients, plus a `GraveBillingCorrection` row (Start, 2026, 1, 19, item, 40.00 → 40.02) and the End/Full rows the rule needs (compute them with `GraveBillingCheck.EvaluateWithCorrections` in the test and store exactly those) → generate 2026/1 → every recipient's line has `UnitPrice` 40.02 and the prorated description.
  - `GraveTermGenerationTests.Generation_stops_when_a_grave_has_no_stored_correction_it_needs`: same grave without correction rows → `POST GenerateGraveInvoices` (2026/1) answers 400 with code `CorrectionsOutdated` and args containing the grave number; no invoices exist afterwards.
  - `GraveTermGenerationTests.Generation_stops_when_a_stored_correction_differs`: rows stored with 40.03 instead of 40.02 → same refusal.
  - `GraveTermGenerationTests.The_status_counts_stored_corrections`: the status row of the grave with two rows has `CorrectionCount` 2.
  - `GraveGenerationTests.A_grave_whose_prices_do_not_divide_is_skipped_rather_than_billed_wrong`: such a grave is now correctable and has no stored corrections, so the run is refused with `CorrectionsOutdated` naming it — rewrite the test to that (keep its seed).

- [ ] **Step 2: Run, expect failures.**

- [ ] **Step 3: `ExampleFor`**

```csharp
    /// <summary>Which block of the term a concrete half-year is: the start, the
    /// end, both, or a full half. Null when the half-year is outside the term.</summary>
    public static BillingExample? ExampleFor(DateOnly start, DateOnly? end, int year, int half)
    {
        if (PeriodIn(start, end, year, half) is not { } period)
        {
            return null;
        }

        var isStart = HalfOf(start) == (year, half);
        var isEnd = end is { } until && HalfOf(until) == (year, half);
        return (isStart, isEnd) switch
        {
            (true, true) => new BillingExample(BillingExampleKind.StartAndEnd, period),
            (true, false) => new BillingExample(BillingExampleKind.Start, period),
            (false, true) => new BillingExample(BillingExampleKind.End, period),
            _ => new BillingExample(
                half == 1 ? BillingExampleKind.FullFirstHalf : BillingExampleKind.FullSecondHalf,
                BillingPeriod.Full(half)
            ),
        };
    }
```

- [ ] **Step 4: Generator** — after `HalfYearBilling.Lines(links, period)` (keep `NothingToBill`): load the grave's stored corrections; `var example = HalfYearBilling.ExampleFor(grave.StartDate, grave.EndDate, year, half)!;` select those with `Kind == example.Kind && Year == example.Period.Year && Half == example.Period.Half`; bill with `GraveBillingCheck.ApplyCorrections(links, HalfYearBilling.Bill(links, period), stored.Select(c => (c.ItemId, c.VatRate, c.CorrectedPrice)))` and use those lines (`.Select(b => b.Line).ToList()`) for the split check and the invoice. Update the comment block that says nothing adjusts a price.

- [ ] **Step 5: `GraveCorrectionCheck`**

```csharp
/// <summary>Whether the corrections stored with the graves still match the
/// rule. A run bills stored corrections, so one that the rule would now compute
/// differently must be looked at and saved again before anything is invoiced.</summary>
public sealed class GraveCorrectionCheck
{
    private readonly AppDbContext _db;

    public GraveCorrectionCheck(AppDbContext db) => _db = db;

    /// <summary>The numbers of the graves due in the half-year, not yet invoiced
    /// for it, whose stored corrections differ from the computed ones.</summary>
    public async Task<IReadOnlyList<string>> OutdatedGraveNumbersAsync(
        int year,
        int half,
        CancellationToken ct
    )
    {
        var invoiced = await _db
            .GraveInvoiceRuns.Where(r => r.Year == year && r.Half == half)
            .Select(r => r.GraveId)
            .ToListAsync(ct);
        var graves = await _db.Graves.AsNoTracking().ToListAsync(ct);
        var links = await GraveBilling.LinksByGraveAsync(_db, ct);
        var recipients = await _db
            .GraveRecipients.GroupBy(r => r.GraveId)
            .Select(g => new { GraveId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.GraveId, x => x.Count, ct);
        var stored = (await _db.GraveBillingCorrections.AsNoTracking().ToListAsync(ct))
            .GroupBy(c => c.GraveId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var outdated = new List<string>();
        foreach (var grave in graves)
        {
            var graveLinks = links.GetValueOrDefault(grave.Id) ?? [];
            if (
                invoiced.Contains(grave.Id)
                || !recipients.TryGetValue(grave.Id, out var count)
                || !GraveBilling.IsDue(grave.StartDate, grave.EndDate, graveLinks, year, half)
            )
            {
                continue;
            }

            var computed = GraveBillingCheck
                .EvaluateWithCorrections(graveLinks, grave.StartDate, grave.EndDate, count)
                .CorrectionKeys;
            var saved = (stored.GetValueOrDefault(grave.Id) ?? []).Select(c => new CorrectionKey(
                c.Kind,
                c.Year,
                c.Half,
                c.VatRate,
                c.ItemId,
                c.CorrectedPrice
            ));
            if (!GraveBillingCheck.SameCorrections(computed, saved))
            {
                outdated.Add(grave.GraveNumber);
            }
        }

        return outdated.OrderBy(n => n, StringComparer.Ordinal).ToList();
    }
}
```

Check `GraveBilling.LinksByGraveAsync`'s real signature in `GraveBilling.cs` and adapt the call. Register `GraveCorrectionCheck` as scoped in `Program.cs` like `GraveService`.

- [ ] **Step 6: Generate action** — inject `GraveCorrectionCheck` into `InvoiceActionsController`; after the half-year validation and before starting the job:

```csharp
        var outdated = await _corrections.OutdatedGraveNumbersAsync(
            request.Year,
            request.Half,
            HttpContext.RequestAborted
        );
        if (outdated.Count > 0)
        {
            return BadRequest(
                new
                {
                    error = new
                    {
                        code = "CorrectionsOutdated",
                        message = "The rounding corrections of some graves have changed.",
                        args = new[] { string.Join(", ", outdated) },
                    },
                }
            );
        }
```

- [ ] **Step 7: Status** — `CorrectionCount` per grave from one grouped query over `GraveBillingCorrections`, added to the projected row.

- [ ] **Step 8: Run the touched classes and every `*Generation*`, `JobTests`, `BulkDownloadTests`; full suite.**

- [ ] **Step 9: Commit** — `feat(generation): bill stored corrections and stop a run whose corrections are outdated`

---

### Task 5: Import stores corrections; old suggestion path removed

**Files:**

- Modify: `backend/src/Invoicing.Api/Services/Import/ImportValidator.cs` (`CheckGraveSplits`, remove `Suggest`)
- Modify: `backend/src/Invoicing.Api/Services/Import/ImportService.cs`
- Modify: `backend/src/Invoicing.Api/Services/Import/ImportModel.cs` (`ImportSummary.CorrectedGraves`)
- Modify: `backend/src/Invoicing.Domain/GraveBillingCheck.cs` (delete old `Evaluate`, `Suggest`, `AllEven`, `With`, `SuggestionReachCents`, `GraveBillingEvaluation.Suggestions`; rename `EvaluateWithCorrections` → `Evaluate`, update all callers)
- Test: `ImportTests.cs`, `GraveBillingCheckTests.cs` (remove suggestion tests), callers' tests

**Interfaces:**

- Produces: `ImportSummary(int Recipients, int Items, int Graves, int ItemLinks, int RecipientLinks, int CorrectedGraves)`; `GraveBillingCheck.Evaluate(links, start, end, recipients)` (corrected); no `Suggestions` anywhere.

- [ ] **Step 1: Tests**
  - `ImportTests.A_grave_whose_prices_do_not_divide_is_refused_naming_what_would_work` becomes `A_grave_whose_prices_do_not_divide_is_imported_with_corrections`: the `UnsplittableArchive` (120,01 over 2, term 2026-01–2040-12) imports; `GraveBillingCorrections` has rows for the grave with `CorrectedPrice` 120.02 for each block that needs one (all four are full here → Start, FullFirstHalf, FullSecondHalf, End); the summary's `CorrectedGraves` is 1.
  - Delete `The_refusal_says_recipient_for_a_single_one` and `A_problem_carries_a_message_key_and_its_values` (they asserted the split report). A block that no line can fix within 1,00 € cannot be built with net prices and at most 4 recipients (see the spec's "Why 1,00 € is enough"), so there is no archive to test the remaining `PricesDoNotDivideIn` path with; keep the code path.
  - `GraveBillingCheckTests`: delete the suggestion tests; keep or adapt the others to `Evaluate`.

- [ ] **Step 2: Run, expect failures.**

- [ ] **Step 3: Implement**
  - `CheckGraveSplits`: evaluate; report only `evaluation.FirstFailing` problems as today (label first); remove the suggestion loop and `Suggest`.
  - `ImportService`: after graves, items and links are added and saved (so ids exist), for each grave with recipients compute `GraveBillingCheck.Evaluate(links, start, end, recipientCount)` from its links (`GraveBilling.LinkFor(item entity, count, null, priceIncludesVat)`) and add a `GraveBillingCorrection` per correction; count graves with at least one; save in the same transaction; put the count into the summary.
  - Domain: delete the old path and rename; fix every caller (`GraveService`, preview action, `GraveCorrectionCheck`, import, tests).

- [ ] **Step 4: Full backend suite.**

- [ ] **Step 5: Commit** — `feat(import): store rounding corrections and drop the price suggestions`

---

### Task 6: Frontend — preview cards, confirmation dialog, save

**Files:**

- Modify: `frontend/src/api/endpoints.ts`
- Modify: `frontend/src/components/GraveBillingPreview.vue`
- Modify: `frontend/src/components/LineEditor.vue` (remove `suggestions`)
- Create: `frontend/src/components/CorrectionsDialog.vue`
- Modify: `frontend/src/components/GraveEditor.vue`
- Modify: `frontend/src/i18n/de.json`

**Interfaces:**

- Consumes: Tasks 2–3 contracts.
- Produces:
  - TS: `BillingCorrection { Kind: BillingExampleKind; Year: number | null; Half: 1 | 2; LineIndex: number; VatRate: number; ItemId: number; ItemNumber: string; EnteredPrice: number; CorrectedPrice: number }`; `BillingPreviewLine` gains `EnteredPrice: number; Corrected: boolean`; `BillingPreviewInvoice` gains `Corrections: BillingCorrection[]`; `BillingPreview` loses `LineSuggestions`; `SaveGraveInput` gains `corrections: { kind: string; year: number | null; half: number; vatRate: number; itemId: number; correctedPrice: number }[]`
  - `CorrectionsDialog.vue`: props `{ rows: CorrectionRow[] }`, `v-model:visible`, emits `confirm`; `CorrectionRow { invoice: string; item: string; vatRate: number; entered: number; corrected: number }`

- [ ] **Step 1: Types** in `endpoints.ts` as above; `saveGrave` sends `corrections` as given.

- [ ] **Step 2: Preview cards** — in `GraveBillingPreview.vue`: remove everything about `LineSuggestions`/`suggested`/`needsAdvice` except the `pricesAdjustAPrice` advice, which now shows whenever `!invoice.Ok`; a line with `Corrected` shows its price with class `text-orange-600 dark:text-orange-400 font-semibold` and below the description a `<small class="block text-orange-700 dark:text-orange-300">{{ t("billingCorrectedNote", [formatAmount(line.EnteredPrice)]) }}</small>`; the tag: `!invoice.Ok` → "Geht nicht auf" danger; `invoice.Corrections.length > 0` → `t('billingOkCorrected')`, `severity="warn"`, icon `pi pi-exclamation-circle`; else "Geht auf" success. A corrected card keeps `border-surface` but gains `data-billing-corrected`.

- [ ] **Step 3: LineEditor** — remove the `suggestions` prop, `suggestionsFor`, the `:invalid` binding and the suggestion row; remove `lineSuggestion`/`applySuggestedPrice` from `de.json`.

- [ ] **Step 4: `CorrectionsDialog.vue`**

```vue
<script setup lang="ts">
import Button from "primevue/button";
import Dialog from "primevue/dialog";
import { useI18n } from "vue-i18n";
import { formatAmount, formatVatRate } from "@/lib/format";

export interface CorrectionRow {
  invoice: string;
  item: string;
  vatRate: number;
  entered: number;
  corrected: number;
}

defineProps<{ rows: CorrectionRow[] }>();
const visible = defineModel<boolean>("visible", { required: true });
const emit = defineEmits<{ confirm: [] }>();
const { t } = useI18n();
</script>

<template>
  <Dialog
    v-model:visible="visible"
    modal
    :header="t('correctionsTitle')"
    :style="{ width: '40rem' }"
    :breakpoints="{ '640px': '100vw' }"
  >
    <p class="mt-0">{{ t("correctionsIntro") }}</p>
    <div class="overflow-x-auto">
      <table class="w-full text-sm">
        <thead>
          <tr class="text-left text-muted-color">
            <th class="py-1 pr-2 font-medium">{{ t("correctionsInvoice") }}</th>
            <th class="py-1 pr-2 font-medium">{{ t("correctionsItem") }}</th>
            <th class="py-1 pr-2 font-medium">{{ t("vatRate") }}</th>
            <th class="py-1 pr-2 text-right font-medium">{{ t("correctionsEntered") }}</th>
            <th class="py-1 text-right font-medium">{{ t("correctionsCorrected") }}</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="(row, index) in rows" :key="index">
            <td class="py-1 pr-2">{{ row.invoice }}</td>
            <td class="py-1 pr-2">{{ row.item }}</td>
            <td class="py-1 pr-2 whitespace-nowrap">{{ formatVatRate(row.vatRate) }} %</td>
            <td class="py-1 pr-2 text-right tabular-nums whitespace-nowrap">
              {{ formatAmount(row.entered) }}
            </td>
            <td
              class="py-1 text-right font-semibold tabular-nums whitespace-nowrap text-orange-600 dark:text-orange-400"
            >
              {{ formatAmount(row.corrected) }}
            </td>
          </tr>
        </tbody>
      </table>
    </div>
    <template #footer>
      <Button :label="t('cancel')" severity="secondary" text @click="visible = false" />
      <Button
        :label="t('correctionsConfirm')"
        icon="pi pi-check"
        severity="warn"
        @click="emit('confirm')"
      />
    </template>
  </Dialog>
</template>
```

- [ ] **Step 5: Editor save** — in `GraveEditor.vue` remove the `suggestions` computed and its prop binding. Keep the all-year guard. After `const check = await billing.check()`: if `check && !check.Ok` keep the existing refusal; otherwise collect `check?.Invoices.flatMap(...)` corrections. If none → save with `corrections: []`. If some → fill `correctionRows` (invoice title via the same `billingKind…` i18n as the cards — export a `billingTitle(invoice, t)` helper from `lib/billing.ts` and use it in both places; item = `${ItemNumber} ${Description}` of the invoice line whose `LineIndex` the correction names — the correction's `ItemNumber` matches that line), open the dialog, and save only on `confirm`, sending `corrections` mapped to `{ kind: c.Kind, year: c.Year, half: c.Half, vatRate: c.VatRate, itemId: c.ItemId, correctedPrice: c.CorrectedPrice }`. On `ApiError` code `CorrectionsChanged`: close the dialog, `await billing.check()`, show the error toast (key `errorCorrectionsChanged`), stay in the editor.

- [ ] **Step 6: i18n** (`de.json`):

```json
  "billingOkCorrected": "Geht auf (angepasst)",
  "billingCorrectedNote": "angepasst, eingegeben {0}",
  "correctionsTitle": "Preise werden angepasst",
  "correctionsIntro": "Damit sich die Rechnungen gleichmäßig auf die Empfänger aufteilen lassen, werden diese Preise angehoben:",
  "correctionsInvoice": "Rechnung",
  "correctionsItem": "Leistung",
  "correctionsEntered": "eingegeben",
  "correctionsCorrected": "angepasst",
  "correctionsConfirm": "Mit Anpassungen speichern",
  "errorCorrectionsChanged": "Die Rundungsanpassungen haben sich geändert. Bitte prüfen und erneut speichern."
```

- [ ] **Step 7: Verify** — typecheck, lint, build.

- [ ] **Step 8: Commit** — `feat(frontend): show rounding corrections and confirm them before saving a grave`

---

### Task 7: Frontend — overview filter, generation refusal, import summary

**Files:**

- Modify: `frontend/src/api/endpoints.ts` (`GraveStatusRow.CorrectionCount`, `ImportSummary.CorrectedGraves`)
- Modify: `frontend/src/pages/GravesPage.vue`
- Modify: `frontend/src/pages/ImportExportPage.vue`
- Modify: `frontend/src/i18n/de.json`

- [ ] **Step 1: Graves overview** — a `Checkbox` (binary) with label "Nur mit Rundungsanpassungen" next to the existing payment filter (`input-id="gravesCorrectedOnly"`); `rows` additionally filters `row.CorrectionCount > 0` when checked; in the grave number cell, rows with `CorrectionCount > 0` get `<Tag severity="warn" :value="t('graveCorrectedTag')" class="ml-2" />`.
- [ ] **Step 2: Generation refusal** — `CorrectionsOutdated` arrives with one arg, so `useErrorToast` resolves `errorCorrectionsOutdated1`; add it. Confirm the generate flow calls `showError` on a failed POST (read `GravesPage.vue`/`lib/generation.ts`); if it shows something else, route this code to the toast.
- [ ] **Step 3: Import summary** — when `CorrectedGraves > 0`, the summary shows `t("importCorrectedGraves", [CorrectedGraves])`.
- [ ] **Step 4: i18n**

```json
  "gravesFilterCorrected": "Nur mit Rundungsanpassungen",
  "graveCorrectedTag": "angepasst",
  "errorCorrectionsOutdated1": "Bei diesen Gräbern haben sich die Rundungsanpassungen geändert. Bitte öffnen, prüfen und speichern: {0}",
  "importCorrectedGraves": "davon {0} Gräber mit Rundungsanpassungen"
```

- [ ] **Step 5: Verify** — typecheck, lint, build.
- [ ] **Step 6: Commit** — `feat(frontend): filter graves with rounding corrections and explain a stopped run`

---

### Task 8: E2E and documentation

**Files:**

- Modify: `e2e/tests/graves.spec.ts`, `e2e/tests/layout.spec.ts`, `e2e/tests/import-export.spec.ts` (if it asserts the old split report)
- Modify: `docs/spec.md`, `docs/superpowers/specs/2026-10-05-grave-quarters-and-prorated-billing-design.md` (note at its "Suggestions" section)

- [ ] **Step 1: Rewrite** `"a grave whose prices do not divide is refused until a price is changed"` (47,80 at 19 % over three recipients, term 01/2026–12/2040) into `"a grave whose prices do not divide is saved with confirmed corrections"`: the "Volles H1" card shows "Geht auf (angepasst)" and "angepasst, eingegeben 47,80 €" and "47,82 €"; Speichern opens the dialog "Preise werden angepasst" listing 47,80 € → 47,82 €; "Abbrechen" closes it and leaves the editor open; Speichern again and "Mit Anpassungen speichern" saves (`await expect(editor).toHaveCount(0)`); on the overview the switch "Nur mit Rundungsanpassungen" shows the grave with the tag "angepasst" and hides a grave without corrections from the same run.
- [ ] **Step 2: Remove** any assertion on the old suggestion buttons ("Preis … übernehmen", "Geht auf mit:").
- [ ] **Step 3: Layout** — in `layout.spec.ts`, a test that opens the corrections dialog (a grave needing a correction, e.g. 47,80 over three recipients seeded through the UI editor up to the dialog) and checks `expectNoSidewaysScrolling(page)` on phone and tablet.
- [ ] **Step 4: Docs** — `docs/spec.md`: FR-021a (corrections instead of refusal; one line per block and rate, all-year first; at most 1,00 €; confirmed on save; stored), the rounding edge case, FR-025 (the precheck stops a run with outdated corrections), the overview filter, the import summary. The quarter design spec: a one-line note under "### Suggestions" that `2026-10-05-grave-billing-corrections-design.md` replaces it.
- [ ] **Step 5: Run** `cd e2e && npm test -- tests/graves.spec.ts tests/import-export.spec.ts --project=desktop`, then `npm test -- tests/layout.spec.ts`, then the full suite `npm test`; stack down. `npm run format:check` at the root.
- [ ] **Step 6: Commit** — `test(e2e): cover confirmed rounding corrections and the overview filter` and `docs: describe automatic rounding corrections`
