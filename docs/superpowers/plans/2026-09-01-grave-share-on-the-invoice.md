# The Grave Share on the Invoice — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Bill every recipient of a grave the identical amount, by printing the
grave's lines at 100 % and applying the recipient's share afterwards.

**Architecture:** Nothing is split per line any more. Each VAT-rate subtotal is
nudged up by whole cents on unit prices until it divides by the recipient
count; every recipient's invoice then carries the same 100 % lines, and the
share and its VAT are worked out from the subtotals. `GraveInvoiceSplitter.Split`
and `ShareCalculator` disappear. `DocumentDataBuilder` gains a second rendering
mode so invoices generated before this change still reproduce exactly.

**Tech Stack:** .NET 10 / EF Core / SQLite (through `scripts/dotnet.sh` and
`scripts/ef.sh`), QuestPDF, OpenUI5 1.151 with TypeScript, Playwright, xUnit.

**Spec:** `docs/superpowers/specs/2026-09-01-grave-share-on-the-invoice-design.md`

## Global Constraints

- **One migration for the whole release.** `main` carries two unreleased ones
  (`InvoicePaymentStatus`, `VatIncludedLinePrices`); Task 2 consolidates those
  plus this feature's column into a single migration. No later task adds one.
- **The user's dev database must survive.** `data/invoicing.db` holds real data
  and has `InvoicePaymentStatus` applied but not `VatIncludedLinePrices`.
  Task 2 patches it in place. **Never run `scripts/reset-dev-db.sh`.**
- **No three-decimal prices.** Cents are added to _unit prices_, so every price
  keeps two decimals and every line multiplies out exactly.
- **Documents must stay reproducible** (constitution Principle II). An invoice
  with `RecipientCount is null` renders exactly as it does today, forever.
- **Non-grave invoices do not change** in any way.
- Tie-break when two combinations cost the same to bump: **the combination whose
  sorted list of bumped line numbers is lexicographically smallest wins** —
  decided by our own code, never by a library's unspecified iteration order, so
  a revision reproduces the same split.
- English source, identifiers and comments; i18n for user-visible strings;
  Conventional Commits.
- **Backend tests:** `./scripts/dotnet.sh test Invoicing.sln`, narrowed with
  `--filter FullyQualifiedName~ClassName`, from the repository root. There is no
  local `dotnet` or `dotnet-ef`.
- **Frontend checks:** `cd frontend && npm run typecheck && npm run lint && npm run ui5lint`.
- **E2E:** `cd e2e && npx playwright test tests/<file> --project=desktop`. Never
  set `E2E_BASE_URL`; never touch the dev stack on port 8080.
- **Formatting is automatic** (CSharpier for `backend/`, Prettier elsewhere).
- Baseline before this plan: 220 backend tests, 65/65 E2E, all checks clean.

---

### Task 1: The divisibility rule

**Files:**

- Create: `backend/src/Invoicing.Domain/GraveShare.cs`
- Test: `backend/tests/Invoicing.IntegrationTests/GraveShareTests.cs`

**Interfaces:**

- Consumes: `GraveLine` (existing record in `GraveInvoiceSplitter.cs`).
- Produces: `GraveShare.MakeDivisible(IReadOnlyList<GraveLine> lines, int recipients) → IReadOnlyList<GraveLine>` — the same lines with unit prices raised by whole cents so that every VAT-rate subtotal divides by `recipients`. Tasks 3 and 5 call it.

- [ ] **Step 1: Write the failing tests**

Create `backend/tests/Invoicing.IntegrationTests/GraveShareTests.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using Burkard.Invoicing.Domain;
using Xunit;

namespace Burkard.Invoicing.IntegrationTests;

public class GraveShareTests
{
    private static GraveLine Line(int count, decimal unitPrice, decimal vatRate) =>
        new(1, "1001", "Grabpflege", count, unitPrice, vatRate, false);

    private static long NetCents(GraveLine l) =>
        (long)decimal.Round(l.Count * l.UnitPrice * 100m, 0, MidpointRounding.AwayFromZero);

    [Fact]
    public void Subtotals_that_already_divide_are_left_alone()
    {
        var lines = new[] { Line(1, 99m, 7m) };

        var adjusted = GraveShare.MakeDivisible(lines, 3);

        Assert.Equal(99m, Assert.Single(adjusted).UnitPrice);
    }

    [Fact]
    public void Each_vat_rate_divides_on_its_own()
    {
        // 7%: 118,50 already divides by 3. 19%: 97,70 does not.
        var lines = new[] { Line(1, 100m, 7m), Line(5, 3.70m, 7m), Line(2, 24.95m, 19m), Line(1, 47.80m, 19m) };

        var adjusted = GraveShare.MakeDivisible(lines, 3);

        foreach (var rate in new[] { 7m, 19m })
        {
            var subtotal = adjusted.Where(l => l.VatRate == rate).Sum(NetCents);
            Assert.Equal(0, subtotal % 3);
        }
    }

    [Fact]
    public void The_cheapest_line_takes_the_cents()
    {
        // 19%: 97,70 over three needs one cent. Bumping the count-1 line costs
        // one cent; bumping the count-2 line would cost two.
        var lines = new[] { Line(2, 24.95m, 19m), Line(1, 47.80m, 19m) };

        var adjusted = GraveShare.MakeDivisible(lines, 3);

        Assert.Equal(24.95m, adjusted[0].UnitPrice);
        Assert.Equal(47.81m, adjusted[1].UnitPrice);
    }

    [Fact]
    public void Prices_keep_two_decimals()
    {
        var lines = new[] { Line(3, 8.31m, 19m), Line(7, 2.22m, 19m) };

        var adjusted = GraveShare.MakeDivisible(lines, 4);

        Assert.All(adjusted, l => Assert.Equal(l.UnitPrice, decimal.Round(l.UnitPrice, 2)));
    }

    // Round-robin — add a cent to each line in turn — cannot solve this: one lap
    // adds three cents, which is zero modulo three, so every lap returns to the
    // same residue and only two of the three are ever reachable.
    [Fact]
    public void The_case_round_robin_cannot_solve()
    {
        var lines = new[] { Line(1, 24.95m, 19m), Line(2, 0.01m, 19m) };

        var adjusted = GraveShare.MakeDivisible(lines, 3);

        Assert.Equal(0, adjusted.Sum(NetCents) % 3);
    }

    [Fact]
    public void It_never_lowers_a_price()
    {
        var lines = new[] { Line(1, 33.33m, 19m), Line(2, 11.11m, 7m) };

        var adjusted = GraveShare.MakeDivisible(lines, 3);

        Assert.True(adjusted.Zip(lines).All(p => p.First.UnitPrice >= p.Second.UnitPrice));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(7)]
    public void It_terminates_for_every_combination_of_counts(int recipients)
    {
        foreach (var a in new[] { 1, 2, 3, 4, 5, 7, 10 })
        foreach (var b in new[] { 1, 2, 3, 4, 5, 7, 10 })
        {
            var lines = new[] { Line(a, 24.95m, 19m), Line(b, 3.70m, 19m) };

            var adjusted = GraveShare.MakeDivisible(lines, recipients);

            Assert.Equal(0, adjusted.Sum(NetCents) % recipients);
        }
    }
}
```

- [ ] **Step 2: Run them to verify they fail**

Run: `./scripts/dotnet.sh test Invoicing.sln --filter FullyQualifiedName~GraveShareTests`
Expected: build error — `GraveShare` does not exist.

- [ ] **Step 3: Write the rule**

Create `backend/src/Invoicing.Domain/GraveShare.cs`:

```csharp
namespace Burkard.Invoicing.Domain;

/// <summary>Raises a grave's unit prices by whole cents until every VAT-rate
/// subtotal divides evenly across the recipients, so each of them can be billed
/// the identical share.
///
/// Adding one cent to a line's unit price moves its subtotal by the line's
/// count. With g = gcd(counts, recipients), g divides every count and therefore
/// the subtotal too, and the reachable additions are exactly the multiples of
/// g — so the residue needed is always reachable. Adding a cent to each line in
/// turn is not enough: with three recipients and lines of count 1 and 2, a full
/// lap adds three cents and returns to the same residue forever. Choosing which
/// line to bump is what makes it terminate, and a shortest path over the
/// residues finds the fewest cents.</summary>
public static class GraveShare
{
    public static IReadOnlyList<GraveLine> MakeDivisible(
        IReadOnlyList<GraveLine> lines,
        int recipients
    )
    {
        if (recipients <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(recipients));
        }

        var result = lines.ToList();
        // materialised: the loop rewrites result as it goes
        foreach (var rate in result.Select(l => l.VatRate).Distinct().ToList())
        {
            var indices = Enumerable
                .Range(0, result.Count)
                .Where(i => result[i].VatRate == rate)
                .ToList();
            var subtotal = indices.Sum(i => NetCents(result[i]));
            foreach (var i in CheapestBumps(indices.Select(i => result[i].Count).ToList(), subtotal, recipients))
            {
                var line = result[indices[i]];
                result[indices[i]] = line with { UnitPrice = line.UnitPrice + 0.01m };
            }
        }

        return result;
    }

    /// <summary>Which lines to bump by one cent, fewest cents added first. Each
    /// bump of line i moves the subtotal by counts[i], so this is a shortest
    /// path from the current residue to zero over the residues modulo
    /// <paramref name="recipients"/>. Equal-cost choices take the lower line
    /// number, so a revision reproduces the same split.</summary>
    private static IReadOnlyList<int> CheapestBumps(
        IReadOnlyList<int> counts,
        long subtotal,
        int recipients
    )
    {
        var start = (int)(((subtotal % recipients) + recipients) % recipients);
        if (start == 0)
        {
            return Array.Empty<int>();
        }

        var best = new long[recipients];
        var path = new List<int>?[recipients];
        Array.Fill(best, long.MaxValue);
        best[start] = 0;
        path[start] = new List<int>();

        var queue = new PriorityQueue<int, long>();
        queue.Enqueue(start, 0);
        while (queue.TryDequeue(out var residue, out var cost))
        {
            if (cost > best[residue])
            {
                continue;
            }

            for (var i = 0; i < counts.Count; i++)
            {
                var next = (residue + counts[i]) % recipients;
                var nextCost = cost + counts[i];
                if (nextCost >= best[next])
                {
                    continue;
                }

                best[next] = nextCost;
                path[next] = new List<int>(path[residue]!) { i };
                queue.Enqueue(next, nextCost);
            }
        }

        return path[0]
            ?? throw new InvalidOperationException(
                "No combination of cents makes the subtotal divisible, which the "
                    + "gcd argument says cannot happen."
            );
    }

    private static long NetCents(GraveLine line) =>
        (long)decimal.Round(line.Count * line.UnitPrice * 100m, 0, MidpointRounding.AwayFromZero);
}
```

- [ ] **Step 4: Run them to verify they pass**

Run: `./scripts/dotnet.sh test Invoicing.sln --filter FullyQualifiedName~GraveShareTests`
Expected: PASS, 11 tests.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Invoicing.Domain/GraveShare.cs backend/tests/Invoicing.IntegrationTests/GraveShareTests.cs
git commit -m "feat(graves): raise a grave's prices until its subtotals divide"
```

---

### Task 2: One migration, and the dev database

The riskiest task in the plan. It rewrites migration history and touches the
user's real data.

**Files:**

- Modify: `backend/src/Invoicing.Domain/Entities/Invoice.cs`
- Delete + regenerate: `backend/src/Invoicing.Api/Migrations/20260831163518_InvoicePaymentStatus*.cs`, `20260831194226_VatIncludedLinePrices*.cs`
- Modify: `backend/src/Invoicing.Api/Migrations/AppDbContextModelSnapshot.cs` (generated)
- Create: `scripts/patch-dev-db.sh`
- Test: `backend/tests/Invoicing.IntegrationTests/InvoicePaidTests.cs` (existing, must stay green)

**Interfaces:**

- Consumes: nothing.
- Produces: `Invoice.RecipientCount` (`int?`). Tasks 3–6 read it.

- [ ] **Step 1: Add the column to the model**

In `backend/src/Invoicing.Domain/Entities/Invoice.cs`, after `SharePercent`:

```csharp
    /// <summary>How many recipients the grave's bill was divided by, on invoices
    /// that carry the grave's lines at 100 % and apply a share. Null on every
    /// other invoice — including grave invoices generated before that change,
    /// whose lines are already one recipient's share. The document renderer
    /// branches on it, so it is what keeps those older documents reproducible.</summary>
    public int? RecipientCount { get; set; }
```

- [ ] **Step 2: Pop the two unreleased migrations**

Neither is released. Remove them newest-first so the model snapshot rewinds
with them — deleting the files by hand would leave the snapshot claiming
columns that no migration creates:

```bash
./scripts/ef.sh migrations remove -p src/Invoicing.Api -s src/Invoicing.Api
./scripts/ef.sh migrations remove -p src/Invoicing.Api -s src/Invoicing.Api
```

Add `--force` if it asks to reach a database; nothing has applied them in any
environment this plan can see. Confirm afterwards that
`backend/src/Invoicing.Api/Migrations/` holds only the three `2026082*` and
`20260829*` migrations, and that `AppDbContextModelSnapshot.cs` no longer
mentions `PaidOn`, `PriceIncludesVat` or `RecipientCount`.

If removal fails in a way `--force` does not fix, **stop and report BLOCKED** —
a half-rewound snapshot is worse than no progress.

- [ ] **Step 3: Generate the single replacement**

```bash
./scripts/ef.sh migrations add PaymentVatAndGraveShare -p src/Invoicing.Api -s src/Invoicing.Api
```

Read the generated `Up` and confirm it contains exactly:

- `DropIndex` `IX_Invoices_GraveId_IsLatestRevision`
- `AddColumn<DateOnly>` `PaidOn` on `Invoices`, nullable
- `CreateIndex` `IX_Invoices_GraveId_IsLatestRevision_PaidOn`
- `AddColumn<bool>` `PriceIncludesVat` on `InvoiceLines`, not null, default false
- `AddColumn<bool>` `PriceIncludesVat` on `GraveItems`, not null, default false
- `AddColumn<int>` `RecipientCount` on `Invoices`, nullable

Anything else means the rewind was incomplete. Report BLOCKED rather than
editing the generated file.

- [ ] **Step 4: Run the whole suite**

Run: `./scripts/dotnet.sh test Invoicing.sln`
Expected: 220 passing. The tests build their databases from migrations, so a
green run is what proves the consolidated migration is equivalent to the two it
replaces.

- [ ] **Step 5: Write the dev-database patch**

`data/invoicing.db` has `InvoicePaymentStatus` applied — its `PaidOn` column and
the reworked index are already there — but not `VatIncludedLinePrices`. After
the squash, EF would find history for a migration that no longer exists and
then try to add a column that already exists.

Create `scripts/patch-dev-db.sh`:

```bash
#!/usr/bin/env bash
# One-off: the dev database applied 20260831163518_InvoicePaymentStatus before
# that migration was consolidated away. This brings it to the consolidated
# migration without touching any data: it adds the columns the database is
# missing and rewrites the history row.
#
# Safe to re-run: every step is guarded. Takes a backup first.
set -euo pipefail
DB="${1:-data/invoicing.db}"
NEW_ID="$(basename "$(ls backend/src/Invoicing.Api/Migrations/*_PaymentVatAndGraveShare.cs)" .cs)"

cp "$DB" "$DB.backup-$(date +%Y%m%d%H%M%S)"

python3 - "$DB" "$NEW_ID" <<'PY'
import sqlite3, sys
db, new_id = sqlite3.connect(sys.argv[1]), sys.argv[2]
def columns(table):
    return {r[1] for r in db.execute(f"pragma table_info({table})")}

if "PriceIncludesVat" not in columns("InvoiceLines"):
    db.execute("ALTER TABLE InvoiceLines ADD COLUMN PriceIncludesVat INTEGER NOT NULL DEFAULT 0")
if "PriceIncludesVat" not in columns("GraveItems"):
    db.execute("ALTER TABLE GraveItems ADD COLUMN PriceIncludesVat INTEGER NOT NULL DEFAULT 0")
if "RecipientCount" not in columns("Invoices"):
    db.execute("ALTER TABLE Invoices ADD COLUMN RecipientCount INTEGER NULL")

version = db.execute("SELECT ProductVersion FROM __EFMigrationsHistory LIMIT 1").fetchone()[0]
db.execute("DELETE FROM __EFMigrationsHistory WHERE MigrationId LIKE '%InvoicePaymentStatus'")
db.execute("DELETE FROM __EFMigrationsHistory WHERE MigrationId = ?", (new_id,))
db.execute(
    "INSERT INTO __EFMigrationsHistory (MigrationId, ProductVersion) VALUES (?, ?)",
    (new_id, version),
)
db.commit()
print("applied:", *[r[0] for r in db.execute(
    "SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId")], sep="\n  ")
PY
```

Make it executable: `chmod +x scripts/patch-dev-db.sh`

- [ ] **Step 6: Rehearse the patch on a copy, then apply it**

Never rehearse on the real file:

```bash
cp data/invoicing.db /tmp/rehearse.db
./scripts/patch-dev-db.sh /tmp/rehearse.db
```

Expected: four migration ids listed, ending in `_PaymentVatAndGraveShare`.
Then confirm the copy still holds the user's data and starts cleanly:

```bash
python3 -c "
import sqlite3; d=sqlite3.connect('file:/tmp/rehearse.db?mode=ro', uri=True)
for t in ('Invoices','Graves','Recipients','InvoiceItems'):
    print(t, d.execute(f'select count(*) from {t}').fetchone()[0])
print('RecipientCount', [r[1] for r in d.execute('pragma table_info(Invoices)')].count('RecipientCount'))
"
```

Only when that looks right, run it on the real database:

```bash
./scripts/patch-dev-db.sh
```

It writes a timestamped backup beside the file first.

- [ ] **Step 7: Commit**

```bash
git add backend/src/Invoicing.Domain/Entities/Invoice.cs backend/src/Invoicing.Api/Migrations scripts/patch-dev-db.sh
git commit -m "refactor(db): consolidate the unreleased migrations into one"
```

---

### Task 3: Generation bills the grave, not the split

**Files:**

- Modify: `backend/src/Invoicing.Api/Services/GraveInvoiceGenerator.cs`
- Delete: `backend/src/Invoicing.Domain/ShareCalculator.cs`, `backend/tests/Invoicing.IntegrationTests/ShareCalculatorTests.cs`
- Modify: `backend/src/Invoicing.Domain/GraveInvoiceSplitter.cs` (remove `Split`)
- Modify: `backend/tests/Invoicing.IntegrationTests/GraveInvoiceSplitterTests.cs`
- Test: `backend/tests/Invoicing.IntegrationTests/GraveShareGenerationTests.cs` (create)

**Interfaces:**

- Consumes: `GraveShare.MakeDivisible` (Task 1), `Invoice.RecipientCount` (Task 2).
- Produces: generated grave invoices whose `Lines` are the grave's 100 % lines and whose `RecipientCount` is set. Tasks 4–6 read both.

- [ ] **Step 1: Write the failing test**

Create `backend/tests/Invoicing.IntegrationTests/GraveShareGenerationTests.cs`.
Seed the spec's worked example — four items and three recipients — following
the seeding style of `GraveMultiItemGenerationTests` (a `SeedAsync` that adds
`VatRate`s, `InvoiceItem`s, `Recipient`s, a `Grave` and its `GraveItem`s
through `AppDbContext`, then `GraveGenerationHelper.RunAsync(client, 2026, 1)`).
The items are Grabpflege 1 × 100,00 @ 7 %, Blumenschmuck 2 × 24,95 @ 19 %,
Grabstein 1 × 47,80 @ 19 % and Gießdienst 5 × 3,70 @ 7 %.

```csharp
    [Fact]
    public async Task Every_recipient_is_billed_the_graves_own_lines()
    {
        using var host = new ApiTestHost();
        await SeedAsync(host);
        using var client = host.CreateClient();

        await GraveGenerationHelper.RunAsync(client, 2026, 1);

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var invoices = await db.Invoices.Include(i => i.Lines).ToListAsync();

        Assert.Equal(3, invoices.Count);
        Assert.All(invoices, i => Assert.Equal(3, i.RecipientCount));

        // the 19 % subtotal is 97,70, which does not divide by three, so one
        // cent goes on the cheapest line to bump — the count-1 Grabstein
        foreach (var invoice in invoices)
        {
            var lines = invoice.Lines.OrderBy(l => l.LineNumber).ToList();
            Assert.Equal(4, lines.Count);
            Assert.Equal(100.00m, lines[0].UnitPrice);
            Assert.Equal(24.95m, lines[1].UnitPrice);
            Assert.Equal(47.81m, lines[2].UnitPrice);
            Assert.Equal(3.70m, lines[3].UnitPrice);
        }

        // and every recipient's invoice carries exactly the same lines
        var first = invoices[0].Lines.OrderBy(l => l.LineNumber).Select(l => (l.Count, l.UnitPrice, l.VatRate));
        Assert.All(
            invoices.Skip(1),
            i => Assert.Equal(first, i.Lines.OrderBy(l => l.LineNumber).Select(l => (l.Count, l.UnitPrice, l.VatRate)))
        );
    }

    [Fact]
    public async Task Each_vat_subtotal_divides_by_the_recipient_count()
    {
        using var host = new ApiTestHost();
        await SeedAsync(host);
        using var client = host.CreateClient();

        await GraveGenerationHelper.RunAsync(client, 2026, 1);

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var lines = (await db.Invoices.Include(i => i.Lines).FirstAsync()).Lines;

        foreach (var group in lines.GroupBy(l => l.VatRate))
        {
            var subtotal = group.Sum(l =>
                (long)decimal.Round(l.Count * l.UnitPrice * 100m, 0, MidpointRounding.AwayFromZero)
            );
            Assert.Equal(0, subtotal % 3);
        }
    }
```

- [ ] **Step 2: Run it to verify it fails**

Run: `./scripts/dotnet.sh test Invoicing.sln --filter FullyQualifiedName~GraveShareGenerationTests`
Expected: FAIL — the lines are one third of the grave's, and `RecipientCount` is null.

- [ ] **Step 3: Generate the grave's lines, not shares**

In `GraveInvoiceGenerator.GenerateForGraveAsync`, replace the
`GraveInvoiceSplitter.Split(graveLines, recipients.Count)` call and its
`perRecipient` variable. Build `graveLines` as today, then:

```csharp
        // Every recipient is billed the same lines — the grave's own, raised by
        // whole cents where a VAT subtotal would not divide. The share is applied
        // when the document is built, not by dividing the lines.
        var billedLines = GraveShare.MakeDivisible(graveLines, recipients.Count);
```

Inside the per-recipient loop, set `RecipientCount = recipients.Count,` on the
`Invoice`, and build its lines from `billedLines`:

```csharp
            invoice.Lines = billedLines
                .Select((line, index) => new InvoiceLine
                {
                    ItemId = line.ItemId,
                    ItemNumber = line.ItemNumber,
                    Description = line.Description,
                    Count = line.Count,
                    UnitPrice = line.UnitPrice,
                    VatRate = line.VatRate,
                    PriceIncludesVat = line.PriceIncludesVat,
                    LineNumber = index + 1,
                })
                .ToList();
```

`GraveUnitPrice` is left null: the line's own price is the grave's price now.

- [ ] **Step 4: Delete what no longer has a caller**

Remove `Split` from `GraveInvoiceSplitter` and delete
`backend/src/Invoicing.Domain/ShareCalculator.cs` together with
`backend/tests/Invoicing.IntegrationTests/ShareCalculatorTests.cs`. Keep
`Combine` and `GraveLine`; `Combine` still reconstructs a grave's lines from
invoices generated before this change, and its summary should say so.

In `GraveInvoiceSplitterTests`, delete every test that exercises `Split` —
`Net_shares_sum_exactly_to_the_grave_net`,
`Split_cannot_place_every_cent_once_the_count_reaches_ten`,
`Zero_recipients_throws`, `Every_recipient_line_remembers_how_the_grave_price_was_entered`
and any other whose body calls it. Keep the `Combine` tests; where one builds
its input with `Split`, replace that with a hand-written list of per-recipient
lines so it still covers the legacy reconstruction.

- [ ] **Step 5: Run the suite**

Run: `./scripts/dotnet.sh test Invoicing.sln`
Expected: the new test passes. `VatIncludedGraveTests`, `GraveMultiItemGenerationTests`,
`GraveInvoiceRevisionTests` and `GraveInvoiceGroupTests` will fail — they assert
per-recipient split amounts that no longer exist. **Leave them**; Tasks 4 and 5
rewrite them once the document and the revision path are in place. Report the
exact failure list.

- [ ] **Step 6: Commit**

```bash
git add backend/src backend/tests
git commit -m "feat(graves): bill every recipient the grave's own lines"
```

---

### Task 4: The document renders the share

**Files:**

- Modify: `backend/src/Invoicing.Documents/InvoiceDocumentData.cs`
- Modify: `backend/src/Invoicing.Api/Services/DocumentDataBuilder.cs`
- Modify: `backend/src/Invoicing.Documents/InvoiceDocument.cs:140-167`
- Modify: `backend/src/Invoicing.Documents/DocumentTexts.cs`
- Test: `backend/tests/Invoicing.DocumentTests/GraveShareDocumentTests.cs` (create)
- Test: `backend/tests/Invoicing.IntegrationTests/VatIncludedGraveTests.cs`, `GraveMultiItemGenerationTests.cs`

**Interfaces:**

- Consumes: `Invoice.RecipientCount` (Task 2), the generated lines (Task 3).
- Produces: `InvoiceDocumentData.Share` (`ShareData?`). Task 6 reads the same totals through OData.

- [ ] **Step 1: Write the failing test**

Create `backend/tests/Invoicing.DocumentTests/GraveShareDocumentTests.cs` with
two tests, built directly against `DocumentDataBuilder` (no HTTP):

1. **the share block** — an invoice with the spec's four lines and
   `RecipientCount = 3` produces `NetTotal == 216.21m`, `Share.NetTotal == 72.07m`,
   `Share.VatSummaries` of `(7m, 39.50m, 2.77m)` and `(19m, 32.57m, 6.19m)`,
   and `Share.GrossTotal == 81.03m`;
2. **reproducibility** — an invoice with `RecipientCount = null` and a single
   line of 1 × 50,42 @ 19 % still produces `NetTotal == 50.42m`,
   `GrossTotal == 60.00m` and `Share == null`, exactly as today.

- [ ] **Step 2: Run them to verify they fail**

Run: `./scripts/dotnet.sh test Invoicing.sln --filter FullyQualifiedName~GraveShareDocumentTests`
Expected: build error — `InvoiceDocumentData` has no `Share`.

- [ ] **Step 3: Add the share to the document data**

In `InvoiceDocumentData.cs`, add the record and the property:

```csharp
    /// <summary>Set when the lines are the whole grave's and the recipient pays a
    /// share of them. Null on every other invoice, including grave invoices
    /// generated before that change, whose lines are already the share.</summary>
    public ShareData? Share { get; set; }

    /// <summary>One recipient's part of a grave bill: the share of the net, the
    /// VAT worked out from that share per rate, and what they owe.</summary>
    public sealed record ShareData(
        decimal Percent,
        decimal NetTotal,
        IReadOnlyList<VatSummary> VatSummaries,
        decimal VatTotal,
        decimal GrossTotal
    );
```

- [ ] **Step 4: Build it**

In `DocumentDataBuilder.Build`, after the existing `summaries` are computed and
before the `return`, add:

```csharp
        // A grave invoice generated since the lines became the grave's own: the
        // recipient pays a share of them, and their VAT is worked out from that
        // share rather than from the whole. MakeDivisible guarantees each rate's
        // net divides exactly, so no rounding happens here.
        InvoiceDocumentData.ShareData? share = null;
        if (invoice.RecipientCount is { } recipients && recipients > 0)
        {
            var shareSummaries = summaries
                .Select(s =>
                {
                    var net = s.NetTotal / recipients;
                    var vat = Math.Round(net * s.Rate / 100m, 2, MidpointRounding.AwayFromZero);
                    return new InvoiceDocumentData.VatSummary(s.Rate, net, vat, net + vat);
                })
                .ToArray();
            var shareNet = shareSummaries.Sum(s => s.NetTotal);
            var shareVat = shareSummaries.Sum(s => s.VatAmount);
            share = new InvoiceDocumentData.ShareData(
                100m / recipients,
                shareNet,
                shareSummaries,
                shareVat,
                shareNet + shareVat
            );
        }
```

and set `Share = share,` in the returned object. Leave `NetTotal`, `TotalVat`,
`GrossTotal` and `VatSummaries` as they are — with a share present they are the
100 % figures the document prints above it.

- [ ] **Step 5: Add the texts**

In `DocumentTexts.cs`, add two members beside `NetTotal`, following the same
`string.Format(Culture, …)` shape, with German and English strings alongside the
existing ones: `NetSumTotal(decimal amount)` rendering "Nettosumme gesamt" and
`SharePart(decimal percent, decimal amount)` rendering
"Ihr Anteil (33,33 %)". Reuse `VatForRate` for the share's per-rate lines and
`GrossTotal` for the final amount.

- [ ] **Step 6: Render it**

In `InvoiceDocument.cs`, in the summary column around line 150, branch: when
`_data.Share is null`, keep exactly today's block. When it is set, render the
100 % net sum via `NetSumTotal(_data.NetTotal)`, then `SharePart`, then one
`VatForRate` per entry of `_data.Share.VatSummaries`, then
`GrossTotal(_data.Share.GrossTotal)` in the existing emphasised style.

Keep the block inside the existing content band. `CLAUDE.md` warns that content
overflowing `page.Background()`'s bands makes the letterhead vanish from
continuation pages — so after this renders, check page two of a multi-page
invoice, not only page one.

The old `ShareText` line above the table is now redundant for new invoices;
leave it for `Share is null` ones and suppress it when `Share` is set.

- [ ] **Step 7: Rewrite the tests that asserted split amounts**

`VatIncludedGraveTests` and `GraveMultiItemGenerationTests` assert per-recipient
unit prices from the old split. Rewrite their assertions against the new form:
lines carry the grave's price, and the recipient's total comes from
`new DocumentDataBuilder().Build(invoice).Share!.GrossTotal`. Keep every scenario;
only the expected numbers change. Derive each expected value from the rule
rather than from a previous run, and show the derivation in a comment.

- [ ] **Step 8: Run the suite**

Run: `./scripts/dotnet.sh test Invoicing.sln`
Expected: everything green except `GraveInvoiceRevisionTests` and
`GraveInvoiceGroupTests`, which Task 5 owns.

- [ ] **Step 9: Commit**

```bash
git add backend/src backend/tests
git commit -m "feat(documents): print the grave in full and the recipient's share"
```

---

### Task 5: Revisions

**Files:**

- Modify: `backend/src/Invoicing.Api/Services/GraveInvoiceRevisionService.cs`
- Modify: `backend/src/Invoicing.Api/OData/Controllers/GraveInvoiceGroupController.cs`
- Test: `backend/tests/Invoicing.IntegrationTests/GraveInvoiceRevisionTests.cs`, `GraveInvoiceGroupTests.cs`

**Interfaces:**

- Consumes: `GraveShare.MakeDivisible`, `Invoice.RecipientCount`, the generation of Task 3.
- Produces: revisions in the new form. Nothing later depends on it.

- [ ] **Step 1: Write the failing tests**

In `GraveInvoiceRevisionTests`, rewrite the assertions that pin the old split —
including the one whose comment reads "SplitCents(1071, 2) = [536, 535] — a
genuine remainder cent", which documents behaviour that no longer exists.
Add two tests:

1. revising a group generated in the new form reproduces identical invoices for
   every recipient, each with `RecipientCount` set;
2. revising a group generated in the **old** form — lines written as
   per-recipient shares, `RecipientCount` null — produces new-form revisions,
   while the superseded invoices keep their original lines and still build the
   same document totals they did before.

- [ ] **Step 2: Run them to verify they fail**

Run: `./scripts/dotnet.sh test Invoicing.sln --filter FullyQualifiedName~GraveInvoiceRevision`
Expected: FAIL — revisions still store divided lines and leave `RecipientCount` null.

- [ ] **Step 3: Write the revision in the new form**

In `GraveInvoiceRevisionService.CreateAsync`, replace the
`GraveInvoiceSplitter.Split(graveLines, siblings.Count)` call with
`GraveShare.MakeDivisible(graveLines, siblings.Count)`, give every revision the
same lines built the way Task 3 builds them, and set
`RecipientCount = siblings.Count` on each.

- [ ] **Step 4: Serve the group from the lines directly**

In `GraveInvoiceGroupController.GraveLines`, take the grave's lines straight
from the first sibling when `RecipientCount` is set — they are the grave's
lines. Keep the existing `GraveUnitPrice` and `Combine` branches for the older
invoices, and say in the summary which branch serves which.

- [ ] **Step 5: Run the whole suite**

Run: `./scripts/dotnet.sh test Invoicing.sln`
Expected: 0 failures.

- [ ] **Step 6: Rewrite SC-004**

The behaviour it describes is complete once revisions are in. In
`docs/spec.md`, replace SC-004 with the wording from the design doc's "What
SC-004 becomes":

> **SC-004**: For graves with multiple recipients, every recipient is billed an
> identical amount, and their shares sum exactly to the net sum printed on the
> invoice. The grave's bill may be raised by a few cents so that its per-rate
> subtotals divide evenly; it is never reduced.

Check whether any other requirement in that file describes the old split and
update it too; `GraveInvoiceSplitterTests` cited SC-004 by name, so grep for
`SC-004` across the repository and fix every reference.

- [ ] **Step 7: Commit**

```bash
git add backend/src backend/tests docs/spec.md
git commit -m "feat(graves): revise a grave bill in the share form"
```

---

### Task 6: Totals come from the backend

**Files:**

- Modify: `backend/src/Invoicing.Api/OData/Controllers/InvoicesController.cs`
- Modify: `frontend/src/model/api.ts`
- Modify: `frontend/src/controller/InvoiceRevisions.controller.ts:55-70`
- Modify: `frontend/src/controller/GraveDetail.controller.ts:120-131`
- Test: `backend/tests/Invoicing.IntegrationTests/InvoiceTotalsTests.cs` (create)

**Interfaces:**

- Consumes: `DocumentDataBuilder` and `Invoice.RecipientCount`.
- Produces: `NetTotal`, `VatTotal`, `GrossTotal` on the OData invoice. Nothing later depends on it.

- [ ] **Step 1: Write the failing test**

Create `InvoiceTotalsTests.cs`: generate a grave for three recipients, read
`/odata/Invoices?$filter=GraveId ne null`, and assert every row's `GrossTotal`
equals what that recipient is billed — the share, not the grave's 100 % total —
and that all three are equal. Add a non-grave invoice and assert its totals are
unchanged.

- [ ] **Step 2: Run it to verify it fails**

Run: `./scripts/dotnet.sh test Invoicing.sln --filter FullyQualifiedName~InvoiceTotalsTests`
Expected: FAIL — the properties do not exist.

- [ ] **Step 3: Expose the totals**

The three totals are derived, not stored, so they are computed on read. In
`InvoicesController`, project them from `DocumentDataBuilder` for each invoice
returned, taking `Share?.GrossTotal ?? GrossTotal` and the matching net and VAT.
Keep `[EnableQuery]` working for the filters and `$orderby` the frontend uses;
if computing them inside the `IQueryable` proves impossible, materialise the
page first and project afterwards, and say so in the code.

- [ ] **Step 4: Use them in the frontend**

In `frontend/src/model/api.ts`, delete `invoiceTotals`, `lineTotal` and
`InvoiceLineAmounts`, and add `NetTotal`, `VatTotal` and `GrossTotal` to
`InvoiceRow`. In `InvoiceRevisions.controller.ts`, take a revision's totals
from those fields instead of summing lines — the per-line `LineTotal` the
revision panel shows stays computed from `Count × UnitPrice`, since that is the
line as printed. In `GraveDetail.controller.ts`, take `GrossTotal` from the row.

- [ ] **Step 5: Run the checks**

Run: `./scripts/dotnet.sh test Invoicing.sln` then
`cd frontend && npm run typecheck && npm run lint && npm run ui5lint`
Expected: all green.

- [ ] **Step 6: Commit**

```bash
git add backend frontend
git commit -m "feat(invoices): serve invoice totals from the backend"
```

---

### Task 7: The grave pages and E2E

**Files:**

- Modify: `frontend/src/controller/GraveDetail.controller.ts`
- Test: `e2e/tests/graves.spec.ts`

**Interfaces:**

- Consumes: everything above.
- Produces: nothing.

- [ ] **Step 1: Write the failing E2E test**

In `e2e/tests/graves.spec.ts`, extend `seedGrave` to take a list of items rather
than one, then add a test using the half-year **2035/2** (unused; 2030/2,
2036/2, 2037/2, 2038/2 and 2039/2 are taken): a grave with the spec's four
lines and three recipients, generated, then

- all three invoice rows on the grave detail page show the same total, and
- that total is `81,03 €`.

Follow the file's `generateFor` helper and its `pickSelectOption` idiom.

- [ ] **Step 2: Run it to verify it fails**

Run: `cd e2e && npx playwright test tests/graves.spec.ts --project=desktop -g "2035"`
Expected: FAIL.

- [ ] **Step 3: Show the adjusted prices on the grave page**

The grave detail page lists the grave's configured item prices. After a
generation the invoices may carry a price a cent higher, and the spec says the
page should show what was billed so the two agree. Take the item rows from the
latest generated invoice for the selected half-year when one exists, and fall
back to the grave's configuration when none does.

- [ ] **Step 4: Run the specs**

Run: `cd e2e && npx playwright test tests/graves.spec.ts tests/invoices.spec.ts --project=desktop`
Expected: green — the whole of both files, since `seedGrave` changed.

- [ ] **Step 5: Commit**

```bash
git add frontend/src e2e/tests/graves.spec.ts
git commit -m "feat(graves): show what the grave was billed"
```

---

### Task 8: Full verification

No code. The gate before the branch is offered.

- [ ] **Step 1: Backend** — `./scripts/dotnet.sh test Invoicing.sln`, 0 failures.
- [ ] **Step 2: Frontend** — `cd frontend && npm run typecheck && npm run lint && npm run ui5lint`, all clean.
- [ ] **Step 3: E2E** — `cd e2e && npm test`, all three projects. Re-run any failing spec alone before calling it a regression; the workers share one stack.
- [ ] **Step 4: Formatting** — `npm run format:check` from the root.
- [ ] **Step 5: One migration** — `ls backend/src/Invoicing.Api/Migrations/` must show exactly four migrations, the newest `PaymentVatAndGraveShare`. Neither `InvoicePaymentStatus` nor `VatIncludedLinePrices` may remain.
- [ ] **Step 6: The dev database** — confirm `data/invoicing.db` still holds the user's rows and that its `__EFMigrationsHistory` lists the four migrations. Do not start the dev stack against it as part of this check; read it read-only.
- [ ] **Step 7: Page two** — build a grave invoice with enough lines to spill onto a second page and confirm the letterhead still prints there, per the `CLAUDE.md` warning about `page.Background()`.
