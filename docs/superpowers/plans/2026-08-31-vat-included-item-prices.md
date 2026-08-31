# VAT-Included Item Prices Implementation Plan

> **Superseded.** This plan describes the design in which `PriceIncludesVat`
> lived on the catalogue item, `InvoiceItem`. That design was replaced by
> `docs/superpowers/plans/2026-08-31-vat-included-flag-on-the-line.md`, which
> moves the flag onto `InvoiceLine` and `GraveItem`; the reasoning for the move
> is in the spec's "Revision" section
> (`docs/superpowers/specs/2026-08-31-vat-included-item-prices-design.md`). Kept
> here as a historical record — the body below is unchanged and describes the
> superseded design.

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let an invoice item be marked as priced VAT-included, so a price
entered for it is the gross the customer pays and the line is billed at that
price minus its VAT — on ad-hoc invoices, on graves, and across the split.

**Architecture:** One boolean column on `InvoiceItem`. A pure domain helper turns
an entered price into the net a line is billed at, and it is called at the three
places where a price becomes a line. `GraveInvoiceSplitter`,
`DocumentDataBuilder` and the PDF are untouched — the split keeps dividing a
net. The frontend shows a gross in every price field of a flagged item, which is
what makes an untouched revision re-save the same net instead of subtracting the
VAT twice.

**Tech Stack:** .NET 10 / EF Core / SQLite (backend, run through
`scripts/dotnet.sh` and `scripts/ef.sh`), OpenUI5 1.151 with TypeScript
(frontend), Playwright (E2E), xUnit (backend tests).

**Spec:** `docs/superpowers/specs/2026-08-31-vat-included-item-prices-design.md`

## Global Constraints

- **One migration for this feature** (constitution Principle IV: at most one
  migration per version).
- **English source, identifiers, comments and commit messages**; every
  user-visible string goes through i18n (Principle VII).
- **Conventional Commits** — the release version is derived from them. Every
  task below commits with `feat(...)` or `test(...)` as given.
- **Backend tests:** `./scripts/dotnet.sh test Invoicing.sln`, narrowed with
  `--filter FullyQualifiedName~ClassName`. Run from the repository root.
- **Frontend checks:** all three must be clean —
  `cd frontend && npm run typecheck && npm run lint && npm run ui5lint`.
- **E2E:** `cd e2e && npx playwright test tests/<file> --project=desktop`. It
  starts its own stack from `docker-compose.e2e.yml` on port 8081. **Never** set
  `E2E_BASE_URL` and never point the suite at the dev stack on 8080.
- **A dialog takes focus ~230 ms after `open()`** — an E2E spec that opens one
  calls `waitForDialogOpen(page)` from `e2e/dialog.ts` before touching it.
- **Formatting is automatic**: CSharpier owns `backend/`, Prettier owns
  everything else, both run from the `pre-commit` hook. Do not hand-format.
- **Rounding, verbatim from the spec:**
  `ToNet(gross, rate) = round(round(gross, 2) / (1 + rate/100), 3, AwayFromZero)`
  and `ToGross(net, rate) = round(net * (1 + rate/100), 2, AwayFromZero)`.
- **Naming used across tasks:** the column is `PriceIncludesVat`, the domain
  helper is `VatInclusivePrice.NetOf`, the i18n keys are
  `itemPriceIncludesVat` and `priceIncludesVatShort`, the TypeScript helper is
  `api.grossFromNet` and the field helper is `api.priceFieldValue`.

---

### Task 1: The net-price conversion

The pure rule, on its own, before anything calls it.

**Files:**

- Create: `backend/src/Invoicing.Domain/VatInclusivePrice.cs`
- Test: `backend/tests/Invoicing.IntegrationTests/VatInclusivePriceTests.cs`

**Interfaces:**

- Consumes: nothing.
- Produces: `Burkard.Invoicing.Domain.VatInclusivePrice.NetOf(decimal price, decimal vatRate, bool priceIncludesVat) → decimal`.
  Tasks 3, 4 and 5 call exactly this.

- [ ] **Step 1: Write the failing test**

Create `backend/tests/Invoicing.IntegrationTests/VatInclusivePriceTests.cs`.
Pure-logic unit tests live in this project alongside `ShareCalculatorTests`.

```csharp
using Burkard.Invoicing.Domain;
using Xunit;

namespace Burkard.Invoicing.IntegrationTests;

public class VatInclusivePriceTests
{
    [Fact]
    public void An_unflagged_price_is_already_net_and_is_returned_untouched()
    {
        // three decimals survive: a grave split stores prices at that precision
        Assert.Equal(50.425m, VatInclusivePrice.NetOf(50.425m, 19m, false));
    }

    [Theory]
    [InlineData(120.00, 19, 100.840)]
    [InlineData(100.00, 19, 84.034)]
    [InlineData(107.00, 7, 100.000)]
    [InlineData(119.00, 7, 111.215)]
    public void A_flagged_price_gives_up_its_vat(decimal gross, decimal rate, decimal net)
    {
        Assert.Equal(net, VatInclusivePrice.NetOf(gross, rate, true));
    }

    [Fact]
    public void A_rate_of_zero_leaves_the_price_alone()
    {
        Assert.Equal(120.00m, VatInclusivePrice.NetOf(120.00m, 0m, true));
    }

    [Fact]
    public void A_gross_below_the_cent_is_rounded_to_the_cent_before_it_is_divided()
    {
        // a gross is what someone pays, so it is a cent amount; 120.005 is
        // billed as 120.01 rather than as a price that shifts on reopening
        Assert.Equal(
            VatInclusivePrice.NetOf(120.01m, 19m, true),
            VatInclusivePrice.NetOf(120.005m, 19m, true)
        );
    }

    // The round trip the revision dialogs depend on: the dialog shows
    // round(net × (1 + rate/100), 2) and sends it back, and the same net must
    // come out — otherwise every save subtracts the VAT again.
    [Theory]
    [InlineData(0.01, 19)]
    [InlineData(1.00, 19)]
    [InlineData(33.33, 19)]
    [InlineData(120.00, 19)]
    [InlineData(999.99, 19)]
    [InlineData(0.01, 7)]
    [InlineData(33.33, 7)]
    [InlineData(119.00, 7)]
    [InlineData(999.99, 7)]
    public void The_displayed_gross_converts_back_to_the_same_net(decimal gross, decimal rate)
    {
        var net = VatInclusivePrice.NetOf(gross, rate, true);
        var displayed = decimal.Round(net * (1m + rate / 100m), 2, MidpointRounding.AwayFromZero);

        Assert.Equal(gross, displayed);
        Assert.Equal(net, VatInclusivePrice.NetOf(displayed, rate, true));
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `./scripts/dotnet.sh test Invoicing.sln --filter FullyQualifiedName~VatInclusivePriceTests`
Expected: build error — `The name 'VatInclusivePrice' does not exist`.

- [ ] **Step 3: Write the implementation**

Create `backend/src/Invoicing.Domain/VatInclusivePrice.cs`:

```csharp
namespace Burkard.Invoicing.Domain;

/// <summary>The net price a line is billed at, given the price entered for its
/// item. Prices in this application are net, except for an item marked as
/// including VAT: there the entered price is the gross the customer pays, and
/// the VAT comes back out of it here.</summary>
public static class VatInclusivePrice
{
    /// <summary>The net keeps three decimals — the precision
    /// <see cref="GraveInvoiceSplitter"/> already stores and the document already
    /// prints. Two would lose a cent as soon as a line has a count above one:
    /// three at 100,00 € gross bill 299,99 € from a net of 84,03 and 300,00 €
    /// from 84,034.
    ///
    /// The gross is rounded to the cent before it is divided, because a gross is
    /// an amount someone pays. That also makes the conversion reversible: the
    /// rounded net is at most 0,0005 from the exact quotient, so multiplying the
    /// rate back on lands inside the half cent of the entered price for any rate
    /// below 900 %. A revision dialog can therefore show the gross, take it back
    /// untouched, and store the same net — without it, every save would subtract
    /// the VAT a second time.</summary>
    public static decimal NetOf(decimal price, decimal vatRate, bool priceIncludesVat)
    {
        if (!priceIncludesVat)
        {
            return price;
        }

        var gross = decimal.Round(price, 2, MidpointRounding.AwayFromZero);
        return decimal.Round(gross / (1m + vatRate / 100m), 3, MidpointRounding.AwayFromZero);
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `./scripts/dotnet.sh test Invoicing.sln --filter FullyQualifiedName~VatInclusivePriceTests`
Expected: PASS, 16 tests.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Invoicing.Domain/VatInclusivePrice.cs backend/tests/Invoicing.IntegrationTests/VatInclusivePriceTests.cs
git commit -m "feat(domain): take the VAT back out of a VAT-included price"
```

---

### Task 2: The `PriceIncludesVat` column

The flag itself: entity, migration, and the one hand-written projection that has
to carry it.

**Files:**

- Modify: `backend/src/Invoicing.Domain/Entities/InvoiceItem.cs`
- Create: `backend/src/Invoicing.Api/Migrations/<timestamp>_VatIncludedItemPrices.cs` (generated)
- Modify: `backend/src/Invoicing.Api/Migrations/AppDbContextModelSnapshot.cs` (generated)
- Modify: `backend/src/Invoicing.Api/OData/Controllers/GravesController.cs:62`
- Test: `backend/tests/Invoicing.IntegrationTests/InvoiceItemTests.cs`

**Interfaces:**

- Consumes: nothing.
- Produces: `InvoiceItem.PriceIncludesVat` (bool, default `false`), readable and
  writable over OData at `/odata/InvoiceItems`, and present in the grave detail
  projection at `/odata/GraveDetails` under `Items[].Item.PriceIncludesVat`.
  Tasks 3–9 all read it.

- [ ] **Step 1: Write the failing tests**

Append to `backend/tests/Invoicing.IntegrationTests/InvoiceItemTests.cs`, inside
the class:

```csharp
    [Fact]
    public async Task An_item_is_priced_net_unless_it_says_otherwise()
    {
        using var client = _host.CreateClient();
        await SeedVatRateAsync(client);

        var response = await client.PostAsync(
            "/odata/InvoiceItems",
            ODataJson.Body(
                new
                {
                    Number = "1101",
                    Description = "Netto",
                    VatRate = 7.0m,
                }
            )
        );
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        Assert.False(json.RootElement.GetProperty("PriceIncludesVat").GetBoolean());
    }

    [Fact]
    public async Task An_item_can_be_marked_and_unmarked_as_priced_vat_included()
    {
        using var client = _host.CreateClient();
        await SeedVatRateAsync(client);

        var created = await client.PostAsync(
            "/odata/InvoiceItems",
            ODataJson.Body(
                new
                {
                    Number = "1102",
                    Description = "Brutto",
                    VatRate = 7.0m,
                    DefaultPrice = 107.00m,
                    PriceIncludesVat = true,
                }
            )
        );
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var createdJson = await ODataJson.ReadAsync(created);
        var id = createdJson.RootElement.GetProperty("Id").GetInt64();
        Assert.True(createdJson.RootElement.GetProperty("PriceIncludesVat").GetBoolean());

        var patched = await client.PatchAsync(
            $"/odata/InvoiceItems({id})",
            ODataJson.Body(new { PriceIncludesVat = false })
        );
        // Updated() answers 204 or 200 depending on the Prefer header, so the
        // status is only checked for success; the re-read below is the assertion
        Assert.True(patched.IsSuccessStatusCode, $"PATCH returned {patched.StatusCode}");

        var reread = await client.GetAsync($"/odata/InvoiceItems({id})");
        using var rereadJson = await ODataJson.ReadAsync(reread);
        Assert.False(rereadJson.RootElement.GetProperty("PriceIncludesVat").GetBoolean());
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `./scripts/dotnet.sh test Invoicing.sln --filter FullyQualifiedName~InvoiceItemTests`
Expected: FAIL — `PriceIncludesVat` is not a property of the entity, so the
response has no such member.

- [ ] **Step 3: Add the property**

In `backend/src/Invoicing.Domain/Entities/InvoiceItem.cs`, after `VatRate`:

```csharp
    /// <summary>True when prices entered for this item are gross — the amount
    /// the customer pays, VAT included. The line is still billed net: the VAT is
    /// taken back out when the line is built.</summary>
    public bool PriceIncludesVat { get; set; }
```

No controller work is needed: `InvoiceItemsController.Post` binds the whole
entity, `Patch` applies a `Delta<InvoiceItem>`, and
`ODataConventionModelBuilder` exposes every public property.

- [ ] **Step 4: Generate the migration**

Run:

```bash
./scripts/ef.sh migrations add VatIncludedItemPrices -p src/Invoicing.Api -s src/Invoicing.Api
```

Expected: a new pair of files under `backend/src/Invoicing.Api/Migrations/` and
an updated `AppDbContextModelSnapshot.cs`. Open the generated `Up` and confirm it
is exactly one `AddColumn<bool>` on `InvoiceItems` named `PriceIncludesVat`,
`nullable: false`, `defaultValue: false`. If EF generated anything else, the
model has drifted — stop and report it rather than editing the migration.

- [ ] **Step 5: Carry the flag into the grave detail projection**

In `backend/src/Invoicing.Api/OData/Controllers/GravesController.cs`, in the
`Item = new { ... }` projection around line 62, add the property after
`i.Item.VatRate`:

```csharp
                        i.Item.VatRate,
                        i.Item.PriceIncludesVat,
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `./scripts/dotnet.sh test Invoicing.sln`
Expected: PASS — the whole suite, since the migration touches the shared schema.

- [ ] **Step 7: Commit**

```bash
git add backend/src/Invoicing.Domain/Entities/InvoiceItem.cs backend/src/Invoicing.Api/Migrations backend/src/Invoicing.Api/OData/Controllers/GravesController.cs backend/tests/Invoicing.IntegrationTests/InvoiceItemTests.cs
git commit -m "feat(items): mark an item as priced VAT-included"
```

---

### Task 3: Ad-hoc invoices bill the net

**Files:**

- Modify: `backend/src/Invoicing.Api/Services/InvoiceService.cs:186-205`
- Test: `backend/tests/Invoicing.IntegrationTests/VatIncludedInvoiceTests.cs` (create)

**Interfaces:**

- Consumes: `VatInclusivePrice.NetOf` (Task 1), `InvoiceItem.PriceIncludesVat` (Task 2).
- Produces: `/odata/CreateInvoice` and `/odata/CreateRevision` accept a gross
  `unitPrice` for a flagged item and store the net. Task 8's dialog relies on it.

- [ ] **Step 1: Write the failing tests**

Create `backend/tests/Invoicing.IntegrationTests/VatIncludedInvoiceTests.cs`:

```csharp
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Api.Services;
using Burkard.Invoicing.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Burkard.Invoicing.IntegrationTests;

/// <summary>An item whose price includes VAT, on an ordinary invoice: the line
/// is stored net, and the document totals to the price that was entered.</summary>
public class VatIncludedInvoiceTests
{
    private static async Task<(long GrossItemId, long NetItemId, long RecipientId)> SeedAsync(
        ApiTestHost host
    )
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.VatRates.Add(new VatRate { Rate = 19m });
        var gross = new InvoiceItem
        {
            Number = "V-120",
            Description = "Grabpflege inklusive",
            VatRate = 19m,
            DefaultPrice = 120.00m,
            PriceIncludesVat = true,
        };
        var net = new InvoiceItem
        {
            Number = "V-100",
            Description = "Grabpflege netto",
            VatRate = 19m,
            DefaultPrice = 100.00m,
        };
        var recipient = new Recipient
        {
            Number = "E-9001",
            Name = "Familie Brutto",
            Address = "Musterstraße 1\n12345 Musterstadt",
        };
        db.InvoiceItems.AddRange(gross, net);
        db.Recipients.Add(recipient);
        await db.SaveChangesAsync();
        return (gross.Id, net.Id, recipient.Id);
    }

    private static async Task<Invoice> LoadAsync(ApiTestHost host, long id)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Invoices.Include(i => i.Lines).SingleAsync(i => i.Id == id);
    }

    [Fact]
    public async Task The_default_price_is_billed_net_and_totals_to_the_entered_gross()
    {
        using var host = new ApiTestHost();
        var (grossItemId, _, recipientId) = await SeedAsync(host);
        using var client = host.CreateClient();

        var response = await client.PostAsync(
            "/odata/CreateInvoice",
            ODataJson.Body(
                new
                {
                    recipientId,
                    lines = new[]
                    {
                        new
                        {
                            itemId = grossItemId,
                            count = 1,
                            unitPrice = (decimal?)null,
                        },
                    },
                }
            )
        );
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        var invoice = await LoadAsync(host, json.RootElement.GetProperty("Id").GetInt64());

        var line = Assert.Single(invoice.Lines);
        Assert.Equal(100.840m, line.UnitPrice);

        var data = new DocumentDataBuilder().Build(invoice);
        Assert.Equal(120.00m, data.GrossTotal);
        Assert.Equal(100.84m, data.NetTotal);
    }

    [Fact]
    public async Task Only_the_flagged_line_gives_up_its_vat()
    {
        using var host = new ApiTestHost();
        var (grossItemId, netItemId, recipientId) = await SeedAsync(host);
        using var client = host.CreateClient();

        var response = await client.PostAsync(
            "/odata/CreateInvoice",
            ODataJson.Body(
                new
                {
                    recipientId,
                    lines = new[]
                    {
                        new
                        {
                            itemId = grossItemId,
                            count = 1,
                            unitPrice = (decimal?)null,
                        },
                        new
                        {
                            itemId = netItemId,
                            count = 1,
                            unitPrice = (decimal?)null,
                        },
                    },
                }
            )
        );
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        var invoice = await LoadAsync(host, json.RootElement.GetProperty("Id").GetInt64());

        Assert.Equal(100.840m, invoice.Lines.Single(l => l.ItemNumber == "V-120").UnitPrice);
        Assert.Equal(100.00m, invoice.Lines.Single(l => l.ItemNumber == "V-100").UnitPrice);
    }

    [Fact]
    public async Task A_revision_that_returns_the_shown_gross_stores_the_same_net()
    {
        using var host = new ApiTestHost();
        var (grossItemId, _, recipientId) = await SeedAsync(host);
        using var client = host.CreateClient();

        var created = await client.PostAsync(
            "/odata/CreateInvoice",
            ODataJson.Body(
                new
                {
                    recipientId,
                    lines = new[]
                    {
                        new
                        {
                            itemId = grossItemId,
                            count = 1,
                            unitPrice = (decimal?)null,
                        },
                    },
                }
            )
        );
        using var createdJson = await ODataJson.ReadAsync(created);
        var invoiceId = createdJson.RootElement.GetProperty("Id").GetInt64();

        // 120,00 is what the dialog shows for a stored net of 100,840 — saving it
        // back untouched must not take the VAT out a second time
        var revised = await client.PostAsync(
            "/odata/CreateRevision",
            ODataJson.Body(
                new
                {
                    invoiceId,
                    recipientId,
                    lines = new[]
                    {
                        new
                        {
                            itemId = grossItemId,
                            count = 1,
                            unitPrice = 120.00m,
                        },
                    },
                }
            )
        );
        Assert.Equal(HttpStatusCode.Created, revised.StatusCode);
        using var revisedJson = await ODataJson.ReadAsync(revised);
        var revision = await LoadAsync(host, revisedJson.RootElement.GetProperty("Id").GetInt64());

        Assert.Equal(100.840m, Assert.Single(revision.Lines).UnitPrice);
        Assert.Equal(120.00m, new DocumentDataBuilder().Build(revision).GrossTotal);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `./scripts/dotnet.sh test Invoicing.sln --filter FullyQualifiedName~VatIncludedInvoiceTests`
Expected: FAIL — `Assert.Equal() Failure: Expected 100.840, Actual 120.00`.

- [ ] **Step 3: Convert in `BuildLinesAsync`**

In `backend/src/Invoicing.Api/Services/InvoiceService.cs`, inside the `result.Add`
of `BuildLinesAsync`, replace `UnitPrice = unitPrice.Value,` with:

```csharp
                    // an item priced VAT-included is entered as the gross the
                    // customer pays; the line itself is always net
                    UnitPrice = VatInclusivePrice.NetOf(
                        unitPrice.Value,
                        item.VatRate,
                        item.PriceIncludesVat
                    ),
```

`Burkard.Invoicing.Domain` is already imported in this file.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `./scripts/dotnet.sh test Invoicing.sln --filter FullyQualifiedName~VatIncludedInvoiceTests`
Expected: PASS, 3 tests.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Invoicing.Api/Services/InvoiceService.cs backend/tests/Invoicing.IntegrationTests/VatIncludedInvoiceTests.cs
git commit -m "feat(invoices): bill a VAT-included item at its net price"
```

---

### Task 4: Grave generation converts before the split

**Files:**

- Modify: `backend/src/Invoicing.Api/Services/GraveInvoiceGenerator.cs:63-72`
- Test: `backend/tests/Invoicing.IntegrationTests/VatIncludedGraveTests.cs` (create)

**Interfaces:**

- Consumes: `VatInclusivePrice.NetOf` (Task 1), `InvoiceItem.PriceIncludesVat` (Task 2).
- Produces: generated grave invoices whose lines and `GraveUnitPrice` are net.
  Task 5 revises them and Task 9's dialog displays them.

- [ ] **Step 1: Write the failing test**

Create `backend/tests/Invoicing.IntegrationTests/VatIncludedGraveTests.cs`:

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

/// <summary>A grave whose item is priced VAT-included. The split divides a net —
/// that is what lets each recipient's document work out its own VAT and add up —
/// so the entered gross has to give up its VAT before the split, not after.
///
/// 120,00 € gross at 19 % is a net of 100,840, or 10 084 cents. Split across two
/// recipients that is 5 042 cents each, a unit price of 50,420, and a document
/// total of 50,42 € + 9,58 € = 60,00 € — twice 60,00 € being the 120,00 € that
/// was entered.</summary>
public class VatIncludedGraveTests
{
    private const decimal EnteredGross = 120.00m;
    private const decimal ExpectedGraveNet = 100.840m;
    private const decimal ExpectedShareNet = 50.420m;
    private const decimal ExpectedShareGross = 60.00m;

    private static async Task<long> SeedAsync(ApiTestHost host)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.VatRates.Add(new VatRate { Rate = 19m });
        var item = new InvoiceItem
        {
            Number = "G-120",
            Description = "Grabpflege inklusive",
            VatRate = 19m,
            DefaultPrice = EnteredGross,
            PriceIncludesVat = true,
        };
        db.InvoiceItems.Add(item);
        for (var i = 1; i <= 2; i++)
        {
            db.Recipients.Add(
                new Recipient
                {
                    Number = $"E-70{i:D2}",
                    Name = $"Empfänger {i}",
                    Address = $"Weg {i}",
                }
            );
        }

        var grave = new Grave
        {
            GraveNumber = "V-1",
            StartDate = new DateOnly(2020, 1, 1),
            EndDate = new DateOnly(2040, 12, 31),
            CreatedAt = DateOnly.FromDateTime(DateTime.UtcNow),
        };
        db.Graves.Add(grave);
        await db.SaveChangesAsync();

        db.GraveItems.Add(
            new GraveItem
            {
                GraveId = grave.Id,
                ItemId = item.Id,
                Count = 1,
            }
        );
        foreach (var recipient in await db.Recipients.ToListAsync())
        {
            db.GraveRecipients.Add(
                new GraveRecipient { GraveId = grave.Id, RecipientId = recipient.Id }
            );
        }

        await db.SaveChangesAsync();
        return grave.Id;
    }

    [Fact]
    public async Task The_split_divides_the_net_and_the_shares_add_up_to_the_entered_gross()
    {
        using var host = new ApiTestHost();
        await SeedAsync(host);
        using var client = host.CreateClient();

        await GraveGenerationHelper.RunAsync(client, 2026, 1);

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var invoices = await db.Invoices.Include(i => i.Lines).ToListAsync();

        Assert.Equal(2, invoices.Count);
        var builder = new DocumentDataBuilder();
        foreach (var invoice in invoices)
        {
            var line = Assert.Single(invoice.Lines);
            Assert.Equal(ExpectedShareNet, line.UnitPrice);
            // the grave's own price, before the split, is a net too
            Assert.Equal(ExpectedGraveNet, line.GraveUnitPrice);
            Assert.Equal(ExpectedShareGross, builder.Build(invoice).GrossTotal);
        }

        Assert.Equal(EnteredGross, invoices.Sum(i => builder.Build(i).GrossTotal));
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `./scripts/dotnet.sh test Invoicing.sln --filter FullyQualifiedName~VatIncludedGraveTests`
Expected: FAIL — the unit price is 60.000, the grave price 120.00, and the total
142,80 € rather than 120,00 €.

- [ ] **Step 3: Convert before the `GraveLine` is built**

In `backend/src/Invoicing.Api/Services/GraveInvoiceGenerator.cs`, replace the
`graveLines` projection with:

```csharp
        var graveLines = graveItems
            .Select(item => new GraveLine(
                item.Item.Id,
                item.Item.Number,
                item.Item.Description,
                item.Count,
                // the splitter divides a net — that is what makes every
                // recipient's document add up on its own — so a VAT-included
                // price gives up its VAT here, before the split rather than after
                VatInclusivePrice.NetOf(
                    EffectivePrice(item)!.Value,
                    item.Item.VatRate,
                    item.Item.PriceIncludesVat
                ),
                item.Item.VatRate
            ))
            .ToList();
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `./scripts/dotnet.sh test Invoicing.sln --filter FullyQualifiedName~VatIncludedGraveTests`
Expected: PASS, 1 test.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Invoicing.Api/Services/GraveInvoiceGenerator.cs backend/tests/Invoicing.IntegrationTests/VatIncludedGraveTests.cs
git commit -m "feat(graves): split a VAT-included price after taking its VAT out"
```

---

### Task 5: Grave invoice revisions convert too

**Files:**

- Modify: `backend/src/Invoicing.Api/Services/GraveInvoiceRevisionService.cs:160-180`
- Test: `backend/tests/Invoicing.IntegrationTests/VatIncludedGraveTests.cs` (extend)

**Interfaces:**

- Consumes: `VatInclusivePrice.NetOf` (Task 1), the generation of Task 4.
- Produces: `/odata/CreateGraveInvoiceRevision` accepts the grave's gross for a
  flagged item. Task 9's dialog relies on it.

- [ ] **Step 1: Write the failing test**

Append to the `VatIncludedGraveTests` class:

```csharp
    [Fact]
    public async Task A_revision_that_returns_the_shown_gross_reproduces_the_same_shares()
    {
        using var host = new ApiTestHost();
        await SeedAsync(host);
        using var client = host.CreateClient();
        await GraveGenerationHelper.RunAsync(client, 2026, 1);

        long invoiceId;
        long itemId;
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            invoiceId = await db.Invoices.OrderBy(i => i.Id).Select(i => i.Id).FirstAsync();
            itemId = await db.InvoiceItems.Select(i => i.Id).FirstAsync();
        }

        // the dialog opens on GraveUnitPrice (100,840) and shows it as 120,00;
        // sending that back untouched must reproduce the same split
        var response = await client.PostAsync(
            "/odata/CreateGraveInvoiceRevision",
            ODataJson.Body(
                new
                {
                    invoiceId,
                    lines = new[]
                    {
                        new
                        {
                            itemId,
                            count = 1,
                            unitPrice = EnteredGross,
                        },
                    },
                }
            )
        );
        // the action answers with the revisions themselves, so 200 rather than 201
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var verifyScope = host.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var revisions = await verifyDb
            .Invoices.Include(i => i.Lines)
            .Where(i => i.RevisionNumber == 1)
            .ToListAsync();

        Assert.Equal(2, revisions.Count);
        var builder = new DocumentDataBuilder();
        foreach (var revision in revisions)
        {
            Assert.Equal(ExpectedShareNet, Assert.Single(revision.Lines).UnitPrice);
            Assert.Equal(ExpectedShareGross, builder.Build(revision).GrossTotal);
        }
    }
```

Add `using System.Net;` to the file's usings for `HttpStatusCode`.

- [ ] **Step 2: Run the test to verify it fails**

Run: `./scripts/dotnet.sh test Invoicing.sln --filter FullyQualifiedName~VatIncludedGraveTests`
Expected: FAIL — the revised share is 60.000 instead of 50.420.

- [ ] **Step 3: Convert in `BuildGraveLinesAsync`**

In `backend/src/Invoicing.Api/Services/GraveInvoiceRevisionService.cs`, in the
`lines.Add(new GraveLine(...))` call, replace the `unitPrice` argument with:

```csharp
                    // the dialog sends the grave's price as it shows it, which
                    // for a VAT-included item is the gross; the split needs the net
                    VatInclusivePrice.NetOf(unitPrice, item.VatRate, item.PriceIncludesVat),
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `./scripts/dotnet.sh test Invoicing.sln`
Expected: PASS — the full suite, since this touches the shared revision path.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Invoicing.Api/Services/GraveInvoiceRevisionService.cs backend/tests/Invoicing.IntegrationTests/VatIncludedGraveTests.cs
git commit -m "feat(graves): keep VAT-included prices stable across revisions"
```

---

### Task 6: The import archive carries the flag

**Files:**

- Modify: `backend/src/Invoicing.Api/Services/Import/ImportArchiveLayout.cs:18-24`
- Modify: `backend/src/Invoicing.Api/Services/Import/ImportModel.cs:18-24`
- Modify: `backend/src/Invoicing.Api/Services/Import/ImportValidator.cs:189-268`
- Modify: `backend/src/Invoicing.Api/Services/Import/ImportService.cs:71-80`
- Test: `backend/tests/Invoicing.IntegrationTests/ImportTests.cs`
- Modify: `e2e/tests/import-export.spec.ts:9,27,70`

**Interfaces:**

- Consumes: `InvoiceItem.PriceIncludesVat` (Task 2).
- Produces: `items.csv` header
  `ItemNumber,Description,VatRate,Price,PriceIncludesVat`; an empty cell means
  false. Nothing later depends on it.

- [ ] **Step 1: Write the failing tests**

In `backend/tests/Invoicing.IntegrationTests/ImportTests.cs`, change the header
constant and every `items.csv` data row to carry the new column:

```csharp
    private const string ItemsHeader = "ItemNumber,Description,VatRate,Price,PriceIncludesVat\r\n";
```

Then update the five data rows, in order of appearance:

- line ~67: `ItemsHeader + "9001,Grabpflege,7,120.00,\r\n" + "9002,Sonderleistung,7,,true\r\n"`
- line ~270: `ItemsHeader + "9001,Grabpflege,abc,10,\r\n"`
- line ~304: `ItemsHeader + "9001,Grabpflege,7,120,\r\n"`
- line ~353: `ItemsHeader + "9001,Grabpflege,7,120,\r\n"`

(The three `("items.csv", ItemsHeader)` occurrences with no data row need no
change.)

Add two tests to the class:

```csharp
    [Fact]
    public async Task An_item_row_carries_its_vat_included_flag()
    {
        using var host = new ApiTestHost();
        await ConfigureVatAsync(host, 7m);
        using var client = host.CreateClient();

        var response = await client.PostAsync("/odata/ImportArchive", Upload(ValidArchive()));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.InvoiceItems.Where(i => i.Number == "9001").Select(i => i.PriceIncludesVat).SingleAsync());
        Assert.True(await db.InvoiceItems.Where(i => i.Number == "9002").Select(i => i.PriceIncludesVat).SingleAsync());
    }

    [Fact]
    public async Task An_unreadable_vat_included_cell_is_reported()
    {
        using var host = new ApiTestHost();
        await ConfigureVatAsync(host, 7m);
        using var client = host.CreateClient();

        var archive = Zip(
            ("recipients.csv", RecipientsHeader),
            ("items.csv", ItemsHeader + "9001,Grabpflege,7,120.00,vielleicht\r\n"),
            ("graves.csv", GravesHeader),
            ("mapping.csv", MappingHeader)
        );
        var response = await client.PostAsync("/odata/ImportArchive", Upload(archive));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problems = await ProblemsAsync(response);
        Assert.Contains(
            problems,
            p => p.File == "items.csv" && p.Row == 2 && p.Code == "PriceIncludesVatInvalid"
        );
    }
```

`ProblemsAsync` and `AssertNothingImportedAsync` are the file's own helpers, used
by every other rejection test in it.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `./scripts/dotnet.sh test Invoicing.sln --filter FullyQualifiedName~ImportTests`
Expected: FAIL — every archive is now rejected with `HeaderInvalid`, because the
layout still declares four columns.

- [ ] **Step 3: Widen the layout and the row**

In `ImportArchiveLayout.cs`:

```csharp
    public static readonly string[] ItemsHeader =
    {
        "ItemNumber",
        "Description",
        "VatRate",
        "Price",
        "PriceIncludesVat",
    };
```

In `ImportModel.cs`:

```csharp
public sealed record ItemRow(
    int Row,
    string Number,
    string Description,
    decimal VatRate,
    decimal? Price,
    bool PriceIncludesVat
);
```

- [ ] **Step 4: Parse the column**

In `ImportValidator.ReadItems`, change the column guard and read the new cell:

```csharp
            if (!HasColumns(row, 5, file, line, problems))
                continue;

            var number = row[0].Trim();
            var description = row[1].Trim();
            var rawVat = row[2].Trim();
            var rawPrice = row[3].Trim();
            var rawIncludesVat = row[4].Trim();
```

After the price block and before the duplicate-number check, add:

```csharp
            if (!TryParseBool(rawIncludesVat, out var priceIncludesVat))
            {
                problems.Add(
                    new ImportProblem(
                        file,
                        line,
                        "PriceIncludesVatInvalid",
                        $"'{rawIncludesVat}' is not a valid yes/no value."
                    )
                );
                continue;
            }
```

Change the row that is added:

```csharp
            data.Items.Add(
                new ItemRow(
                    line,
                    number,
                    description,
                    VatRateNormalizer.Normalize(vatRate),
                    price,
                    priceIncludesVat
                )
            );
```

And add the parser next to `TryParseDecimal` at the bottom of the class:

```csharp
    // Templates are filled in by hand, in German and in English, so both
    // spellings have to be understood. An empty cell is the common case and
    // means net — what every item was before this column existed.
    private static bool TryParseBool(string raw, out bool value)
    {
        switch (raw.ToLowerInvariant())
        {
            case "":
            case "false":
            case "0":
            case "nein":
            case "no":
                value = false;
                return true;
            case "true":
            case "1":
            case "ja":
            case "yes":
                value = true;
                return true;
            default:
                value = false;
                return false;
        }
    }
```

- [ ] **Step 5: Carry it into the item**

In `ImportService.ImportAsync`, in the `data.Items.ToDictionary` projection, add
after `DefaultPrice = row.Price,`:

```csharp
                PriceIncludesVat = row.PriceIncludesVat,
```

- [ ] **Step 6: Update the E2E archives**

In `e2e/tests/import-export.spec.ts`:

- line 9: `const ITEMS_HEADER = "ItemNumber,Description,VatRate,Price,PriceIncludesVat\r\n";`
- line 27: `` ITEMS_HEADER + `IMP-I1-${suffix},Grabpflege importiert,7,120.00,\r\n` ``
- line 70: `` ITEMS_HEADER + `BAD-I-${RUN},Grabpflege,7,120,\r\n` ``

- [ ] **Step 7: Run the tests to verify they pass**

Run: `./scripts/dotnet.sh test Invoicing.sln --filter FullyQualifiedName~ImportTests`
Expected: PASS — including the template test, which compares the downloaded
template against the same layout constant.

- [ ] **Step 8: Commit**

```bash
git add backend/src/Invoicing.Api/Services/Import backend/tests/Invoicing.IntegrationTests/ImportTests.cs e2e/tests/import-export.spec.ts
git commit -m "feat(import): carry the VAT-included flag in items.csv"
```

---

### Task 7: The item dialog and the item list

The first frontend task, and the one that introduces the shared i18n keys.

**Files:**

- Modify: `frontend/src/model/state.ts` (`ItemDraft`)
- Modify: `frontend/src/model/api.ts` (`ItemRow`, `createItem`, `updateItem`)
- Modify: `frontend/src/view/fragments/ItemDialog.fragment.xml`
- Modify: `frontend/src/controller/Items.controller.ts`
- Modify: `frontend/src/view/Items.view.xml`
- Modify: `frontend/src/i18n/i18n.properties`, `frontend/src/i18n/i18n_en.properties`
- Test: `e2e/tests/vat-included.spec.ts` (create)

**Interfaces:**

- Consumes: `PriceIncludesVat` on `/odata/InvoiceItems` (Task 2).
- Produces: `api.ItemRow.PriceIncludesVat: boolean`, the i18n keys
  `itemPriceIncludesVat` and `priceIncludesVatShort`. Tasks 8 and 9 use both.

- [ ] **Step 1: Write the failing E2E test**

Install the E2E dependencies first (this worktree has none):

```bash
cd e2e && npm install
```

Create `e2e/tests/vat-included.spec.ts`:

```ts
import { expect, test } from "@playwright/test";
import { waitForDialogOpen } from "../dialog";
import { createRow, ensureRow } from "../seed";

// Unique per run: the specs share one stack and write into it in parallel.
const RUN = Date.now();

test("an item can be marked as priced VAT-included and stays marked", async ({ page }) => {
  await ensureRow(page, "/odata/VatRates", { Rate: 19 });
  const number = `VI-${RUN}`;

  await page.goto("/index.html#/items");
  await page.getByRole("button", { name: "Leistung anlegen" }).click();
  await waitForDialogOpen(page);

  const dialog = page.locator(".sapMDialog");
  // scope to the dialog: the page's search field placeholder also says "Nummer"
  await dialog.getByLabel("Nummer").fill(number);
  await dialog.getByLabel("Bezeichnung").fill("Grabpflege inklusive");
  await dialog.locator(".sapMSlt").click();
  await page.locator("ul.sapMSltList-CTX li.sapMSelectListItem", { hasText: "19" }).click();
  await dialog.getByLabel("Standardpreis").fill("120");
  await dialog.getByText("Preis inkl. MwSt.").click();
  await page.getByRole("button", { name: "Speichern" }).click();

  // the list says which prices are gross, so the price column is not ambiguous
  const itemsTable = page.locator("[id$='itemsTable-listUl']");
  const row = itemsTable.locator("tbody tr", { hasText: number });
  await expect(row).toContainText("inkl. MwSt.");

  // and the dialog reopens on what was saved, price and flag alike
  await row.getByRole("button", { name: "Bearbeiten" }).click();
  await waitForDialogOpen(page);
  await expect(page.locator(".sapMDialog").getByLabel("Standardpreis")).toHaveValue("120");
  await expect(page.locator(".sapMDialog input[type='checkbox']")).toBeChecked();
});
```

- [ ] **Step 2: Run it to verify it fails**

Run: `cd e2e && npx playwright test tests/vat-included.spec.ts --project=desktop`
Expected: FAIL — the dialog has no "Preis inkl. MwSt." control to click.

- [ ] **Step 3: Add the i18n keys**

`frontend/src/i18n/i18n.properties`, after `itemDefaultPrice=Standardpreis`:

```properties
itemPriceIncludesVat=Preis inkl. MwSt.
priceIncludesVatShort=inkl. MwSt.
```

`frontend/src/i18n/i18n_en.properties`, after `itemDefaultPrice=Default price`:

```properties
itemPriceIncludesVat=Price includes VAT
priceIncludesVatShort=incl. VAT
```

- [ ] **Step 4: Widen the draft and the API types**

In `frontend/src/model/state.ts`, in `ItemDraft`, after `defaultPrice`:

```ts
/** True when the price above is the gross the customer pays. Ticking it does
 *  not rewrite the number — it changes what the number means. */
priceIncludesVat: boolean;
```

In `frontend/src/model/api.ts`, add `PriceIncludesVat: boolean;` to the
`ItemRow` interface, and add `PriceIncludesVat?: boolean;` to the field objects
of both `createItem` and `updateItem`.

- [ ] **Step 5: Add the checkbox and the list marker**

In `frontend/src/view/fragments/ItemDialog.fragment.xml`, after the
`itemDefaultPrice` `Input`, inside the same `VBox`:

```xml
<CheckBox
  id="itemPriceIncludesVat"
  text="{i18n>itemPriceIncludesVat}"
  selected="{itemDraft>/priceIncludesVat}"
/>
```

In `frontend/src/view/Items.view.xml`, replace the price cell
`<Text text="{odata>DefaultPrice}" />` with:

```xml
<VBox>
                                <Text text="{odata>DefaultPrice}" />
                                <!-- the column holds a gross for these items, so
                                     it has to say so -->
                                <Text
    text="{i18n>priceIncludesVatShort}"
    visible="{odata>PriceIncludesVat}"
  />
                            </VBox>
```

- [ ] **Step 6: Read and write the flag**

In `frontend/src/controller/Items.controller.ts`, in `openItemDialog`, add to
the editing branch of the draft:

```ts
              priceIncludesVat: context.getProperty("PriceIncludesVat") as boolean,
```

and to the creating branch:

```ts
              priceIncludesVat: false,
```

In `onDialogSave`, add to `fields`:

```ts
      PriceIncludesVat: draft.priceIncludesVat,
```

- [ ] **Step 7: Run the frontend checks**

Run: `cd frontend && npm run typecheck && npm run lint && npm run ui5lint`
Expected: all three clean.

- [ ] **Step 8: Run the E2E test to verify it passes**

Run: `cd e2e && npx playwright test tests/vat-included.spec.ts --project=desktop`
Expected: PASS, 1 test.

- [ ] **Step 9: Commit**

```bash
git add frontend/src e2e/tests/vat-included.spec.ts
git commit -m "feat(items): tick VAT-included in the item dialog"
```

---

### Task 8: The invoice dialog shows a gross

**Files:**

- Modify: `frontend/src/model/state.ts` (`DraftLine`)
- Modify: `frontend/src/model/api.ts` (`grossFromNet`, `priceFieldValue`, `ItemPricing`)
- Modify: `frontend/src/controller/InvoiceRevisions.controller.ts` (shared loader)
- Modify: `frontend/src/controller/InvoiceEdit.controller.ts`
- Modify: `frontend/src/view/fragments/InvoiceDialog.fragment.xml`
- Test: `e2e/tests/vat-included.spec.ts` (extend)

**Interfaces:**

- Consumes: Task 7's `ItemRow.PriceIncludesVat` and `priceIncludesVatShort`;
  Task 3's backend conversion.
- Produces: `api.ItemPricing`, `api.grossFromNet(net, vatRate)`,
  `api.priceFieldValue(net, pricing)`, and
  `InvoiceRevisions.loadItemPricing(): Promise<Map<number, api.ItemPricing>>`.
  Task 9 uses all four.

- [ ] **Step 1: Write the failing E2E test**

Append to `e2e/tests/vat-included.spec.ts`:

```ts
test("an invoice bills a VAT-included item at the price that was entered", async ({ page }) => {
  await ensureRow(page, "/odata/VatRates", { Rate: 19 });
  const item = await createRow(page, "/odata/InvoiceItems", {
    Number: `VII-${RUN}`,
    Description: "Grabpflege inklusive",
    VatRate: 19,
    DefaultPrice: 120,
    PriceIncludesVat: true,
  });
  const recipient = await createRow(page, "/odata/Recipients", {
    Name: `Brutto ${RUN}`,
    Address: "Musterstraße 1\n12345 Musterstadt",
  });
  const created = await createRow(page, "/odata/CreateInvoice", {
    recipientId: recipient.Id,
    lines: [{ itemId: item.Id, count: 1, unitPrice: null }],
  });

  await page.goto(`/index.html#/invoices/${created.RootId ?? created.Id}`);
  // 120,00 gross in, 100,84 net + 19 % = 120,00 out
  await expect(page.getByText("120,00 €").first()).toBeVisible();

  // the edit dialog opens on the gross, not on the 100,84 that is stored, and
  // says so; saving it untouched must not take the VAT out a second time
  await page.getByRole("button", { name: "Bearbeiten" }).first().click();
  await waitForDialogOpen(page);
  const dialog = page.locator("[id$='invoiceDialog']");
  await expect(dialog.getByLabel("Preis")).toHaveValue("120");
  await expect(dialog).toContainText("inkl. MwSt.");

  await dialog.getByRole("button", { name: "Speichern" }).click();
  await expect(page.getByText("Revision 1", { exact: true })).toBeVisible();
  // both the original and the revision total to the entered price
  await expect(page.getByText("120,00 €").first()).toBeVisible();
});
```

- [ ] **Step 2: Run it to verify it fails**

Run: `cd e2e && npx playwright test tests/vat-included.spec.ts --project=desktop`
Expected: FAIL — the price field holds `100.84`, and the dialog says nothing
about VAT.

- [ ] **Step 3: Add the conversion helpers**

In `frontend/src/model/api.ts`, next to `lineTotal` and `invoiceTotals`:

```ts
/** The two fields a price field needs about an item: whether its prices are
 *  gross, and the rate to convert with. Both are read from the item as it is
 *  now, never from a snapshot — the backend converts what the dialog sends with
 *  the item's current values, so a display using older ones would move the price
 *  of a line nobody touched. */
export interface ItemPricing {
  VatRate: number;
  PriceIncludesVat: boolean;
}

/** The gross a price field shows for a stored net. Mirrors the backend's
 *  ToGross: round to the cent, away from zero. */
export function grossFromNet(net: number, vatRate: number): number {
  return Math.round(net * (1 + vatRate / 100) * 100) / 100;
}

/** What a price field shows for a line's stored net price: a gross for an item
 *  priced VAT-included, and the untouched net for every other one — including
 *  the three decimals a grave split leaves behind. */
export function priceFieldValue(net: number, pricing: ItemPricing | undefined): number {
  return pricing?.PriceIncludesVat ? grossFromNet(net, pricing.VatRate) : net;
}
```

- [ ] **Step 4: Widen `DraftLine`**

In `frontend/src/model/state.ts`, in `DraftLine`, after `defaultPrice`:

```ts
/** True when this row's item is priced VAT-included: the price above is a
 *  gross, and the marker beside it says so. */
priceIncludesVat: boolean;
/** The item's current VAT rate, for the gross the price field shows. */
vatRate: number;
```

- [ ] **Step 5: Add the shared catalogue loader**

In `frontend/src/controller/InvoiceRevisions.controller.ts`, add to the class:

```ts
  /**
   * The item catalogue indexed by id, for the price fields. Both edit dialogs
   * already bind the whole catalogue into their item ComboBox, so this is one
   * more request rather than more data — and it has to be the catalogue rather
   * than the line, because the backend converts what is sent back using the
   * item's current rate and flag.
   */
  protected async loadItemPricing(): Promise<Map<number, api.ItemPricing>> {
    const items = await this.readList<{
      Id: number | string;
      VatRate: number | string;
      PriceIncludesVat: boolean;
    }>("/InvoiceItems", { $select: "Id,VatRate,PriceIncludesVat" });

    return new Map(
      items.map((item) => [
        Number(item.Id),
        { VatRate: Number(item.VatRate), PriceIncludesVat: Boolean(item.PriceIncludesVat) },
      ]),
    );
  }
```

- [ ] **Step 6: Fill the new fields in the invoice dialog**

In `frontend/src/controller/InvoiceEdit.controller.ts`:

`openInvoiceDialog` — pass the catalogue into `loadLines`:

```ts
const pricing = await this.loadItemPricing();
const lines =
  invoiceId != null ? await this.loadLines(invoiceId, pricing) : [InvoiceEdit.emptyLine()];
```

`loadLines` — take the map and use it:

```ts
  private async loadLines(
    invoiceId: number,
    pricing: Map<number, api.ItemPricing>,
  ): Promise<DraftLine[]> {
    try {
      const response = await fetch(`/odata/Invoices(${invoiceId})/Lines`);
      if (!response.ok) {
        throw new Error(`HTTP ${response.status}`);
      }
      const body = (await response.json()) as {
        value: api.InvoiceLineRow[];
      };
      return body.value.map((line) => {
        const item = line.ItemId != null ? pricing.get(Number(line.ItemId)) : undefined;
        return {
          // the ComboBox matches its keys as strings
          itemId: line.ItemId != null ? String(line.ItemId) : "",
          itemNumber: line.ItemNumber,
          count: line.Count,
          // the line stores a net; a VAT-included item is edited as its gross
          unitPrice: api.priceFieldValue(Number(line.UnitPrice), item),
          defaultPrice: null,
          priceIncludesVat: item?.PriceIncludesVat ?? false,
          vatRate: item?.VatRate ?? 0,
          description: line.Description,
        };
      });
    } catch (error) {
      api.showError(this, error);
      return [InvoiceEdit.emptyLine()];
    }
  }
```

`onLineItemSelected` — after the `defaultPrice` lines, add:

```ts
// the default price is stored as it was entered, so for a VAT-included item
// it is already the gross this field wants
model.setProperty(
  `/lines/${row}/priceIncludesVat`,
  itemContext.getProperty("PriceIncludesVat") as boolean,
);
model.setProperty(`/lines/${row}/vatRate`, Number(itemContext.getProperty("VatRate")));
```

`emptyLine` — add `priceIncludesVat: false,` and `vatRate: 0,`.

- [ ] **Step 7: Add the marker to the dialog**

In `frontend/src/view/fragments/InvoiceDialog.fragment.xml`, wrap the price
`Input` cell:

```xml
<VBox>
                                    <!-- pre-filled with the item's default price; clearing it or
                                         typing another one overrides it for this invoice -->
                                    <Input
    value="{invoiceDraft>unitPrice}"
    type="Number"
    width="7rem"
    placeholder="{invoiceDraft>defaultPrice}"
    ariaLabel="{i18n>itemPrice}"
  />
                                    <!-- this row's price is the gross the customer pays -->
                                    <Text
    text="{i18n>priceIncludesVatShort}"
    visible="{invoiceDraft>priceIncludesVat}"
  />
                                </VBox>
```

- [ ] **Step 8: Run the frontend checks**

Run: `cd frontend && npm run typecheck && npm run lint && npm run ui5lint`
Expected: all three clean — but only after the grave side compiles.
`GraveDraft.Items` and `GraveInvoiceDraft.lines` are `DraftLine[]` too, so
`typecheck` will report three literals in
`frontend/src/controller/GraveEdit.controller.ts` that are now missing the two
new fields: the `Items` map in `loadGraveDraft` (~line 65), the `lines` map in
the grave invoice dialog (~line 276), and `GraveEdit.emptyLine` (~line 172). Add
`priceIncludesVat: false,` and `vatRate: 0,` to each of the three so the branch
builds; Task 9 replaces those placeholders with the item's real values.

- [ ] **Step 9: Run the E2E tests to verify they pass**

Run: `cd e2e && npx playwright test tests/vat-included.spec.ts --project=desktop`
Expected: PASS, 2 tests.

- [ ] **Step 10: Commit**

```bash
git add frontend/src e2e/tests/vat-included.spec.ts
git commit -m "feat(invoices): enter a VAT-included price as its gross"
```

---

### Task 9: The grave dialogs and the grave detail page

**Files:**

- Modify: `frontend/src/controller/GraveEdit.controller.ts`
- Modify: `frontend/src/view/fragments/GraveDialog.fragment.xml`
- Modify: `frontend/src/view/fragments/GraveInvoiceDialog.fragment.xml`
- Modify: `frontend/src/controller/GraveDetail.controller.ts`
- Modify: `frontend/src/view/GraveDetail.view.xml:79`
- Modify: `frontend/src/model/api.ts` (`GraveItemLink` already carries `ItemRow`)
- Test: `e2e/tests/graves.spec.ts` (extend — see the note below)

**Interfaces:**

- Consumes: `api.priceFieldValue`, `api.ItemPricing`,
  `InvoiceRevisions.loadItemPricing` (Task 8); `ItemRow.PriceIncludesVat`
  (Task 7); the backend of Tasks 4 and 5.
- Produces: nothing later depends on it.

**Note on the E2E test:** the grave test goes into `e2e/tests/graves.spec.ts`,
not into `vat-included.spec.ts`. `GenerateGraveInvoices` runs over every grave at
once and refuses a second concurrent job, so all generation lives in that one
file — a spec in another file would race it. Use the half-year **2036/2**, which
no other test uses (2030/2, 2037/2, 2038/2 and 2039/2 are taken).

- [ ] **Step 1: Write the failing E2E test**

In `e2e/tests/graves.spec.ts`, give `seedGrave` optional pricing, keeping today's
values as the defaults so no existing caller changes behaviour:

```ts
/** A grave with one item at 100.00 net and the given recipients, unique per run.
 *  `pricing` overrides the item's rate, price and VAT-included flag. */
async function seedGrave(
  page: Page,
  marker: string,
  recipientNames: string[],
  pricing: { vatRate?: number; price?: number; priceIncludesVat?: boolean } = {},
): Promise<{ graveId: number; recipientNames: string[] }> {
  const vatRate = pricing.vatRate ?? 7;
  await ensureRow(page, "/odata/VatRates", { Rate: vatRate });
  const item = await createRow(page, "/odata/InvoiceItems", {
    Number: `${marker}-item`,
    Description: "Grabpflege",
    VatRate: vatRate,
    DefaultPrice: pricing.price ?? 100,
    PriceIncludesVat: pricing.priceIncludesVat ?? false,
  });
```

(the rest of the function is unchanged)

Then append the test:

```ts
test("a grave splits a VAT-included price without taking its VAT out twice", async ({ page }) => {
  const marker = `GV-${Date.now()}`;
  const names = [`Vera ${marker}`, `Walter ${marker}`];
  const { graveId } = await seedGrave(page, marker, names, {
    vatRate: 7,
    price: 119,
    priceIncludesVat: true,
  });
  await generateFor(page, 2036, 2);

  await page.goto(`/index.html#/graves/${graveId}`);
  // the grave's own item list shows the gross and says so
  await expect(page.getByText("inkl. MwSt.").first()).toBeVisible();

  // 119,00 gross at 7 % is a net of 111,215; halved that is 55,61 net and
  // 59,50 gross each — 119,00 together, the price that was entered
  const invoicesTable = page.locator("[id$='graveInvoicesTable-listUl']");
  await expect(invoicesTable.locator("tbody tr", { hasText: "59,50 €" })).toHaveCount(2);

  await invoicesTable
    .locator("tbody tr", { hasText: names[0] })
    .getByRole("button", { name: "Bearbeiten" })
    .click();
  await waitForDialogOpen(page);

  // the dialog opens on the grave's gross — not on the 111,215 net it stores,
  // and not on either recipient's share
  const dialog = page.locator("[id$='graveInvoiceDialog']");
  await expect(dialog.getByLabel("Preis")).toHaveValue("119");
  await expect(dialog).toContainText("inkl. MwSt.");

  // saved back untouched, every share stays where it was
  await dialog.getByRole("button", { name: "Speichern" }).click();
  await expect(invoicesTable.locator("tbody tr", { hasText: "59,50 €" })).toHaveCount(2);
});
```

- [ ] **Step 2: Run it to verify it fails**

Run: `cd e2e && npx playwright test tests/graves.spec.ts --project=desktop -g "VAT-included"`
Expected: FAIL — the dialog's price field holds `111.215`.

- [ ] **Step 3: Fill the fields in the grave dialog**

In `frontend/src/controller/GraveEdit.controller.ts`, `loadGraveDraft` — the
grave stores its override as entered, so no conversion, only the two fields:

```ts
      Items: details.Items.map((link) => ({
        // the ComboBox matches its keys as strings
        itemId: String(link.Item.Id),
        itemNumber: link.Item.Number,
        description: link.Item.Description,
        count: link.Count,
        // stored as it was typed — for a VAT-included item that is the gross
        unitPrice: link.UnitPrice,
        defaultPrice: link.Item.DefaultPrice,
        priceIncludesVat: link.Item.PriceIncludesVat,
        vatRate: Number(link.Item.VatRate),
      })),
```

`onGraveItemSelected` — after the `defaultPrice` lines:

```ts
model.setProperty(
  `/Items/${row}/priceIncludesVat`,
  itemContext.getProperty("PriceIncludesVat") as boolean,
);
model.setProperty(`/Items/${row}/vatRate`, Number(itemContext.getProperty("VatRate")));
```

`emptyLine` — add `priceIncludesVat: false,` and `vatRate: 0,`.

- [ ] **Step 4: Convert in the grave invoice dialog**

In the same file, in the method that opens the grave invoice dialog, load the
catalogue before building the model and use it for the price:

```ts
const pricing = await this.loadItemPricing();

this._graveInvoiceDialog.setModel(
  new TypedJSONModel<GraveInvoiceDraft>({
    invoiceId,
    warning,
    lines: group.Lines.map((line) => {
      const item = pricing.get(Number(line.ItemId));
      return {
        // the ComboBox matches its keys as strings
        itemId: String(line.ItemId),
        itemNumber: line.ItemNumber,
        description: line.Description,
        count: Number(line.Count),
        // No rounding for a net price: a pre-GraveUnitPrice-migration line is
        // reconstructed by GraveInvoiceSplitter.Combine at 3-decimal precision
        // on purpose, to reproduce its historical net cents exactly. Rounding
        // here would resubmit a different price for every line the user never
        // touched. A VAT-included item is the one case that does convert, and
        // its round trip is exact.
        unitPrice: api.priceFieldValue(Number(line.UnitPrice), item),
        defaultPrice: null,
        priceIncludesVat: item?.PriceIncludesVat ?? false,
        vatRate: item?.VatRate ?? 0,
      };
    }),
  }),
  "graveInvoiceDraft",
);
```

`onGraveInvoiceItemSelected` — after the `defaultPrice` lines:

```ts
model.setProperty(
  `/lines/${row}/priceIncludesVat`,
  itemContext.getProperty("PriceIncludesVat") as boolean,
);
model.setProperty(`/lines/${row}/vatRate`, Number(itemContext.getProperty("VatRate")));
```

- [ ] **Step 5: Add the markers to both fragments**

In `frontend/src/view/fragments/GraveInvoiceDialog.fragment.xml`, wrap the price
`Input`:

```xml
<VBox>
                                    <Input
    value="{graveInvoiceDraft>unitPrice}"
    type="Number"
    width="7rem"
    placeholder="{graveInvoiceDraft>defaultPrice}"
    ariaLabel="{i18n>itemPrice}"
  />
                                    <!-- this row's price is the gross the customer pays -->
                                    <Text
    text="{i18n>priceIncludesVatShort}"
    visible="{graveInvoiceDraft>priceIncludesVat}"
  />
                                </VBox>
```

In `frontend/src/view/fragments/GraveDialog.fragment.xml`, wrap its price `Input`
the same way — it sits between the count `Input` and the delete `Button`, around
line 99:

```xml
<VBox>
                                        <!-- pre-filled with the item's default price like the
                                             invoice dialog; clearing it means "use the item's
                                             default price", which the placeholder shows -->
                                        <Input
    value="{graveDraft>unitPrice}"
    type="Number"
    width="7rem"
    placeholder="{graveDraft>defaultPrice}"
    ariaLabel="{i18n>itemPrice}"
  />
                                        <!-- this row's price is the gross the customer pays -->
                                        <Text
    text="{i18n>priceIncludesVatShort}"
    visible="{graveDraft>priceIncludesVat}"
  />
                                    </VBox>
```

- [ ] **Step 6: Mark the grave detail item table**

In `frontend/src/controller/GraveDetail.controller.ts`, add to `GraveItemRow`:

```ts
PriceIncludesVat: boolean;
```

and to the `Items` projection, after `VatRate`:

```ts
          PriceIncludesVat: link.Item.PriceIncludesVat,
```

In `frontend/src/view/GraveDetail.view.xml`, replace the price cell at line 79
with:

```xml
<VBox>
                                    <Text
    text="{path: 'grave>Price', formatter: '.formatAmount'}"
  />
                                    <Text
    text="{i18n>priceIncludesVatShort}"
    visible="{grave>PriceIncludesVat}"
  />
                                </VBox>
```

- [ ] **Step 7: Run the frontend checks**

Run: `cd frontend && npm run typecheck && npm run lint && npm run ui5lint`
Expected: all three clean.

- [ ] **Step 8: Run the E2E test to verify it passes**

Run: `cd e2e && npx playwright test tests/graves.spec.ts --project=desktop`
Expected: PASS — the whole file, not just the new test: `seedGrave` changed.

- [ ] **Step 9: Commit**

```bash
git add frontend/src e2e/tests/graves.spec.ts
git commit -m "feat(graves): enter a VAT-included price as its gross"
```

---

### Task 10: Full verification

Nothing new is written here. This is the gate before the branch is offered for
review, and it is the first time everything runs together.

**Files:** none.

- [ ] **Step 1: Run the whole backend suite**

Run: `./scripts/dotnet.sh test Invoicing.sln`
Expected: PASS, 0 failures. The baseline before this feature was 181 tests
(32 document, 149 integration); expect roughly 195 now.

- [ ] **Step 2: Run the frontend checks**

Run: `cd frontend && npm run typecheck && npm run lint && npm run ui5lint`
Expected: all three clean.

- [ ] **Step 3: Run the whole E2E suite**

Run: `cd e2e && npm test`
Expected: PASS on all three projects. It builds and starts its own stack on port
8081 — never point it at the dev stack.

If a spec other than the new ones fails, re-run that spec alone before treating
it as a regression: the suite's workers share one stack, so a full-suite red can
be cross-spec interference.

- [ ] **Step 4: Check the formatting**

Run: `npm run format:check`
Expected: clean. If not, `npm run format` and amend.

- [ ] **Step 5: Confirm the release shape**

Run: `scripts/next-version.sh`
Expected: a minor bump — every commit above is a `feat`, and the feature adds
exactly one migration.
