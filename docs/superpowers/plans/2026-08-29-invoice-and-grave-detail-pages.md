# Invoice and Grave Detail Pages Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Turn the invoice page into a searchable list of non-grave invoices with
per-row actions and a revision detail page, and move all grave invoicing — status
colours, mass download, history, and editing that revises every recipient at once —
onto grave detail pages.

**Architecture:** One EF Core migration adds `Invoice.IsLatestRevision` and
`InvoiceLine.GraveUnitPrice`. The even split across a grave's recipients moves out
of `GraveInvoiceGenerator` into a pure `GraveInvoiceSplitter` in the domain, so
generation and editing share one implementation of the rounding rules. The
frontend gains three detail views wired into the existing `sap.m.routing.Router`,
and the controllers grow a shared base chain so each dialog has exactly one owner.

**Tech Stack:** .NET 10 / ASP.NET Core OData v4 / EF Core + SQLite; OpenUI5 1.151
with TypeScript; Playwright for E2E; Docker for everything.

**Spec:** `docs/superpowers/specs/2026-08-29-invoice-and-grave-detail-pages-design.md`

## Global Constraints

- **Never run the E2E suite against the dev stack.** `cd e2e && npm test` starts
  its own stack on port 8081. `E2E_BASE_URL` / `npm run test:dev` is forbidden:
  the specs create rows the API cannot delete, and the dev database holds the
  user's data.
- **Backend tests need the dev backend stopped**, because the dev container and
  the SDK container share `backend/obj` and overwrite each other's
  `project.assets.json` (`NETSDK1064`). The full command, used everywhere below
  as "run the backend tests", is:

  ```sh
  docker compose stop backend
  find backend -type d \( -name bin -o -name obj \) -prune -exec rm -rf {} +
  scripts/dotnet.sh test Invoicing.sln
  docker compose start backend
  ```

- **Frontend types:** `cd frontend && npm run typecheck`.
- **Only `e2e/tests/graves.spec.ts` may trigger grave generation**, and only it
  may assert on the current half-year's invoicing state. The backend allows one
  `GraveInvoices` job at a time and answers a second with `JobAlreadyRunning`,
  and a generation invoices _every_ grave in the stack — including the ones
  other specs just created. Playwright runs spec files in parallel but the tests
  inside one file sequentially, so keeping generation in a single file makes
  both the job and the resulting status deterministic. New generation-dependent
  tests are appended to that file, after the ones already there.
- **Migrations:** `scripts/ef.sh migrations add <Name> --project src/Invoicing.Api --startup-project src/Invoicing.Api`.
  Exactly **one** migration in this whole plan (Principle IV) — Task 2 creates it,
  no later task adds another.
- **English codebase, German UI** (Principle VII). Identifiers, comments and
  commit messages in English; every user-visible string comes from
  `i18n.properties` (German, the fallback bundle) and `i18n_en.properties`.
- **Long-running actions block the UI** (Principle VIII): a `BusyDialog` plus a
  re-entrancy guard, as `Invoices.onInvoiceDialogSave` already does.
- **OData JSON casing:** non-EDM routes answer with
  `ODataJsonDefaults.PascalCase` so the whole API reads as one casing (D-001).
- **Decimals arrive as strings.** The OData V4 model serialises `Edm.Decimal` as
  a string; convert with `Number(...)` before any arithmetic in TypeScript.

---

## File Structure

**Backend — created**

| File                                                                           | Responsibility                                                                                                                  |
| ------------------------------------------------------------------------------ | ------------------------------------------------------------------------------------------------------------------------------- |
| `backend/src/Invoicing.Domain/GraveInvoiceSplitter.cs`                         | `GraveLine` record; `Split` (grave lines → per-recipient lines) and `Combine` (the best-effort inverse, for pre-migration rows) |
| `backend/src/Invoicing.Api/Services/GraveInvoiceRevisionService.cs`            | Revises every recipient invoice of one grave + half-year in one transaction                                                     |
| `backend/src/Invoicing.Api/OData/Controllers/GraveInvoiceRunsController.cs`    | Read-only entity set over the existing runs table                                                                               |
| `backend/src/Invoicing.Api/OData/Controllers/GraveInvoiceGroupController.cs`   | The `GraveInvoiceGroup` projection that fills the edit dialog                                                                   |
| `backend/src/Invoicing.Api/Migrations/<ts>_LatestRevisionAndGraveUnitPrice.cs` | The single migration                                                                                                            |

**Backend — modified**

`Invoice.cs` (two properties) · `AppDbContext.cs` (one index) · `InvoiceService.cs`
and `GraveInvoiceGenerator.cs` (flag + grave price + splitter) ·
`InvoicesController.cs` (`search`) · `BulkDownloadController.cs` (latest only) ·
`GravesController.cs` + `GraveService.cs` (recipient immutability) ·
`GraveRecipientsController.cs` (same) · `InvoiceActionsController.cs` (new action) ·
`EdmModelBuilder.cs` · `Program.cs` (DI).

**Frontend — controller hierarchy**

```
Base                     formatDate, formatAmount, formatInvoiceNumber,
│                        formatRevisionBadge, openDocument, readList
└── InvoiceRevisions     builds the revision detail model from a root id
    ├── InvoiceEdit      the invoice dialog + recipient quick-create
    │   ├── Invoices           (list)
    │   └── InvoiceDetail      (detail)
    └── GraveEdit        the grave dialog + the grave-invoice dialog
        ├── Graves             (list, adds the generate dialog)
        ├── GraveDetail        (detail)
        └── GraveInvoiceDetail (detail)
```

Single inheritance forces this shape: each dialog needs exactly one owner that
all of its users descend from. `InvoiceRevisions` sits above the list controllers
too, which costs them nothing.

**Frontend — created**

`view/InvoiceDetail.view.xml` + `controller/InvoiceDetail.controller.ts` ·
`view/GraveDetail.view.xml` + `controller/GraveDetail.controller.ts` ·
`view/GraveInvoiceDetail.view.xml` + `controller/GraveInvoiceDetail.controller.ts` ·
`view/fragments/GraveInvoiceDialog.fragment.xml` ·
`controller/InvoiceRevisions.controller.ts` · `controller/InvoiceEdit.controller.ts` ·
`controller/GraveEdit.controller.ts`

The revision panel markup is shared between `InvoiceDetail.view.xml` and
`GraveInvoiceDetail.view.xml` as `view/fragments/RevisionPanel.fragment.xml`,
used as the aggregation-binding template for the `{detail>/revisions}` `VBox`
on both views (`<core:Fragment fragmentName="..." type="XML"/>` as the
`VBox`'s sole item, cloned and bound once per revision the same way inline
markup would be). The _logic_ is shared through `InvoiceRevisions`.

---

### Task 1: `GraveInvoiceSplitter` — the split, in one place

**Files:**

- Create: `backend/src/Invoicing.Domain/GraveInvoiceSplitter.cs`
- Test: `backend/tests/Invoicing.IntegrationTests/GraveInvoiceSplitterTests.cs`
- Modify: `backend/src/Invoicing.Api/Services/GraveInvoiceGenerator.cs` (drop its
  private `EffectivePrice`/`GrossCents`/`BuildLines`, call the splitter)

**Interfaces:**

- Consumes: `ShareCalculator.SplitCents(long totalCents, int recipients) -> long[]`
  and `InvoiceLine` from `Burkard.Invoicing.Domain.Entities`.
- Produces:
  - `record GraveLine(long ItemId, string ItemNumber, string Description, int Count, decimal UnitPrice, decimal VatRate)`
  - `GraveInvoiceSplitter.Split(IReadOnlyList<GraveLine> lines, int recipients) -> IReadOnlyList<IReadOnlyList<InvoiceLine>>`
  - `GraveInvoiceSplitter.Combine(IReadOnlyList<IReadOnlyList<InvoiceLine>> perRecipient) -> IReadOnlyList<GraveLine>`

`Split` must reproduce today's arithmetic **exactly** — no generated document may
change. See the spec's Risks section for the pre-existing rounding loss it
preserves.

- [ ] **Step 1: Write the failing tests**

Create `backend/tests/Invoicing.IntegrationTests/GraveInvoiceSplitterTests.cs`:

```csharp
using System;
using System.Linq;
using Burkard.Invoicing.Domain;
using Xunit;

namespace Burkard.Invoicing.IntegrationTests;

public class GraveInvoiceSplitterTests
{
    private static GraveLine Line(int count, decimal unitPrice, decimal vatRate) =>
        new(ItemId: 1, ItemNumber: "1001", Description: "Grabpflege", count, unitPrice, vatRate);

    [Fact]
    public void One_recipient_gets_the_grave_line_unchanged()
    {
        var split = GraveInvoiceSplitter.Split(new[] { Line(2, 120m, 7m) }, 1);

        var line = Assert.Single(Assert.Single(split));
        Assert.Equal(2, line.Count);
        Assert.Equal(120m, line.UnitPrice);
        Assert.Equal(7m, line.VatRate);
        Assert.Equal(1, line.LineNumber);
        Assert.Equal("1001", line.ItemNumber);
    }

    [Fact]
    public void Every_line_is_split_on_its_own_so_proportions_survive()
    {
        var split = GraveInvoiceSplitter.Split(
            new[] { Line(2, 120m, 7m), Line(1, 50m, 19m) }, 2);

        Assert.Equal(2, split.Count);
        Assert.All(split, lines => Assert.Equal(2, lines.Count));
        Assert.All(split, lines => Assert.Equal(60m, lines[0].UnitPrice));
        Assert.All(split, lines => Assert.Equal(25m, lines[1].UnitPrice));
        // the count stays the grave's count; only the price is a share
        Assert.All(split, lines => Assert.Equal(2, lines[0].Count));
        Assert.All(split, lines => Assert.Equal(new[] { 1, 2 }, lines.Select(l => l.LineNumber)));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    public void Combine_is_the_inverse_of_split(int recipients)
    {
        var lines = new[] { Line(2, 120m, 7m), Line(3, 33.33m, 19m), Line(1, 1000m, 7m) };

        var combined = GraveInvoiceSplitter.Combine(GraveInvoiceSplitter.Split(lines, recipients));

        Assert.Equal(lines.Select(l => l.UnitPrice), combined.Select(l => l.UnitPrice));
        Assert.Equal(lines.Select(l => l.Count), combined.Select(l => l.Count));
        Assert.Equal(lines.Select(l => l.ItemNumber), combined.Select(l => l.ItemNumber));
        Assert.Equal(lines.Select(l => l.VatRate), combined.Select(l => l.VatRate));
    }

    // Documented limitation, not a wish: splitting on gross cents and storing a
    // rounded net unit price is not reversible for very small amounts. This is
    // why InvoiceLine.GraveUnitPrice exists and Combine is only the fallback for
    // invoices generated before that column.
    [Fact]
    public void Combine_is_lossy_for_amounts_too_small_to_round_cleanly()
    {
        var lines = new[] { Line(1, 2.60m, 19m) };

        var combined = Assert.Single(GraveInvoiceSplitter.Combine(GraveInvoiceSplitter.Split(lines, 3)));

        Assert.Equal(2.62m, combined.UnitPrice);
    }

    [Fact]
    public void Zero_recipients_throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => GraveInvoiceSplitter.Split(new[] { Line(1, 100m, 7m) }, 0));
    }

    [Fact]
    public void Combining_nothing_throws()
    {
        Assert.Throws<ArgumentException>(
            () => GraveInvoiceSplitter.Combine(Array.Empty<System.Collections.Generic.IReadOnlyList<Domain.Entities.InvoiceLine>>()));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run the backend tests (see Global Constraints).
Expected: FAIL — `GraveInvoiceSplitter` and `GraveLine` do not exist.

- [ ] **Step 3: Write the implementation**

Create `backend/src/Invoicing.Domain/GraveInvoiceSplitter.cs`:

```csharp
using Burkard.Invoicing.Domain.Entities;

namespace Burkard.Invoicing.Domain;

/// <summary>One line of a grave invoice before it is split across the grave's
/// recipients: the grave's count at the grave's price, as if the grave were
/// invoiced to a single recipient.</summary>
public sealed record GraveLine(
    long ItemId,
    string ItemNumber,
    string Description,
    int Count,
    decimal UnitPrice,
    decimal VatRate);

/// <summary>Splits a grave's lines evenly across its recipients, and recovers
/// the grave's lines from the result.</summary>
public static class GraveInvoiceSplitter
{
    /// <summary>The lines of each recipient's invoice. Every line is split on
    /// its own, so each invoice keeps the grave's items in their configured
    /// proportion; splitting the grave total instead would rewrite every unit
    /// price.</summary>
    public static IReadOnlyList<IReadOnlyList<InvoiceLine>> Split(
        IReadOnlyList<GraveLine> lines, int recipients)
    {
        if (recipients <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(recipients));
        }

        var shares = lines.Select(l => ShareCalculator.SplitCents(GrossCents(l.Count, l.UnitPrice, l.VatRate), recipients)).ToList();

        var result = new List<IReadOnlyList<InvoiceLine>>(recipients);
        for (var r = 0; r < recipients; r++)
        {
            var forRecipient = new List<InvoiceLine>(lines.Count);
            for (var i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                var netCents = NetCents(shares[i][r], line.VatRate);
                forRecipient.Add(new InvoiceLine
                {
                    ItemId = line.ItemId,
                    ItemNumber = line.ItemNumber,
                    Description = line.Description,
                    Count = line.Count,
                    // 3 decimals keep round(count × unitPrice × 100) exactly equal to the
                    // derived net for realistic counts, so the persisted line gross stays exact.
                    UnitPrice = decimal.Round(netCents / 100m / line.Count, 3, MidpointRounding.AwayFromZero),
                    VatRate = line.VatRate,
                    LineNumber = i + 1,
                    GraveUnitPrice = line.UnitPrice,
                });
            }

            result.Add(forRecipient);
        }

        return result;
    }

    /// <summary>The grave's lines, recovered from the recipients' invoices by
    /// summing each line's gross cents. Best-effort: for amounts too small to
    /// round cleanly it can land a cent or two off, which is why it only serves
    /// grave invoices generated before <c>InvoiceLine.GraveUnitPrice</c>.</summary>
    public static IReadOnlyList<GraveLine> Combine(
        IReadOnlyList<IReadOnlyList<InvoiceLine>> perRecipient)
    {
        if (perRecipient.Count == 0)
        {
            throw new ArgumentException("At least one recipient is required.", nameof(perRecipient));
        }

        var ordered = perRecipient.Select(lines => lines.OrderBy(l => l.LineNumber).ToList()).ToList();
        if (ordered.Any(lines => lines.Count != ordered[0].Count))
        {
            throw new ArgumentException("Every recipient must carry the same lines.", nameof(perRecipient));
        }

        var result = new List<GraveLine>(ordered[0].Count);
        for (var i = 0; i < ordered[0].Count; i++)
        {
            var template = ordered[0][i];
            var grossCents = ordered.Sum(lines => GrossCents(lines[i].Count, lines[i].UnitPrice, lines[i].VatRate));
            var netCents = NetCents(grossCents, template.VatRate);
            result.Add(new GraveLine(
                template.ItemId ?? 0,
                template.ItemNumber,
                template.Description,
                template.Count,
                decimal.Round(netCents / 100m / template.Count, 3, MidpointRounding.AwayFromZero),
                template.VatRate));
        }

        return result;
    }

    /// <summary>Gross total in cents for one line: round(net cents × (100 + rate) / 100).</summary>
    private static long GrossCents(int count, decimal unitPrice, decimal vatRate)
    {
        var netCents = (long)Math.Round(count * unitPrice * 100m, 0, MidpointRounding.AwayFromZero);
        return (long)Math.Round(netCents * (100m + vatRate) / 100m, 0, MidpointRounding.AwayFromZero);
    }

    private static long NetCents(long grossCents, decimal vatRate) =>
        (long)Math.Round(grossCents * 100m / (100m + vatRate), 0, MidpointRounding.AwayFromZero);
}
```

`GraveUnitPrice` is set here but only exists after Task 2 — add the property to
`InvoiceLine` now as part of this step so the domain compiles:

In `backend/src/Invoicing.Domain/Entities/Invoice.cs`, inside `InvoiceLine`, after
`public decimal UnitPrice { get; set; }`:

```csharp
    /// <summary>On a grave invoice, the line's price before it was split across
    /// the grave's recipients. Null on non-grave invoices and on grave invoices
    /// generated before this column existed.</summary>
    public decimal? GraveUnitPrice { get; set; }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run the backend tests.
Expected: PASS — all seven `GraveInvoiceSplitterTests` green.

- [ ] **Step 5: Refactor `GraveInvoiceGenerator` onto the splitter**

In `backend/src/Invoicing.Api/Services/GraveInvoiceGenerator.cs`, delete the
private `GrossCents` and `BuildLines` methods and replace the split block. Keep
`EffectivePrice` — the "which price applies" rule belongs to the grave link, not
to the splitter.

Replace:

```csharp
        // Each line is split across the recipients on its own, so every invoice
        // keeps the grave's items in their configured proportion; splitting the
        // grave total instead would rewrite each line's unit price.
        var lineShares = graveItems
            .Select(item => ShareCalculator.SplitCents(GrossCents(item), recipients.Count))
            .ToList();
```

with:

```csharp
        var graveLines = graveItems
            .Select(item => new GraveLine(
                item.Item.Id, item.Item.Number, item.Item.Description,
                item.Count, EffectivePrice(item)!.Value, item.Item.VatRate))
            .ToList();
        var perRecipient = GraveInvoiceSplitter.Split(graveLines, recipients.Count);
```

and replace `invoice.Lines = BuildLines(graveItems, lineShares, r);` with:

```csharp
            invoice.Lines = perRecipient[r].ToList();
```

- [ ] **Step 6: Run the full backend suite to prove nothing changed**

Run the backend tests.
Expected: PASS — in particular `GraveGenerationTests` and
`GraveMultiItemGenerationTests`, which pin the generated amounts. If either
fails, `Split` is not reproducing the old arithmetic; fix the splitter, not
the test.

- [ ] **Step 7: Commit**

```bash
git add backend/src/Invoicing.Domain/GraveInvoiceSplitter.cs \
        backend/src/Invoicing.Domain/Entities/Invoice.cs \
        backend/src/Invoicing.Api/Services/GraveInvoiceGenerator.cs \
        backend/tests/Invoicing.IntegrationTests/GraveInvoiceSplitterTests.cs
git commit -m "refactor: move the grave invoice split into the domain

Generation and the coming grave-invoice edit need the same arithmetic, so
it lives in one place with its own tests, including one that pins the
known lossy case for small amounts."
```

---

### Task 2: The migration — `IsLatestRevision` and `GraveUnitPrice`

**Files:**

- Modify: `backend/src/Invoicing.Domain/Entities/Invoice.cs`
- Modify: `backend/src/Invoicing.Api/Persistence/AppDbContext.cs`
- Create: `backend/src/Invoicing.Api/Migrations/<timestamp>_LatestRevisionAndGraveUnitPrice.cs` (generated)
- Modify: `backend/src/Invoicing.Api/Services/InvoiceService.cs`
- Modify: `backend/src/Invoicing.Api/OData/Controllers/BulkDownloadController.cs`
- Test: `backend/tests/Invoicing.IntegrationTests/LatestRevisionTests.cs`

**Interfaces:**

- Consumes: `GraveLine`/`Split` from Task 1; `InvoiceLine.GraveUnitPrice`, added
  in Task 1 Step 3.
- Produces: `Invoice.IsLatestRevision` (bool, default `true`) — every later task
  filters lists on it.

This is the plan's **only** migration.

- [ ] **Step 1: Write the failing tests**

Create `backend/tests/Invoicing.IntegrationTests/LatestRevisionTests.cs`:

```csharp
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Burkard.Invoicing.IntegrationTests;

public class LatestRevisionTests
{
    private static async Task<(long InvoiceId, long ItemId, long RecipientId)> SeedAsync(ApiTestHost host)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.VatRates.Add(new VatRate { Rate = 7m });
        var item = new InvoiceItem { Number = "6001", Description = "Grabpflege", VatRate = 7m, DefaultPrice = 100m };
        var recipient = new Recipient { Number = "E-0001", Name = "Familie Beispiel", Address = "Hauptstraße 1" };
        db.InvoiceItems.Add(item);
        db.Recipients.Add(recipient);
        await db.SaveChangesAsync();

        using var client = host.CreateClient();
        var created = await client.PostAsync("/odata/CreateInvoice", ODataJson.Body(new
        {
            recipientId = recipient.Id,
            lines = new[] { new { itemId = item.Id, count = 1, unitPrice = (decimal?)null } },
        }));
        created.EnsureSuccessStatusCode();
        using var json = await ODataJson.ReadAsync(created);
        return (json.RootElement.GetProperty("Id").GetInt64(), item.Id, recipient.Id);
    }

    [Fact]
    public async Task A_new_invoice_is_the_latest_revision_of_its_chain()
    {
        using var host = new ApiTestHost();
        var (invoiceId, _, _) = await SeedAsync(host);

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.True((await db.Invoices.SingleAsync(i => i.Id == invoiceId)).IsLatestRevision);
    }

    [Fact]
    public async Task Revising_moves_the_flag_to_the_new_row()
    {
        using var host = new ApiTestHost();
        var (invoiceId, itemId, recipientId) = await SeedAsync(host);

        using var client = host.CreateClient();
        var response = await client.PostAsync("/odata/CreateRevision", ODataJson.Body(new
        {
            invoiceId,
            recipientId,
            lines = new[] { new { itemId, count = 3, unitPrice = (decimal?)null } },
        }));
        response.EnsureSuccessStatusCode();

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var chain = await db.Invoices.Where(i => i.RootId == invoiceId)
            .OrderBy(i => i.RevisionNumber).ToListAsync();
        Assert.Equal(2, chain.Count);
        Assert.False(chain[0].IsLatestRevision);
        Assert.True(chain[1].IsLatestRevision);
    }

    [Fact]
    public async Task Revising_twice_leaves_exactly_one_latest()
    {
        using var host = new ApiTestHost();
        var (invoiceId, itemId, recipientId) = await SeedAsync(host);
        using var client = host.CreateClient();

        for (var attempt = 1; attempt <= 2; attempt++)
        {
            var response = await client.PostAsync("/odata/CreateRevision", ODataJson.Body(new
            {
                invoiceId,
                recipientId,
                lines = new[] { new { itemId, count = attempt, unitPrice = (decimal?)null } },
            }));
            response.EnsureSuccessStatusCode();
        }

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var latest = await db.Invoices.Where(i => i.RootId == invoiceId && i.IsLatestRevision).ToListAsync();
        Assert.Equal(2, Assert.Single(latest).RevisionNumber);
    }

    [Fact]
    public async Task The_migrations_backfill_statement_clears_superseded_rows()
    {
        // The migration runs against a database that predates the column, which
        // this host never has. Run its statement against a chain whose flags are
        // all still at the column default, which is exactly the state it faces.
        using var host = new ApiTestHost();
        var (invoiceId, itemId, recipientId) = await SeedAsync(host);

        using var client = host.CreateClient();
        var revised = await client.PostAsync("/odata/CreateRevision", ODataJson.Body(new
        {
            invoiceId,
            recipientId,
            lines = new[] { new { itemId, count = 2, unitPrice = (decimal?)null } },
        }));
        revised.EnsureSuccessStatusCode();

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.ExecuteSqlRawAsync("UPDATE Invoices SET IsLatestRevision = 1;");

        await db.Database.ExecuteSqlRawAsync(@"
            UPDATE Invoices SET IsLatestRevision = 0
            WHERE EXISTS (
                SELECT 1 FROM Invoices later
                WHERE later.RootId = Invoices.RootId
                  AND later.RevisionNumber > Invoices.RevisionNumber);");

        var chain = await db.Invoices.Where(i => i.RootId == invoiceId)
            .OrderBy(i => i.RevisionNumber).ToListAsync();
        Assert.Equal(new[] { false, true }, chain.Select(i => i.IsLatestRevision));
    }

    [Fact]
    public async Task Bulk_download_leaves_out_superseded_revisions()
    {
        using var host = new ApiTestHost();
        var (invoiceId, itemId, recipientId) = await SeedAsync(host);
        using var client = host.CreateClient();

        var revised = await client.PostAsync("/odata/CreateRevision", ODataJson.Body(new
        {
            invoiceId,
            recipientId,
            lines = new[] { new { itemId, count = 2, unitPrice = (decimal?)null } },
        }));
        revised.EnsureSuccessStatusCode();

        var response = await client.PostAsync("/odata/BulkDownloadDocuments",
            ODataJson.Body(new { kind = "Normal" }));
        response.EnsureSuccessStatusCode();

        using var zip = new ZipArchive(await response.Content.ReadAsStreamAsync(), ZipArchiveMode.Read);
        var name = Assert.Single(zip.Entries).Name;
        Assert.Contains("-R1", name);
    }

    [Fact]
    public async Task A_grave_invoice_line_keeps_the_grave_price_it_was_split_from()
    {
        using var host = new ApiTestHost();
        long graveId;
        {
            using var scope = host.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.VatRates.Add(new VatRate { Rate = 7m });
            var item = new InvoiceItem { Number = "6002", Description = "Grabpflege", VatRate = 7m, DefaultPrice = 100m };
            var first = new Recipient { Number = "E-0002", Name = "Anna", Address = "A 1" };
            var second = new Recipient { Number = "E-0003", Name = "Bernd", Address = "B 2" };
            db.InvoiceItems.Add(item);
            db.Recipients.AddRange(first, second);
            var grave = new Grave
            {
                GraveNumber = "G-100",
                StartDate = new DateOnly(2020, 1, 1),
                EndDate = new DateOnly(2040, 12, 31),
                CreatedAt = DateOnly.FromDateTime(System.DateTime.UtcNow),
            };
            db.Graves.Add(grave);
            await db.SaveChangesAsync();
            db.GraveItems.Add(new GraveItem { GraveId = grave.Id, ItemId = item.Id, Count = 1 });
            db.GraveRecipients.Add(new GraveRecipient { GraveId = grave.Id, RecipientId = first.Id });
            db.GraveRecipients.Add(new GraveRecipient { GraveId = grave.Id, RecipientId = second.Id });
            await db.SaveChangesAsync();
            graveId = grave.Id;
        }

        using var client = host.CreateClient();
        await GraveGenerationHelper.RunAsync(client, 2029, 1);

        using var verify = host.Services.CreateScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<AppDbContext>();
        var lines = await verifyDb.InvoiceLines
            .Where(l => verifyDb.Invoices.Any(i => i.Id == l.InvoiceId && i.GraveId == graveId))
            .ToListAsync();
        Assert.Equal(2, lines.Count);
        // each recipient pays half, but the line remembers the grave's price
        Assert.All(lines, l => Assert.Equal(50m, l.UnitPrice));
        Assert.All(lines, l => Assert.Equal(100m, l.GraveUnitPrice));
    }
}
```

- [ ] **Step 2: Add the shared generation helper the test above uses**

Create `backend/tests/Invoicing.IntegrationTests/GraveGenerationHelper.cs`. Grave
generation is a queued job, so several tests need the same "start it and wait"
loop; `RevisionTests` currently inlines it.

```csharp
using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;

namespace Burkard.Invoicing.IntegrationTests;

/// <summary>Starts a grave generation and waits for the job to finish. Generation
/// runs as a background job, so a test that asserts on its invoices has to wait
/// for it rather than for the POST.</summary>
public static class GraveGenerationHelper
{
    public static async Task RunAsync(HttpClient client, int year, int half)
    {
        var start = await client.PostAsync("/odata/GenerateGraveInvoices",
            ODataJson.Body(new { year, half }));
        Assert.Equal(HttpStatusCode.Accepted, start.StatusCode);
        using var startJson = await ODataJson.ReadAsync(start);
        var jobId = startJson.RootElement.GetProperty("Id").GetInt64();

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(20);
        while (DateTime.UtcNow < deadline)
        {
            var response = await client.GetAsync($"/odata/Jobs({jobId})");
            response.EnsureSuccessStatusCode();
            using var json = await ODataJson.ReadAsync(response);
            var status = json.RootElement.GetProperty("Status").GetString();
            Assert.NotEqual("Failed", status);
            if (status == "Succeeded")
            {
                return;
            }

            await Task.Delay(100);
        }

        Assert.Fail($"Grave generation for {year}-{half} did not finish in time.");
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run the backend tests.
Expected: FAIL — `Invoice.IsLatestRevision` does not exist.

- [ ] **Step 4: Add the entity property and the index**

In `backend/src/Invoicing.Domain/Entities/Invoice.cs`, inside `Invoice`, after
`public int RevisionNumber { get; set; }   // 0 = original`:

```csharp
    /// <summary>True when no other invoice shares this RootId with a higher
    /// RevisionNumber. Lists and bulk downloads show the latest revision only,
    /// which cannot be expressed as an OData $filter without this flag.</summary>
    public bool IsLatestRevision { get; set; } = true;
```

In `backend/src/Invoicing.Api/Persistence/AppDbContext.cs`, in `OnModelCreating`,
after `mb.Entity<Invoice>().HasIndex(i => i.RootId);`:

```csharp
        // the invoice list and the grave detail page both filter on this pair
        mb.Entity<Invoice>().HasIndex(i => new { i.GraveId, i.IsLatestRevision });
```

- [ ] **Step 5: Generate the migration**

```bash
scripts/ef.sh migrations add LatestRevisionAndGraveUnitPrice \
  --project src/Invoicing.Api --startup-project src/Invoicing.Api
```

Check the generated `Up` contains exactly two `AddColumn` calls —
`IsLatestRevision` on `Invoices` (bool, not null, `defaultValue: true`) and
`GraveUnitPrice` on `InvoiceLines` (`TEXT`, nullable) — plus the new index. If
`GraveUnitPrice` is missing, Task 1 Step 3's entity change was not saved.

- [ ] **Step 6: Add the backfill to the migration**

In the generated migration's `Up`, after the `AddColumn` for `IsLatestRevision`:

```csharp
            // Existing chains were written before the flag existed: everything is
            // "latest" by the column default, so only the superseded rows need
            // clearing. GraveUnitPrice is deliberately left null on existing rows —
            // GraveInvoiceGroup falls back to reconstructing it for them.
            migrationBuilder.Sql(@"
                UPDATE Invoices SET IsLatestRevision = 0
                WHERE EXISTS (
                    SELECT 1 FROM Invoices later
                    WHERE later.RootId = Invoices.RootId
                      AND later.RevisionNumber > Invoices.RevisionNumber);");
```

- [ ] **Step 7: Maintain the flag in `InvoiceService`**

In `backend/src/Invoicing.Api/Services/InvoiceService.cs`, in
`CreateRevisionAsync`, replace:

```csharp
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        _db.Invoices.Add(revision);
        await _db.SaveChangesAsync(ct);
```

with:

```csharp
        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        // the chain has exactly one latest revision, and it is now this one
        var superseded = await _db.Invoices
            .Where(i => i.RootId == edited.RootId && i.IsLatestRevision)
            .ToListAsync(ct);
        foreach (var invoice in superseded)
        {
            invoice.IsLatestRevision = false;
        }

        _db.Invoices.Add(revision);
        await _db.SaveChangesAsync(ct);
```

`PersistAsync` and `GraveInvoiceGenerator` need no change: the property defaults
to `true`.

- [ ] **Step 8: Filter the bulk download**

In `backend/src/Invoicing.Api/OData/Controllers/BulkDownloadController.cs`,
replace:

```csharp
        var kind = request.Kind == "Normal" ? DocumentKind.Normal : DocumentKind.Copy;
        IQueryable<Document> query = _db.Documents.Where(d => d.Kind == kind);
```

with:

```csharp
        var kind = request.Kind == "Normal" ? DocumentKind.Normal : DocumentKind.Copy;
        // a superseded revision's documents were replaced; a bulk download must
        // not put them next to the revision that replaced them
        IQueryable<Document> query = _db.Documents
            .Where(d => d.Kind == kind && d.Invoice != null && d.Invoice.IsLatestRevision);
```

- [ ] **Step 9: Run the tests to verify they pass**

Run the backend tests.
Expected: PASS — the six `LatestRevisionTests` and the whole existing suite.

- [ ] **Step 10: Commit**

```bash
git add backend/src/Invoicing.Domain/Entities/Invoice.cs \
        backend/src/Invoicing.Api/Persistence/AppDbContext.cs \
        backend/src/Invoicing.Api/Migrations/ \
        backend/src/Invoicing.Api/Services/InvoiceService.cs \
        backend/src/Invoicing.Api/OData/Controllers/BulkDownloadController.cs \
        backend/tests/Invoicing.IntegrationTests/LatestRevisionTests.cs \
        backend/tests/Invoicing.IntegrationTests/GraveGenerationHelper.cs
git commit -m "feat: mark the latest revision and remember the grave line price

Lists and bulk downloads show one row per invoice chain, which needs a
flag no OData \$filter can derive. Grave invoice lines additionally
remember the price they were split from, so editing them later shows what
was invoiced instead of a reconstruction."
```

---

### Task 3: Invoice search as a custom query option

**Files:**

- Create: `backend/src/Invoicing.Api/Services/InvoiceSearch.cs`
- Modify: `backend/src/Invoicing.Api/OData/Controllers/InvoicesController.cs`
- Test: `backend/tests/Invoicing.IntegrationTests/InvoiceSearchTests.cs`

**Interfaces:**

- Produces: `GET /odata/Invoices?search=<term>` — narrows the entity set before
  `EnableQuery` applies `$filter`, `$orderby`, `$skip` and `$top`, so paging still
  works. Task 8's search field is the only consumer.
- Produces: `InvoiceSearch.Matches(InvoiceSearch.Row row, string term) -> bool` and
  `InvoiceSearch.GrossTotal(IEnumerable<(int Count, decimal UnitPrice, decimal VatRate)>) -> decimal`.

The term is read from `Request.Query` rather than taken as an action parameter:
adding a parameter to `Get()` risks the OData routing convention no longer
recognising it as the entity-set action.

- [ ] **Step 1: Write the failing tests**

Create `backend/tests/Invoicing.IntegrationTests/InvoiceSearchTests.cs`:

```csharp
using System;
using System.Linq;
using System.Threading.Tasks;
using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Api.Services;
using Burkard.Invoicing.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Burkard.Invoicing.IntegrationTests;

public class InvoiceSearchTests
{
    private static InvoiceSearch.Row Row(string number = "2026-0001", string name = "Familie Müller",
        string address = "Hauptstraße 1\n12345 Musterstadt", decimal gross = 214.00m) =>
        new(1, number, name, address, new DateTime(2026, 3, 14, 10, 0, 0, DateTimeKind.Utc), gross);

    [Theory]
    [InlineData("0001")]
    [InlineData("2026-0001")]
    public void Matches_the_invoice_number(string term) =>
        Assert.True(InvoiceSearch.Matches(Row(), term));

    [Theory]
    [InlineData("müller")]
    [InlineData("MÜLLER")]
    [InlineData("Müller")]
    public void Matches_the_recipient_name_regardless_of_case_including_umlauts(string term) =>
        Assert.True(InvoiceSearch.Matches(Row(), term));

    [Fact]
    public void Matches_the_recipient_address() =>
        Assert.True(InvoiceSearch.Matches(Row(), "hauptstraße"));

    [Theory]
    [InlineData("14.03.2026")]
    [InlineData("03.2026")]
    public void Matches_the_date_as_it_is_displayed(string term) =>
        Assert.True(InvoiceSearch.Matches(Row(), term));

    [Theory]
    [InlineData("214.00")]
    [InlineData("214,00")]
    [InlineData("214")]
    public void Matches_the_total_with_either_decimal_separator(string term) =>
        Assert.True(InvoiceSearch.Matches(Row(), term));

    [Fact]
    public void Does_not_match_an_unrelated_term() =>
        Assert.False(InvoiceSearch.Matches(Row(), "Schmidt"));

    [Fact]
    public void Gross_total_rounds_each_line_the_way_the_document_does()
    {
        var total = InvoiceSearch.GrossTotal(new[] { (2, 100m, 7m) });
        Assert.Equal(214.00m, total);
    }

    [Fact]
    public async Task The_search_query_option_narrows_the_entity_set()
    {
        using var host = new ApiTestHost();
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.VatRates.Add(new VatRate { Rate = 7m });
        var item = new InvoiceItem { Number = "4001", Description = "Grabpflege", VatRate = 7m, DefaultPrice = 100m };
        var mueller = new Recipient { Number = "E-0001", Name = "Familie Müller", Address = "Hauptstraße 1" };
        var schmidt = new Recipient { Number = "E-0002", Name = "Familie Schmidt", Address = "Nebenweg 2" };
        db.InvoiceItems.Add(item);
        db.Recipients.AddRange(mueller, schmidt);
        await db.SaveChangesAsync();

        using var client = host.CreateClient();
        foreach (var recipient in new[] { mueller, schmidt })
        {
            var created = await client.PostAsync("/odata/CreateInvoice", ODataJson.Body(new
            {
                recipientId = recipient.Id,
                lines = new[] { new { itemId = item.Id, count = 2, unitPrice = (decimal?)null } },
            }));
            created.EnsureSuccessStatusCode();
        }

        var response = await client.GetAsync("/odata/Invoices?search=m%C3%BCller");
        response.EnsureSuccessStatusCode();
        using var json = await ODataJson.ReadAsync(response);
        var names = json.RootElement.GetProperty("value").EnumerateArray()
            .Select(i => i.GetProperty("RecipientName").GetString()).ToList();
        Assert.Equal(new[] { "Familie Müller" }, names);
    }

    [Fact]
    public async Task The_search_query_option_composes_with_filter_and_top()
    {
        using var host = new ApiTestHost();
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.VatRates.Add(new VatRate { Rate = 7m });
        var item = new InvoiceItem { Number = "4002", Description = "Grabpflege", VatRate = 7m, DefaultPrice = 100m };
        var recipient = new Recipient { Number = "E-0003", Name = "Familie Beispiel", Address = "Weg 3" };
        db.InvoiceItems.Add(item);
        db.Recipients.Add(recipient);
        await db.SaveChangesAsync();

        using var client = host.CreateClient();
        for (var i = 0; i < 3; i++)
        {
            var created = await client.PostAsync("/odata/CreateInvoice", ODataJson.Body(new
            {
                recipientId = recipient.Id,
                lines = new[] { new { itemId = item.Id, count = 1, unitPrice = (decimal?)null } },
            }));
            created.EnsureSuccessStatusCode();
        }

        var response = await client.GetAsync(
            "/odata/Invoices?search=Beispiel&$filter=GraveId eq null&$orderby=Id desc&$top=2");
        response.EnsureSuccessStatusCode();
        using var json = await ODataJson.ReadAsync(response);
        Assert.Equal(2, json.RootElement.GetProperty("value").GetArrayLength());
    }

    [Fact]
    public async Task An_empty_search_returns_everything()
    {
        using var host = new ApiTestHost();
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.VatRates.Add(new VatRate { Rate = 7m });
        var item = new InvoiceItem { Number = "4003", Description = "Grabpflege", VatRate = 7m, DefaultPrice = 100m };
        var recipient = new Recipient { Number = "E-0004", Name = "Familie Beispiel", Address = "Weg 4" };
        db.InvoiceItems.Add(item);
        db.Recipients.Add(recipient);
        await db.SaveChangesAsync();

        using var client = host.CreateClient();
        var created = await client.PostAsync("/odata/CreateInvoice", ODataJson.Body(new
        {
            recipientId = recipient.Id,
            lines = new[] { new { itemId = item.Id, count = 1, unitPrice = (decimal?)null } },
        }));
        created.EnsureSuccessStatusCode();

        var response = await client.GetAsync("/odata/Invoices?search=");
        response.EnsureSuccessStatusCode();
        using var json = await ODataJson.ReadAsync(response);
        Assert.Equal(1, json.RootElement.GetProperty("value").GetArrayLength());
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run the backend tests.
Expected: FAIL — `InvoiceSearch` does not exist.

- [ ] **Step 3: Write the matcher**

Create `backend/src/Invoicing.Api/Services/InvoiceSearch.cs`:

```csharp
using System.Globalization;

namespace Burkard.Invoicing.Api.Services;

/// <summary>Matches a search term against an invoice's own fields — number,
/// recipient, date and total. Line items are deliberately not searched.
///
/// The comparison runs in memory rather than as a SQL LIKE: SQLite folds case
/// for ASCII only, so a term typed as 'ö' would miss a name spelled 'Ö'.</summary>
public static class InvoiceSearch
{
    public sealed record Row(
        long Id,
        string InvoiceNumber,
        string RecipientName,
        string RecipientAddress,
        DateTime GeneratedAt,
        decimal GrossTotal);

    public static bool Matches(Row row, string term)
    {
        var needle = term.Trim();
        if (needle.Length == 0)
        {
            return true;
        }

        return Contains(row.InvoiceNumber, needle)
            || Contains(row.RecipientName, needle)
            || Contains(row.RecipientAddress, needle)
            // the date as the list shows it, so what the user reads is what they can type
            || Contains(row.GeneratedAt.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture), needle)
            // the total is rendered with a '.'; a German '," in the term still matches
            || Contains(row.GrossTotal.ToString("0.00", CultureInfo.InvariantCulture),
                        needle.Replace(',', '.'));
    }

    /// <summary>The invoice's gross total, rounded exactly the way
    /// DocumentDataBuilder rounds it, so the number the user reads on the
    /// document is the number they can search for.</summary>
    public static decimal GrossTotal(IEnumerable<(int Count, decimal UnitPrice, decimal VatRate)> lines)
    {
        decimal netCents = 0;
        decimal vatCents = 0;
        foreach (var line in lines)
        {
            var lineCents = Math.Round(line.Count * line.UnitPrice * 100m, 0, MidpointRounding.AwayFromZero);
            netCents += lineCents;
            vatCents += Math.Round(lineCents * line.VatRate / 100m, 0, MidpointRounding.AwayFromZero);
        }

        return (netCents + vatCents) / 100m;
    }

    private static bool Contains(string haystack, string needle) =>
        haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);
}
```

- [ ] **Step 4: Wire it into the entity set**

In `backend/src/Invoicing.Api/OData/Controllers/InvoicesController.cs`, add
`using Burkard.Invoicing.Api.Services;` and replace:

```csharp
    [EnableQuery]
    public IQueryable<Invoice> Get() => _db.Invoices.AsNoTracking();
```

with:

```csharp
    /// <summary>The invoice set, optionally narrowed by the 'search' custom query
    /// option. Returning an IQueryable keeps $filter, $orderby and paging working
    /// on top of the narrowed set. The term is read from the query string rather
    /// than taken as a parameter so the OData routing convention still recognises
    /// this as the entity set's Get.</summary>
    [EnableQuery]
    public IQueryable<Invoice> Get()
    {
        var term = Request.Query["search"].ToString();
        if (string.IsNullOrWhiteSpace(term))
        {
            return _db.Invoices.AsNoTracking();
        }

        var candidates = _db.Invoices.AsNoTracking()
            .Select(i => new
            {
                i.Id,
                i.InvoiceNumber,
                i.RecipientName,
                i.RecipientAddress,
                i.GeneratedAt,
                Lines = i.Lines.Select(l => new { l.Count, l.UnitPrice, l.VatRate }).ToList(),
            })
            .ToList();

        var matched = candidates
            .Where(c => InvoiceSearch.Matches(
                new InvoiceSearch.Row(c.Id, c.InvoiceNumber, c.RecipientName, c.RecipientAddress, c.GeneratedAt,
                    InvoiceSearch.GrossTotal(c.Lines.Select(l => (l.Count, l.UnitPrice, l.VatRate)))),
                term))
            .Select(c => c.Id)
            .ToHashSet();

        return _db.Invoices.AsNoTracking().Where(i => matched.Contains(i.Id));
    }
```

- [ ] **Step 5: Run the tests to verify they pass**

Run the backend tests.
Expected: PASS — all `InvoiceSearchTests`, and the existing suite unchanged.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Invoicing.Api/Services/InvoiceSearch.cs \
        backend/src/Invoicing.Api/OData/Controllers/InvoicesController.cs \
        backend/tests/Invoicing.IntegrationTests/InvoiceSearchTests.cs
git commit -m "feat: search invoices by number, recipient, date and total

A custom query option rather than a route: narrowing the IQueryable keys
\$filter and paging off the same request. Matching runs in memory because
SQLite folds case for ASCII only, which would miss German umlauts."
```

---

### Task 4: `GraveInvoiceRuns` and `GraveInvoiceGroup`

**Files:**

- Create: `backend/src/Invoicing.Api/OData/Controllers/GraveInvoiceRunsController.cs`
- Create: `backend/src/Invoicing.Api/OData/Controllers/GraveInvoiceGroupController.cs`
- Modify: `backend/src/Invoicing.Api/OData/EdmModelBuilder.cs`
- Test: `backend/tests/Invoicing.IntegrationTests/GraveInvoiceGroupTests.cs`

**Interfaces:**

- Consumes: `GraveInvoiceSplitter.Combine`, `InvoiceLine.GraveUnitPrice`,
  `Invoice.IsLatestRevision`.
- Produces:
  - `GET /odata/GraveInvoiceRuns?$filter=GraveId eq {id}` — EDM entity set, read-only.
  - `GET /odata/GraveInvoiceGroup?invoiceId={id}` →
    `{ GraveId, GraveNumber, PeriodYear, PeriodHalf, Recipients: [{ Id, Name, InvoiceId }], Lines: [{ ItemId, ItemNumber, Description, Count, UnitPrice, VatRate }] }`.
    Task 12's dialog and Task 13's detail page consume it.

- [ ] **Step 1: Write the failing tests**

Create `backend/tests/Invoicing.IntegrationTests/GraveInvoiceGroupTests.cs`:

```csharp
using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Burkard.Invoicing.IntegrationTests;

public class GraveInvoiceGroupTests
{
    /// <summary>A grave with one item at 100.00 and two recipients, invoiced for
    /// 2029 H1. Returns the grave id and one of its invoice ids.</summary>
    private static async Task<(long GraveId, long InvoiceId, long ItemId)> SeedInvoicedGraveAsync(ApiTestHost host)
    {
        long graveId, itemId;
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.VatRates.Add(new VatRate { Rate = 7m });
            var item = new InvoiceItem { Number = "3001", Description = "Grabpflege", VatRate = 7m, DefaultPrice = 100m };
            var anna = new Recipient { Number = "E-0001", Name = "Anna Beispiel", Address = "A 1" };
            var bernd = new Recipient { Number = "E-0002", Name = "Bernd Beispiel", Address = "B 2" };
            db.InvoiceItems.Add(item);
            db.Recipients.AddRange(anna, bernd);
            var grave = new Grave
            {
                GraveNumber = "G-200",
                StartDate = new DateOnly(2020, 1, 1),
                EndDate = new DateOnly(2040, 12, 31),
                CreatedAt = DateOnly.FromDateTime(DateTime.UtcNow),
            };
            db.Graves.Add(grave);
            await db.SaveChangesAsync();
            db.GraveItems.Add(new GraveItem { GraveId = grave.Id, ItemId = item.Id, Count = 2 });
            db.GraveRecipients.Add(new GraveRecipient { GraveId = grave.Id, RecipientId = anna.Id });
            db.GraveRecipients.Add(new GraveRecipient { GraveId = grave.Id, RecipientId = bernd.Id });
            await db.SaveChangesAsync();
            (graveId, itemId) = (grave.Id, item.Id);
        }

        using var client = host.CreateClient();
        await GraveGenerationHelper.RunAsync(client, 2029, 1);

        using var verify = host.Services.CreateScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<AppDbContext>();
        var invoiceId = (await verifyDb.Invoices.FirstAsync(i => i.GraveId == graveId)).Id;
        return (graveId, invoiceId, itemId);
    }

    [Fact]
    public async Task Returns_the_grave_lines_as_they_were_invoiced()
    {
        using var host = new ApiTestHost();
        var (graveId, invoiceId, _) = await SeedInvoicedGraveAsync(host);

        using var client = host.CreateClient();
        var response = await client.GetAsync($"/odata/GraveInvoiceGroup?invoiceId={invoiceId}");
        response.EnsureSuccessStatusCode();
        using var json = await ODataJson.ReadAsync(response);
        var root = json.RootElement;

        Assert.Equal(graveId, root.GetProperty("GraveId").GetInt64());
        Assert.Equal("G-200", root.GetProperty("GraveNumber").GetString());
        Assert.Equal(2029, root.GetProperty("PeriodYear").GetInt32());
        Assert.Equal(1, root.GetProperty("PeriodHalf").GetInt32());

        var names = root.GetProperty("Recipients").EnumerateArray()
            .Select(r => r.GetProperty("Name").GetString()).ToList();
        Assert.Equal(new[] { "Anna Beispiel", "Bernd Beispiel" }, names);

        var line = Assert.Single(root.GetProperty("Lines").EnumerateArray().ToList());
        Assert.Equal(2, line.GetProperty("Count").GetInt32());
        // the grave's price, not the 50.00 share each recipient was billed
        Assert.Equal(100m, line.GetProperty("UnitPrice").GetDecimal());
        Assert.Equal("3001", line.GetProperty("ItemNumber").GetString());
    }

    [Fact]
    public async Task Is_unaffected_by_a_later_grave_edit()
    {
        using var host = new ApiTestHost();
        var (graveId, invoiceId, itemId) = await SeedInvoicedGraveAsync(host);

        using var client = host.CreateClient();
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var link = await db.GraveItems.SingleAsync(i => i.GraveId == graveId);
            link.Count = 9;
            link.UnitPrice = 777m;
            await db.SaveChangesAsync();
        }

        var response = await client.GetAsync($"/odata/GraveInvoiceGroup?invoiceId={invoiceId}");
        response.EnsureSuccessStatusCode();
        using var json = await ODataJson.ReadAsync(response);

        var line = Assert.Single(json.RootElement.GetProperty("Lines").EnumerateArray().ToList());
        Assert.Equal(2, line.GetProperty("Count").GetInt32());
        Assert.Equal(100m, line.GetProperty("UnitPrice").GetDecimal());
    }

    [Fact]
    public async Task Rejects_an_invoice_that_is_not_a_grave_invoice()
    {
        using var host = new ApiTestHost();
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.VatRates.Add(new VatRate { Rate = 7m });
        var item = new InvoiceItem { Number = "3002", Description = "X", VatRate = 7m, DefaultPrice = 10m };
        var recipient = new Recipient { Number = "E-0009", Name = "Solo", Address = "S 1" };
        db.InvoiceItems.Add(item);
        db.Recipients.Add(recipient);
        await db.SaveChangesAsync();

        using var client = host.CreateClient();
        var created = await client.PostAsync("/odata/CreateInvoice", ODataJson.Body(new
        {
            recipientId = recipient.Id,
            lines = new[] { new { itemId = item.Id, count = 1, unitPrice = (decimal?)null } },
        }));
        created.EnsureSuccessStatusCode();
        using var createdJson = await ODataJson.ReadAsync(created);
        var invoiceId = createdJson.RootElement.GetProperty("Id").GetInt64();

        var response = await client.GetAsync($"/odata/GraveInvoiceGroup?invoiceId={invoiceId}");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        Assert.Equal("NotAGraveInvoice", json.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Reports_an_unknown_invoice()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();

        var response = await client.GetAsync("/odata/GraveInvoiceGroup?invoiceId=987654");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        Assert.Equal("InvoiceNotFound", json.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Runs_are_readable_per_grave()
    {
        using var host = new ApiTestHost();
        var (graveId, _, _) = await SeedInvoicedGraveAsync(host);

        using var client = host.CreateClient();
        var response = await client.GetAsync($"/odata/GraveInvoiceRuns?$filter=GraveId eq {graveId}");
        response.EnsureSuccessStatusCode();
        using var json = await ODataJson.ReadAsync(response);

        var run = Assert.Single(json.RootElement.GetProperty("value").EnumerateArray().ToList());
        Assert.Equal(2029, run.GetProperty("Year").GetInt32());
        Assert.Equal(1, run.GetProperty("Half").GetInt32());
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run the backend tests.
Expected: FAIL — 404 for both new routes.

- [ ] **Step 3: Add the runs entity set**

Create `backend/src/Invoicing.Api/OData/Controllers/GraveInvoiceRunsController.cs`:

```csharp
using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Domain.Entities;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using Microsoft.EntityFrameworkCore;

namespace Burkard.Invoicing.Api.OData.Controllers;

/// <summary>Read-only: runs are written by generation alone. The grave detail
/// page reads them to show which half-years a grave was invoiced for.</summary>
public class GraveInvoiceRunsController : ODataController
{
    private readonly AppDbContext _db;

    public GraveInvoiceRunsController(AppDbContext db) => _db = db;

    [EnableQuery]
    public IQueryable<GraveInvoiceRun> Get() => _db.GraveInvoiceRuns.AsNoTracking();
}
```

In `backend/src/Invoicing.Api/OData/EdmModelBuilder.cs`, after
`b.EntitySet<GraveRecipient>("GraveRecipients");`:

```csharp
        b.EntitySet<GraveInvoiceRun>("GraveInvoiceRuns");
```

- [ ] **Step 4: Add the group projection**

Create `backend/src/Invoicing.Api/OData/Controllers/GraveInvoiceGroupController.cs`:

```csharp
using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Domain;
using Burkard.Invoicing.Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using Microsoft.EntityFrameworkCore;

namespace Burkard.Invoicing.Api.OData.Controllers;

/// <summary>The grave, half-year and recipients one grave invoice belongs to,
/// together with the grave's lines as they were invoiced. Editing a grave invoice
/// edits the grave's lines, not one recipient's share, so the dialog opens on
/// this rather than on the grave's current configuration — a grave edited after
/// the invoice was generated must not leak into a revision of that period.</summary>
[Route("odata")]
public class GraveInvoiceGroupController : ODataController
{
    private readonly AppDbContext _db;

    public GraveInvoiceGroupController(AppDbContext db) => _db = db;

    [HttpGet("GraveInvoiceGroup")]
    public async Task<IActionResult> Group([FromQuery] long invoiceId)
    {
        var invoice = await _db.Invoices.AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == invoiceId, HttpContext.RequestAborted);
        if (invoice is null)
        {
            return NotFound(new
            {
                error = new { code = "InvoiceNotFound", message = "The invoice does not exist." }
            });
        }

        if (invoice.GraveId is null || invoice.PeriodYear is null || invoice.PeriodHalf is null)
        {
            return BadRequest(new
            {
                error = new { code = "NotAGraveInvoice", message = "The invoice does not belong to a grave." }
            });
        }

        var siblings = await _db.Invoices.AsNoTracking()
            .Include(i => i.Lines)
            .Where(i => i.GraveId == invoice.GraveId
                        && i.PeriodYear == invoice.PeriodYear
                        && i.PeriodHalf == invoice.PeriodHalf
                        && i.IsLatestRevision)
            .OrderBy(i => i.RecipientName)
            .ThenBy(i => i.Id)
            .ToListAsync(HttpContext.RequestAborted);

        var graveNumber = await _db.Graves.AsNoTracking()
            .Where(g => g.Id == invoice.GraveId)
            .Select(g => g.GraveNumber)
            .FirstAsync(HttpContext.RequestAborted);

        var value = new
        {
            GraveId = invoice.GraveId.Value,
            GraveNumber = graveNumber,
            PeriodYear = invoice.PeriodYear.Value,
            PeriodHalf = invoice.PeriodHalf.Value,
            Recipients = siblings.Select(s => new { Id = s.RecipientId, Name = s.RecipientName, InvoiceId = s.Id }),
            Lines = GraveLines(siblings).Select(l => new
            {
                l.ItemId, l.ItemNumber, l.Description, l.Count, l.UnitPrice, l.VatRate,
            }),
        };

        return new JsonResult(value, ODataJsonDefaults.PascalCase);
    }

    /// <summary>The grave's lines as invoiced. Invoices generated after
    /// InvoiceLine.GraveUnitPrice exists carry the price directly; older ones are
    /// reconstructed from the recipients' shares, which is best-effort.</summary>
    private static IReadOnlyList<GraveLine> GraveLines(List<Invoice> siblings)
    {
        var reference = siblings[0].Lines.OrderBy(l => l.LineNumber).ToList();
        if (reference.All(l => l.GraveUnitPrice is not null))
        {
            return reference
                .Select(l => new GraveLine(l.ItemId ?? 0, l.ItemNumber, l.Description,
                    l.Count, l.GraveUnitPrice!.Value, l.VatRate))
                .ToList();
        }

        return GraveInvoiceSplitter.Combine(siblings
            .Select(s => (IReadOnlyList<InvoiceLine>)s.Lines.OrderBy(l => l.LineNumber).ToList())
            .ToList());
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run the backend tests.
Expected: PASS — all five `GraveInvoiceGroupTests`.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Invoicing.Api/OData/Controllers/GraveInvoiceRunsController.cs \
        backend/src/Invoicing.Api/OData/Controllers/GraveInvoiceGroupController.cs \
        backend/src/Invoicing.Api/OData/EdmModelBuilder.cs \
        backend/tests/Invoicing.IntegrationTests/GraveInvoiceGroupTests.cs
git commit -m "feat: expose a grave invoice's group and a grave's invoice runs

The edit dialog needs the grave's lines as they were invoiced, which is a
report about a group of invoices rather than an entity. Runs become a
read-only entity set so a grave detail page can show its half-years."
```

---

### Task 5: `CreateGraveInvoiceRevision` — revise every recipient at once

**Files:**

- Create: `backend/src/Invoicing.Api/Services/InvoiceDocumentFactory.cs`
- Create: `backend/src/Invoicing.Api/Services/GraveInvoiceRevisionService.cs`
- Modify: `backend/src/Invoicing.Api/Services/InvoiceService.cs` (use the factory)
- Modify: `backend/src/Invoicing.Api/Services/GraveInvoiceGenerator.cs` (use the factory)
- Modify: `backend/src/Invoicing.Api/OData/Controllers/InvoiceActionsController.cs`
- Modify: `backend/src/Invoicing.Api/OData/EdmModelBuilder.cs`
- Modify: `backend/src/Invoicing.Api/Program.cs`
- Test: `backend/tests/Invoicing.IntegrationTests/GraveInvoiceRevisionTests.cs`

**Interfaces:**

- Consumes: `GraveInvoiceSplitter.Split`, `InvoiceLineInput`, `ValidationException`,
  `Invoice.IsLatestRevision`.
- Produces:
  - `InvoiceDocumentFactory.Build(Invoice invoice, DateTime generatedAt) -> List<Document>`
  - `GraveInvoiceRevisionService.CreateAsync(long invoiceId, IReadOnlyList<InvoiceLineInput> lines, CancellationToken ct) -> Task<List<Invoice>>`
  - `POST /odata/CreateGraveInvoiceRevision` with body `{ invoiceId, lines: [{ itemId, count, unitPrice }] }`,
    where `lines` are **grave-level**. Task 12's dialog is the only consumer.

- [ ] **Step 1: Write the failing tests**

Create `backend/tests/Invoicing.IntegrationTests/GraveInvoiceRevisionTests.cs`:

```csharp
using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Burkard.Invoicing.IntegrationTests;

public class GraveInvoiceRevisionTests
{
    /// <summary>A grave with one item (count 2 at 100.00, 7% VAT) and two
    /// recipients, invoiced for 2031 H1.</summary>
    private static async Task<(long GraveId, long ItemId, long InvoiceId)> SeedAsync(ApiTestHost host)
    {
        long graveId, itemId;
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.VatRates.Add(new VatRate { Rate = 7m });
            var item = new InvoiceItem { Number = "2001", Description = "Grabpflege", VatRate = 7m, DefaultPrice = 100m };
            var anna = new Recipient { Number = "E-0001", Name = "Anna Beispiel", Address = "A 1" };
            var bernd = new Recipient { Number = "E-0002", Name = "Bernd Beispiel", Address = "B 2" };
            db.InvoiceItems.Add(item);
            db.Recipients.AddRange(anna, bernd);
            var grave = new Grave
            {
                GraveNumber = "G-300",
                StartDate = new DateOnly(2020, 1, 1),
                EndDate = new DateOnly(2040, 12, 31),
                CreatedAt = DateOnly.FromDateTime(DateTime.UtcNow),
            };
            db.Graves.Add(grave);
            await db.SaveChangesAsync();
            db.GraveItems.Add(new GraveItem { GraveId = grave.Id, ItemId = item.Id, Count = 2 });
            db.GraveRecipients.Add(new GraveRecipient { GraveId = grave.Id, RecipientId = anna.Id });
            db.GraveRecipients.Add(new GraveRecipient { GraveId = grave.Id, RecipientId = bernd.Id });
            await db.SaveChangesAsync();
            (graveId, itemId) = (grave.Id, item.Id);
        }

        using var client = host.CreateClient();
        await GraveGenerationHelper.RunAsync(client, 2031, 1);

        using var verify = host.Services.CreateScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<AppDbContext>();
        var invoiceId = (await verifyDb.Invoices.OrderBy(i => i.Id).FirstAsync(i => i.GraveId == graveId)).Id;
        return (graveId, itemId, invoiceId);
    }

    [Fact]
    public async Task Editing_one_invoice_revises_every_recipient()
    {
        using var host = new ApiTestHost();
        var (graveId, itemId, invoiceId) = await SeedAsync(host);

        using var client = host.CreateClient();
        var response = await client.PostAsync("/odata/CreateGraveInvoiceRevision", ODataJson.Body(new
        {
            invoiceId,
            lines = new[] { new { itemId, count = 3, unitPrice = (decimal?)200m } },
        }));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var invoices = await db.Invoices
            .Include(i => i.Lines).Include(i => i.Documents)
            .Where(i => i.GraveId == graveId)
            .ToListAsync();

        // two originals plus one revision each
        Assert.Equal(4, invoices.Count);
        var revisions = invoices.Where(i => i.RevisionNumber == 1).ToList();
        Assert.Equal(2, revisions.Count);
        Assert.All(revisions, r => Assert.True(r.IsLatestRevision));
        Assert.All(revisions, r => Assert.EndsWith("-R1", r.InvoiceNumber));
        Assert.All(invoices.Where(i => i.RevisionNumber == 0), o => Assert.False(o.IsLatestRevision));

        // each revision carries the grave's new line, split in half
        Assert.All(revisions, r =>
        {
            var line = Assert.Single(r.Lines);
            Assert.Equal(3, line.Count);
            Assert.Equal(200m, line.GraveUnitPrice);
            Assert.Equal(100m, line.UnitPrice);
        });

        // both recipients keep their own invoice and get both documents again
        Assert.Equal(new[] { "Anna Beispiel", "Bernd Beispiel" },
            revisions.Select(r => r.RecipientName).OrderBy(n => n));
        Assert.All(revisions, r => Assert.Equal(2, r.Documents.Count));
        Assert.All(revisions, r => Assert.All(r.Documents, d => Assert.NotEmpty(d.Content)));
    }

    [Fact]
    public async Task The_revisions_keep_the_grave_period_and_share()
    {
        using var host = new ApiTestHost();
        var (graveId, itemId, invoiceId) = await SeedAsync(host);

        using var client = host.CreateClient();
        var response = await client.PostAsync("/odata/CreateGraveInvoiceRevision", ODataJson.Body(new
        {
            invoiceId,
            lines = new[] { new { itemId, count = 2, unitPrice = (decimal?)100m } },
        }));
        response.EnsureSuccessStatusCode();

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var revisions = await db.Invoices.Where(i => i.GraveId == graveId && i.RevisionNumber == 1).ToListAsync();

        Assert.All(revisions, r => Assert.Equal(graveId, r.GraveId));
        Assert.All(revisions, r => Assert.Equal(2031, r.PeriodYear));
        Assert.All(revisions, r => Assert.Equal(1, r.PeriodHalf));
        Assert.All(revisions, r => Assert.Equal(50m, r.SharePercent));
    }

    [Fact]
    public async Task Editing_twice_keeps_the_chains_in_step()
    {
        using var host = new ApiTestHost();
        var (graveId, itemId, invoiceId) = await SeedAsync(host);
        using var client = host.CreateClient();

        for (var count = 3; count <= 4; count++)
        {
            var response = await client.PostAsync("/odata/CreateGraveInvoiceRevision", ODataJson.Body(new
            {
                invoiceId,
                lines = new[] { new { itemId, count, unitPrice = (decimal?)100m } },
            }));
            response.EnsureSuccessStatusCode();
        }

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var latest = await db.Invoices.Where(i => i.GraveId == graveId && i.IsLatestRevision).ToListAsync();
        Assert.Equal(2, latest.Count);
        Assert.All(latest, i => Assert.Equal(2, i.RevisionNumber));
    }

    [Fact]
    public async Task Rejects_a_non_grave_invoice()
    {
        using var host = new ApiTestHost();
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.VatRates.Add(new VatRate { Rate = 7m });
        var item = new InvoiceItem { Number = "2002", Description = "X", VatRate = 7m, DefaultPrice = 10m };
        var recipient = new Recipient { Number = "E-0009", Name = "Solo", Address = "S 1" };
        db.InvoiceItems.Add(item);
        db.Recipients.Add(recipient);
        await db.SaveChangesAsync();

        using var client = host.CreateClient();
        var created = await client.PostAsync("/odata/CreateInvoice", ODataJson.Body(new
        {
            recipientId = recipient.Id,
            lines = new[] { new { itemId = item.Id, count = 1, unitPrice = (decimal?)null } },
        }));
        created.EnsureSuccessStatusCode();
        using var createdJson = await ODataJson.ReadAsync(created);

        var response = await client.PostAsync("/odata/CreateGraveInvoiceRevision", ODataJson.Body(new
        {
            invoiceId = createdJson.RootElement.GetProperty("Id").GetInt64(),
            lines = new[] { new { itemId = item.Id, count = 1, unitPrice = (decimal?)10m } },
        }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        Assert.Equal("NotAGraveInvoice", json.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Rejects_an_empty_line_list_without_touching_anything()
    {
        using var host = new ApiTestHost();
        var (graveId, _, invoiceId) = await SeedAsync(host);

        using var client = host.CreateClient();
        var response = await client.PostAsync("/odata/CreateGraveInvoiceRevision", ODataJson.Body(new
        {
            invoiceId,
            lines = Array.Empty<object>(),
        }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        Assert.Equal("NoLines", json.RootElement.GetProperty("error").GetProperty("code").GetString());

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(2, await db.Invoices.CountAsync(i => i.GraveId == graveId));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run the backend tests.
Expected: FAIL — 404 for `/odata/CreateGraveInvoiceRevision`.

- [ ] **Step 3: Extract the document pair into a factory**

Three services now need the same normal-plus-copy pair. Create
`backend/src/Invoicing.Api/Services/InvoiceDocumentFactory.cs`:

```csharp
using Burkard.Invoicing.Domain.Entities;
using Burkard.Invoicing.Documents;

namespace Burkard.Invoicing.Api.Services;

/// <summary>Builds an invoice's document pair. Both are produced in one step so a
/// half-generated pair can never be persisted (FR-043).</summary>
public sealed class InvoiceDocumentFactory
{
    private readonly DocumentDataBuilder _dataBuilder;
    private readonly IDocumentGenerator _documents;

    public InvoiceDocumentFactory(DocumentDataBuilder dataBuilder, IDocumentGenerator documents)
    {
        _dataBuilder = dataBuilder;
        _documents = documents;
    }

    public List<Document> Build(Invoice invoice, DateTime generatedAt)
    {
        var data = _dataBuilder.Build(invoice);
        return new List<Document>
        {
            new()
            {
                Kind = DocumentKind.Normal,
                FileName = DocumentNaming.NormalFileName(invoice.InvoiceNumber),
                Content = _documents.Generate(data, isCopy: false),
                GeneratedAt = generatedAt,
            },
            new()
            {
                Kind = DocumentKind.Copy,
                FileName = DocumentNaming.CopyFileName(invoice.InvoiceNumber),
                Content = _documents.Generate(data, isCopy: true),
                GeneratedAt = generatedAt,
            },
        };
    }
}
```

In `InvoiceService.cs`: delete the private `BuildDocuments` method, inject
`InvoiceDocumentFactory _documentFactory` in place of `DocumentDataBuilder
_dataBuilder` and `IDocumentGenerator _documents` (update the constructor and its
fields), and replace both `BuildDocuments(invoice, now)` /
`BuildDocuments(revision, now)` calls with `_documentFactory.Build(..., now)`.

In `GraveInvoiceGenerator.cs`: do the same — inject `InvoiceDocumentFactory`
instead of `DocumentDataBuilder` and `IDocumentGenerator`, and replace the inline
`invoice.Documents = new List<Document> { ... }` block with:

```csharp
            invoice.Documents = _documentFactory.Build(invoice, now);
```

- [ ] **Step 4: Write the revision service**

Create `backend/src/Invoicing.Api/Services/GraveInvoiceRevisionService.cs`:

```csharp
using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Domain;
using Burkard.Invoicing.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Burkard.Invoicing.Api.Services;

/// <summary>Revises a grave's invoices for one half-year. A grave invoice is one
/// recipient's share of one grave bill, so editing it edits the bill: every
/// recipient of that grave and half-year gets a new revision, split the same way
/// generation splits it.</summary>
public sealed class GraveInvoiceRevisionService
{
    private readonly AppDbContext _db;
    private readonly InvoiceDocumentFactory _documentFactory;

    public GraveInvoiceRevisionService(AppDbContext db, InvoiceDocumentFactory documentFactory)
    {
        _db = db;
        _documentFactory = documentFactory;
    }

    public async Task<List<Invoice>> CreateAsync(long invoiceId,
        IReadOnlyList<InvoiceLineInput> lineInputs, CancellationToken ct)
    {
        var edited = await _db.Invoices.AsNoTracking().FirstOrDefaultAsync(i => i.Id == invoiceId, ct)
            ?? throw new ValidationException("InvoiceNotFound", "The invoice does not exist.");

        if (edited.GraveId is null || edited.PeriodYear is null || edited.PeriodHalf is null)
        {
            throw new ValidationException("NotAGraveInvoice", "The invoice does not belong to a grave.");
        }

        var graveLines = await BuildGraveLinesAsync(lineInputs, ct);

        var siblings = await _db.Invoices
            .Where(i => i.GraveId == edited.GraveId
                        && i.PeriodYear == edited.PeriodYear
                        && i.PeriodHalf == edited.PeriodHalf
                        && i.IsLatestRevision)
            .OrderBy(i => i.RecipientName)
            .ThenBy(i => i.Id)
            .ToListAsync(ct);
        if (siblings.Count == 0)
        {
            throw new ValidationException("InvoiceNotFound", "The invoice does not exist.");
        }

        // A grave's recipients cannot change, so the group is always the grave's
        // full set and the split matches the one generation used.
        var perRecipient = GraveInvoiceSplitter.Split(graveLines, siblings.Count);
        var settings = await _db.GlobalSettings.FirstAsync(ct);
        var sharePercent = 100m / siblings.Count;
        var now = DateTime.UtcNow;

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        var created = new List<Invoice>(siblings.Count);
        for (var r = 0; r < siblings.Count; r++)
        {
            var sibling = siblings[r];
            var recipient = await _db.Recipients.FindAsync(new object[] { sibling.RecipientId }, ct)
                ?? throw new ValidationException("RecipientNotFound", "The recipient does not exist.");
            if (string.IsNullOrWhiteSpace(recipient.Address))
            {
                throw new ValidationException("RecipientAddressRequired", "The recipient address is required.");
            }

            // the number continues the whole chain, not just the row that was edited
            var lastRevision = await _db.Invoices
                .Where(i => i.RootId == sibling.RootId)
                .MaxAsync(i => (int?)i.RevisionNumber, ct) ?? sibling.RevisionNumber;
            var revisionNumber = lastRevision + 1;
            var baseNumber = InvoiceNumberFormatter.StripRevision(sibling.InvoiceNumber);

            var revision = new Invoice
            {
                RootId = sibling.RootId,
                RevisionNumber = revisionNumber,
                Year = sibling.Year,
                Sequence = sibling.Sequence,
                InvoiceNumber = InvoiceNumberFormatter.ForRevision(baseNumber, revisionNumber),
                GraveId = sibling.GraveId,
                PeriodYear = sibling.PeriodYear,
                PeriodHalf = sibling.PeriodHalf,
                SharePercent = sharePercent,
                RecipientId = recipient.Id,
                // a revision is newly generated, so it uses the recipient's
                // current name and address (FR-017)
                RecipientName = recipient.Name,
                RecipientAddress = recipient.Address,
                EndingText = settings.EndingText,
                StampWord = settings.StampWord,
                Language = settings.Language,
                GeneratedAt = now,
                Lines = perRecipient[r].ToList(),
            };

            sibling.IsLatestRevision = false;
            _db.Invoices.Add(revision);
            await _db.SaveChangesAsync(ct);

            revision.Documents = _documentFactory.Build(revision, now);
            await _db.SaveChangesAsync(ct);
            created.Add(revision);
        }

        await tx.CommitAsync(ct);
        return created;
    }

    /// <summary>The grave's lines from what the dialog sent: the full count at the
    /// full price, before any split.</summary>
    private async Task<List<GraveLine>> BuildGraveLinesAsync(
        IReadOnlyList<InvoiceLineInput> inputs, CancellationToken ct)
    {
        if (inputs.Count == 0)
        {
            throw new ValidationException("NoLines", "At least one line is required.");
        }

        var itemIds = inputs.Select(l => l.ItemId).ToArray();
        var items = await _db.InvoiceItems
            .Where(i => itemIds.Contains(i.Id))
            .ToDictionaryAsync(i => i.Id, ct);

        var lines = new List<GraveLine>(inputs.Count);
        foreach (var input in inputs)
        {
            if (!items.TryGetValue(input.ItemId, out var item))
            {
                throw new ValidationException($"ItemNotFound:{input.ItemId}", $"The item {input.ItemId} does not exist.");
            }

            if (input.Count <= 0)
            {
                throw new ValidationException("InvalidCount", "The count must be positive.");
            }

            var unitPrice = input.UnitPrice ?? item.DefaultPrice
                ?? throw new ValidationException("PriceRequired", $"A price is required for item '{item.Number}'.");

            lines.Add(new GraveLine(item.Id, item.Number, item.Description, input.Count, unitPrice, item.VatRate));
        }

        return lines;
    }
}
```

- [ ] **Step 5: Expose the action**

In `backend/src/Invoicing.Api/OData/EdmModelBuilder.cs`, after the
`CreateRevision` line:

```csharp
        b.Action("CreateGraveInvoiceRevision").ReturnsCollectionFromEntitySet<Invoice>("Invoices");
```

In `backend/src/Invoicing.Api/Program.cs`, next to the other scoped services:

```csharp
builder.Services.AddScoped<InvoiceDocumentFactory>();
builder.Services.AddScoped<GraveInvoiceRevisionService>();
```

In `backend/src/Invoicing.Api/OData/Controllers/InvoiceActionsController.cs`,
add `GraveInvoiceRevisionService _graveRevisions` to the constructor and fields,
then add the action after `CreateRevision`:

```csharp
    [HttpPost("CreateGraveInvoiceRevision")]
    public async Task<IActionResult> CreateGraveInvoiceRevision([FromBody] CreateGraveInvoiceRevisionRequest? request)
    {
        if (request is null)
        {
            return BadRequest(new
            {
                error = new { code = "InvalidRequest", message = "The request body is required." }
            });
        }

        if (request.Lines is null || request.Lines.Count == 0)
        {
            return BadRequest(new
            {
                error = new { code = "NoLines", message = "At least one line is required." }
            });
        }

        try
        {
            var revisions = await _graveRevisions.CreateAsync(
                request.InvoiceId, request.Lines, HttpContext.RequestAborted);
            return Ok(revisions);
        }
        catch (ValidationException ex)
        {
            return BadRequest(new { error = new { code = ex.Code, message = ex.Message } });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CreateGraveInvoiceRevision failed");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { error = new { code = "InternalError", message = "An internal error occurred." } });
        }
    }
```

and the request type next to the others:

```csharp
    public sealed class CreateGraveInvoiceRevisionRequest
    {
        public long InvoiceId { get; set; }
        /// <summary>Grave-level lines: the grave's count at the grave's price,
        /// before the even split across its recipients.</summary>
        public List<InvoiceLineInput> Lines { get; set; } = new();
    }
```

- [ ] **Step 6: Run the tests to verify they pass**

Run the backend tests.
Expected: PASS — all five `GraveInvoiceRevisionTests`, plus the whole suite
(the document-factory extraction must not change any generated document).

- [ ] **Step 7: Commit**

```bash
git add backend/src/Invoicing.Api/Services/InvoiceDocumentFactory.cs \
        backend/src/Invoicing.Api/Services/GraveInvoiceRevisionService.cs \
        backend/src/Invoicing.Api/Services/InvoiceService.cs \
        backend/src/Invoicing.Api/Services/GraveInvoiceGenerator.cs \
        backend/src/Invoicing.Api/OData/Controllers/InvoiceActionsController.cs \
        backend/src/Invoicing.Api/OData/EdmModelBuilder.cs \
        backend/src/Invoicing.Api/Program.cs \
        backend/tests/Invoicing.IntegrationTests/GraveInvoiceRevisionTests.cs
git commit -m "feat: editing a grave invoice revises every recipient

A grave invoice is one recipient's share of one bill, so an edit is an
edit to the bill. All of it happens in one transaction: a grave and
half-year is never left with some recipients revised and others not."
```

---

### Task 6: A grave's recipients cannot change

**Files:**

- Modify: `backend/src/Invoicing.Api/Services/GraveService.cs`
- Modify: `backend/src/Invoicing.Api/OData/Controllers/GraveRecipientsController.cs`
- Test: `backend/tests/Invoicing.IntegrationTests/GraveRecipientImmutabilityTests.cs`

**Interfaces:**

- Produces: error code `GraveRecipientsImmutable`, mapped to German text by
  Task 7's `CODE_TO_I18N`.

Two enforcement points, deliberately: `SaveGrave` is the rule as the user stated
it, and the raw entity set is the case that would actually corrupt a split. The
raw set stays writable for graves that were never invoiced — that is how
`layout.spec.ts` and `graves.spec.ts` seed their links.

- [ ] **Step 1: Write the failing tests**

Create `backend/tests/Invoicing.IntegrationTests/GraveRecipientImmutabilityTests.cs`:

```csharp
using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Burkard.Invoicing.IntegrationTests;

public class GraveRecipientImmutabilityTests
{
    private static async Task<(long GraveId, long ItemId, long AnnaId, long BerndId)> SeedAsync(ApiTestHost host)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.VatRates.Add(new VatRate { Rate = 7m });
        var item = new InvoiceItem { Number = "1001", Description = "Grabpflege", VatRate = 7m, DefaultPrice = 100m };
        var anna = new Recipient { Number = "E-0001", Name = "Anna Beispiel", Address = "A 1" };
        var bernd = new Recipient { Number = "E-0002", Name = "Bernd Beispiel", Address = "B 2" };
        db.InvoiceItems.Add(item);
        db.Recipients.AddRange(anna, bernd);
        var grave = new Grave
        {
            GraveNumber = "G-400",
            StartDate = new DateOnly(2020, 1, 1),
            EndDate = new DateOnly(2040, 12, 31),
            CreatedAt = DateOnly.FromDateTime(DateTime.UtcNow),
        };
        db.Graves.Add(grave);
        await db.SaveChangesAsync();
        db.GraveItems.Add(new GraveItem { GraveId = grave.Id, ItemId = item.Id, Count = 1 });
        db.GraveRecipients.Add(new GraveRecipient { GraveId = grave.Id, RecipientId = anna.Id });
        await db.SaveChangesAsync();
        return (grave.Id, item.Id, anna.Id, bernd.Id);
    }

    [Fact]
    public async Task Saving_a_grave_with_a_different_recipient_set_is_rejected()
    {
        using var host = new ApiTestHost();
        var (graveId, itemId, annaId, berndId) = await SeedAsync(host);

        using var client = host.CreateClient();
        var response = await client.PostAsync("/odata/SaveGrave", ODataJson.Body(new
        {
            graveId,
            graveNumber = "G-400",
            startDate = "2020-01-01",
            endDate = "2040-12-31",
            items = new[] { new { itemId, count = 1, unitPrice = (decimal?)null } },
            recipientIds = new[] { annaId, berndId },
        }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        Assert.Equal("GraveRecipientsImmutable",
            json.RootElement.GetProperty("error").GetProperty("code").GetString());

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.GraveRecipients.CountAsync(r => r.GraveId == graveId));
    }

    [Fact]
    public async Task Saving_a_grave_with_the_same_recipients_still_works()
    {
        using var host = new ApiTestHost();
        var (graveId, itemId, annaId, _) = await SeedAsync(host);

        using var client = host.CreateClient();
        var response = await client.PostAsync("/odata/SaveGrave", ODataJson.Body(new
        {
            graveId,
            graveNumber = "G-400-neu",
            startDate = "2020-01-01",
            endDate = "2041-12-31",
            items = new[] { new { itemId, count = 5, unitPrice = (decimal?)null } },
            recipientIds = new[] { annaId },
        }));

        response.EnsureSuccessStatusCode();

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var grave = await db.Graves.SingleAsync(g => g.Id == graveId);
        Assert.Equal("G-400-neu", grave.GraveNumber);
        Assert.Equal(5, (await db.GraveItems.SingleAsync(i => i.GraveId == graveId)).Count);
    }

    [Fact]
    public async Task Creating_a_grave_still_takes_its_recipients()
    {
        using var host = new ApiTestHost();
        var (_, itemId, annaId, berndId) = await SeedAsync(host);

        using var client = host.CreateClient();
        var response = await client.PostAsync("/odata/SaveGrave", ODataJson.Body(new
        {
            graveId = (long?)null,
            graveNumber = "G-401",
            startDate = "2020-01-01",
            endDate = "2040-12-31",
            items = new[] { new { itemId, count = 1, unitPrice = (decimal?)null } },
            recipientIds = new[] { annaId, berndId },
        }));

        response.EnsureSuccessStatusCode();
        using var json = await ODataJson.ReadAsync(response);
        var newGraveId = json.RootElement.GetProperty("Id").GetInt64();

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(2, await db.GraveRecipients.CountAsync(r => r.GraveId == newGraveId));
    }

    [Fact]
    public async Task Linking_a_recipient_to_a_never_invoiced_grave_still_works()
    {
        using var host = new ApiTestHost();
        var (graveId, _, _, berndId) = await SeedAsync(host);

        using var client = host.CreateClient();
        var response = await client.PostAsync("/odata/GraveRecipients",
            ODataJson.Body(new { GraveId = graveId, RecipientId = berndId }));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Linking_or_unlinking_after_the_grave_was_invoiced_is_rejected()
    {
        using var host = new ApiTestHost();
        var (graveId, _, _, berndId) = await SeedAsync(host);

        using var client = host.CreateClient();
        await GraveGenerationHelper.RunAsync(client, 2033, 1);

        var added = await client.PostAsync("/odata/GraveRecipients",
            ODataJson.Body(new { GraveId = graveId, RecipientId = berndId }));
        Assert.Equal(HttpStatusCode.BadRequest, added.StatusCode);
        using var addedJson = await ODataJson.ReadAsync(added);
        Assert.Equal("GraveRecipientsImmutable",
            addedJson.RootElement.GetProperty("error").GetProperty("code").GetString());

        long linkId;
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            linkId = (await db.GraveRecipients.SingleAsync(r => r.GraveId == graveId)).Id;
        }

        var removed = await client.DeleteAsync($"/odata/GraveRecipients({linkId})");
        Assert.Equal(HttpStatusCode.BadRequest, removed.StatusCode);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run the backend tests.
Expected: FAIL — the changed recipient set is accepted and the links are replaced.

- [ ] **Step 3: Enforce it in `GraveService`**

In `backend/src/Invoicing.Api/Services/GraveService.cs`, in `SaveAsync`, after
`await ValidateAsync(...)` and before `await using var tx = ...`:

```csharp
        // A grave's recipients are fixed at creation: the even split across them is
        // baked into every invoice already generated, and changing the set would
        // make a later revision disagree with what was sent out.
        if (graveId is { } existingId)
        {
            var stored = await _db.GraveRecipients
                .Where(r => r.GraveId == existingId)
                .Select(r => r.RecipientId)
                .ToListAsync(ct);
            if (!stored.OrderBy(id => id).SequenceEqual(recipientIds.OrderBy(id => id)))
            {
                throw new ValidationException("GraveRecipientsImmutable",
                    "A grave's recipients cannot be changed.");
            }
        }
```

- [ ] **Step 4: Enforce it on the raw entity set**

In `backend/src/Invoicing.Api/OData/Controllers/GraveRecipientsController.cs`, add
a guard used by both writes:

```csharp
    /// <summary>Once a grave has been invoiced, its recipient set is frozen: the
    /// even split across them is baked into the invoices already generated. Links
    /// on a grave that was never invoiced stay writable.</summary>
    private bool WasInvoiced(long graveId) => _db.GraveInvoiceRuns.Any(r => r.GraveId == graveId);

    private static ObjectResult Immutable() => new(new
    {
        error = new { code = "GraveRecipientsImmutable", message = "A grave's recipients cannot be changed." }
    })
    { StatusCode = StatusCodes.Status400BadRequest };
```

In `Post`, after the existing-link check and before `_db.GraveRecipients.Add(link);`:

```csharp
        if (WasInvoiced(link.GraveId))
        {
            return Immutable();
        }
```

In `Delete`, after `if (link is null) return NotFound();`:

```csharp
        if (WasInvoiced(link.GraveId))
        {
            return Immutable();
        }
```

- [ ] **Step 5: Run the tests to verify they pass**

Run the backend tests.
Expected: PASS — the five new tests plus `SaveGraveTests` and `GraveTests`
unchanged. If `SaveGraveTests` now fails, it is editing a grave's recipients;
split that case into a create instead of weakening the rule.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Invoicing.Api/Services/GraveService.cs \
        backend/src/Invoicing.Api/OData/Controllers/GraveRecipientsController.cs \
        backend/tests/Invoicing.IntegrationTests/GraveRecipientImmutabilityTests.cs
git commit -m "feat: freeze a grave's recipients after creation

The even split across a grave's recipients is baked into every invoice it
has generated, so changing the set would make a later revision disagree
with what was sent out."
```

---

### Task 7: Frontend groundwork — shared controllers, API, texts

**Files:**

- Modify: `frontend/src/controller/Base.controller.ts`
- Create: `frontend/src/controller/InvoiceRevisions.controller.ts`
- Modify: `frontend/src/model/api.ts`
- Modify: `frontend/src/i18n/i18n.properties`, `frontend/src/i18n/i18n_en.properties`
- Modify: `frontend/src/controller/Invoices.controller.ts`, `frontend/src/controller/Graves.controller.ts`
  (drop what moved to `Base`)

**Interfaces:**

- Consumes: Task 3–5's endpoints.
- Produces, on `BaseController`: `router()`, `odata()`, `bindODataRoot()`,
  `readList(path, parameters) -> Promise<any[]>`, `formatDate`, `formatAmount`,
  `formatInvoiceNumber`, `formatRevisionBadge`, `openDocument(invoiceId, isCopy)`.
- Produces, on `InvoiceRevisions`: `loadChain(rootId) -> Promise<any[]>`,
  `buildRevisionModel(chain) -> JSONModel`.
- Produces, in `api`: `lineTotal`, `invoiceTotals`, `halfYearText`,
  `fetchGraveInvoiceGroup`, `createGraveInvoiceRevision`, and the two new error codes.

No user-visible change — this task is verified by the type check and by the
existing E2E suite still passing.

- [ ] **Step 1: Extend `Base.controller.ts`**

Replace `frontend/src/controller/Base.controller.ts` entirely:

```ts
import Router from "sap/m/routing/Router";
import Controller from "sap/ui/core/mvc/Controller";
import UIComponent from "sap/ui/core/UIComponent";
import Context from "sap/ui/model/odata/v4/Context";
import ODataModel from "sap/ui/model/odata/v4/ODataModel";
import * as api from "../model/api";

export default abstract class BaseController extends Controller {
  public onNavBack(): void {
    this.router().navTo("home");
  }

  protected router(): Router {
    return UIComponent.getRouterFor(this) as Router;
  }

  protected odata(): ODataModel {
    return this.getOwnerComponent()!.getModel("odata") as ODataModel;
  }

  /**
   * Binds the view to the OData root context. Routed views run onInit before
   * they are added to the App, so view.getModel("odata") is not visible yet.
   */
  protected bindODataRoot(): void {
    this.getView()!.setBindingContext(this.odata().createBindingContext("/") as Context, "odata");
  }

  /**
   * Reads a list through the OData model — every read stays on the model
   * (D-002) — and hands back plain objects for a JSONModel. The detail pages
   * compute totals per revision, which a list binding cannot express.
   */
  protected async readList(path: string, parameters: Record<string, unknown>): Promise<any[]> {
    const binding = this.odata().bindList(path, undefined, undefined, undefined, parameters as any);
    const contexts = await binding.requestContexts(0, 500);
    const rows = contexts.map((context) => context.getObject());
    binding.destroy();
    return rows;
  }

  protected i18nText(key: string): string {
    return (this.getView()!.getModel("i18n") as any).getResourceBundle().getText(key);
  }

  public formatDate(value: Date | string | null | undefined): string {
    if (value == null) {
      return "";
    }
    const date = value instanceof Date ? value : new Date(value);
    return date.toLocaleDateString("de-DE");
  }

  /** The same locale formatDate uses, so amounts and dates read as one page. */
  public formatAmount(value: number | string | null | undefined): string {
    if (value == null || value === "") {
      return "";
    }
    return `${Number(value).toLocaleString("de-DE", {
      minimumFractionDigits: 2,
      maximumFractionDigits: 2,
    })} €`;
  }

  /** "2026-0042-R1" -> "2026-0042": the chain, without the revision marker. */
  public formatInvoiceNumber(value: string | null | undefined): string {
    if (value == null) {
      return "";
    }
    const index = value.lastIndexOf("-R");
    return index >= 0 ? value.slice(0, index) : value;
  }

  public formatRevisionBadge(value: number | null | undefined): string {
    return value != null && value > 0 ? `R${value}` : "";
  }

  /** Opens an invoice's PDF in a new tab, where the browser displays it. */
  protected async openDocument(invoiceId: number, isCopy: boolean): Promise<void> {
    // The tab is opened before the documents are fetched: one opened after the
    // await no longer belongs to the click, and browsers block it as a popup.
    const tab = window.open("", "_blank");
    try {
      const documents = await api.fetchDocuments(invoiceId);
      const wanted = isCopy ? "Copy" : "Normal";
      const match = documents.find((d) => d.Kind === wanted);
      if (!match) {
        tab?.close();
        return;
      }

      if (tab) {
        tab.location.href = api.documentUrl(match.Id);
      } else {
        // a browser that refused the empty tab may still allow a real URL
        window.open(api.documentUrl(match.Id), "_blank");
      }
    } catch (error) {
      tab?.close();
      await api.showError(this, error);
    }
  }
}
```

- [ ] **Step 2: Add the API helpers**

In `frontend/src/model/api.ts`, add to `CODE_TO_I18N`:

```ts
  GraveRecipientsImmutable: "errorGraveRecipientsImmutable",
  NotAGraveInvoice: "errorNotAGraveInvoice",
```

and append at the end of the file:

```ts
export interface InvoiceLineAmounts {
  Count: number;
  UnitPrice: number;
  VatRate: number;
}

export interface InvoiceTotals {
  net: number;
  vat: number;
  gross: number;
}

/** A line's net total, rounded to the cent the way the document rounds it. */
export function lineTotal(line: InvoiceLineAmounts): number {
  return Math.round(line.Count * line.UnitPrice * 100) / 100;
}

/** Net, VAT and gross of an invoice. Each line is rounded on its own, exactly
 *  as DocumentDataBuilder does it, so a detail page never disagrees with the
 *  PDF it links to. */
export function invoiceTotals(lines: InvoiceLineAmounts[]): InvoiceTotals {
  let netCents = 0;
  let vatCents = 0;
  for (const line of lines) {
    const lineCents = Math.round(line.Count * line.UnitPrice * 100);
    netCents += lineCents;
    vatCents += Math.round((lineCents * line.VatRate) / 100);
  }
  return {
    net: netCents / 100,
    vat: vatCents / 100,
    gross: (netCents + vatCents) / 100,
  };
}

/** "H1 2026" — the label the half-year selects already use. */
export function halfYearText(year: number, half: number): string {
  return `H${half} ${year}`;
}

export interface GraveInvoiceGroupLine {
  ItemId: number;
  ItemNumber: string;
  Description: string;
  Count: number;
  UnitPrice: number;
  VatRate: number;
}

export interface GraveInvoiceGroup {
  GraveId: number;
  GraveNumber: string;
  PeriodYear: number;
  PeriodHalf: number;
  Recipients: { Id: number; Name: string; InvoiceId: number }[];
  Lines: GraveInvoiceGroupLine[];
}

/** The grave, half-year and recipients a grave invoice belongs to, with the
 *  grave's lines as they were invoiced — not as the grave looks today. */
export async function fetchGraveInvoiceGroup(invoiceId: number): Promise<GraveInvoiceGroup> {
  const response = await fetch(`/odata/GraveInvoiceGroup?invoiceId=${invoiceId}`);
  await ensureOk(response);
  return (await response.json()) as GraveInvoiceGroup;
}

/** Revises every recipient invoice of the grave and half-year the given invoice
 *  belongs to. `lines` are grave-level: the full count at the full price. */
export async function createGraveInvoiceRevision(
  invoiceId: number,
  lines: InvoiceLineDto[],
): Promise<void> {
  await ensureOk(await post("/odata/CreateGraveInvoiceRevision", { invoiceId, lines }));
}
```

- [ ] **Step 3: Add the revision loader**

Create `frontend/src/controller/InvoiceRevisions.controller.ts`:

```ts
import JSONModel from "sap/ui/model/json/JSONModel";
import * as api from "../model/api";
import BaseController from "./Base.controller";

/**
 * Loads an invoice chain — every revision with its lines, documents and totals —
 * for the two detail pages. They render different markup around it, so only the
 * loading is shared.
 */
export default abstract class InvoiceRevisions extends BaseController {
  /**
   * The whole chain, newest revision first. Takes the chain's root id, which is
   * what every list row carries; a revision's id pasted into the URL resolves
   * through one extra read rather than showing an empty page.
   */
  protected async loadChain(rootId: number): Promise<any[]> {
    const parameters = {
      $orderby: "RevisionNumber desc",
      $expand: "Lines,Documents($select=Id,FileName,Kind)",
    };

    const rows = await this.readList("/Invoices", {
      ...parameters,
      $filter: `RootId eq ${rootId}`,
    });
    if (rows.length) {
      return rows;
    }

    const single = await this.readList("/Invoices", { $filter: `Id eq ${rootId}` });
    if (!single.length) {
      return [];
    }

    return this.readList("/Invoices", {
      ...parameters,
      $filter: `RootId eq ${Number(single[0].RootId)}`,
    });
  }

  /** The model the header block and the revision panels bind to. */
  protected buildRevisionModel(chain: any[]): JSONModel {
    const revisions = chain.map((invoice, index) => {
      const lines = ((invoice.Lines as any[]) ?? [])
        .slice()
        .sort((a, b) => Number(a.LineNumber) - Number(b.LineNumber))
        .map((line) => {
          // the OData V4 model serialises Edm.Decimal as a string
          const amounts = {
            Count: Number(line.Count),
            UnitPrice: Number(line.UnitPrice),
            VatRate: Number(line.VatRate),
          };
          return {
            ...amounts,
            ItemNumber: line.ItemNumber,
            Description: line.Description,
            LineTotal: api.lineTotal(amounts),
          };
        });
      const totals = api.invoiceTotals(lines);
      const revisionNumber = Number(invoice.RevisionNumber);

      return {
        Id: Number(invoice.Id),
        InvoiceNumber: invoice.InvoiceNumber,
        RevisionNumber: revisionNumber,
        GeneratedAt: invoice.GeneratedAt,
        Title:
          revisionNumber === 0
            ? this.i18nText("revisionOriginal")
            : this.i18nText("revisionLabel").replace("{0}", String(revisionNumber)),
        // the current revision opens; the history stays collapsed
        Expanded: index === 0,
        Lines: lines,
        NetTotal: totals.net,
        VatTotal: totals.vat,
        GrossTotal: totals.gross,
      };
    });

    const latest = chain[0];
    return new JSONModel({
      invoiceNumber: this.formatInvoiceNumber(latest.InvoiceNumber),
      recipientName: latest.RecipientName,
      recipientAddress: latest.RecipientAddress,
      // the edit dialog reopens on this recipient
      recipientId: Number(latest.RecipientId),
      latestInvoiceId: Number(latest.Id),
      graveId: latest.GraveId != null ? Number(latest.GraveId) : null,
      periodYear: latest.PeriodYear != null ? Number(latest.PeriodYear) : null,
      periodHalf: latest.PeriodHalf != null ? Number(latest.PeriodHalf) : null,
      halfYear:
        latest.PeriodYear != null && latest.PeriodHalf != null
          ? api.halfYearText(Number(latest.PeriodYear), Number(latest.PeriodHalf))
          : "",
      sharePercent: latest.SharePercent != null ? Number(latest.SharePercent) : null,
      revisions,
    });
  }
}
```

- [ ] **Step 4: Add the texts**

Append to `frontend/src/i18n/i18n.properties`:

```properties
searchInvoice=Rechnung suchen
actionDetails=Details
actionDownloadNormal=Rechnung herunterladen
actionDownloadCopy=Kopie herunterladen
actionEdit=Bearbeiten
bulkGraveInvoices=Rechnungen (ZIP)
invoiceDetailTitle=Rechnung
graveDetailTitle=Grab
graveInvoiceDetailTitle=Grabrechnung
revisionOriginal=Original
revisionLabel=Revision {0}
lineTotal=Zeilensumme
netTotal=Netto
vatTotal=MwSt.
grossTotal=Gesamt
graveCreatedAt=Angelegt am
currentHalfYearStatus=Halbjahr {0}
graveInvoices=Rechnungen
sharePercent=Anteil
otherRecipients=Weitere Empfänger
editGrave=Grab bearbeiten
graveEditWarning=Änderungen wirken sich nur auf künftig erstellte Rechnungen aus. Bereits erstellte Rechnungen bleiben unverändert.
graveRecipientsFixed=Die Empfänger eines Grabs können nach dem Anlegen nicht mehr geändert werden.
graveInvoiceEditTitle=Grabrechnung bearbeiten
graveInvoiceEditWarning=Die Änderung betrifft alle Empfänger dieses Grabs im Halbjahr {0} ({1}). Für jeden wird eine neue Revision erstellt.
noGraveInvoices=Für dieses Grab wurden noch keine Rechnungen erstellt.
errorGraveRecipientsImmutable=Die Empfänger eines Grabs können nicht geändert werden.
errorNotAGraveInvoice=Diese Rechnung gehört zu keinem Grab.
```

Append the English equivalents to `frontend/src/i18n/i18n_en.properties`:

```properties
searchInvoice=Search invoice
actionDetails=Details
actionDownloadNormal=Download invoice
actionDownloadCopy=Download copy
actionEdit=Edit
bulkGraveInvoices=Invoices (ZIP)
invoiceDetailTitle=Invoice
graveDetailTitle=Grave
graveInvoiceDetailTitle=Grave invoice
revisionOriginal=Original
revisionLabel=Revision {0}
lineTotal=Line total
netTotal=Net
vatTotal=VAT
grossTotal=Total
graveCreatedAt=Created on
currentHalfYearStatus=Half-year {0}
graveInvoices=Invoices
sharePercent=Share
otherRecipients=Other recipients
editGrave=Edit grave
graveEditWarning=Changes apply only to invoices generated from now on. Invoices already generated stay unchanged.
graveRecipientsFixed=A grave's recipients cannot be changed after it has been created.
graveInvoiceEditTitle=Edit grave invoice
graveInvoiceEditWarning=This change affects every recipient of this grave in half-year {0} ({1}). Each of them gets a new revision.
noGraveInvoices=No invoices have been generated for this grave yet.
errorGraveRecipientsImmutable=A grave's recipients cannot be changed.
errorNotAGraveInvoice=This invoice does not belong to a grave.
```

- [ ] **Step 5: Drop what moved out of the two list controllers**

In `frontend/src/controller/Invoices.controller.ts`: delete `formatDate`,
`formatInvoiceNumber`, `formatRevisionBadge` and the private `openDocument`;
change `onDownloadNormal` / `onDownloadCopy` to

```ts
  public async onDownloadNormal(event: any): Promise<void> {
    const context = event.getSource().getBindingContext("odata") as Context;
    await this.openDocument(context.getProperty("Id") as number, false);
  }

  public async onDownloadCopy(event: any): Promise<void> {
    const context = event.getSource().getBindingContext("odata") as Context;
    await this.openDocument(context.getProperty("Id") as number, true);
  }
```

and replace the two lines of `onInit` that bind the root context with
`this.bindODataRoot();`.

In `frontend/src/controller/Graves.controller.ts`: delete `formatDate` and
replace the root-context lines of `onInit` with `this.bindODataRoot();`.

- [ ] **Step 6: Type-check**

Run: `cd frontend && npm run typecheck`
Expected: no errors. Unused imports (`Context`, `ODataModel`) left behind in the
two controllers are errors here — remove them.

- [ ] **Step 7: Prove nothing regressed**

Run: `cd e2e && npm test`
Expected: PASS — the whole suite, unchanged behaviour.

- [ ] **Step 8: Commit**

```bash
git add frontend/src/controller/Base.controller.ts \
        frontend/src/controller/InvoiceRevisions.controller.ts \
        frontend/src/controller/Invoices.controller.ts \
        frontend/src/controller/Graves.controller.ts \
        frontend/src/model/api.ts \
        frontend/src/i18n/
git commit -m "refactor: hoist the shared view helpers into the base controller

Five screens are about to format the same dates and amounts and open the
same documents. Adds the revision loader the two detail pages share, and
the texts they need."
```

---

### Task 8: The invoice detail page

**Files:**

- Create: `frontend/src/controller/InvoiceEdit.controller.ts`
- Create: `frontend/src/view/InvoiceDetail.view.xml`
- Create: `frontend/src/controller/InvoiceDetail.controller.ts`
- Modify: `frontend/src/manifest.json` (route + target)
- Modify: `frontend/src/controller/Invoices.controller.ts` (extend `InvoiceEdit`)
- Test: `e2e/tests/invoice-detail.spec.ts`

**Interfaces:**

- Consumes: `InvoiceRevisions.loadChain` / `buildRevisionModel` (Task 7).
- Produces:
  - Route `invoiceDetail`, pattern `invoices/{rootId}` — Task 9's details button
    navigates to it.
  - `InvoiceEdit.openInvoiceDialog(invoiceId: number | null, recipientId: number | null)`
    and the abstract `afterInvoiceSaved(): void | Promise<void>` its subclasses implement.

The page is reachable by URL in this task; the list wires a button to it in Task 9.

- [ ] **Step 1: Write the failing E2E test**

Create `e2e/tests/invoice-detail.spec.ts`:

```ts
import { expect, test } from "@playwright/test";

// Unique per run: other specs write invoices into the same stack in parallel.
const RUN = Date.now();
const RECIPIENT_NAME = `Detail ${RUN}`;

test("the detail page shows every revision of an invoice", async ({ page }) => {
  await page.request.post("/odata/VatRates", { data: { Rate: 7 } });
  const item = await (
    await page.request.post("/odata/InvoiceItems", {
      data: { Number: `D${RUN}`, Description: "Grabpflege", VatRate: 7, DefaultPrice: 100 },
    })
  ).json();
  const recipient = await (
    await page.request.post("/odata/Recipients", {
      data: { Name: RECIPIENT_NAME, Address: "Musterstraße 1\n12345 Musterstadt" },
    })
  ).json();

  const created = await (
    await page.request.post("/odata/CreateInvoice", {
      data: { recipientId: recipient.Id, lines: [{ itemId: item.Id, count: 2, unitPrice: 100 }] },
    })
  ).json();
  await page.request.post("/odata/CreateRevision", {
    data: {
      invoiceId: created.Id,
      recipientId: recipient.Id,
      lines: [{ itemId: item.Id, count: 3, unitPrice: 100 }],
    },
  });

  await page.goto(`/index.html#/invoices/${created.RootId ?? created.Id}`);

  // the original and its revision are both on the page, newest first
  await expect(page.getByText("Revision 1", { exact: true })).toBeVisible();
  await expect(page.getByText("Original", { exact: true })).toBeVisible();
  await expect(page.getByText(RECIPIENT_NAME)).toBeVisible();
  // 3 × 100.00 net + 7% = 321,00
  await expect(page.getByText("321,00 €").first()).toBeVisible();

  // both documents of a revision are reachable from its panel
  await expect(page.getByRole("button", { name: "Rechnung herunterladen" }).first()).toBeVisible();
  await expect(page.getByRole("button", { name: "Kopie herunterladen" }).first()).toBeVisible();
});
```

- [ ] **Step 2: Run it to verify it fails**

Run: `cd e2e && npm test -- --project=desktop invoice-detail.spec.ts`
Expected: FAIL — the route does not exist, so the app stays on the home page.

- [ ] **Step 3: Move the invoice dialog into its own controller**

Create `frontend/src/controller/InvoiceEdit.controller.ts` and move the dialog
code out of `Invoices.controller.ts` unchanged apart from the entry point, which
now takes plain ids so a detail page can open it too:

```ts
import { ComboBox$SelectionChangeEvent } from "sap/m/ComboBox";
import BusyDialog from "sap/m/BusyDialog";
import Input from "sap/m/Input";
import Select from "sap/m/Select";
import TextArea from "sap/m/TextArea";
import JSONModel from "sap/ui/model/json/JSONModel";
import ODataListBinding from "sap/ui/model/odata/v4/ODataListBinding";
import * as api from "../model/api";
import InvoiceRevisions from "./InvoiceRevisions.controller";

/**
 * Owns the invoice dialog. Both the invoice list and the invoice detail page
 * open it, so it lives here rather than being copied into each of them.
 */
export default abstract class InvoiceEdit extends InvoiceRevisions {
  /** Runs after a save so the screen can refresh whatever it is showing. */
  protected abstract afterInvoiceSaved(): void | Promise<void>;

  protected async openInvoiceDialog(
    invoiceId: number | null,
    recipientId: number | null,
  ): Promise<void> {
    if (!this._invoiceDialog) {
      // loadFragment passes this controller and the view ID prefix so that
      // this.byId(...) finds the dialog controls and IDs stay unique
      this._invoiceDialog = (await this.loadFragment({
        name: "burkard.invoicing.view.fragments.InvoiceDialog",
      })) as any;
    }

    const lines = invoiceId != null ? await this.loadLines(invoiceId) : [InvoiceEdit.emptyLine()];
    this._invoiceDialog.setModel(new JSONModel({ recipientId, invoiceId, lines }), "invoiceDraft");
    this._invoiceDialog.open();
  }

  private async loadLines(invoiceId: number): Promise<any[]> {
    try {
      const response = await fetch(`/odata/Invoices(${invoiceId})/Lines`);
      if (!response.ok) {
        throw new Error(`HTTP ${response.status}`);
      }
      const body = await response.json();
      return (body.value as any[]).map((line) => ({
        // the ComboBox matches its keys as strings
        itemId: line.ItemId != null ? String(line.ItemId) : "",
        itemNumber: line.ItemNumber,
        count: line.Count,
        unitPrice: line.UnitPrice,
        defaultPrice: null,
        description: line.Description,
      }));
    } catch (error) {
      await api.showError(this, error);
      return [InvoiceEdit.emptyLine()];
    }
  }

  public onLineItemSelected(event: ComboBox$SelectionChangeEvent): void {
    // the ComboBox writes its key into the draft itself; fill in what the row
    // shows alongside it
    const selected = event.getParameter("selectedItem");
    const rowContext = event.getSource().getBindingContext("invoiceDraft");
    if (!selected || !rowContext) {
      return;
    }
    const itemContext = selected.getBindingContext("odata");
    if (!itemContext) {
      return;
    }

    const path = rowContext.getPath();
    const model = rowContext.getModel() as JSONModel;
    model.setProperty(path + "/itemNumber", itemContext.getProperty("Number") as string);
    model.setProperty(path + "/description", itemContext.getProperty("Description") as string);
    // the item's default price is pre-filled and stays overridable; an item
    // without one leaves the field empty so a price has to be typed
    const defaultPrice = itemContext.getProperty("DefaultPrice");
    model.setProperty(path + "/defaultPrice", defaultPrice);
    model.setProperty(path + "/unitPrice", defaultPrice != null ? Number(defaultPrice) : null);
  }

  public onAddLine(): void {
    const model = this._invoiceDialog.getModel("invoiceDraft") as JSONModel;
    const lines = (model.getProperty("/lines") as any[]) ?? [];
    lines.push(InvoiceEdit.emptyLine());
    model.setProperty("/lines", lines);
  }

  private static emptyLine(): Record<string, unknown> {
    return {
      itemId: "",
      itemNumber: "",
      count: 1,
      unitPrice: null,
      defaultPrice: null,
      description: "",
    };
  }

  public async onInvoiceDialogSave(): Promise<void> {
    // a second press while the first invoice is still being created would
    // create a second one; the busy dialog below blocks the interface, this
    // guard covers the moment before it is on screen
    if (this._savingInvoice) {
      return;
    }

    const model = this._invoiceDialog.getModel("invoiceDraft") as JSONModel;
    const draft = model.getData() as any;
    const rows = draft.lines as any[];
    if (rows.some((row) => !row.itemId)) {
      await api.showError(this, new Error("ItemRequired"));
      return;
    }

    const lines = rows.map((line) => ({
      itemId: Number(line.itemId),
      count: Number(line.count),
      // a price of 0 is a real value, not a missing one
      unitPrice: line.unitPrice == null || line.unitPrice === "" ? null : Number(line.unitPrice),
    }));

    // The invoice goes to the recipient the dialog shows. The dialog is built
    // once and reused, so its select keeps the selection of the previous open
    // while the draft of this open starts out empty — reading the draft would
    // send recipient 0 and fail with RecipientNotFound.
    const recipientId = Number((this.byId("recipient") as Select).getSelectedKey());

    // long-running task: creating an invoice renders its documents, so the
    // interface is blocked until it finishes
    this._savingInvoice = true;
    const busy = new BusyDialog({ title: this.i18nText("invoiceRunning") });
    busy.open();

    let failure: unknown = null;
    try {
      if (draft.invoiceId) {
        await api.createRevision(Number(draft.invoiceId), recipientId, lines);
      } else {
        await api.createInvoice(recipientId, lines);
      }

      this._invoiceDialog.close();
      await this.afterInvoiceSaved();
    } catch (error) {
      failure = error;
    } finally {
      // close the busy dialog before any error message opens
      busy.close();
      this._savingInvoice = false;
    }

    if (failure) {
      await api.showError(this, failure);
    }
  }

  public onInvoiceDialogCancel(): void {
    this._invoiceDialog.close();
  }

  public async onNewRecipient(): Promise<void> {
    if (!this._quickDialog) {
      this._quickDialog = (await this.loadFragment({
        name: "burkard.invoicing.view.fragments.RecipientQuickCreate",
      })) as any;
    }
    (this.byId("quickName") as Input).setValue("");
    (this.byId("quickAddress") as TextArea).setValue("");
    this._quickDialog.open();
  }

  public async onQuickRecipientSave(event: any): Promise<void> {
    const dialog = event.getSource().getParent() as any;
    try {
      const id = await api.createRecipient({
        Name: (this.byId("quickName") as Input).getValue(),
        Address: (this.byId("quickAddress") as TextArea).getValue(),
      });
      // refresh the recipient select's list so the new row is included,
      // then select it in the parent invoice dialog, which stays open
      const select = this.byId("recipient") as Select;
      await (select.getBinding("items") as ODataListBinding).refresh();
      (this._invoiceDialog.getModel("invoiceDraft") as JSONModel).setProperty("/recipientId", id);
      dialog.close();
    } catch (error) {
      await api.showError(this, error);
    }
  }

  public onQuickRecipientCancel(event: any): void {
    (event.getSource().getParent() as any).close();
  }

  private _savingInvoice = false;
  private _quickDialog: any;
  protected _invoiceDialog: any;
}
```

Then in `Invoices.controller.ts`: extend `InvoiceEdit` instead of
`BaseController`, delete everything that moved (the dialog, the quick-create, the
line helpers, `_saving`, `_dialog`, `_quickDialog`), and keep the entry points:

```ts
  public async onCreateInvoice(): Promise<void> {
    await this.openInvoiceDialog(null, null);
  }

  public async onEditInvoice(event: any): Promise<void> {
    const context = event.getSource().getBindingContext("odata") as Context;
    await this.openInvoiceDialog(
      context.getProperty("Id") as number,
      context.getProperty("RecipientId") as number,
    );
  }

  protected afterInvoiceSaved(): void {
    (
      (this.byId("invoicesTable") as any).getBinding("items") as ODataListBinding
    ).refresh();
  }
```

- [ ] **Step 4: Add the route and target**

In `frontend/src/manifest.json`, add to `routes` after the `invoices` entry:

```json
        {
          "name": "invoiceDetail",
          "pattern": "invoices/{rootId}",
          "target": [
            "invoiceDetail"
          ]
        },
```

and to `targets`:

```json
        "invoiceDetail": {
          "name": "InvoiceDetail",
          "level": 1,
          "type": "View"
        },
```

- [ ] **Step 5: Write the view**

Create `frontend/src/view/InvoiceDetail.view.xml`:

```xml
<mvc:View
  xmlns:mvc="sap.ui.core.mvc"
  xmlns="sap.m"
  controllerName="burkard.invoicing.controller.InvoiceDetail"
  displayBlock="true"
>
    <Page
    id="invoiceDetailPage"
    title="{i18n>invoiceDetailTitle}"
    titleLevel="H1"
    showNavButton="true"
    navButtonPress=".onNavBack"
    class="sapUiResponsiveContentPadding"
  >
        <headerContent>
            <Button icon="sap-icon://edit" tooltip="{i18n>actionEdit}" press=".onEditInvoice" />
        </headerContent>
        <content>
            <VBox class="sapUiSmallMarginTop">
                <Title text="{detail>/invoiceNumber}" level="H2" />
                <Text text="{detail>/recipientName}" class="sapUiTinyMarginTop" />
                <!-- the address is stored with line breaks, as it is printed -->
                <Text text="{detail>/recipientAddress}" renderWhitespace="true" />
            </VBox>
            <!-- one panel per revision, newest first; the history stays collapsed -->
            <VBox items="{detail>/revisions}">
                <Panel
          headerText="{detail>Title}"
          expandable="true"
          expanded="{detail>Expanded}"
          class="sapUiSmallMarginTop"
        >
                    <content>
                        <HBox wrap="Wrap" alignItems="Center" class="sapUiSmallMarginBottom">
                            <Text text="{detail>InvoiceNumber}" class="sapUiSmallMarginEnd" />
                            <Text
                text="{path: 'detail>GeneratedAt', formatter: '.formatDate'}"
                class="sapUiSmallMarginEnd"
              />
                            <Button
                icon="sap-icon://pdf-attachment"
                type="Transparent"
                tooltip="{i18n>actionDownloadNormal}"
                press=".onDownloadRevisionNormal"
              />
                            <Button
                icon="sap-icon://copy"
                type="Transparent"
                tooltip="{i18n>actionDownloadCopy}"
                press=".onDownloadRevisionCopy"
              />
                        </HBox>
                        <Table items="{detail>Lines}" autoPopinMode="true" popinLayout="Block">
                            <columns>
                                <Column importance="High"><Text text="{i18n>itemNumber}" /></Column>
                                <Column importance="Medium" popinDisplay="Inline"><Text
                    text="{i18n>itemDescription}"
                  /></Column>
                                <Column importance="High" popinDisplay="Inline"><Text
                    text="{i18n>itemCount}"
                  /></Column>
                                <Column importance="High" popinDisplay="Inline"><Text
                    text="{i18n>itemPrice}"
                  /></Column>
                                <Column importance="Low" popinDisplay="Inline"><Text
                    text="{i18n>vatRate}"
                  /></Column>
                                <Column importance="Medium" popinDisplay="Inline"><Text
                    text="{i18n>lineTotal}"
                  /></Column>
                            </columns>
                            <items>
                                <ColumnListItem>
                                    <cells>
                                        <Text text="{detail>ItemNumber}" />
                                        <Text text="{detail>Description}" wrapping="true" />
                                        <Text text="{detail>Count}" />
                                        <Text
                      text="{path: 'detail>UnitPrice', formatter: '.formatAmount'}"
                    />
                                        <Text text="{detail>VatRate}" />
                                        <Text
                      text="{path: 'detail>LineTotal', formatter: '.formatAmount'}"
                    />
                                    </cells>
                                </ColumnListItem>
                            </items>
                        </Table>
                        <HBox wrap="Wrap" justifyContent="End" class="sapUiSmallMarginTop">
                            <ObjectStatus
                title="{i18n>netTotal}"
                class="sapUiSmallMarginEnd"
                text="{path: 'detail>NetTotal', formatter: '.formatAmount'}"
              />
                            <ObjectStatus
                title="{i18n>vatTotal}"
                class="sapUiSmallMarginEnd"
                text="{path: 'detail>VatTotal', formatter: '.formatAmount'}"
              />
                            <ObjectStatus
                title="{i18n>grossTotal}"
                state="Information"
                text="{path: 'detail>GrossTotal', formatter: '.formatAmount'}"
              />
                        </HBox>
                    </content>
                </Panel>
            </VBox>
        </content>
    </Page>
</mvc:View>
```

- [ ] **Step 6: Write the controller**

Create `frontend/src/controller/InvoiceDetail.controller.ts`:

```ts
import JSONModel from "sap/ui/model/json/JSONModel";
import * as api from "../model/api";
import InvoiceEdit from "./InvoiceEdit.controller";

/** Every revision of one invoice, newest first. */
export default class InvoiceDetail extends InvoiceEdit {
  public onInit(): void {
    this.bindODataRoot();
    this.router().getRoute("invoiceDetail")!.attachPatternMatched(this.onRouteMatched, this);
  }

  private async onRouteMatched(event: any): Promise<void> {
    this._rootId = Number(event.getParameter("arguments").rootId);
    await this.refresh();
  }

  private async refresh(): Promise<void> {
    try {
      const chain = await this.loadChain(this._rootId);
      this.getView()!.setModel(
        chain.length ? this.buildRevisionModel(chain) : new JSONModel({ revisions: [] }),
        "detail",
      );
    } catch (error) {
      await api.showError(this, error);
    }
  }

  /** Back to the list this page was opened from, not to the home screen. */
  public onNavBack(): void {
    this.router().navTo("invoices");
  }

  public async onEditInvoice(): Promise<void> {
    const model = this.getView()!.getModel("detail") as JSONModel;
    const invoiceId = model.getProperty("/latestInvoiceId") as number | null;
    if (invoiceId == null) {
      return;
    }
    await this.openInvoiceDialog(invoiceId, model.getProperty("/recipientId") as number);
  }

  protected async afterInvoiceSaved(): Promise<void> {
    await this.refresh();
  }

  public async onDownloadRevisionNormal(event: any): Promise<void> {
    await this.downloadRevision(event, false);
  }

  public async onDownloadRevisionCopy(event: any): Promise<void> {
    await this.downloadRevision(event, true);
  }

  private async downloadRevision(event: any, isCopy: boolean): Promise<void> {
    const context = event.getSource().getBindingContext("detail");
    await this.openDocument(Number(context.getProperty("Id")), isCopy);
  }

  private _rootId = 0;
}
```

- [ ] **Step 7: Type-check and run the E2E test**

Run: `cd frontend && npm run typecheck`
Expected: no errors.

Run: `cd e2e && npm test -- --project=desktop invoice-detail.spec.ts`
Expected: PASS.

- [ ] **Step 8: Run the whole suite**

Run: `cd e2e && npm test`
Expected: PASS — `invoices.spec.ts` still passes; the dialog moved but behaves
identically.

- [ ] **Step 9: Commit**

```bash
git add frontend/src/controller/InvoiceEdit.controller.ts \
        frontend/src/controller/InvoiceDetail.controller.ts \
        frontend/src/view/InvoiceDetail.view.xml \
        frontend/src/controller/Invoices.controller.ts \
        frontend/src/manifest.json \
        e2e/tests/invoice-detail.spec.ts
git commit -m "feat: an invoice detail page showing every revision

Each revision gets its own panel with its lines, totals and both
documents. The invoice dialog moves into a shared controller so the
detail page can open the same one the list does."
```

---

### Task 9: The invoice page — no grave invoices, a search, four icon actions

**Files:**

- Modify: `frontend/src/view/Invoices.view.xml`
- Modify: `frontend/src/controller/Invoices.controller.ts`
- Modify: `frontend/src/i18n/i18n.properties`, `frontend/src/i18n/i18n_en.properties`
  (remove three keys)
- Test: `e2e/tests/invoices.spec.ts`

**Interfaces:**

- Consumes: `search` (Task 3), `IsLatestRevision` (Task 2), route `invoiceDetail`
  (Task 8).

The half-year select and both bulk downloads are removed here; the grave mass
download arrives on the grave page in Task 13.

- [ ] **Step 1: Write the failing E2E test**

Append to `e2e/tests/invoices.spec.ts`:

```ts
test("the list shows non-grave invoices only and searches them (Story 9)", async ({ page }) => {
  const marker = `Suche ${Date.now()}`;
  const { itemNumber, recipientName } = await seedOwnMasterData(page, marker);
  const item = await (
    await page.request.get(`/odata/InvoiceItems?$filter=Number eq '${itemNumber}'`)
  ).json();
  const recipients = await (
    await page.request.get(`/odata/Recipients?$filter=Name eq '${recipientName}'`)
  ).json();
  await page.request.post("/odata/CreateInvoice", {
    data: {
      recipientId: recipients.value[0].Id,
      lines: [{ itemId: item.value[0].Id, count: 1, unitPrice: 120 }],
    },
  });

  // The list asks the server for non-grave invoices, latest revision only.
  // Asserting the request rather than counting rows keeps this independent of
  // the graves other specs invoice in parallel.
  const listRequest = page.waitForRequest(
    (request) =>
      request.url().includes("/odata/Invoices") &&
      decodeURIComponent(request.url()).includes("GraveId eq null and IsLatestRevision eq true"),
  );
  await page.goto("/index.html#/invoices");
  await listRequest;

  const invoicesTable = page.locator("[id$='invoicesTable-listUl']");
  await expect(invoicesTable.locator("tbody tr", { hasText: recipientName })).toBeVisible();

  // the search reaches the backend as the custom query option
  const search = page.locator("[id$='invoiceSearch'] input");
  const searchRequest = page.waitForRequest(
    (request) => request.url().includes("/odata/Invoices") && request.url().includes("search="),
  );
  await search.fill(recipientName);
  await search.press("Enter");
  await searchRequest;

  await expect(invoicesTable.locator("tbody tr", { hasText: recipientName })).toBeVisible();
  await expect(invoicesTable.locator("tbody tr")).toHaveCount(1);

  // clearing the field restores the list
  await search.fill("");
  await search.press("Enter");
  await expect(invoicesTable.locator("tbody tr", { hasText: recipientName })).toBeVisible();

  // narrow it again for the row assertions below
  await search.fill(recipientName);
  await search.press("Enter");
  await expect(invoicesTable.locator("tbody tr")).toHaveCount(1);

  // the four row actions are icon-only, named by their tooltips
  const row = invoicesTable.locator("tbody tr", { hasText: recipientName });
  for (const name of ["Details", "Rechnung herunterladen", "Kopie herunterladen", "Bearbeiten"]) {
    await expect(row.getByRole("button", { name, exact: true })).toBeVisible();
  }

  // details opens the revision page for that invoice
  await row.getByRole("button", { name: "Details", exact: true }).click();
  await expect(page.getByText("Original", { exact: true })).toBeVisible();
});
```

Also fix the existing download assertion in the first test of that file, which
targeted the old text button: replace

```ts
    .getByRole("button", { name: "Rechnung", exact: true })
```

with

```ts
    .getByRole("button", { name: "Rechnung herunterladen", exact: true })
```

- [ ] **Step 2: Run it to verify it fails**

Run: `cd e2e && npm test -- --project=desktop invoices.spec.ts`
Expected: FAIL — there is no search field, and grave invoices are still listed.

- [ ] **Step 3: Rewrite the view**

Replace the `subHeader` and the table's binding and action cell in
`frontend/src/view/Invoices.view.xml`:

```xml
<subHeader>
            <OverflowToolbar id="invoicesToolbar">
                <SearchField
      id="invoiceSearch"
      width="18rem"
      search=".onSearchInvoices"
      placeholder="{i18n>searchInvoice}"
    >
                    <layoutData>
                        <OverflowToolbarLayoutData priority="Low" />
                    </layoutData>
                </SearchField>
                <ToolbarSpacer />
                <Button text="{i18n>createInvoice}" press=".onCreateInvoice" type="Emphasized">
                    <layoutData>
                        <OverflowToolbarLayoutData priority="High" />
                    </layoutData>
                </Button>
            </OverflowToolbar>
        </subHeader>
```

The table binding becomes — grave invoices live on the grave pages, and a chain
appears once, as its newest revision:

```xml
            <Table id="invoicesTable" class="sapUiSmallMarginTop" growing="true" growingThreshold="50"
                autoPopinMode="true" popinLayout="Block"
                items="{path: 'odata>/Invoices', parameters: {$filter: 'GraveId eq null and IsLatestRevision eq true', $orderby: 'Id desc'}}"
                noDataText="{i18n>noData}">
```

and the action cell:

```xml
<!-- wraps in the pop-in, where the actions sit
                                         below the row instead of next to it -->
<HBox wrap="Wrap">
                                        <Button
    icon="sap-icon://detail-view"
    type="Transparent"
    tooltip="{i18n>actionDetails}"
    press=".onShowDetails"
  />
                                        <Button
    icon="sap-icon://pdf-attachment"
    type="Transparent"
    tooltip="{i18n>actionDownloadNormal}"
    press=".onDownloadNormal"
  />
                                        <Button
    icon="sap-icon://copy"
    type="Transparent"
    tooltip="{i18n>actionDownloadCopy}"
    press=".onDownloadCopy"
  />
                                        <Button
    icon="sap-icon://edit"
    type="Transparent"
    tooltip="{i18n>actionEdit}"
    press=".onEditInvoice"
  />
                                    </HBox>
```

- [ ] **Step 4: Rewrite the controller's list behaviour**

In `frontend/src/controller/Invoices.controller.ts`:

`onInit` shrinks to the root binding — the half-year select is gone:

```ts
  public onInit(): void {
    this.bindODataRoot();
  }
```

Delete `onHalfYearChange`, `onBulk`, `onBulkNormal` and `onBulkCopy`, and the now
unused imports (`ResourceBundle`, `Item`, `Filter`, `FilterOperator`,
`ResourceModel`, `Select` if no longer referenced — `Select` is still used by
`InvoiceEdit`, not here).

Add:

```ts
  /**
   * Narrows the list through the backend's `search` custom query option, which
   * matches the invoice number, recipient, date and total. An empty term is a
   * match-everything on the backend, so clearing the field restores the list
   * without special-casing it here.
   */
  public onSearchInvoices(event: any): void {
    const term = (event.getParameter("query") as string) ?? "";
    (
      (this.byId("invoicesTable") as any).getBinding("items") as ODataListBinding
    ).changeParameters({ search: term });
  }

  public onShowDetails(event: any): void {
    const context = event.getSource().getBindingContext("odata") as Context;
    this.router().navTo("invoiceDetail", {
      rootId: String(context.getProperty("RootId")),
    });
  }
```

- [ ] **Step 5: Remove the texts nothing renders any more**

Delete from `frontend/src/i18n/i18n.properties` and
`frontend/src/i18n/i18n_en.properties`:

```
allInvoices
bulkNormal
bulkCopy
editInvoice
downloadNormal
downloadCopy
```

The last three were the labels of the row's text buttons; the icon buttons use
`actionEdit`, `actionDownloadNormal` and `actionDownloadCopy` as their tooltips
instead. `halfYear` stays — the grave page still uses it.

- [ ] **Step 6: Type-check and run the E2E test**

Run: `cd frontend && npm run typecheck`
Expected: no errors.

Run: `cd e2e && npm test -- --project=desktop invoices.spec.ts`
Expected: PASS.

- [ ] **Step 7: Run the whole suite**

Run: `cd e2e && npm test`
Expected: PASS. `layout.spec.ts` still finds `invoicesToolbar` and "Rechnung
erstellen", and its seeded non-grave invoice still gives the table a row.

- [ ] **Step 8: Commit**

```bash
git add frontend/src/view/Invoices.view.xml \
        frontend/src/controller/Invoices.controller.ts \
        frontend/src/i18n/ \
        e2e/tests/invoices.spec.ts
git commit -m "feat: the invoice page lists non-grave invoices and searches them

Grave invoices belong to their grave, so they move off this list, and a
chain now appears once as its newest revision. The half-year filter and
both bulk downloads give way to a search over number, recipient, date and
total."
```

---

### Task 10: The grave dialog — a warning, and recipients that cannot change

**Files:**

- Create: `frontend/src/controller/GraveEdit.controller.ts`
- Modify: `frontend/src/view/fragments/GraveDialog.fragment.xml`
- Modify: `frontend/src/controller/Graves.controller.ts` (extend `GraveEdit`)
- Test: `e2e/tests/graves.spec.ts`

**Interfaces:**

- Produces: `GraveEdit.openGraveDialog(graveId: number | null)` and the abstract
  `refreshAfterSave(): void | Promise<void>`, which every screen implements to
  reload what it shows. Task 11's detail page opens the same dialog; Task 12 adds
  the grave-invoice dialog — and a second caller of the same hook — here.

- [ ] **Step 1: Write the failing E2E test**

Append to `e2e/tests/graves.spec.ts`:

```ts
test("editing a grave warns about future invoices and freezes its recipients", async ({ page }) => {
  const marker = Date.now();
  await page.request.post("/odata/VatRates", { data: { Rate: 7 } });
  const item = await (
    await page.request.post("/odata/InvoiceItems", {
      data: { Number: `7100-${marker}`, Description: "Grabpflege", VatRate: 7, DefaultPrice: 100 },
    })
  ).json();
  const recipient = await (
    await page.request.post("/odata/Recipients", {
      data: { Name: `Fest ${marker}`, Address: "Festweg 1" },
    })
  ).json();
  const grave = await (
    await page.request.post("/odata/Graves", {
      data: { GraveNumber: `F-${marker}`, StartDate: "2026-01-01", EndDate: "2040-12-31" },
    })
  ).json();
  await page.request.post("/odata/GraveItems", {
    data: { GraveId: grave.Id, ItemId: item.Id, Count: 1 },
  });
  await page.request.post("/odata/GraveRecipients", {
    data: { GraveId: grave.Id, RecipientId: recipient.Id },
  });

  await page.goto("/index.html#/graves");
  const gravesTable = page.locator("[id$='gravesTable-listUl']");
  const row = gravesTable.locator("tbody tr", { hasText: `F-${marker}` });
  await expect(row).toBeVisible();
  await row.getByRole("button", { name: "Bearbeiten" }).click();

  const dialog = page.locator("[id$='graveDialog']");
  await expect(dialog).toContainText("nur auf künftig erstellte Rechnungen");
  await expect(dialog).toContainText("Empfänger eines Grabs");
  // the recipient stays visible but cannot be removed or added to
  await expect(dialog.getByText(`Fest ${marker}`)).toBeVisible();
  await expect(dialog.getByRole("button", { name: "Hinzufügen" })).toBeHidden();
  await expect(dialog.getByRole("button", { name: "Löschen" })).toBeHidden();
});
```

- [ ] **Step 2: Run it to verify it fails**

Run: `cd e2e && npm test -- --project=desktop graves.spec.ts`
Expected: FAIL — no warning text, and the add/delete controls are still visible.

- [ ] **Step 3: Move the grave dialog into its own controller**

Create `frontend/src/controller/GraveEdit.controller.ts`, moving the grave-dialog
code out of `Graves.controller.ts` unchanged apart from the entry point:

```ts
import { ComboBox$SelectionChangeEvent } from "sap/m/ComboBox";
import Input from "sap/m/Input";
import Select from "sap/m/Select";
import TextArea from "sap/m/TextArea";
import JSONModel from "sap/ui/model/json/JSONModel";
import ODataListBinding from "sap/ui/model/odata/v4/ODataListBinding";
import * as api from "../model/api";
import InvoiceRevisions from "./InvoiceRevisions.controller";

/**
 * Owns the dialogs that edit a grave and its invoices. The grave list, the grave
 * detail page and the grave-invoice detail page all open one of them.
 */
export default abstract class GraveEdit extends InvoiceRevisions {
  /** Runs after a save in either dialog so the screen can reload what it shows.
   *  One hook for both, because every screen answers it the same way. */
  protected abstract refreshAfterSave(): void | Promise<void>;

  protected async openGraveDialog(graveId: number | null): Promise<void> {
    if (!this._graveDialog) {
      // loadFragment passes this controller and the view ID prefix so that
      // this.byId(...) finds the dialog controls and IDs stay unique
      this._graveDialog = (await this.loadFragment({
        name: "burkard.invoicing.view.fragments.GraveDialog",
      })) as any;
    }

    // everything the dialog collects lives in this draft until it is saved, so
    // items and recipients can be added before the grave itself exists
    this._graveDialog.setModel(
      new JSONModel(
        graveId != null
          ? await this.loadGraveDraft(graveId)
          : {
              Id: null,
              GraveNumber: "",
              StartDate: "",
              EndDate: "",
              Items: [],
              Recipients: [],
            },
      ),
      "graveDraft",
    );

    this._graveDialog.open();
  }

  private async loadGraveDraft(graveId: number): Promise<any> {
    const details = await api.fetchGraveDetails(graveId);
    return {
      Id: details.Id,
      GraveNumber: details.GraveNumber,
      StartDate: details.StartDate,
      EndDate: details.EndDate,
      Items: details.Items.map((link: any) => ({
        // the ComboBox matches its keys as strings
        itemId: String(link.Item.Id),
        itemNumber: link.Item.Number,
        description: link.Item.Description,
        count: link.Count,
        unitPrice: link.UnitPrice,
        defaultPrice: link.Item.DefaultPrice,
      })),
      Recipients: details.Recipients.map((link: any) => ({
        recipientId: String(link.Recipient.Id),
        name: link.Recipient.Name,
      })),
    };
  }

  public onGraveItemSelected(event: ComboBox$SelectionChangeEvent): void {
    // the ComboBox writes the key itself; fill in what the row shows alongside it
    const itemContext = event.getParameter("selectedItem")?.getBindingContext("odata");
    const rowContext = event.getSource().getBindingContext("graveDraft");
    if (!itemContext || !rowContext) {
      return;
    }

    const model = rowContext.getModel() as JSONModel;
    const path = rowContext.getPath();
    model.setProperty(path + "/itemNumber", itemContext.getProperty("Number") as string);
    model.setProperty(path + "/description", itemContext.getProperty("Description") as string);
    // an empty price falls back to this, which the input shows as its placeholder
    model.setProperty(path + "/defaultPrice", itemContext.getProperty("DefaultPrice"));
  }

  public onAddItemLink(): void {
    const model = this._graveDialog.getModel("graveDraft") as JSONModel;
    const rows = (model.getProperty("/Items") as any[]) ?? [];
    rows.push({
      itemId: "",
      itemNumber: "",
      description: "",
      count: 1,
      unitPrice: null,
      defaultPrice: null,
    });
    model.setProperty("/Items", rows);
  }

  public async onAddRecipientLink(): Promise<void> {
    const select = this.byId("linkRecipient") as Select;
    const selected = select.getSelectedItem();
    if (!selected) {
      return;
    }

    const model = this._graveDialog.getModel("graveDraft") as JSONModel;
    const rows = (model.getProperty("/Recipients") as any[]) ?? [];
    const recipientId = select.getSelectedKey();
    if (rows.some((row) => row.recipientId === recipientId)) {
      await api.showError(this, new Error("GraveRecipientLinkExists"));
      return;
    }

    rows.push({ recipientId, name: selected.getText() });
    model.setProperty("/Recipients", rows);
  }

  public onDeleteItemLink(event: any): void {
    this.removeDraftRow(event, "Items");
  }

  public onDeleteRecipientLink(event: any): void {
    this.removeDraftRow(event, "Recipients");
  }

  private removeDraftRow(event: any, collection: string): void {
    const context = event.getSource().getBindingContext("graveDraft") as any;
    const model = context.getModel() as JSONModel;
    const rows = model.getProperty(`/${collection}`) as any[];
    // the row's binding path ends in its index, e.g. "/Items/2"
    rows.splice(Number(context.getPath().split("/").pop()), 1);
    model.setProperty(`/${collection}`, rows);
  }

  public async onGraveDialogSave(): Promise<void> {
    const model = this._graveDialog.getModel("graveDraft") as JSONModel;
    const draft = model.getData() as any;
    const items = draft.Items as any[];
    if (items.some((row) => !row.itemId)) {
      await api.showError(this, new Error("ItemRequired"));
      return;
    }

    try {
      await api.saveGrave({
        graveId: draft.Id != null ? Number(draft.Id) : null,
        graveNumber: draft.GraveNumber ?? "",
        startDate: draft.StartDate ?? "",
        endDate: draft.EndDate ?? "",
        items: items.map((row) => ({
          itemId: Number(row.itemId),
          count: Number(row.count),
          // a price of 0 is a real value, not a missing one
          unitPrice: row.unitPrice == null || row.unitPrice === "" ? null : Number(row.unitPrice),
        })),
        recipientIds: (draft.Recipients as any[]).map((row) => Number(row.recipientId)),
      });

      this._graveDialog.close();
      await this.refreshAfterSave();
    } catch (error) {
      await api.showError(this, error);
    }
  }

  public onGraveDialogCancel(event: any): void {
    (event.getSource().getParent() as any).close();
  }

  public async onNewRecipient(): Promise<void> {
    if (!this._quickDialog) {
      this._quickDialog = (await this.loadFragment({
        name: "burkard.invoicing.view.fragments.RecipientQuickCreate",
      })) as any;
    }
    (this.byId("quickName") as Input).setValue("");
    (this.byId("quickAddress") as TextArea).setValue("");
    this._quickDialog.open();
  }

  public async onQuickRecipientSave(event: any): Promise<void> {
    const dialog = event.getSource().getParent() as any;
    try {
      const id = await api.createRecipient({
        Name: (this.byId("quickName") as Input).getValue(),
        Address: (this.byId("quickAddress") as TextArea).getValue(),
      });
      // refresh the link select's list so the new row is included, then
      // select it; the parent grave dialog stays open
      const select = this.byId("linkRecipient") as Select;
      await (select.getBinding("items") as ODataListBinding).refresh();
      select.setSelectedKey(String(id));
      dialog.close();
    } catch (error) {
      await api.showError(this, error);
    }
  }

  public onQuickRecipientCancel(event: any): void {
    (event.getSource().getParent() as any).close();
  }

  protected _graveDialog: any;
  private _quickDialog: any;
}
```

Then in `Graves.controller.ts`: extend `GraveEdit`, delete everything that moved,
and keep the entry points:

```ts
  public async onCreateGrave(): Promise<void> {
    await this.openGraveDialog(null);
  }

  public async onEditGrave(event: any): Promise<void> {
    const context = event.getSource().getBindingContext("status");
    await this.openGraveDialog(Number(context.getProperty("Id")));
  }

  protected async refreshAfterSave(): Promise<void> {
    await this.refreshStatus();
  }
```

`refreshStatus` must become `protected` if it was private, and the generate
dialog, the search and the status model stay in `Graves.controller`.

- [ ] **Step 4: Add the warning and freeze the recipients in the fragment**

In `frontend/src/view/fragments/GraveDialog.fragment.xml`:

Give the dialog a title that says which of the two things it is doing:

```xml
    <Dialog id="graveDialog" stretch="true"
        title="{= ${graveDraft>/Id} === null ? ${i18n>createGrave} : ${i18n>editGrave} }">
```

Add the warning as the first child of the content `VBox`:

```xml
<!-- an edit changes the grave, never an invoice already issued
                     from it; the invoices are snapshots -->
<MessageStrip
  type="Warning"
  showIcon="true"
  class="sapUiSmallMarginBottom"
  text="{i18n>graveEditWarning}"
  visible="{= ${graveDraft>/Id} !== null }"
/>
```

In the recipients `Panel`, add the note and hide both ways of changing the set
once the grave exists:

```xml
<Panel headerText="{i18n>graveRecipients}">
                    <!-- the even split across them is baked into every invoice the
                         grave has generated, so the set is fixed at creation -->
                    <MessageStrip
    type="Information"
    showIcon="true"
    class="sapUiSmallMarginBottom"
    text="{i18n>graveRecipientsFixed}"
    visible="{= ${graveDraft>/Id} !== null }"
  />
                    <Table
    id="graveRecipientsTable"
    items="{graveDraft>/Recipients}"
    autoPopinMode="true"
    popinLayout="Block"
  >
                        <columns>
                            <Column importance="High"><Text text="{i18n>recipientName}" /></Column>
                            <Column importance="Low" popinDisplay="WithoutHeader" />
                        </columns>
                        <items>
                            <ColumnListItem>
                                <cells>
                                    <Text text="{graveDraft>name}" />
                                    <Button
            text="{i18n>delete}"
            press=".onDeleteRecipientLink"
            type="Transparent"
            visible="{= ${graveDraft>/Id} === null }"
          />
                                </cells>
                            </ColumnListItem>
                        </items>
                    </Table>
                    <!-- wraps so the select and both buttons stay usable on a phone -->
                    <HBox
    wrap="Wrap"
    alignItems="Center"
    class="sapUiSmallMarginTop"
    visible="{= ${graveDraft>/Id} === null }"
  >
                        <Select
      id="linkRecipient"
      width="12rem"
      items="{path: 'odata>/Recipients', parameters: {$orderby: 'Id desc'}}"
    >
                            <core:Item key="{odata>Id}" text="{odata>Name}" />
                        </Select>
                        <Button text="{i18n>add}" press=".onAddRecipientLink" />
                        <Button
      text="{i18n>newRecipient}"
      press=".onNewRecipient"
      type="Transparent"
    />
                    </HBox>
                </Panel>
```

- [ ] **Step 5: Type-check and run the E2E test**

Run: `cd frontend && npm run typecheck`
Expected: no errors.

Run: `cd e2e && npm test -- --project=desktop graves.spec.ts`
Expected: PASS.

- [ ] **Step 6: Run the whole suite**

Run: `cd e2e && npm test`
Expected: PASS — creating a grave with recipients still works, which
`graves.spec.ts` and `layout.spec.ts` both exercise.

- [ ] **Step 7: Commit**

```bash
git add frontend/src/controller/GraveEdit.controller.ts \
        frontend/src/controller/Graves.controller.ts \
        frontend/src/view/fragments/GraveDialog.fragment.xml \
        e2e/tests/graves.spec.ts
git commit -m "feat: say what editing a grave does, and freeze its recipients

An edit never touches an invoice already issued, and the recipient set
cannot change at all once the grave exists — the dialog now says both
instead of leaving the user to find out."
```

---

### Task 11: The grave detail page

**Files:**

- Create: `frontend/src/view/GraveDetail.view.xml`
- Create: `frontend/src/controller/GraveDetail.controller.ts`
- Modify: `frontend/src/manifest.json` (route + target)
- Modify: `backend/src/Invoicing.Api/OData/Controllers/GravesController.cs`
  (`CreatedAt` in the single-grave projection)
- Test: `e2e/tests/graves.spec.ts`

**Interfaces:**

- Consumes: `GraveEdit.openGraveDialog` (Task 10), `GraveInvoiceRuns` (Task 4),
  `IsLatestRevision` (Task 2), `api.invoiceTotals` / `api.halfYearText` (Task 7).
- Produces: route `graveDetail`, pattern `graves/{graveId}` — Task 13's details
  button navigates to it; and route name `graveInvoiceDetail` is referenced from
  the invoice rows here, arriving in Task 14.

The invoice rows' details button navigates to `graveInvoiceDetail`, which Task 14
creates. Until then that one button is inert; everything else on the page works.

- [ ] **Step 1: Write the failing E2E test**

Grave generation belongs to `e2e/tests/graves.spec.ts` alone (see Global
Constraints), so this test is appended **there**, after the two generation tests
already in the file. Its grave is therefore created after those runs, which is
what makes the current half-year's status deterministic.

First add the helper the next three tasks share, next to the file's other helpers:

```ts
/**
 * Generates the grave invoices of one half-year and waits for the job. Each
 * caller uses a period no other test touches, so a run never collides with the
 * generation tests above or with another test's expectations.
 */
async function generateFor(
  page: import("@playwright/test").Page,
  year: number,
  half: number,
): Promise<void> {
  const response = await page.request.post("/odata/GenerateGraveInvoices", {
    data: { year, half },
  });
  expect(response.status()).toBe(202);
  const job = await response.json();
  await expect
    .poll(async () => (await (await page.request.get(`/odata/Jobs(${job.Id})`)).json()).Status, {
      timeout: 90000,
    })
    .toBe("Succeeded");
}

/** A grave with one item at 100.00 and the given recipients, unique per run. */
async function seedGrave(
  page: import("@playwright/test").Page,
  marker: string,
  recipientNames: string[],
): Promise<{ graveId: number; recipientNames: string[] }> {
  await page.request.post("/odata/VatRates", { data: { Rate: 7 } });
  const item = await (
    await page.request.post("/odata/InvoiceItems", {
      data: {
        Number: `${marker}-item`,
        Description: "Grabpflege",
        VatRate: 7,
        DefaultPrice: 100,
      },
    })
  ).json();
  const grave = await (
    await page.request.post("/odata/Graves", {
      data: {
        GraveNumber: marker,
        StartDate: "2026-01-01",
        EndDate: "2040-12-31",
      },
    })
  ).json();
  await page.request.post("/odata/GraveItems", {
    data: { GraveId: grave.Id, ItemId: item.Id, Count: 1 },
  });
  for (const name of recipientNames) {
    const recipient = await (
      await page.request.post("/odata/Recipients", {
        data: { Name: name, Address: "Musterweg 1" },
      })
    ).json();
    await page.request.post("/odata/GraveRecipients", {
      data: { GraveId: grave.Id, RecipientId: recipient.Id },
    });
  }
  return { graveId: grave.Id, recipientNames };
}
```

Then append the test:

```ts
test("the grave detail page shows the grave, its status and its invoices", async ({ page }) => {
  const marker = `GD-${Date.now()}`;
  const names = [`Anna ${marker}`, `Bernd ${marker}`];
  const { graveId } = await seedGrave(page, marker, names);

  await page.goto(`/index.html#/graves/${graveId}`);

  await expect(page.getByText(marker, { exact: true })).toBeVisible();
  // this grave was created after the generation tests above ran, so nothing has
  // invoiced it for the current half-year
  await expect(page.getByText("offen").first()).toBeVisible();
  for (const name of names) {
    await expect(page.getByText(name)).toBeVisible();
  }
  await expect(page.getByText("Für dieses Grab wurden noch keine")).toBeVisible();

  // a period no other test uses, so the rows below are exactly this generation
  await generateFor(page, 2039, 2);
  await page.reload();

  const invoicesTable = page.locator("[id$='graveInvoicesTable-listUl']");
  await expect(invoicesTable.locator("tbody tr")).toHaveCount(2);
  await expect(invoicesTable).toContainText("H2 2039");
  // each recipient pays half of 1 × 100.00 + 7% = 53,50
  await expect(invoicesTable).toContainText("53,50 €");

  // both documents are reachable per row
  const row = invoicesTable.locator("tbody tr", { hasText: names[0] });
  await expect(row.getByRole("button", { name: "Rechnung herunterladen" })).toBeVisible();
  await expect(row.getByRole("button", { name: "Kopie herunterladen" })).toBeVisible();
});
```

- [ ] **Step 2: Run it to verify it fails**

Run: `cd e2e && npm test -- --project=desktop graves.spec.ts`
Expected: FAIL — the route does not exist.

- [ ] **Step 3: Return the creation date with the grave**

In `backend/src/Invoicing.Api/OData/Controllers/GravesController.cs`, in the
single-grave projection, add `CreatedAt` after `grave.EndDate`:

```csharp
            grave.EndDate,
            grave.CreatedAt,
```

- [ ] **Step 4: Add the route and target**

In `frontend/src/manifest.json`, add to `routes`:

```json
        {
          "name": "graveDetail",
          "pattern": "graves/{graveId}",
          "target": [
            "graveDetail"
          ]
        },
```

and to `targets`:

```json
        "graveDetail": {
          "name": "GraveDetail",
          "level": 1,
          "type": "View"
        },
```

- [ ] **Step 5: Write the view**

Create `frontend/src/view/GraveDetail.view.xml`:

```xml
<mvc:View
  xmlns:mvc="sap.ui.core.mvc"
  xmlns="sap.m"
  controllerName="burkard.invoicing.controller.GraveDetail"
  displayBlock="true"
>
    <Page
    id="graveDetailPage"
    title="{i18n>graveDetailTitle}"
    titleLevel="H1"
    showNavButton="true"
    navButtonPress=".onNavBack"
    class="sapUiResponsiveContentPadding"
  >
        <headerContent>
            <Button icon="sap-icon://edit" tooltip="{i18n>editGrave}" press=".onEditGrave" />
        </headerContent>
        <content>
            <VBox class="sapUiSmallMarginTop">
                <Title text="{grave>/GraveNumber}" level="H2" />
                <HBox wrap="Wrap" class="sapUiTinyMarginTop">
                    <ObjectStatus
            title="{i18n>startDate}"
            class="sapUiSmallMarginEnd"
            text="{path: 'grave>/StartDate', formatter: '.formatDate'}"
          />
                    <ObjectStatus
            title="{i18n>endDate}"
            class="sapUiSmallMarginEnd"
            text="{path: 'grave>/EndDate', formatter: '.formatDate'}"
          />
                    <ObjectStatus
            title="{i18n>graveCreatedAt}"
            class="sapUiSmallMarginEnd"
            text="{path: 'grave>/CreatedAt', formatter: '.formatDate'}"
          />
                    <!-- green when this half-year is done, red while it still needs
                         invoices — the same reading as the grave list -->
                    <ObjectStatus
            title="{grave>/currentHalfYearLabel}"
            text="{grave>/currentStatusText}"
            state="{= ${grave>/currentInvoiced} ? 'Success' : 'Error' }"
            icon="{= ${grave>/currentInvoiced} ? 'sap-icon://accept' : 'sap-icon://error' }"
          />
                </HBox>
            </VBox>

            <Panel headerText="{i18n>graveItems}" class="sapUiSmallMarginTop">
                <Table
          items="{grave>/Items}"
          autoPopinMode="true"
          popinLayout="Block"
          noDataText="{i18n>noData}"
        >
                    <columns>
                        <Column importance="High"><Text text="{i18n>itemNumber}" /></Column>
                        <Column importance="Medium" popinDisplay="Inline"><Text
                text="{i18n>itemDescription}"
              /></Column>
                        <Column importance="High" popinDisplay="Inline"><Text
                text="{i18n>itemCount}"
              /></Column>
                        <Column importance="High" popinDisplay="Inline"><Text
                text="{i18n>itemPrice}"
              /></Column>
                        <Column importance="Low" popinDisplay="Inline"><Text
                text="{i18n>vatRate}"
              /></Column>
                    </columns>
                    <items>
                        <ColumnListItem>
                            <cells>
                                <Text text="{grave>Number}" />
                                <Text text="{grave>Description}" wrapping="true" />
                                <Text text="{grave>Count}" />
                                <Text text="{path: 'grave>Price', formatter: '.formatAmount'}" />
                                <Text text="{grave>VatRate}" />
                            </cells>
                        </ColumnListItem>
                    </items>
                </Table>
            </Panel>

            <Panel headerText="{i18n>graveRecipients}" class="sapUiSmallMarginTop">
                <!-- read-only always: the split across them is baked into every
                     invoice the grave has generated -->
                <Table
          items="{grave>/Recipients}"
          autoPopinMode="true"
          popinLayout="Block"
          noDataText="{i18n>noData}"
        >
                    <columns>
                        <Column importance="High"><Text text="{i18n>recipientName}" /></Column>
                        <Column importance="Medium" popinDisplay="Inline"><Text
                text="{i18n>recipientAddress}"
              /></Column>
                    </columns>
                    <items>
                        <ColumnListItem>
                            <cells>
                                <Text text="{grave>Name}" />
                                <Text text="{grave>Address}" renderWhitespace="true" />
                            </cells>
                        </ColumnListItem>
                    </items>
                </Table>
            </Panel>

            <Panel headerText="{i18n>graveInvoices}" class="sapUiSmallMarginTop">
                <Table
          id="graveInvoicesTable"
          items="{grave>/Invoices}"
          autoPopinMode="true"
          popinLayout="Block"
          noDataText="{i18n>noGraveInvoices}"
        >
                    <columns>
                        <Column importance="High"><Text text="{i18n>halfYear}" /></Column>
                        <Column importance="High" popinDisplay="Inline"><Text
                text="{i18n>recipientName}"
              /></Column>
                        <Column importance="Medium" popinDisplay="Inline"><Text
                text="{i18n>invoiceNumber}"
              /></Column>
                        <Column importance="Low" popinDisplay="Inline"><Text
                text="{i18n>invoiceDate}"
              /></Column>
                        <Column importance="Medium" popinDisplay="Inline"><Text
                text="{i18n>grossTotal}"
              /></Column>
                        <Column width="11rem" importance="Low" popinDisplay="WithoutHeader" />
                    </columns>
                    <items>
                        <ColumnListItem>
                            <cells>
                                <Text text="{grave>HalfYear}" />
                                <Text text="{grave>RecipientName}" />
                                <HBox>
                                    <Text text="{grave>InvoiceNumber}" />
                                    <Text
                    text="{path: 'grave>RevisionNumber', formatter: '.formatRevisionBadge'}"
                    class="sapUiTinyMarginBegin"
                  />
                                </HBox>
                                <Text
                  text="{path: 'grave>GeneratedAt', formatter: '.formatDate'}"
                />
                                <Text
                  text="{path: 'grave>GrossTotal', formatter: '.formatAmount'}"
                />
                                <HBox wrap="Wrap">
                                    <Button
                    icon="sap-icon://detail-view"
                    type="Transparent"
                    tooltip="{i18n>actionDetails}"
                    press=".onShowInvoiceDetails"
                  />
                                    <Button
                    icon="sap-icon://pdf-attachment"
                    type="Transparent"
                    tooltip="{i18n>actionDownloadNormal}"
                    press=".onDownloadInvoiceNormal"
                  />
                                    <Button
                    icon="sap-icon://copy"
                    type="Transparent"
                    tooltip="{i18n>actionDownloadCopy}"
                    press=".onDownloadInvoiceCopy"
                  />
                                    <Button
                    icon="sap-icon://edit"
                    type="Transparent"
                    tooltip="{i18n>actionEdit}"
                    press=".onEditGraveInvoice"
                  />
                                </HBox>
                            </cells>
                        </ColumnListItem>
                    </items>
                </Table>
            </Panel>
        </content>
    </Page>
</mvc:View>
```

- [ ] **Step 6: Write the controller**

Create `frontend/src/controller/GraveDetail.controller.ts`:

```ts
import JSONModel from "sap/ui/model/json/JSONModel";
import * as api from "../model/api";
import GraveEdit from "./GraveEdit.controller";

/** One grave: its configuration, this half-year's status, and every invoice it
 *  has produced across all half-years and recipients. */
export default class GraveDetail extends GraveEdit {
  public onInit(): void {
    this.bindODataRoot();
    this.router().getRoute("graveDetail")!.attachPatternMatched(this.onRouteMatched, this);
  }

  private async onRouteMatched(event: any): Promise<void> {
    this._graveId = Number(event.getParameter("arguments").graveId);
    await this.refresh();
  }

  private async refresh(): Promise<void> {
    try {
      const [details, runs, invoices] = await Promise.all([
        api.fetchGraveDetails(this._graveId),
        this.readList("/GraveInvoiceRuns", { $filter: `GraveId eq ${this._graveId}` }),
        this.readList("/Invoices", {
          $filter: `GraveId eq ${this._graveId} and IsLatestRevision eq true`,
          $orderby: "PeriodYear desc,PeriodHalf desc,RecipientName",
          $expand: "Lines",
        }),
      ]);

      const now = new Date();
      const year = now.getFullYear();
      // a half-year is Jan–Jun or Jul–Dec
      const half = now.getMonth() < 6 ? 1 : 2;
      const invoiced = runs.some((run) => Number(run.Year) === year && Number(run.Half) === half);

      this.getView()!.setModel(
        new JSONModel({
          Id: details.Id,
          GraveNumber: details.GraveNumber,
          StartDate: details.StartDate,
          EndDate: details.EndDate,
          CreatedAt: details.CreatedAt,
          currentHalfYearLabel: this.i18nText("currentHalfYearStatus").replace(
            "{0}",
            api.halfYearText(year, half),
          ),
          currentStatusText: this.i18nText(invoiced ? "statusInvoiced" : "statusOpen"),
          currentInvoiced: invoiced,
          Items: (details.Items as any[]).map((link) => ({
            Number: link.Item.Number,
            Description: link.Item.Description,
            Count: link.Count,
            // the link's own price wins; without one the item's default is billed
            Price: Number(link.UnitPrice ?? link.Item.DefaultPrice ?? 0),
            VatRate: link.Item.VatRate,
          })),
          Recipients: (details.Recipients as any[]).map((link) => ({
            Name: link.Recipient.Name,
            Address: link.Recipient.Address,
          })),
          Invoices: invoices.map((invoice) => {
            const lines = ((invoice.Lines as any[]) ?? []).map((line) => ({
              // the OData V4 model serialises Edm.Decimal as a string
              Count: Number(line.Count),
              UnitPrice: Number(line.UnitPrice),
              VatRate: Number(line.VatRate),
            }));
            return {
              Id: Number(invoice.Id),
              RootId: Number(invoice.RootId),
              HalfYear: api.halfYearText(Number(invoice.PeriodYear), Number(invoice.PeriodHalf)),
              RecipientName: invoice.RecipientName,
              InvoiceNumber: this.formatInvoiceNumber(invoice.InvoiceNumber),
              RevisionNumber: Number(invoice.RevisionNumber),
              GeneratedAt: invoice.GeneratedAt,
              GrossTotal: api.invoiceTotals(lines).gross,
            };
          }),
        }),
        "grave",
      );
    } catch (error) {
      await api.showError(this, error);
    }
  }

  /** Back to the list this page was opened from, not to the home screen. */
  public onNavBack(): void {
    this.router().navTo("graves");
  }

  public async onEditGrave(): Promise<void> {
    await this.openGraveDialog(this._graveId);
  }

  protected async refreshAfterSave(): Promise<void> {
    await this.refresh();
  }

  public onShowInvoiceDetails(event: any): void {
    const context = event.getSource().getBindingContext("grave");
    this.router().navTo("graveInvoiceDetail", {
      graveId: String(this._graveId),
      rootId: String(context.getProperty("RootId")),
    });
  }

  public async onDownloadInvoiceNormal(event: any): Promise<void> {
    await this.downloadRow(event, false);
  }

  public async onDownloadInvoiceCopy(event: any): Promise<void> {
    await this.downloadRow(event, true);
  }

  private async downloadRow(event: any, isCopy: boolean): Promise<void> {
    const context = event.getSource().getBindingContext("grave");
    await this.openDocument(Number(context.getProperty("Id")), isCopy);
  }

  private _graveId = 0;
}
```

`onEditGraveInvoice` arrives with the dialog in Task 12. Until then, add a stub so
the XML's handler resolves:

```ts
  public onEditGraveInvoice(): void {
    // the grave-invoice dialog arrives in the next task
  }
```

- [ ] **Step 7: Type-check and run the E2E test**

Run: `cd frontend && npm run typecheck`
Expected: no errors.

Run the backend tests — the `CreatedAt` projection change touches
`GraveTests`/`SaveGraveTests`.
Expected: PASS.

Run: `cd e2e && npm test -- --project=desktop graves.spec.ts`
Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add frontend/src/view/GraveDetail.view.xml \
        frontend/src/controller/GraveDetail.controller.ts \
        frontend/src/manifest.json \
        backend/src/Invoicing.Api/OData/Controllers/GravesController.cs \
        e2e/tests/graves.spec.ts
git commit -m "feat: a grave detail page with its whole invoicing history

Every invoice the grave has produced, across all half-years and
recipients, next to the grave's own configuration and this half-year's
status."
```

---

### Task 12: The grave-invoice edit dialog

**Files:**

- Create: `frontend/src/view/fragments/GraveInvoiceDialog.fragment.xml`
- Modify: `frontend/src/controller/GraveEdit.controller.ts`
- Modify: `frontend/src/controller/GraveDetail.controller.ts` (replace the stub)
- Test: `e2e/tests/graves.spec.ts`

**Interfaces:**

- Consumes: `api.fetchGraveInvoiceGroup` and `api.createGraveInvoiceRevision` (Task 7),
  which reach Tasks 4 and 5.
- Produces: `GraveEdit.openGraveInvoiceDialog(invoiceId: number)`.

The dialog edits the **grave's** lines — the full count at the full price — not
one recipient's share, and it opens on what was invoiced for that half-year, not
on the grave's current items.

- [ ] **Step 1: Write the failing E2E test**

Append to `e2e/tests/graves.spec.ts`, reusing `seedGrave` and `generateFor` from
Task 11:

```ts
test("editing a grave invoice revises every recipient", async ({ page }) => {
  const marker = `GE-${Date.now()}`;
  const names = [`Anna ${marker}`, `Bernd ${marker}`];
  const { graveId } = await seedGrave(page, marker, names);

  // its own period, so the rows below belong to this test alone
  await generateFor(page, 2038, 2);

  await page.goto(`/index.html#/graves/${graveId}`);
  const invoicesTable = page.locator("[id$='graveInvoicesTable-listUl']");
  await expect(invoicesTable.locator("tbody tr")).toHaveCount(2);

  await invoicesTable
    .locator("tbody tr", { hasText: names[0] })
    .getByRole("button", { name: "Bearbeiten" })
    .click();

  // the dialog names the half-year and both recipients it is about to revise
  const dialog = page.locator("[id$='graveInvoiceDialog']");
  await expect(dialog).toContainText("H2 2038");
  await expect(dialog).toContainText(names[0]);
  await expect(dialog).toContainText(names[1]);
  // it opens on the grave's price, not on the 50.00 share each recipient pays
  await expect(dialog.getByLabel("Preis")).toHaveValue("100");

  await dialog.getByLabel("Preis").fill("200");
  await dialog.getByRole("button", { name: "Speichern" }).click();

  // both recipients moved to R1, each at half of 1 × 200.00 + 7% = 107,00
  await expect(invoicesTable.locator("tbody tr")).toHaveCount(2);
  await expect(invoicesTable.locator("tbody tr", { hasText: "R1" })).toHaveCount(2);
  await expect(invoicesTable.getByText("107,00 €").first()).toBeVisible();
});
```

- [ ] **Step 2: Run it to verify it fails**

Run: `cd e2e && npm test -- --project=desktop graves.spec.ts`
Expected: FAIL — the edit button does nothing (Task 11's stub).

- [ ] **Step 3: Write the fragment**

Create `frontend/src/view/fragments/GraveInvoiceDialog.fragment.xml`:

```xml
<core:FragmentDefinition xmlns:core="sap.ui.core" xmlns="sap.m">
    <!-- stretch like the other dialogs of this app, so a phone gets the full
         screen instead of a window with fields cut off at its edges -->
    <Dialog id="graveInvoiceDialog" title="{i18n>graveInvoiceEditTitle}" stretch="true">
        <content>
            <VBox class="sapUiMediumMargin">
                <!-- an invoice per recipient is one grave bill split evenly, so an
                     edit is an edit to the bill: every recipient is revised -->
                <MessageStrip
          type="Warning"
          showIcon="true"
          class="sapUiSmallMarginBottom"
          text="{graveInvoiceDraft>/warning}"
        />
                <Table
          id="graveInvoiceLineTable"
          items="{graveInvoiceDraft>/lines}"
          autoPopinMode="true"
          popinLayout="Block"
        >
                    <columns>
                        <Column importance="High"><Text text="{i18n>itemNumber}" /></Column>
                        <Column importance="Medium" popinDisplay="Inline"><Text
                text="{i18n>itemDescription}"
              /></Column>
                        <Column importance="High" popinDisplay="Inline"><Text
                text="{i18n>itemCount}"
              /></Column>
                        <Column importance="High" popinDisplay="Inline"><Text
                text="{i18n>itemPrice}"
              /></Column>
                        <Column importance="Low" popinDisplay="WithoutHeader" />
                    </columns>
                    <items>
                        <ColumnListItem>
                            <cells>
                                <ComboBox
                  id="graveInvoiceItem"
                  width="10rem"
                  selectedKey="{graveInvoiceDraft>itemId}"
                  showSecondaryValues="true"
                  items="{path: 'odata>/InvoiceItems', parameters: {$orderby: 'Id desc'}, templateShareable: true}"
                  selectionChange=".onGraveInvoiceItemSelected"
                >
                                    <core:ListItem
                    key="{odata>Id}"
                    text="{odata>Number}"
                    additionalText="{odata>Description}"
                  />
                                </ComboBox>
                                <Text text="{graveInvoiceDraft>description}" wrapping="true" />
                                <!-- the grave's count and price, before the split -->
                                <Input
                  value="{graveInvoiceDraft>count}"
                  type="Number"
                  width="5rem"
                  ariaLabel="{i18n>itemCount}"
                />
                                <Input
                  value="{graveInvoiceDraft>unitPrice}"
                  type="Number"
                  width="7rem"
                  placeholder="{graveInvoiceDraft>defaultPrice}"
                  ariaLabel="{i18n>itemPrice}"
                />
                                <Button
                  text="{i18n>delete}"
                  press=".onDeleteGraveInvoiceLine"
                  type="Transparent"
                />
                            </cells>
                        </ColumnListItem>
                    </items>
                </Table>
                <Button
          text="{i18n>addLine}"
          press=".onAddGraveInvoiceLine"
          class="sapUiSmallMarginTop"
        />
            </VBox>
        </content>
        <beginButton>
            <Button text="{i18n>save}" type="Emphasized" press=".onGraveInvoiceDialogSave" />
        </beginButton>
        <endButton>
            <Button text="{i18n>cancel}" press=".onGraveInvoiceDialogCancel" />
        </endButton>
    </Dialog>
</core:FragmentDefinition>
```

- [ ] **Step 4: Add the handlers to `GraveEdit`**

In `frontend/src/controller/GraveEdit.controller.ts`, add `BusyDialog` to the
imports and append these members:

```ts
  /**
   * Opens the dialog on the grave's lines as they were invoiced for that
   * half-year — read back from the invoices, not from the grave's current items,
   * so a grave edited since then cannot leak into a revision of this period.
   */
  protected async openGraveInvoiceDialog(invoiceId: number): Promise<void> {
    let group: api.GraveInvoiceGroup;
    try {
      group = await api.fetchGraveInvoiceGroup(invoiceId);
    } catch (error) {
      await api.showError(this, error);
      return;
    }

    if (!this._graveInvoiceDialog) {
      this._graveInvoiceDialog = (await this.loadFragment({
        name: "burkard.invoicing.view.fragments.GraveInvoiceDialog",
      })) as any;
    }

    const warning = this.i18nText("graveInvoiceEditWarning")
      .replace("{0}", api.halfYearText(group.PeriodYear, group.PeriodHalf))
      .replace("{1}", group.Recipients.map((r) => r.Name).join(", "));

    this._graveInvoiceDialog.setModel(
      new JSONModel({
        invoiceId,
        warning,
        lines: group.Lines.map((line) => ({
          // the ComboBox matches its keys as strings
          itemId: String(line.ItemId),
          itemNumber: line.ItemNumber,
          description: line.Description,
          count: Number(line.Count),
          unitPrice: Number(line.UnitPrice),
          defaultPrice: null,
        })),
      }),
      "graveInvoiceDraft",
    );
    this._graveInvoiceDialog.open();
  }

  public onGraveInvoiceItemSelected(event: ComboBox$SelectionChangeEvent): void {
    // the ComboBox writes the key itself; fill in what the row shows alongside it
    const itemContext = event.getParameter("selectedItem")?.getBindingContext("odata");
    const rowContext = event.getSource().getBindingContext("graveInvoiceDraft");
    if (!itemContext || !rowContext) {
      return;
    }

    const model = rowContext.getModel() as JSONModel;
    const path = rowContext.getPath();
    model.setProperty(path + "/itemNumber", itemContext.getProperty("Number") as string);
    model.setProperty(path + "/description", itemContext.getProperty("Description") as string);
    const defaultPrice = itemContext.getProperty("DefaultPrice");
    model.setProperty(path + "/defaultPrice", defaultPrice);
    model.setProperty(
      path + "/unitPrice",
      defaultPrice != null ? Number(defaultPrice) : null,
    );
  }

  public onAddGraveInvoiceLine(): void {
    const model = this._graveInvoiceDialog.getModel("graveInvoiceDraft") as JSONModel;
    const rows = (model.getProperty("/lines") as any[]) ?? [];
    rows.push({
      itemId: "",
      itemNumber: "",
      description: "",
      count: 1,
      unitPrice: null,
      defaultPrice: null,
    });
    model.setProperty("/lines", rows);
  }

  public onDeleteGraveInvoiceLine(event: any): void {
    const context = event.getSource().getBindingContext("graveInvoiceDraft") as any;
    const model = context.getModel() as JSONModel;
    const rows = model.getProperty("/lines") as any[];
    // the row's binding path ends in its index, e.g. "/lines/2"
    rows.splice(Number(context.getPath().split("/").pop()), 1);
    model.setProperty("/lines", rows);
  }

  public async onGraveInvoiceDialogSave(): Promise<void> {
    // a second press would revise the group twice; the busy dialog blocks the
    // interface, this guard covers the moment before it is on screen
    if (this._savingGraveInvoice) {
      return;
    }

    const model = this._graveInvoiceDialog.getModel("graveInvoiceDraft") as JSONModel;
    const draft = model.getData() as any;
    const rows = draft.lines as any[];
    if (rows.some((row) => !row.itemId)) {
      await api.showError(this, new Error("ItemRequired"));
      return;
    }

    const lines = rows.map((row) => ({
      itemId: Number(row.itemId),
      count: Number(row.count),
      // a price of 0 is a real value, not a missing one
      unitPrice:
        row.unitPrice == null || row.unitPrice === "" ? null : Number(row.unitPrice),
    }));

    // long-running task: a revision renders both documents for every recipient
    this._savingGraveInvoice = true;
    const busy = new BusyDialog({ title: this.i18nText("invoiceRunning") });
    busy.open();

    let failure: unknown = null;
    try {
      await api.createGraveInvoiceRevision(Number(draft.invoiceId), lines);
      this._graveInvoiceDialog.close();
      await this.refreshAfterSave();
    } catch (error) {
      failure = error;
    } finally {
      // close the busy dialog before any error message opens
      busy.close();
      this._savingGraveInvoice = false;
    }

    if (failure) {
      await api.showError(this, failure);
    }
  }

  public onGraveInvoiceDialogCancel(): void {
    this._graveInvoiceDialog.close();
  }

  private _savingGraveInvoice = false;
  private _graveInvoiceDialog: any;
```

- [ ] **Step 5: Wire it into the grave detail page**

In `frontend/src/controller/GraveDetail.controller.ts`, replace the stub:

```ts
  public async onEditGraveInvoice(event: any): Promise<void> {
    const context = event.getSource().getBindingContext("grave");
    await this.openGraveInvoiceDialog(Number(context.getProperty("Id")));
  }
```

- [ ] **Step 6: Type-check and run the E2E test**

Run: `cd frontend && npm run typecheck`
Expected: no errors.

Run: `cd e2e && npm test -- --project=desktop graves.spec.ts`
Expected: PASS — both rows show the R1 badge and the new total.

- [ ] **Step 7: Commit**

```bash
git add frontend/src/view/fragments/GraveInvoiceDialog.fragment.xml \
        frontend/src/controller/GraveEdit.controller.ts \
        frontend/src/controller/GraveDetail.controller.ts \
        e2e/tests/graves.spec.ts
git commit -m "feat: edit a grave invoice for all of its recipients at once

The dialog edits the grave's bill, not one recipient's share, and says so
by naming the recipients it is about to revise. It opens on what was
invoiced for that half-year rather than on the grave's current items."
```

---

### Task 13: The grave page — colour, a mass download, icon actions

**Files:**

- Modify: `frontend/src/view/Graves.view.xml`
- Modify: `frontend/src/controller/Graves.controller.ts`
- Test: `e2e/tests/graves.spec.ts`

**Interfaces:**

- Consumes: route `graveDetail` (Task 11), `api.bulkDownload` (already present),
  `IsLatestRevision`-filtered bulk download (Task 2).

The mass download covers the **normal** documents of the half-year selected in
the toolbar, across all graves. There is no copy mass download any more; single
copies stay downloadable everywhere.

- [ ] **Step 1: Write the failing E2E test**

Two changes to `e2e/tests/graves.spec.ts`. First, tighten the existing
generation test so the invoiced state is asserted as green, not just as text —
replace

```ts
await expect(gravesTable.getByText("erstellt").first()).toBeVisible();
```

with

```ts
await expect(gravesTable.getByText("erstellt").first()).toBeVisible();
await expect(gravesTable.locator(".sapMObjStatus.sapMObjStatusSuccess").first()).toBeVisible();
```

Then append, reusing `seedGrave` from Task 11:

```ts
test("the grave list colours its status and offers the invoice mass download", async ({ page }) => {
  const marker = `GL-${Date.now()}`;
  await seedGrave(page, marker, [`Liste ${marker}`]);

  await page.goto("/index.html#/graves");
  const gravesTable = page.locator("[id$='gravesTable-listUl']");
  const row = gravesTable.locator("tbody tr", { hasText: marker });
  await expect(row).toBeVisible();

  // created after the generation tests above, so this grave is still open —
  // and an open half-year reads red
  await expect(row.locator(".sapMObjStatus.sapMObjStatusError")).toBeVisible();

  // the details action reaches the grave's own page
  await row.getByRole("button", { name: "Details", exact: true }).click();
  await expect(page.getByText(marker, { exact: true })).toBeVisible();
  await expect(page.getByRole("button", { name: "Grab bearbeiten" })).toBeVisible();

  // the mass download hands back a ZIP of the half-year the toolbar shows
  await page.goto("/index.html#/graves");
  const downloadPromise = page.waitForEvent("download");
  await page.getByRole("button", { name: "Rechnungen (ZIP)" }).click();
  const download = await downloadPromise;
  expect(download.suggestedFilename()).toBe("rechnungen.zip");
});
```

- [ ] **Step 2: Run it to verify it fails**

Run: `cd e2e && npm test -- --project=desktop graves.spec.ts`
Expected: FAIL — no coloured status, no details action, no mass download.

- [ ] **Step 3: Update the view**

In `frontend/src/view/Graves.view.xml`, add the mass download to the toolbar
before "Grab anlegen":

```xml
<Button text="{i18n>bulkGraveInvoices}" press=".onBulkGraveInvoices">
                    <layoutData>
                        <OverflowToolbarLayoutData priority="Low" />
                    </layoutData>
                </Button>
```

Replace the status cell with a coloured `ObjectStatus`, and the edit button with
the two icon actions:

```xml
                                    <ObjectStatus text="{path: 'status>Invoiced', formatter: '.statusFormatter'}"
                                        state="{= ${status>Invoiced} ? 'Success' : 'Error' }"
                                        icon="{= ${status>Invoiced} ? 'sap-icon://accept' : 'sap-icon://error' }" />
                                    <HBox wrap="Wrap">
                                        <Button icon="sap-icon://detail-view" type="Transparent"
                                            tooltip="{i18n>actionDetails}" press=".onShowGraveDetails" />
                                        <Button icon="sap-icon://edit" type="Transparent"
                                            tooltip="{i18n>actionEdit}" press=".onEditGrave" />
                                    </HBox>
```

and widen that last column so both icons stay on one line:

```xml
<Column width="7rem" importance="Low" popinDisplay="WithoutHeader" />
```

- [ ] **Step 4: Update the controller**

In `frontend/src/controller/Graves.controller.ts`, add:

```ts
  public onShowGraveDetails(event: any): void {
    const context = event.getSource().getBindingContext("status");
    this.router().navTo("graveDetail", {
      graveId: String(context.getProperty("Id")),
    });
  }

  /**
   * The normal documents of the half-year the toolbar shows, across all graves.
   * Copies are downloaded one at a time from the detail pages; a bulk of copies
   * is the one thing that could be mistaken for a bulk of originals.
   */
  public async onBulkGraveInvoices(): Promise<void> {
    const [year, half] = (this.byId("halfYearSelect") as Select)
      .getSelectedKey()
      .split("-")
      .map(Number);
    try {
      await api.bulkDownload("Normal", "rechnungen.zip", year, half);
    } catch (error) {
      await api.showError(this, error);
    }
  }
```

- [ ] **Step 5: Type-check and run the E2E test**

Run: `cd frontend && npm run typecheck`
Expected: no errors.

Run: `cd e2e && npm test -- --project=desktop graves.spec.ts`
Expected: PASS.

- [ ] **Step 6: Run the whole suite**

Run: `cd e2e && npm test`
Expected: PASS. `layout.spec.ts` checks that `gravesToolbar` neither clips nor
truncates — the new button carries `priority="Low"`, so a phone moves it into the
overflow menu. If that test fails, the button's label is too long for the bar
rather than the priority being wrong.

- [ ] **Step 7: Commit**

```bash
git add frontend/src/view/Graves.view.xml \
        frontend/src/controller/Graves.controller.ts \
        e2e/tests/graves.spec.ts
git commit -m "feat: colour the half-year status and download a half-year's invoices

Open half-years read red and invoiced ones green, so the list says at a
glance what still needs work. The mass download follows the invoices onto
this page; copies are downloaded one at a time."
```

---

### Task 14: The grave-invoice detail page, and the small-screen sweep

**Files:**

- Create: `frontend/src/view/GraveInvoiceDetail.view.xml`
- Create: `frontend/src/controller/GraveInvoiceDetail.controller.ts`
- Modify: `frontend/src/manifest.json` (route + target)
- Test: `e2e/tests/graves.spec.ts`, `e2e/tests/layout.spec.ts`

**Interfaces:**

- Consumes: `InvoiceRevisions.loadChain` / `buildRevisionModel` (Task 7),
  `GraveEdit.openGraveInvoiceDialog` (Task 12), route target from Task 11's
  `onShowInvoiceDetails`.
- Produces: route `graveInvoiceDetail`, pattern `graves/{graveId}/invoices/{rootId}`.

Same revision history as the invoice detail page, plus what makes it a grave
invoice: the grave, the half-year, the share, and the other recipients.

- [ ] **Step 1: Write the failing E2E tests**

Append to `e2e/tests/graves.spec.ts`, reusing `seedGrave` and `generateFor` from
Task 11:

```ts
test("a grave invoice's detail page shows its revisions and its siblings", async ({ page }) => {
  const marker = `GI-${Date.now()}`;
  const names = [`Anna ${marker}`, `Bernd ${marker}`];
  const { graveId } = await seedGrave(page, marker, names);

  // its own period, so the rows below belong to this test alone
  await generateFor(page, 2037, 2);

  await page.goto(`/index.html#/graves/${graveId}`);
  const invoicesTable = page.locator("[id$='graveInvoicesTable-listUl']");
  await invoicesTable
    .locator("tbody tr", { hasText: names[0] })
    .getByRole("button", { name: "Details", exact: true })
    .click();

  await expect(page.getByText("Original", { exact: true })).toBeVisible();
  await expect(page.getByText(marker, { exact: true })).toBeVisible();
  await expect(page.getByText("H2 2037")).toBeVisible();
  // the other recipient of the same half-year is listed
  await expect(page.getByText(names[1])).toBeVisible();

  // back navigation returns to the grave, not to the home screen
  await page.locator("[id$='graveInvoiceDetailPage'] .sapMBtn").first().click();
  await expect(page.getByRole("button", { name: "Grab bearbeiten" })).toBeVisible();
});
```

Append to `e2e/tests/layout.spec.ts` a sweep over the detail pages, after the
existing `for (const screen of LIST_SCREENS)` block. They are built from panels
rather than a toolbar and a table, so they get their own, narrower check.

The grave-invoice detail page is not in the sweep: reaching it needs a generated
grave invoice, and generation belongs to `graves.spec.ts` alone. It shares its
markup with the invoice detail page, which is swept.

```ts
test("detail pages fit the viewport", async ({ page }) => {
  await seed(page);

  const invoices = await (
    await page.request.get("/odata/Invoices?$filter=GraveId eq null&$select=Id,RootId&$top=1")
  ).json();
  const graves = await (await page.request.get("/odata/Graves?$select=Id&$top=1")).json();

  await page.goto(`/index.html#/invoices/${invoices.value[0].RootId}`);
  await expect(page.locator("[id$='invoiceDetailPage']")).toBeVisible();
  await expectNoSidewaysScrolling(page);

  await page.goto(`/index.html#/graves/${graves.value[0].Id}`);
  await expect(page.locator("[id$='graveDetailPage']")).toBeVisible();
  await expectNoSidewaysScrolling(page);
});
```

- [ ] **Step 2: Run them to verify they fail**

Run: `cd e2e && npm test -- --project=desktop graves.spec.ts`
Expected: FAIL — the details button navigates to a route that does not exist.

- [ ] **Step 3: Add the route and target**

In `frontend/src/manifest.json`, add to `routes`:

```json
        {
          "name": "graveInvoiceDetail",
          "pattern": "graves/{graveId}/invoices/{rootId}",
          "target": [
            "graveInvoiceDetail"
          ]
        },
```

and to `targets`:

```json
        "graveInvoiceDetail": {
          "name": "GraveInvoiceDetail",
          "level": 2,
          "type": "View"
        },
```

- [ ] **Step 4: Write the view**

Create `frontend/src/view/GraveInvoiceDetail.view.xml`:

```xml
<mvc:View
  xmlns:mvc="sap.ui.core.mvc"
  xmlns="sap.m"
  controllerName="burkard.invoicing.controller.GraveInvoiceDetail"
  displayBlock="true"
>
    <Page
    id="graveInvoiceDetailPage"
    title="{i18n>graveInvoiceDetailTitle}"
    titleLevel="H1"
    showNavButton="true"
    navButtonPress=".onNavBack"
    class="sapUiResponsiveContentPadding"
  >
        <headerContent>
            <Button
        icon="sap-icon://edit"
        tooltip="{i18n>actionEdit}"
        press=".onEditGraveInvoice"
      />
        </headerContent>
        <content>
            <VBox class="sapUiSmallMarginTop">
                <Title text="{detail>/invoiceNumber}" level="H2" />
                <Text text="{detail>/recipientName}" class="sapUiTinyMarginTop" />
                <!-- the address is stored with line breaks, as it is printed -->
                <Text text="{detail>/recipientAddress}" renderWhitespace="true" />
                <HBox wrap="Wrap" class="sapUiSmallMarginTop">
                    <ObjectStatus
            title="{i18n>graveNumber}"
            class="sapUiSmallMarginEnd"
            text="{detail>/graveNumber}"
          />
                    <ObjectStatus
            title="{i18n>halfYear}"
            class="sapUiSmallMarginEnd"
            text="{detail>/halfYear}"
          />
                    <ObjectStatus title="{i18n>sharePercent}" text="{detail>/shareText}" />
                </HBox>
            </VBox>

            <Panel
        headerText="{i18n>otherRecipients}"
        class="sapUiSmallMarginTop"
        visible="{= ${detail>/others}.length > 0 }"
      >
                <List items="{detail>/others}" noDataText="{i18n>noData}">
                    <StandardListItem
            title="{detail>Name}"
            type="Navigation"
            press=".onOpenSibling"
          />
                </List>
            </Panel>

            <!-- one panel per revision, newest first; the history stays collapsed -->
            <VBox items="{detail>/revisions}">
                <Panel
          headerText="{detail>Title}"
          expandable="true"
          expanded="{detail>Expanded}"
          class="sapUiSmallMarginTop"
        >
                    <content>
                        <HBox wrap="Wrap" alignItems="Center" class="sapUiSmallMarginBottom">
                            <Text text="{detail>InvoiceNumber}" class="sapUiSmallMarginEnd" />
                            <Text
                text="{path: 'detail>GeneratedAt', formatter: '.formatDate'}"
                class="sapUiSmallMarginEnd"
              />
                            <Button
                icon="sap-icon://pdf-attachment"
                type="Transparent"
                tooltip="{i18n>actionDownloadNormal}"
                press=".onDownloadRevisionNormal"
              />
                            <Button
                icon="sap-icon://copy"
                type="Transparent"
                tooltip="{i18n>actionDownloadCopy}"
                press=".onDownloadRevisionCopy"
              />
                        </HBox>
                        <Table items="{detail>Lines}" autoPopinMode="true" popinLayout="Block">
                            <columns>
                                <Column importance="High"><Text text="{i18n>itemNumber}" /></Column>
                                <Column importance="Medium" popinDisplay="Inline"><Text
                    text="{i18n>itemDescription}"
                  /></Column>
                                <Column importance="High" popinDisplay="Inline"><Text
                    text="{i18n>itemCount}"
                  /></Column>
                                <Column importance="High" popinDisplay="Inline"><Text
                    text="{i18n>itemPrice}"
                  /></Column>
                                <Column importance="Low" popinDisplay="Inline"><Text
                    text="{i18n>vatRate}"
                  /></Column>
                                <Column importance="Medium" popinDisplay="Inline"><Text
                    text="{i18n>lineTotal}"
                  /></Column>
                            </columns>
                            <items>
                                <ColumnListItem>
                                    <cells>
                                        <Text text="{detail>ItemNumber}" />
                                        <Text text="{detail>Description}" wrapping="true" />
                                        <Text text="{detail>Count}" />
                                        <Text
                      text="{path: 'detail>UnitPrice', formatter: '.formatAmount'}"
                    />
                                        <Text text="{detail>VatRate}" />
                                        <Text
                      text="{path: 'detail>LineTotal', formatter: '.formatAmount'}"
                    />
                                    </cells>
                                </ColumnListItem>
                            </items>
                        </Table>
                        <HBox wrap="Wrap" justifyContent="End" class="sapUiSmallMarginTop">
                            <ObjectStatus
                title="{i18n>netTotal}"
                class="sapUiSmallMarginEnd"
                text="{path: 'detail>NetTotal', formatter: '.formatAmount'}"
              />
                            <ObjectStatus
                title="{i18n>vatTotal}"
                class="sapUiSmallMarginEnd"
                text="{path: 'detail>VatTotal', formatter: '.formatAmount'}"
              />
                            <ObjectStatus
                title="{i18n>grossTotal}"
                state="Information"
                text="{path: 'detail>GrossTotal', formatter: '.formatAmount'}"
              />
                        </HBox>
                    </content>
                </Panel>
            </VBox>
        </content>
    </Page>
</mvc:View>
```

- [ ] **Step 5: Write the controller**

Create `frontend/src/controller/GraveInvoiceDetail.controller.ts`:

```ts
import JSONModel from "sap/ui/model/json/JSONModel";
import * as api from "../model/api";
import GraveEdit from "./GraveEdit.controller";

/** Every revision of one grave invoice, plus what makes it one: the grave, the
 *  half-year, the share, and the other recipients billed for the same period. */
export default class GraveInvoiceDetail extends GraveEdit {
  public onInit(): void {
    this.bindODataRoot();
    this.router().getRoute("graveInvoiceDetail")!.attachPatternMatched(this.onRouteMatched, this);
  }

  private async onRouteMatched(event: any): Promise<void> {
    const args = event.getParameter("arguments");
    this._graveId = Number(args.graveId);
    this._rootId = Number(args.rootId);
    await this.refresh();
  }

  private async refresh(): Promise<void> {
    try {
      const chain = await this.loadChain(this._rootId);
      if (!chain.length) {
        this.getView()!.setModel(new JSONModel({ revisions: [], others: [] }), "detail");
        return;
      }

      const model = this.buildRevisionModel(chain);
      const latestInvoiceId = model.getProperty("/latestInvoiceId") as number;
      const group = await api.fetchGraveInvoiceGroup(latestInvoiceId);
      const share = model.getProperty("/sharePercent") as number | null;

      model.setProperty("/graveNumber", group.GraveNumber);
      model.setProperty(
        "/shareText",
        share != null ? `${share.toFixed(1).replace(".", ",")} %` : "",
      );
      // the siblings of this half-year, minus the invoice being shown
      model.setProperty(
        "/others",
        group.Recipients.filter((r) => r.InvoiceId !== latestInvoiceId),
      );
      this.getView()!.setModel(model, "detail");
    } catch (error) {
      await api.showError(this, error);
    }
  }

  /** Back to the grave this invoice belongs to, not to the home screen. */
  public onNavBack(): void {
    this.router().navTo("graveDetail", { graveId: String(this._graveId) });
  }

  public onOpenSibling(event: any): void {
    const context = event.getSource().getBindingContext("detail");
    this.router().navTo("graveInvoiceDetail", {
      graveId: String(this._graveId),
      rootId: String(context.getProperty("InvoiceId")),
    });
  }

  public async onEditGraveInvoice(): Promise<void> {
    const invoiceId = (this.getView()!.getModel("detail") as JSONModel).getProperty(
      "/latestInvoiceId",
    ) as number | null;
    if (invoiceId == null) {
      return;
    }
    await this.openGraveInvoiceDialog(invoiceId);
  }

  /** Both dialogs answer here; only the grave-invoice one is reachable from
   *  this page, but the hook is the same. */
  protected async refreshAfterSave(): Promise<void> {
    await this.refresh();
  }

  public async onDownloadRevisionNormal(event: any): Promise<void> {
    await this.downloadRevision(event, false);
  }

  public async onDownloadRevisionCopy(event: any): Promise<void> {
    await this.downloadRevision(event, true);
  }

  private async downloadRevision(event: any, isCopy: boolean): Promise<void> {
    const context = event.getSource().getBindingContext("detail");
    await this.openDocument(Number(context.getProperty("Id")), isCopy);
  }

  private _graveId = 0;
  private _rootId = 0;
}
```

A sibling's `InvoiceId` is that recipient's **latest** invoice id, which
`loadChain` resolves to its chain through its fallback read — so navigating to a
sibling works without the grave detail page's `RootId`.

- [ ] **Step 6: Type-check and run the tests**

Run: `cd frontend && npm run typecheck`
Expected: no errors.

Run: `cd e2e && npm test`
Expected: PASS — the whole suite, including the phone and tablet projects.

- [ ] **Step 7: Commit**

```bash
git add frontend/src/view/GraveInvoiceDetail.view.xml \
        frontend/src/controller/GraveInvoiceDetail.controller.ts \
        frontend/src/manifest.json \
        e2e/tests/graves.spec.ts \
        e2e/tests/layout.spec.ts
git commit -m "feat: a detail page for a grave invoice and its revisions

Shows what makes it a grave invoice — the grave, the half-year, the share
and the other recipients billed for the same period — around the same
revision history the plain invoice detail page shows."
```

---

### Task 15: Bring the specification up to date

**Files:**

- Modify: `docs/spec.md`
- Modify: `docs/deviations.md`

The constitution requires deviations to be recorded and reviewed (Governance), and
this work reverses several requirements the spec still states. Leaving them would
make the spec describe an application that no longer exists.

- [ ] **Step 1: Rewrite the affected user stories**

In `docs/spec.md`:

**US8** ("Download Normal Invoices and Copies Separately") — drop the copy bulk
download. Keep scenarios 1 and 3; replace scenario 2 with: _Given generated
invoices with copies, When the user downloads a copy, Then only that copy is
downloaded; there is no bulk download for copies._

**US9** ("Browse, Filter, and Bulk Download") — rewrite around the new shape: the
invoice overview lists invoices that are not tied to a grave, one row per invoice
chain, searchable by invoice number, recipient, date and total; grave invoices are
browsed through their grave; the bulk download of normal invoices lives on the
grave overview and covers the selected half-year.

- [ ] **Step 2: Rewrite the affected requirements**

- **FR-045**: bulk download covers the normal documents of the grave invoices of
  a selected half-year.
- **FR-046**: unchanged — a bulk download never contains copies.
- **FR-047**: replaced — copies are downloaded individually from the invoice and
  grave detail pages; there is no bulk download for copies.
- **FR-048**: unchanged.
- **FR-020b** (new, after FR-020): a grave's set of recipients is fixed when the
  grave is created and MUST NOT change afterwards.
- **FR-034a** (new): editing a grave invoice MUST create a new revision for every
  recipient of that grave and half-year, splitting the edited amounts the same way
  generation splits them.
- **FR-034b** (new): the edit MUST start from the amounts as they were invoiced for
  that half-year, not from the grave's current configuration.
- **FR-065** (new): a bulk download MUST contain only the latest revision of each
  invoice.
- **FR-066** (new): invoice lists MUST show the latest revision of each invoice;
  earlier revisions MUST remain visible on the invoice's detail page.
- **FR-067** (new): the system MUST provide detail pages for an invoice, a grave,
  and a grave invoice, each showing every revision of the invoices it covers.
- **FR-068** (new): users MUST be able to search invoices by invoice number,
  recipient name, recipient address, invoice date and invoice total.

- [ ] **Step 3: Rewrite the affected success criteria**

- **SC-006**: a bulk download of N normal grave invoices for a half-year yields
  exactly N documents, never a copy and never a superseded revision.
- **SC-012**: replaced — every copy is downloadable individually from the detail
  pages of the invoice it belongs to.

Add a US5 acceptance scenario: _Given a grave with recipients, When the user
tries to change its recipients, Then the change is rejected and the grave keeps
the recipients it was created with._

- [ ] **Step 4: Record the new deviation**

In `docs/deviations.md`, D-001, add `GraveInvoiceGroup` to the list of plain MVC
routes and extend the "Why" with one sentence: _`GraveInvoiceGroup` reports the
grave-level amounts behind a group of invoices, which is a report about several
entities rather than any one of them._

- [ ] **Step 5: Check the spec against what was built**

Read `docs/spec.md` end to end and confirm nothing else still describes the old
invoice overview — in particular the Assumptions section's line "Normal invoices
and copies are always downloaded via separate actions; no download mixes the two
kinds", which stays true and needs no change.

- [ ] **Step 6: Commit**

```bash
git add docs/spec.md docs/deviations.md
git commit -m "docs: bring the specification in line with the detail pages

The copy bulk download and the half-year browsing on the invoice page are
gone, grave invoicing moved behind the grave pages, and a grave's
recipients are now fixed at creation."
```

---

## Done

At this point: the backend suite and `cd e2e && npm test` both pass, the
invoice page lists searchable non-grave invoices with four row actions, the grave
pages carry colour-coded status, a half-year mass download and the full invoicing
history, and editing a grave invoice revises every recipient of its half-year.

Use `superpowers:finishing-a-development-branch` to decide how this branch gets
integrated.
