# Invoice Payment Status Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let the user mark any invoice — grave invoices included — as paid or
unpaid, lock a paid invoice against revision, and filter the invoice and grave
overviews by what is still unpaid.

**Architecture:** One EF Core migration adds a nullable `Invoice.PaidOn` date;
null is unpaid and the date is never shown, only stored. A single new action,
`SetInvoicePaid`, is the only writer. Both revision services refuse to revise
while a payment mark is set. `GraveInvoicingStatus` gains per-grave invoice and
paid counts so the grave overview can show and filter a payment state for the
half-year in its toolbar. The frontend keeps the status formatters and the
mark/unmark flow in `Base.controller.ts`, where all four affected views inherit
them.

**Tech Stack:** .NET 10 / ASP.NET Core OData v4 / EF Core + SQLite; OpenUI5
1.151 with TypeScript; Playwright for E2E; Docker for everything.

**Spec:** `docs/superpowers/specs/2026-08-31-invoice-payment-status-design.md`

## Global Constraints

- **Never run the E2E suite against the dev stack.** `cd e2e && npm test` starts
  its own stack on port 8081. `E2E_BASE_URL` / `npm run test:dev` is forbidden:
  the specs create rows the API cannot delete, and the dev database holds the
  user's data.
- **Backend tests need the dev backend stopped**, because the dev container and
  the SDK container share `backend/obj` and overwrite each other's
  `project.assets.json` (`NETSDK1064`). The full command, referred to below as
  "run the backend tests", is:

  ```sh
  docker compose stop backend
  find backend -type d \( -name bin -o -name obj \) -prune -exec rm -rf {} +
  scripts/dotnet.sh test Invoicing.sln
  docker compose start backend
  ```

- **Frontend checks**, referred to below as "run the frontend checks":

  ```sh
  cd frontend && npm run typecheck && npm run lint && npm run ui5lint
  ```

- **Migrations:** `scripts/ef.sh migrations add <Name> --project src/Invoicing.Api --startup-project src/Invoicing.Api`.
  Exactly **one** migration in this whole plan (Principle IV) — Task 1 creates
  it, no later task adds another. If a later task needs a schema change, amend
  Task 1's migration with `scripts/ef.sh migrations remove --force` and re-add
  it.
- **Only `e2e/tests/graves.spec.ts` may trigger grave generation**, and only it
  may assert on the current half-year's invoicing state. The backend allows one
  `GraveInvoices` job at a time and answers a second with `JobAlreadyRunning`,
  and a generation invoices _every_ grave in the stack. New
  generation-dependent tests are appended to that file, after the ones already
  there.
- **A dialog takes focus when it finishes opening.** Any spec that opens a
  dialog calls `waitForDialogOpen(page)` from `e2e/dialog.ts` before touching
  it.
- **English codebase, German UI** (Principle VII). Identifiers, comments and
  commit messages in English; every user-visible string comes from
  `i18n.properties` (German, the fallback bundle) and `i18n_en.properties`.
- **OData JSON casing:** non-EDM routes answer with
  `ODataJsonDefaults.PascalCase` so the whole API reads as one casing (D-001).
- **Decimals arrive as strings.** The OData V4 model serialises `Edm.Decimal` as
  a string; convert with `Number(...)` before arithmetic in TypeScript.
  `Edm.Date` arrives as `"2026-08-31"` or `null` — this feature only ever tests
  it for null.
- **Formatting is automatic.** `.husky/pre-commit` runs `lint-staged`
  (CSharpier for `backend/`, Prettier for everything else). Commit normally and
  let it rewrite; do not hand-format.
- **Commit messages follow Conventional Commits** — the `commit-msg` hook
  rejects anything else, and `scripts/next-version.sh` reads them.

---

## File Structure

**Backend — created**

| File                                                                | Responsibility                                      |
| ------------------------------------------------------------------- | --------------------------------------------------- |
| `backend/src/Invoicing.Api/Migrations/<ts>_InvoicePaymentStatus.cs` | The single migration: `PaidOn` and the index change |
| `backend/tests/Invoicing.IntegrationTests/PaymentTests.cs`          | Every backend test this feature adds                |

**Backend — modified**

| File                                                                            | Change                                                 |
| ------------------------------------------------------------------------------- | ------------------------------------------------------ |
| `backend/src/Invoicing.Domain/Entities/Invoice.cs`                              | `PaidOn` property                                      |
| `backend/src/Invoicing.Api/Persistence/AppDbContext.cs`                         | `(GraveId, IsLatestRevision)` index gains `PaidOn`     |
| `backend/src/Invoicing.Api/Services/InvoiceService.cs`                          | `IClock` dependency, `SetPaidAsync`, revision lock     |
| `backend/src/Invoicing.Api/Services/GraveInvoiceRevisionService.cs`             | Refuses when a sibling of the half-year is paid        |
| `backend/src/Invoicing.Api/OData/Controllers/InvoiceActionsController.cs`       | `SetInvoicePaid` endpoint                              |
| `backend/src/Invoicing.Api/OData/EdmModelBuilder.cs`                            | Declares the action                                    |
| `backend/src/Invoicing.Api/OData/Controllers/GraveInvoicingStatusController.cs` | `InvoiceCount` and `PaidCount` per grave               |
| `backend/src/Invoicing.Api/OData/Controllers/GraveInvoiceGroupController.cs`    | `Paid` per recipient, so the edit dialog can name them |

**Frontend — modified**

| File                                                                                    | Change                                                               |
| --------------------------------------------------------------------------------------- | -------------------------------------------------------------------- |
| `frontend/src/model/api.ts`                                                             | `setInvoicePaid`, new row fields, three error codes                  |
| `frontend/src/model/state.ts`                                                           | `GravesView.paymentFilter`                                           |
| `frontend/src/controller/Base.controller.ts`                                            | `paidStatusText`, `paidStatusState`, `paidEditEnabled`, `togglePaid` |
| `frontend/src/controller/GraveEdit.controller.ts`                                       | Refuses to open the grave-invoice dialog when a sibling is paid      |
| `frontend/src/view/Invoices.view.xml` + controller                                      | Payment column, toggle button, payment filter                        |
| `frontend/src/view/Graves.view.xml` + controller                                        | Payment column, payment filter                                       |
| `frontend/src/view/GraveDetail.view.xml` + controller                                   | Payment column and toggle on the grave's invoices                    |
| `frontend/src/view/InvoiceDetail.view.xml`, `GraveInvoiceDetail.view.xml` + controllers | Header status and toggle                                             |
| `frontend/src/i18n/i18n.properties`, `i18n_en.properties`                               | Every new string                                                     |

**Tests — modified**

`e2e/tests/invoices.spec.ts` (non-grave marking and filtering) ·
`e2e/tests/graves.spec.ts` (grave half-year payment states) ·
`docs/spec.md` (functional requirements)

---

### Task 1: The migration — `Invoice.PaidOn`

**Files:**

- Modify: `backend/src/Invoicing.Domain/Entities/Invoice.cs`
- Modify: `backend/src/Invoicing.Api/Persistence/AppDbContext.cs` (the
  `(GraveId, IsLatestRevision)` index)
- Create: `backend/src/Invoicing.Api/Migrations/<timestamp>_InvoicePaymentStatus.cs`
- Test: `backend/tests/Invoicing.IntegrationTests/PaymentTests.cs`

**Interfaces:**

- Consumes: nothing.
- Produces: `Invoice.PaidOn` (`DateOnly?`), exposed on the `Invoices` entity set
  as `Edm.Date`, filterable as `PaidOn eq null`. `PaymentTests.SeedAsync` and
  `PaymentTests.CreateInvoiceAsync` are used by Tasks 2 and 3.

- [ ] **Step 1: Write the failing test**

Create `backend/tests/Invoicing.IntegrationTests/PaymentTests.cs`:

```csharp
using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Burkard.Invoicing.IntegrationTests;

public class PaymentTests
{
    /// <summary>One item (100.00 at 7% VAT) and one recipient, ready to be
    /// invoiced through /odata/CreateInvoice.</summary>
    private static async Task<(long ItemId, long RecipientId)> SeedAsync(ApiTestHost host)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.VatRates.Add(new VatRate { Rate = 7m });
        var item = new InvoiceItem
        {
            Number = "7001",
            Description = "Grabpflege",
            VatRate = 7m,
            DefaultPrice = 100m,
        };
        var recipient = new Recipient
        {
            Number = "E-0007",
            Name = "Familie Zahlung",
            Address = "Hauptstraße 7",
        };
        db.InvoiceItems.Add(item);
        db.Recipients.Add(recipient);
        await db.SaveChangesAsync();
        return (item.Id, recipient.Id);
    }

    /// <summary>Creates one invoice with a single line and returns its id.</summary>
    private static async Task<long> CreateInvoiceAsync(
        HttpClient client,
        long itemId,
        long recipientId
    )
    {
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
                            itemId,
                            count = 1,
                            unitPrice = (decimal?)null,
                        },
                    },
                }
            )
        );
        response.EnsureSuccessStatusCode();
        using var json = await ODataJson.ReadAsync(response);
        return json.RootElement.GetProperty("Id").GetInt64();
    }

    [Fact]
    public async Task New_invoice_is_unpaid_and_the_unpaid_filter_returns_it()
    {
        using var host = new ApiTestHost();
        var (itemId, recipientId) = await SeedAsync(host);
        using var client = host.CreateClient();
        var invoiceId = await CreateInvoiceAsync(client, itemId, recipientId);

        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Null((await db.Invoices.SingleAsync(i => i.Id == invoiceId)).PaidOn);
        }

        var unpaid = await client.GetAsync("/odata/Invoices?$filter=PaidOn eq null");
        Assert.Equal(HttpStatusCode.OK, unpaid.StatusCode);
        using var body = await ODataJson.ReadAsync(unpaid);
        var ids = body
            .RootElement.GetProperty("value")
            .EnumerateArray()
            .Select(row => row.GetProperty("Id").GetInt64())
            .ToList();
        Assert.Contains(invoiceId, ids);
    }
}
```

- [ ] **Step 2: Run the backend tests to verify it fails**

Run the backend tests (see Global Constraints).
Expected: FAIL — compile error, `Invoice` has no `PaidOn`.

- [ ] **Step 3: Add the property**

In `backend/src/Invoicing.Domain/Entities/Invoice.cs`, after `GeneratedAt`:

```csharp
    /// <summary>The day the invoice was marked paid; null while it is unpaid.
    /// Nullability carries the whole meaning — there is no second flag to
    /// disagree with it. Only SetInvoicePaid writes it, and the UI reads it as
    /// paid/unpaid without ever showing the date.</summary>
    public DateOnly? PaidOn { get; set; }
```

- [ ] **Step 4: Widen the index**

In `backend/src/Invoicing.Api/Persistence/AppDbContext.cs`, replace:

```csharp
        // the invoice list and the grave detail page both filter on this pair
        mb.Entity<Invoice>().HasIndex(i => new { i.GraveId, i.IsLatestRevision });
```

with:

```csharp
        // the invoice list filters on all three at once; the grave detail page's
        // (GraveId, IsLatestRevision) query still matches the leading columns,
        // so one index serves both
        mb.Entity<Invoice>()
            .HasIndex(i => new
            {
                i.GraveId,
                i.IsLatestRevision,
                i.PaidOn,
            });
```

- [ ] **Step 5: Create the migration**

```sh
scripts/ef.sh migrations add InvoicePaymentStatus --project src/Invoicing.Api --startup-project src/Invoicing.Api
```

- [ ] **Step 6: Check what it generated**

Read the new file under `backend/src/Invoicing.Api/Migrations/`. It must
contain exactly: `AddColumn<DateOnly>(name: "PaidOn", table: "Invoices", type:
"TEXT", nullable: true)`, a `DropIndex` of
`IX_Invoices_GraveId_IsLatestRevision`, and a `CreateIndex` of
`IX_Invoices_GraveId_IsLatestRevision_PaidOn`. No backfill: every existing
invoice reads as unpaid, which is the truth — the application never knew
which ones were settled.

That absence is also why upgrading an existing database needs no test of its
own. `LatestRevisionTests.The_migrations_backfill_statement_clears_superseded_rows`
exists because that migration carried a `Sql(...)` backfill whose logic could be
wrong; this one adds a nullable column, so every pre-existing row is `NULL`, and
Step 1's test already pins that `NULL` reads as unpaid and appears under the
unpaid filter. If the generated migration contains any `Sql(...)` statement,
that reasoning no longer holds — stop and re-check Step 4.

If it contains anything else, an earlier step is wrong. Fix it, then
`scripts/ef.sh migrations remove --force --project src/Invoicing.Api --startup-project src/Invoicing.Api`
and re-add.

- [ ] **Step 7: Run the backend tests to verify they pass**

Run the backend tests.
Expected: PASS, including the whole existing suite — `SeedTests` applies the
migration to a fresh database.

- [ ] **Step 8: Commit**

```bash
git add backend/src/Invoicing.Domain/Entities/Invoice.cs \
        backend/src/Invoicing.Api/Persistence/AppDbContext.cs \
        backend/src/Invoicing.Api/Migrations \
        backend/tests/Invoicing.IntegrationTests/PaymentTests.cs
git commit -m "feat(invoices): record whether an invoice is paid"
```

---

### Task 2: `SetInvoicePaid` — the only writer

**Files:**

- Modify: `backend/src/Invoicing.Api/Services/InvoiceService.cs`
- Modify: `backend/src/Invoicing.Api/OData/Controllers/InvoiceActionsController.cs`
- Modify: `backend/src/Invoicing.Api/OData/EdmModelBuilder.cs`
- Test: `backend/tests/Invoicing.IntegrationTests/PaymentTests.cs`

**Interfaces:**

- Consumes: `Invoice.PaidOn` (Task 1); `PaymentTests.SeedAsync`,
  `PaymentTests.CreateInvoiceAsync` (Task 1).
- Produces: `POST /odata/SetInvoicePaid` with body
  `{ "InvoiceId": <long>, "Paid": <bool> }`, answering `200` with the updated
  invoice or `400 { error: { code, message } }` with code `InvoiceNotFound` or
  `NotLatestRevision`. Service method
  `InvoiceService.SetPaidAsync(long invoiceId, bool paid, CancellationToken ct)`
  returning `Task<Invoice>`.

- [ ] **Step 1: Write the failing tests**

Append to `PaymentTests.cs`, inside the class:

```csharp
    /// <summary>Marks an invoice paid or unpaid through the action.</summary>
    private static Task<HttpResponseMessage> SetPaidAsync(
        HttpClient client,
        long invoiceId,
        bool paid
    ) => client.PostAsync("/odata/SetInvoicePaid", ODataJson.Body(new { invoiceId, paid }));

    [Fact]
    public async Task Marking_stamps_todays_date_and_unmarking_clears_it()
    {
        using var host = new ApiTestHost();
        var (itemId, recipientId) = await SeedAsync(host);
        using var client = host.CreateClient();
        var invoiceId = await CreateInvoiceAsync(client, itemId, recipientId);

        var marked = await SetPaidAsync(client, invoiceId, true);
        Assert.Equal(HttpStatusCode.OK, marked.StatusCode);

        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var invoice = await db.Invoices.SingleAsync(i => i.Id == invoiceId);
            Assert.Equal(DateOnly.FromDateTime(DateTime.Now), invoice.PaidOn);
        }

        var cleared = await SetPaidAsync(client, invoiceId, false);
        Assert.Equal(HttpStatusCode.OK, cleared.StatusCode);

        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Null((await db.Invoices.SingleAsync(i => i.Id == invoiceId)).PaidOn);
        }
    }

    [Fact]
    public async Task Marking_an_already_paid_invoice_keeps_the_recorded_day()
    {
        using var host = new ApiTestHost();
        var (itemId, recipientId) = await SeedAsync(host);
        using var client = host.CreateClient();
        var invoiceId = await CreateInvoiceAsync(client, itemId, recipientId);

        // a payment recorded on an earlier day: marking again must not move it
        // to today, or a double click would silently rewrite a payment date
        var earlier = new DateOnly(2026, 1, 2);
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var invoice = await db.Invoices.SingleAsync(i => i.Id == invoiceId);
            invoice.PaidOn = earlier;
            await db.SaveChangesAsync();
        }

        var again = await SetPaidAsync(client, invoiceId, true);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);

        using (var verify = host.Services.CreateScope())
        {
            var db = verify.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(earlier, (await db.Invoices.SingleAsync(i => i.Id == invoiceId)).PaidOn);
        }
    }

    [Fact]
    public async Task Marking_a_superseded_revision_is_refused()
    {
        using var host = new ApiTestHost();
        var (itemId, recipientId) = await SeedAsync(host);
        using var client = host.CreateClient();
        var invoiceId = await CreateInvoiceAsync(client, itemId, recipientId);

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
                            itemId,
                            count = 2,
                            unitPrice = (decimal?)null,
                        },
                    },
                }
            )
        );
        revised.EnsureSuccessStatusCode();

        var response = await SetPaidAsync(client, invoiceId, true);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        Assert.Equal(
            "NotLatestRevision",
            json.RootElement.GetProperty("error").GetProperty("code").GetString()
        );
    }

    [Fact]
    public async Task Marking_an_unknown_invoice_is_refused()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();

        var response = await SetPaidAsync(client, 987654, true);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        Assert.Equal(
            "InvoiceNotFound",
            json.RootElement.GetProperty("error").GetProperty("code").GetString()
        );
    }
```

- [ ] **Step 2: Run the backend tests to verify they fail**

Run the backend tests.
Expected: FAIL — the four new tests get `404`/`405` for `/odata/SetInvoicePaid`
rather than the asserted statuses.

- [ ] **Step 3: Give `InvoiceService` a clock**

In `backend/src/Invoicing.Api/Services/InvoiceService.cs`, extend the fields and
the constructor (`IClock` is already registered in `Program.cs` and already used
by `InvoiceNumberService`, so DI needs no change):

```csharp
    private readonly AppDbContext _db;
    private readonly InvoiceNumberService _numbers;
    private readonly InvoiceDocumentFactory _documentFactory;
    private readonly IClock _clock;

    public InvoiceService(
        AppDbContext db,
        InvoiceNumberService numbers,
        InvoiceDocumentFactory documentFactory,
        IClock clock
    )
    {
        _db = db;
        _numbers = numbers;
        _documentFactory = documentFactory;
        _clock = clock;
    }
```

- [ ] **Step 4: Add `SetPaidAsync`**

In the same file, after `CreateRevisionAsync`:

```csharp
    /// <summary>Marks an invoice paid on the current local date — "today" here is
    /// the user's day, not UTC's — or takes the mark back. Marking an invoice
    /// that already carries a date keeps that date, so a double click cannot
    /// move a recorded payment day.</summary>
    public async Task<Invoice> SetPaidAsync(long invoiceId, bool paid, CancellationToken ct)
    {
        var invoice =
            await _db.Invoices.SingleOrDefaultAsync(i => i.Id == invoiceId, ct)
            ?? throw new ValidationException("InvoiceNotFound", "The invoice does not exist.");

        // Lists show latest revisions only. A superseded row can only be reached
        // deliberately, and a mark on it would sit where nothing displays it.
        if (!invoice.IsLatestRevision)
        {
            throw new ValidationException(
                "NotLatestRevision",
                "Only the latest revision of an invoice can be marked as paid."
            );
        }

        if (paid)
        {
            invoice.PaidOn ??= DateOnly.FromDateTime(_clock.GetLocalNow());
        }
        else
        {
            invoice.PaidOn = null;
        }

        await _db.SaveChangesAsync(ct);
        return invoice;
    }
```

- [ ] **Step 5: Add the endpoint**

In `backend/src/Invoicing.Api/OData/Controllers/InvoiceActionsController.cs`,
after `CreateGraveInvoiceRevision`:

```csharp
    [HttpPost("SetInvoicePaid")]
    public async Task<IActionResult> SetInvoicePaid([FromBody] SetInvoicePaidRequest? request)
    {
        if (request is null)
        {
            return BadRequest(
                new
                {
                    error = new
                    {
                        code = "InvalidRequest",
                        message = "The request body is required.",
                    },
                }
            );
        }

        try
        {
            var invoice = await _service.SetPaidAsync(
                request.InvoiceId,
                request.Paid,
                HttpContext.RequestAborted
            );
            return Ok(invoice);
        }
        catch (ValidationException ex)
        {
            return BadRequest(new { error = new { code = ex.Code, message = ex.Message } });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SetInvoicePaid failed");
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                new
                {
                    error = new { code = "InternalError", message = "An internal error occurred." },
                }
            );
        }
    }
```

and, with the other request classes at the bottom of the file:

```csharp
    public sealed class SetInvoicePaidRequest
    {
        public long InvoiceId { get; set; }
        public bool Paid { get; set; }
    }
```

- [ ] **Step 6: Declare the action on the EDM**

In `backend/src/Invoicing.Api/OData/EdmModelBuilder.cs`, next to the other
actions:

```csharp
        b.Action("SetInvoicePaid").ReturnsFromEntitySet<Invoice>("Invoices");
```

- [ ] **Step 7: Run the backend tests to verify they pass**

Run the backend tests.
Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add backend/src/Invoicing.Api/Services/InvoiceService.cs \
        backend/src/Invoicing.Api/OData/Controllers/InvoiceActionsController.cs \
        backend/src/Invoicing.Api/OData/EdmModelBuilder.cs \
        backend/tests/Invoicing.IntegrationTests/PaymentTests.cs
git commit -m "feat(invoices): mark an invoice as paid or unpaid"
```

---

### Task 3: A paid invoice cannot be revised

**Files:**

- Modify: `backend/src/Invoicing.Api/Services/InvoiceService.cs`
  (`CreateRevisionAsync`)
- Modify: `backend/src/Invoicing.Api/Services/GraveInvoiceRevisionService.cs`
  (`CreateAsync`)
- Test: `backend/tests/Invoicing.IntegrationTests/PaymentTests.cs`

**Interfaces:**

- Consumes: `SetInvoicePaid` and `PaymentTests.SetPaidAsync` (Task 2).
- Produces: error codes `InvoicePaid` (from `CreateRevision`) and
  `GraveInvoicePaid` (from `CreateGraveInvoiceRevision`, message naming the paid
  recipients), both as `400 { error: { code, message } }`.

- [ ] **Step 1: Write the failing tests**

Append to `PaymentTests.cs`. The grave seed mirrors
`GraveInvoiceRevisionTests.SeedAsync` — a grave with one item (count 2 at
100.00, 7% VAT) and two recipients, invoiced for 2031 H1 — with its own item
and recipient numbers so nothing collides:

```csharp
    /// <summary>A grave with one item (count 2 at 100.00, 7% VAT) and two
    /// recipients, invoiced for 2031 H1. Returns the grave and the two invoice
    /// ids, ordered as generation created them.</summary>
    private static async Task<(long GraveId, long ItemId, long FirstInvoiceId, long SecondInvoiceId)> SeedGraveAsync(
        ApiTestHost host
    )
    {
        long graveId,
            itemId;
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.VatRates.Add(new VatRate { Rate = 7m });
            var item = new InvoiceItem
            {
                Number = "7002",
                Description = "Grabpflege",
                VatRate = 7m,
                DefaultPrice = 100m,
            };
            var anna = new Recipient
            {
                Number = "E-0071",
                Name = "Anna Zahlung",
                Address = "A 1",
            };
            var bernd = new Recipient
            {
                Number = "E-0072",
                Name = "Bernd Zahlung",
                Address = "B 2",
            };
            db.InvoiceItems.Add(item);
            db.Recipients.AddRange(anna, bernd);
            var grave = new Grave
            {
                GraveNumber = "G-700",
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
                    Count = 2,
                }
            );
            db.GraveRecipients.Add(new GraveRecipient { GraveId = grave.Id, RecipientId = anna.Id });
            db.GraveRecipients.Add(
                new GraveRecipient { GraveId = grave.Id, RecipientId = bernd.Id }
            );
            await db.SaveChangesAsync();
            (graveId, itemId) = (grave.Id, item.Id);
        }

        using var client = host.CreateClient();
        await GraveGenerationHelper.RunAsync(client, 2031, 1);

        using var verify = host.Services.CreateScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<AppDbContext>();
        var invoices = await verifyDb
            .Invoices.Where(i => i.GraveId == graveId)
            .OrderBy(i => i.Id)
            .ToListAsync();
        return (graveId, itemId, invoices[0].Id, invoices[1].Id);
    }

    [Fact]
    public async Task Revising_a_paid_invoice_is_refused()
    {
        using var host = new ApiTestHost();
        var (itemId, recipientId) = await SeedAsync(host);
        using var client = host.CreateClient();
        var invoiceId = await CreateInvoiceAsync(client, itemId, recipientId);
        (await SetPaidAsync(client, invoiceId, true)).EnsureSuccessStatusCode();

        var response = await client.PostAsync(
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
                            itemId,
                            count = 3,
                            unitPrice = (decimal?)null,
                        },
                    },
                }
            )
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        Assert.Equal(
            "InvoicePaid",
            json.RootElement.GetProperty("error").GetProperty("code").GetString()
        );

        // the refusal left the chain alone
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.Invoices.CountAsync(i => i.RootId == invoiceId));
    }

    [Fact]
    public async Task Revising_a_grave_invoice_is_refused_when_one_recipient_has_paid()
    {
        using var host = new ApiTestHost();
        var (_, itemId, firstInvoiceId, secondInvoiceId) = await SeedGraveAsync(host);
        using var client = host.CreateClient();
        (await SetPaidAsync(client, firstInvoiceId, true)).EnsureSuccessStatusCode();

        var response = await client.PostAsync(
            "/odata/CreateGraveInvoiceRevision",
            ODataJson.Body(
                new
                {
                    invoiceId = secondInvoiceId,
                    lines = new[]
                    {
                        new
                        {
                            itemId,
                            count = 3,
                            unitPrice = (decimal?)null,
                        },
                    },
                }
            )
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        var error = json.RootElement.GetProperty("error");
        Assert.Equal("GraveInvoicePaid", error.GetProperty("code").GetString());
        // the message names who is paid: from the row the user pressed edit on,
        // another recipient's payment is invisible
        Assert.Contains("Anna Zahlung", error.GetProperty("message").GetString());

        // neither sibling gained a revision
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(2, await db.Invoices.CountAsync(i => i.GraveId != null));
    }

    [Fact]
    public async Task Revising_a_grave_invoice_succeeds_once_the_mark_is_taken_back()
    {
        using var host = new ApiTestHost();
        var (_, itemId, firstInvoiceId, secondInvoiceId) = await SeedGraveAsync(host);
        using var client = host.CreateClient();
        (await SetPaidAsync(client, firstInvoiceId, true)).EnsureSuccessStatusCode();
        (await SetPaidAsync(client, firstInvoiceId, false)).EnsureSuccessStatusCode();

        var response = await client.PostAsync(
            "/odata/CreateGraveInvoiceRevision",
            ODataJson.Body(
                new
                {
                    invoiceId = secondInvoiceId,
                    lines = new[]
                    {
                        new
                        {
                            itemId,
                            count = 3,
                            unitPrice = (decimal?)null,
                        },
                    },
                }
            )
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        // two originals plus one revision per recipient
        Assert.Equal(4, await db.Invoices.CountAsync(i => i.GraveId != null));
    }
```

- [ ] **Step 2: Run the backend tests to verify they fail**

Run the backend tests.
Expected: FAIL — the revisions currently succeed, so the two refusal tests get
`OK`/`Created` instead of `BadRequest`.

- [ ] **Step 3: Lock `CreateRevisionAsync`**

In `backend/src/Invoicing.Api/Services/InvoiceService.cs`, in
`CreateRevisionAsync`, directly after the `GraveInvoiceRevisionRequired` guard:

```csharp
        // A paid invoice is settled; revising it would move the ground under a
        // payment that already happened. The chain is what matters, not the row
        // that was passed: only its latest revision can carry a mark, and an
        // older revision may well be the one handed in.
        if (await _db.Invoices.AnyAsync(i => i.RootId == edited.RootId && i.PaidOn != null, ct))
        {
            throw new ValidationException(
                "InvoicePaid",
                "The invoice is paid and cannot be revised."
            );
        }
```

- [ ] **Step 4: Lock `GraveInvoiceRevisionService.CreateAsync`**

In `backend/src/Invoicing.Api/Services/GraveInvoiceRevisionService.cs`, after
the `siblings.Count == 0` guard and before the split:

```csharp
        // One edit revises every recipient of the half-year, so one paid
        // recipient blocks all of them. Naming them is the point: from the row
        // the user pressed edit on, another recipient's payment is invisible.
        var paidRecipients = siblings
            .Where(s => s.PaidOn is not null)
            .Select(s => s.RecipientName)
            .ToList();
        if (paidRecipients.Count > 0)
        {
            throw new ValidationException(
                "GraveInvoicePaid",
                $"The invoices of {string.Join(", ", paidRecipients)} are paid and cannot be revised."
            );
        }
```

- [ ] **Step 5: Run the backend tests to verify they pass**

Run the backend tests.
Expected: PASS, including the existing `GraveInvoiceRevisionTests` — nothing
there marks an invoice paid, so those revisions still go through.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Invoicing.Api/Services/InvoiceService.cs \
        backend/src/Invoicing.Api/Services/GraveInvoiceRevisionService.cs \
        backend/tests/Invoicing.IntegrationTests/PaymentTests.cs
git commit -m "feat(invoices): refuse to revise a paid invoice"
```

---

### Task 4: Payment in the grave projections

**Files:**

- Modify: `backend/src/Invoicing.Api/OData/Controllers/GraveInvoicingStatusController.cs`
- Modify: `backend/src/Invoicing.Api/OData/Controllers/GraveInvoiceGroupController.cs`
- Test: `backend/tests/Invoicing.IntegrationTests/PaymentTests.cs`

**Interfaces:**

- Consumes: `Invoice.PaidOn` (Task 1); `PaymentTests.SeedGraveAsync` and
  `SetPaidAsync` (Tasks 2, 3).
- Produces: `GET /odata/GraveInvoicingStatus?year=&half=` rows gain
  `InvoiceCount` and `PaidCount` (both `int`, PascalCase);
  `GET /odata/GraveInvoiceGroup?invoiceId=` recipients gain `Paid` (`bool`).

- [ ] **Step 1: Write the failing test**

Append to `PaymentTests.cs`:

```csharp
    [Fact]
    public async Task Grave_status_and_group_report_how_many_recipients_have_paid()
    {
        using var host = new ApiTestHost();
        var (graveId, _, firstInvoiceId, _) = await SeedGraveAsync(host);
        using var client = host.CreateClient();
        (await SetPaidAsync(client, firstInvoiceId, true)).EnsureSuccessStatusCode();

        var status = await client.GetAsync("/odata/GraveInvoicingStatus?year=2031&half=1");
        status.EnsureSuccessStatusCode();
        using var statusJson = await ODataJson.ReadAsync(status);
        var row = statusJson
            .RootElement.GetProperty("value")
            .EnumerateArray()
            .Single(g => g.GetProperty("Id").GetInt64() == graveId);
        Assert.True(row.GetProperty("Invoiced").GetBoolean());
        Assert.Equal(2, row.GetProperty("InvoiceCount").GetInt32());
        Assert.Equal(1, row.GetProperty("PaidCount").GetInt32());

        // a half-year this grave was never invoiced for has nothing outstanding
        var other = await client.GetAsync("/odata/GraveInvoicingStatus?year=2031&half=2");
        other.EnsureSuccessStatusCode();
        using var otherJson = await ODataJson.ReadAsync(other);
        var otherRow = otherJson
            .RootElement.GetProperty("value")
            .EnumerateArray()
            .Single(g => g.GetProperty("Id").GetInt64() == graveId);
        Assert.Equal(0, otherRow.GetProperty("InvoiceCount").GetInt32());
        Assert.Equal(0, otherRow.GetProperty("PaidCount").GetInt32());

        var group = await client.GetAsync($"/odata/GraveInvoiceGroup?invoiceId={firstInvoiceId}");
        group.EnsureSuccessStatusCode();
        using var groupJson = await ODataJson.ReadAsync(group);
        var paid = groupJson
            .RootElement.GetProperty("Recipients")
            .EnumerateArray()
            .Single(r => r.GetProperty("InvoiceId").GetInt64() == firstInvoiceId);
        Assert.True(paid.GetProperty("Paid").GetBoolean());
        var unpaid = groupJson
            .RootElement.GetProperty("Recipients")
            .EnumerateArray()
            .Single(r => r.GetProperty("InvoiceId").GetInt64() != firstInvoiceId);
        Assert.False(unpaid.GetProperty("Paid").GetBoolean());
    }
```

- [ ] **Step 2: Run the backend tests to verify it fails**

Run the backend tests.
Expected: FAIL — `KeyNotFoundException` for the `InvoiceCount` property.

- [ ] **Step 3: Count the invoices in the status projection**

In `backend/src/Invoicing.Api/OData/Controllers/GraveInvoicingStatusController.cs`,
after `invoicedIds` and before the `graves` query:

```csharp
        // How many invoices this half-year produced per grave, and how many of
        // them are paid. Latest revisions only, matching every list that shows
        // them. Counted here rather than derived by the client so the overview
        // stays one request.
        var payment = await _db
            .Invoices.Where(i =>
                i.GraveId != null
                && i.PeriodYear == year
                && i.PeriodHalf == half
                && i.IsLatestRevision
            )
            .GroupBy(i => i.GraveId!.Value)
            .Select(g => new
            {
                GraveId = g.Key,
                InvoiceCount = g.Count(),
                PaidCount = g.Count(i => i.PaidOn != null),
            })
            .ToDictionaryAsync(row => row.GraveId);
```

and extend the returned projection:

```csharp
        var value = graves.Select(g => new
        {
            g.Id,
            g.GraveNumber,
            g.StartDate,
            g.EndDate,
            Invoiced = invoicedIds.Contains(g.Id),
            InvoiceCount = payment.TryGetValue(g.Id, out var counts) ? counts.InvoiceCount : 0,
            PaidCount = payment.TryGetValue(g.Id, out var paid) ? paid.PaidCount : 0,
        });
```

- [ ] **Step 4: Report payment per recipient in the group**

In `backend/src/Invoicing.Api/OData/Controllers/GraveInvoiceGroupController.cs`,
extend the `Recipients` projection so the edit dialog can name who is paid
before it even opens:

```csharp
            Recipients = siblings.Select(s => new
            {
                Id = s.RecipientId,
                Name = s.RecipientName,
                InvoiceId = s.Id,
                RootId = s.RootId,
                Paid = s.PaidOn != null,
            }),
```

- [ ] **Step 5: Run the backend tests to verify they pass**

Run the backend tests.
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Invoicing.Api/OData/Controllers/GraveInvoicingStatusController.cs \
        backend/src/Invoicing.Api/OData/Controllers/GraveInvoiceGroupController.cs \
        backend/tests/Invoicing.IntegrationTests/PaymentTests.cs
git commit -m "feat(graves): report how many of a half-year's invoices are paid"
```

---

### Task 5: Frontend groundwork — API, texts, shared controller code

**Files:**

- Modify: `frontend/src/model/api.ts`
- Modify: `frontend/src/controller/Base.controller.ts`
- Modify: `frontend/src/controller/GraveEdit.controller.ts`
- Modify: `frontend/src/i18n/i18n.properties`, `frontend/src/i18n/i18n_en.properties`

**Interfaces:**

- Consumes: `POST /odata/SetInvoicePaid` (Task 2); `InvoiceCount`, `PaidCount`,
  `Paid` (Task 4).
- Produces, all used by Tasks 6–8:
  - `api.setInvoicePaid(invoiceId: number, paid: boolean): Promise<void>`
  - `api.InvoiceRow.PaidOn: string | null`
  - `api.GraveStatusRow.InvoiceCount: number`, `.PaidCount: number`
  - `api.GraveInvoiceRecipient.Paid: boolean`
  - `BaseController.paidStatusText(paidOn: string | null | undefined): string`
  - `BaseController.paidStatusState(paidOn: string | null | undefined): string`
  - `BaseController.paidActionIcon(paidOn: string | null | undefined): string`
  - `BaseController.paidActionTooltip(paidOn: string | null | undefined): string`
  - `BaseController.paidEditEnabled(paidOn: string | null | undefined): boolean`
  - `BaseController.togglePaid(invoiceId: number, paidOn: string | null): Promise<boolean>`

- [ ] **Step 1: Extend the API module**

In `frontend/src/model/api.ts`, add to the `CODE_TO_I18N` map:

```ts
  InvoicePaid: "errorInvoicePaid",
  GraveInvoicePaid: "errorGraveInvoicePaid",
  NotLatestRevision: "errorNotLatestRevision",
```

Add `PaidOn` to `InvoiceRow`, after `GeneratedAt`:

```ts
/** An Edm.Date ("2026-08-31") when the invoice is paid, null while it is not.
 *  Only ever read as a null check: the date is stored, never shown. */
PaidOn: string | null;
```

Add the counts to `GraveStatusRow`, after `Invoiced`:

```ts
/** Latest-revision invoices this grave has for the requested half-year, and
 *  how many of them are paid. Both 0 when it was never invoiced for it. */
InvoiceCount: number;
PaidCount: number;
```

Add `Paid` to `GraveInvoiceRecipient` (next to `InvoiceId`):

```ts
Paid: boolean;
```

And the write, next to `createRevision`:

```ts
/** Marks an invoice paid — the backend stamps the day — or takes the mark back. */
export async function setInvoicePaid(invoiceId: number, paid: boolean): Promise<void> {
  await ensureOk(await post("/odata/SetInvoicePaid", { invoiceId, paid }));
}
```

- [ ] **Step 2: Add the shared payment code to `BaseController`**

In `frontend/src/controller/Base.controller.ts`, add the import

```ts
import MessageBox from "sap/m/MessageBox";
```

and, after `formatRevisionBadge`:

```ts
  /**
   * The payment state of one invoice, as the lists and detail pages read it.
   * `PaidOn` holds the day the payment was recorded, but no screen shows it
   * (spec decision 3): a set date is "paid" and null is "unpaid", nothing more.
   *
   * "unbezahlt" rather than "offen": the grave overview already says "offen"
   * for a half-year with no invoice, and the two columns sit next to each other.
   */
  public paidStatusText(paidOn: string | null | undefined): string {
    return this.i18nText(paidOn ? "statusPaid" : "statusUnpaid");
  }

  /**
   * Warning, not Error, for an unpaid invoice: one that was just written is the
   * normal case, unlike a grave whose half-year has no invoice at all.
   */
  public paidStatusState(paidOn: string | null | undefined): string {
    return paidOn ? "Success" : "Warning";
  }

  public paidActionIcon(paidOn: string | null | undefined): string {
    return paidOn ? "sap-icon://decline" : "sap-icon://money-bills";
  }

  public paidActionTooltip(paidOn: string | null | undefined): string {
    return this.i18nText(paidOn ? "actionMarkUnpaid" : "actionMarkPaid");
  }

  /** A paid invoice cannot be revised, so its edit button is dead. The backend
   *  refuses either way; this only keeps a dialog from opening on a save that
   *  cannot succeed. */
  public paidEditEnabled(paidOn: string | null | undefined): boolean {
    return !paidOn;
  }

  /**
   * Marks an unpaid invoice paid straight away, or asks before taking an
   * existing mark back — unmarking discards the recorded day, which nothing in
   * the UI could show again.
   *
   * Answers whether anything changed, so each caller refreshes its own model
   * (a list binding, a JSON model, a whole detail page) rather than this
   * guessing which one it is in.
   */
  protected async togglePaid(invoiceId: number, paidOn: string | null): Promise<boolean> {
    if (paidOn && !(await this.confirmUnmark())) {
      return false;
    }

    try {
      await api.setInvoicePaid(invoiceId, !paidOn);
      return true;
    } catch (error) {
      api.showError(this, error);
      return false;
    }
  }

  /** `MessageBox.confirm` reports the pressed action through a callback; this
   *  turns it into something the caller can await. */
  private confirmUnmark(): Promise<boolean> {
    return new Promise((resolve) => {
      MessageBox.confirm(this.i18nText("confirmMarkUnpaid"), {
        onClose: (action: string) => resolve(action === MessageBox.Action.OK),
      });
    });
  }
```

- [ ] **Step 3: Refuse to open the grave-invoice dialog on a paid half-year**

The backend refuses the save with `GraveInvoicePaid`, but by then the user has
filled a dialog. In `frontend/src/controller/GraveEdit.controller.ts`, inside
`openGraveInvoiceDialog`, directly after the group is fetched:

```ts
// One edit revises every recipient of the half-year, so one paid recipient
// blocks all of them. The group knows who they are; saying so here beats
// letting the user fill a dialog the backend will refuse.
const paid = group.Recipients.filter((recipient) => recipient.Paid);
if (paid.length) {
  MessageBox.error(
    this.i18nText("errorGraveInvoicePaidNames").replace(
      "{0}",
      paid.map((recipient) => recipient.Name).join(", "),
    ),
  );
  return;
}
```

Add `import MessageBox from "sap/m/MessageBox";` to that file if it is not
already imported.

- [ ] **Step 4: Add the German texts**

Append to `frontend/src/i18n/i18n.properties`:

```properties
paidStatus=Zahlung
statusPaid=bezahlt
statusUnpaid=unbezahlt
statusPartlyPaid=teilweise bezahlt
actionMarkPaid=Als bezahlt markieren
actionMarkUnpaid=Zahlung zurücknehmen
confirmMarkUnpaid=Soll die Zahlung wirklich zurückgenommen werden?
editBlockedPaid=Eine bezahlte Rechnung kann nicht bearbeitet werden.
paymentFilter=Zahlung
paymentFilterAll=Alle
paymentFilterUnpaid=Nur unbezahlte
paymentFilterPaid=Nur bezahlte
errorInvoicePaid=Die Rechnung ist bezahlt und kann nicht bearbeitet werden.
errorGraveInvoicePaid=Rechnungen dieses Grabs und Halbjahrs sind bezahlt und können nicht bearbeitet werden.
errorGraveInvoicePaidNames=Bezahlt sind bereits die Rechnungen von {0}. Nehmen Sie die Zahlung zurück, um die Rechnungen zu bearbeiten.
errorNotLatestRevision=Nur die aktuelle Revision einer Rechnung kann als bezahlt markiert werden.
```

- [ ] **Step 5: Add the English texts**

Append the same keys to `frontend/src/i18n/i18n_en.properties`:

```properties
paidStatus=Payment
statusPaid=paid
statusUnpaid=unpaid
statusPartlyPaid=partly paid
actionMarkPaid=Mark as paid
actionMarkUnpaid=Undo payment
confirmMarkUnpaid=Really take this payment back?
editBlockedPaid=A paid invoice cannot be edited.
paymentFilter=Payment
paymentFilterAll=All
paymentFilterUnpaid=Unpaid only
paymentFilterPaid=Paid only
errorInvoicePaid=The invoice is paid and cannot be edited.
errorGraveInvoicePaid=Invoices of this grave and half-year are paid and cannot be edited.
errorGraveInvoicePaidNames=The invoices of {0} are already paid. Take the payment back to edit these invoices.
errorNotLatestRevision=Only the latest revision of an invoice can be marked as paid.
```

- [ ] **Step 6: Run the frontend checks**

Run the frontend checks (see Global Constraints).
Expected: all three clean.

- [ ] **Step 7: Commit**

```bash
git add frontend/src/model/api.ts frontend/src/controller/Base.controller.ts \
        frontend/src/controller/GraveEdit.controller.ts frontend/src/i18n
git commit -m "feat(frontend): shared payment status texts and actions"
```

---

### Task 6: The invoices page — a column, a toggle, a filter

**Files:**

- Modify: `frontend/src/view/Invoices.view.xml`
- Modify: `frontend/src/controller/Invoices.controller.ts`
- Modify: `frontend/src/model/state.ts`
- Test: `e2e/tests/invoices.spec.ts`

**Interfaces:**

- Consumes: everything Task 5 produced.
- Produces: `InvoicesView` in `state.ts` (`{ paymentFilter: string }`), bound to
  the toolbar's payment `Select` under the model name `view`.

- [ ] **Step 1: Write the failing E2E test**

Append to `e2e/tests/invoices.spec.ts`. It reuses the file's seeded recipient
and item, and asserts on its own row only — parallel specs write to the same
stack:

```ts
test("mark an invoice paid, filter by payment, and take it back (Story 4)", async ({ page }) => {
  await seedMasterData(page);
  await page.goto("/index.html#/invoices");
  const invoicesTable = page.locator("[id$='invoicesTable-listUl']");

  // a row of this run's own, so the assertions below cannot pick up an invoice
  // another spec or an earlier run created
  await page.getByRole("button", { name: "Rechnung erstellen" }).click();
  await waitForDialogOpen(page);
  await pickRecipient(page, RECIPIENT_NAME);
  const lineInput = page.locator(".sapMDialog input[id*='lineItem'][id$='-inner']");
  await lineInput.click();
  await lineInput.fill("9001");
  await suggestions(page, "lineItem").filter({ hasText: "9001" }).click();
  await page.getByLabel("Anzahl").fill("1");
  await page.getByLabel("Preis").fill("50");
  await page.getByRole("button", { name: "Speichern" }).click();

  const row = invoicesTable.locator("tbody tr", { hasText: RECIPIENT_NAME }).first();
  await expect(row).toContainText("unbezahlt");

  await row.getByRole("button", { name: "Als bezahlt markieren", exact: true }).click();
  await expect(row).toContainText("bezahlt");
  // a paid invoice cannot be revised, so its edit button is dead
  await expect(row.getByRole("button", { name: "Bearbeiten", exact: true })).toBeDisabled();

  // the unpaid filter drops it, the paid filter keeps it
  const paymentFilter = page.locator("[id$='paymentFilter']");
  await paymentFilter.click();
  await page.getByRole("option", { name: "Nur unbezahlte" }).click();
  await expect(invoicesTable.locator("tbody tr", { hasText: RECIPIENT_NAME })).toHaveCount(0);
  await paymentFilter.click();
  await page.getByRole("option", { name: "Nur bezahlte" }).click();
  await expect(invoicesTable.locator("tbody tr", { hasText: RECIPIENT_NAME })).toHaveCount(1);
  await paymentFilter.click();
  await page.getByRole("option", { name: "Alle" }).click();

  // taking the payment back asks first
  await row.getByRole("button", { name: "Zahlung zurücknehmen", exact: true }).click();
  await waitForDialogOpen(page);
  await page.locator(".sapMDialog.sapMMessageBox").getByRole("button", { name: "OK" }).click();
  await expect(row).toContainText("unbezahlt");
  await expect(row.getByRole("button", { name: "Bearbeiten", exact: true })).toBeEnabled();
});
```

- [ ] **Step 2: Run the E2E spec to verify it fails**

```sh
cd e2e && npm test -- --project=desktop tests/invoices.spec.ts
```

Expected: FAIL — no "unbezahlt" text and no payment buttons exist yet.

- [ ] **Step 3: Add the view state**

In `frontend/src/model/state.ts`, next to the other `*View` interfaces:

```ts
/** The invoice overview's toolbar state. */
export interface InvoicesView {
  /** "all" | "unpaid" | "paid" — the payment filter's key. */
  paymentFilter: string;
}
```

- [ ] **Step 4: Add the column, the button and the filter to the view**

In `frontend/src/view/Invoices.view.xml`:

Add `xmlns:core="sap.ui.core"` to the root `<mvc:View>` element (the `Select`
items below need `core:Item`).

In the toolbar, between the `SearchField` and the `ToolbarSpacer`:

```xml
                <Label text="{i18n>paymentFilter}" labelFor="paymentFilter">
                    <layoutData>
                        <OverflowToolbarLayoutData priority="Low" group="1" />
                    </layoutData>
                </Label>
                <Select
                    id="paymentFilter"
                    width="10rem"
                    change=".onPaymentFilterChange"
                    selectedKey="{view>/paymentFilter}"
                >
                    <core:Item key="all" text="{i18n>paymentFilterAll}" />
                    <core:Item key="unpaid" text="{i18n>paymentFilterUnpaid}" />
                    <core:Item key="paid" text="{i18n>paymentFilterPaid}" />
                    <layoutData>
                        <OverflowToolbarLayoutData priority="Low" group="1" />
                    </layoutData>
                </Select>
```

Add a column between the revision column and the action column:

```xml
<Column importance="High" popinDisplay="Inline">
                        <Text text="{i18n>paidStatus}" />
                    </Column>
```

Add the matching cell after the revision `HBox`:

```xml
<ObjectStatus
  text="{path: 'odata>PaidOn', formatter: '.paidStatusText'}"
  state="{path: 'odata>PaidOn', formatter: '.paidStatusState'}"
/>
```

Widen the action column from `11rem` to `13rem` (five buttons now), and add the
toggle to the action `HBox`, after the edit button:

```xml
<Button
  icon="{path: 'odata>PaidOn', formatter: '.paidActionIcon'}"
  type="Transparent"
  tooltip="{path: 'odata>PaidOn', formatter: '.paidActionTooltip'}"
  press=".onTogglePaid"
/>
```

and make the edit button die on a paid row:

```xml
<Button
  icon="sap-icon://edit"
  type="Transparent"
  enabled="{path: 'odata>PaidOn', formatter: '.paidEditEnabled'}"
  tooltip="{i18n>actionEdit}"
  press=".onEditInvoice"
/>
```

- [ ] **Step 5: Wire the controller**

In `frontend/src/controller/Invoices.controller.ts`, extend `onInit` and add the
two handlers:

```ts
  public onInit(): void {
    this.bindODataRoot();
    this.setState<InvoicesView>("view", { paymentFilter: "all" });
  }

  public async onTogglePaid(event: Button$PressEvent): Promise<void> {
    const context = event.getSource().getBindingContext("odata") as Context;
    const changed = await this.togglePaid(
      context.getProperty("Id") as number,
      context.getProperty("PaidOn") as string | null,
    );
    if (changed) {
      this.odata().refresh();
    }
  }

  /**
   * The payment filter and the static list filter are one `$filter`, so the
   * clause is composed rather than set: `changeParameters` is also what the
   * search field uses, and `search` is a separate query option, so a filtered
   * search stays filtered.
   */
  public onPaymentFilterChange(): void {
    const key = this.state<InvoicesView>("view").getProperty("/paymentFilter");
    const payment =
      key === "unpaid" ? " and PaidOn eq null" : key === "paid" ? " and PaidOn ne null" : "";
    const binding = (this.byId("invoicesTable") as Table).getBinding("items") as ODataListBinding;
    binding.changeParameters({
      $filter: `GraveId eq null and IsLatestRevision eq true${payment}`,
    });
  }
```

with the imports

```ts
import type { InvoicesView } from "../model/state";
```

- [ ] **Step 6: Run the frontend checks**

Run the frontend checks.
Expected: all three clean.

- [ ] **Step 7: Run the E2E spec to verify it passes**

```sh
cd e2e && npm test -- --project=desktop tests/invoices.spec.ts
```

Expected: PASS, the whole file.

- [ ] **Step 8: Commit**

```bash
git add frontend/src/view/Invoices.view.xml frontend/src/controller/Invoices.controller.ts \
        frontend/src/model/state.ts e2e/tests/invoices.spec.ts
git commit -m "feat(invoices): mark invoices paid and filter the list by payment"
```

---

### Task 7: The graves page — a payment column and an unpaid filter

**Files:**

- Modify: `frontend/src/view/Graves.view.xml`
- Modify: `frontend/src/controller/Graves.controller.ts`
- Modify: `frontend/src/model/state.ts` (`GravesView`)
- Test: `e2e/tests/graves.spec.ts`

**Interfaces:**

- Consumes: `GraveStatusRow.InvoiceCount` / `.PaidCount` (Task 5),
  `paidStatusText`-style formatters (Task 5).
- Produces: `Graves.paymentText(row)` / `Graves.paymentState(row)` formatters
  bound against the whole `status>` row, and `GravesView.paymentFilter`.

- [ ] **Step 1: Write the failing E2E test**

Append to `e2e/tests/graves.spec.ts` — after the tests already there, since only
this file may trigger generation. It works on graves this run created and
invoiced, and reads the row by its unique grave number:

```ts
test("a half-year reads partly paid until every recipient has paid (Story 6)", async ({ page }) => {
  // the file's earlier tests have already generated this half-year's invoices;
  // this one only marks them, so it starts no job of its own
  await page.goto("/index.html#/graves");

  const gravesTable = page.locator("[id$='gravesTable-listUl']");
  // any grave of this run that has invoices for the current half-year
  const invoices = await (
    await page.request.get(
      `/odata/Invoices?$filter=GraveId ne null and PeriodYear eq ${CURRENT_YEAR}` +
        ` and PeriodHalf eq ${CURRENT_HALF} and IsLatestRevision eq true&$orderby=Id desc&$top=50`,
    )
  ).json();
  const byGrave = new Map<number, number[]>();
  for (const invoice of invoices.value as any[]) {
    byGrave.set(invoice.GraveId, [...(byGrave.get(invoice.GraveId) ?? []), invoice.Id]);
  }
  // a grave billed to more than one recipient is what makes "teilweise bezahlt"
  // observable at all
  const [graveId, invoiceIds] = [...byGrave].find(([, ids]) => ids.length > 1)!;
  const grave = await (await page.request.get(`/odata/Graves(${graveId})`)).json();
  const row = gravesTable.locator("tbody tr", { hasText: grave.GraveNumber }).first();

  await page.request.post("/odata/SetInvoicePaid", {
    data: { invoiceId: invoiceIds[0], paid: true },
  });
  await page.reload();
  await expect(row).toContainText("teilweise bezahlt");

  for (const invoiceId of invoiceIds.slice(1)) {
    await page.request.post("/odata/SetInvoicePaid", { data: { invoiceId, paid: true } });
  }
  await page.reload();
  await expect(row).toContainText("bezahlt");

  // fully paid, so the unpaid filter drops it
  await page.locator("[id$='paymentFilter']").click();
  await page.getByRole("option", { name: "Nur unbezahlte" }).click();
  await expect(gravesTable.locator("tbody tr", { hasText: grave.GraveNumber })).toHaveCount(0);
});
```

- [ ] **Step 2: Run the E2E spec to verify it fails**

```sh
cd e2e && npm test -- --project=desktop tests/graves.spec.ts
```

Expected: FAIL — no payment column and no `paymentFilter` control.

- [ ] **Step 3: Extend the view state**

In `frontend/src/model/state.ts`, add to `GravesView`:

```ts
/** "all" | "unpaid" — the payment filter's key. */
paymentFilter: string;
```

- [ ] **Step 4: Add the column and the filter to the view**

In `frontend/src/view/Graves.view.xml`, add to the toolbar after the half-year
`Select` (same overflow group, so label and select still travel together — give
the payment pair its own group `2`):

```xml
                <Label text="{i18n>paymentFilter}" labelFor="paymentFilter">
                    <layoutData>
                        <OverflowToolbarLayoutData priority="Low" group="2" />
                    </layoutData>
                </Label>
                <Select
                    id="paymentFilter"
                    width="10rem"
                    change=".onPaymentFilterChange"
                    selectedKey="{view>/paymentFilter}"
                >
                    <core:Item key="all" text="{i18n>paymentFilterAll}" />
                    <core:Item key="unpaid" text="{i18n>paymentFilterUnpaid}" />
                    <layoutData>
                        <OverflowToolbarLayoutData priority="Low" group="2" />
                    </layoutData>
                </Select>
```

Add a column after the invoicing status column:

```xml
<Column importance="High" popinDisplay="Inline">
                        <Text text="{i18n>paidStatus}" />
                    </Column>
```

and the matching cell after the invoicing `ObjectStatus`. The state comes from
two properties, so each formatter takes both through `parts` rather than being
bound to the row object:

```xml
<!-- empty while the half-year has no invoice: the
                                 status beside it already reports that in red -->
<ObjectStatus
  text="{parts: ['status>InvoiceCount', 'status>PaidCount'], formatter: '.paymentText'}"
  state="{parts: ['status>InvoiceCount', 'status>PaidCount'], formatter: '.paymentState'}"
  visible="{= ${status>InvoiceCount} > 0}"
/>
```

- [ ] **Step 5: Wire the controller**

In `frontend/src/controller/Graves.controller.ts`, set the new state in
`onInit`:

```ts
this.setState<GravesView>("view", {
  halfYearOptions: api.halfYearOptions(2, 1),
  halfYear: `${year}-${half}`,
  paymentFilter: "all",
});
```

add the two formatters and the filter handler:

```ts
  /**
   * How much of the selected half-year this grave has paid. Takes both counts:
   * the state comes from the pair, not from one property.
   */
  public paymentText(invoiceCount: number, paidCount: number): string {
    if (!invoiceCount) {
      return "";
    }
    if (paidCount === 0) {
      return this.i18nText("statusUnpaid");
    }
    return this.i18nText(paidCount < invoiceCount ? "statusPartlyPaid" : "statusPaid");
  }

  public paymentState(invoiceCount: number, paidCount: number): string {
    return invoiceCount > 0 && paidCount === invoiceCount ? "Success" : "Warning";
  }

  public onPaymentFilterChange(): void {
    this.applyRows();
  }
```

and narrow the rows in `applyRows`, where the search already narrows them:

```ts
  /// The rows the table shows: the status order, narrowed to the last search
  /// and to the payment filter.
  private applyRows(): void {
    const searched = this._searchIds
      ? this._rows.filter((row) => this._searchIds!.has(Number(row.Id)))
      : this._rows;
    // "only unpaid" means money is outstanding, so a grave with nothing
    // invoiced for this half-year is not in it — the invoicing status is where
    // that grave is already visible
    const rows =
      this.state<GravesView>("view").getProperty("/paymentFilter") === "unpaid"
        ? searched.filter((row) => row.InvoiceCount > 0 && row.PaidCount < row.InvoiceCount)
        : searched;
    this.state<GravesStatus>("status").setProperty("/graves", rows);
  }
```

- [ ] **Step 6: Run the frontend checks**

Run the frontend checks.
Expected: all three clean.

- [ ] **Step 7: Run the E2E spec to verify it passes**

```sh
cd e2e && npm test -- --project=desktop tests/graves.spec.ts
```

Expected: PASS, the whole file.

- [ ] **Step 8: Commit**

```bash
git add frontend/src/view/Graves.view.xml frontend/src/controller/Graves.controller.ts \
        frontend/src/model/state.ts e2e/tests/graves.spec.ts
git commit -m "feat(graves): show and filter a half-year's payment state"
```

---

### Task 8: The detail pages

**Files:**

- Modify: `frontend/src/view/GraveDetail.view.xml`, `frontend/src/controller/GraveDetail.controller.ts`
- Modify: `frontend/src/view/InvoiceDetail.view.xml`, `frontend/src/controller/InvoiceDetail.controller.ts`
- Modify: `frontend/src/view/GraveInvoiceDetail.view.xml`, `frontend/src/controller/GraveInvoiceDetail.controller.ts`
- Modify: `frontend/src/model/state.ts` (`GraveDetailView.Invoices`, `RevisionDetailView`)

**Interfaces:**

- Consumes: everything Task 5 produced.
- Produces: `GraveInvoiceRow.PaidOn: string | null` on the grave detail page's
  rows, and `RevisionDetailView.paidOn: string | null` on both detail pages.

- [ ] **Step 1: Carry `PaidOn` into the grave detail rows**

In `frontend/src/controller/GraveDetail.controller.ts`, add to the
`GraveInvoiceRow` interface:

```ts
/** Set when this recipient's invoice is paid; the date itself is never shown. */
PaidOn: string | null;
```

and to the row built in `refresh()`, next to `GeneratedAt`:

```ts
            PaidOn: invoice.PaidOn,
```

- [ ] **Step 2: Show and toggle it on the grave detail page**

In `frontend/src/view/GraveDetail.view.xml`, in the grave invoices table, add a
column after the gross total:

```xml
<Column importance="High" popinDisplay="Inline">
                            <Text text="{i18n>paidStatus}" />
                        </Column>
```

the matching cell after the gross total cell:

```xml
<ObjectStatus
  text="{path: 'grave>PaidOn', formatter: '.paidStatusText'}"
  state="{path: 'grave>PaidOn', formatter: '.paidStatusState'}"
/>
```

widen the action column from `11rem` to `13rem`, make the edit button die on a
paid row:

```xml
<Button
  icon="sap-icon://edit"
  type="Transparent"
  enabled="{path: 'grave>PaidOn', formatter: '.paidEditEnabled'}"
  tooltip="{i18n>actionEdit}"
  press=".onEditGraveInvoice"
/>
```

and add the toggle after it:

```xml
<Button
  icon="{path: 'grave>PaidOn', formatter: '.paidActionIcon'}"
  type="Transparent"
  tooltip="{path: 'grave>PaidOn', formatter: '.paidActionTooltip'}"
  press=".onToggleInvoicePaid"
/>
```

In `frontend/src/controller/GraveDetail.controller.ts`, add the handler next to
`onEditGraveInvoice`:

```ts
  public async onToggleInvoicePaid(event: Button$PressEvent): Promise<void> {
    const context = event.getSource().getBindingContext("grave")!;
    const changed = await this.togglePaid(
      Number(context.getProperty("Id")),
      context.getProperty("PaidOn") as string | null,
    );
    if (changed) {
      // the page's header status and the row are built from one read
      await this.refresh();
    }
  }
```

`refresh` is currently `private`; change it to `protected` so nothing else has
to move.

- [ ] **Step 3: Carry `paidOn` into the shared revision detail state**

In `frontend/src/model/state.ts`, add to `RevisionDetailView`:

```ts
/** The latest revision's payment mark; null while it is unpaid. */
paidOn: string | null;
```

and to `emptyRevisionDetail()`:

```ts
    paidOn: null,
```

In `frontend/src/controller/InvoiceRevisions.controller.ts`, in
`buildRevisionDetail`'s returned object (the block that reads from `latest`):

```ts
      paidOn: latest.PaidOn,
```

- [ ] **Step 4: Show and toggle it on both detail pages**

In `frontend/src/view/InvoiceDetail.view.xml`, replace the `headerContent` with:

```xml
<headerContent>
            <ObjectStatus
    text="{path: 'detail>/paidOn', formatter: '.paidStatusText'}"
    state="{path: 'detail>/paidOn', formatter: '.paidStatusState'}"
    class="sapUiSmallMarginEnd"
  />
            <Button
    icon="{path: 'detail>/paidOn', formatter: '.paidActionIcon'}"
    tooltip="{path: 'detail>/paidOn', formatter: '.paidActionTooltip'}"
    press=".onTogglePaid"
  />
            <Button
    icon="sap-icon://edit"
    enabled="{path: 'detail>/paidOn', formatter: '.paidEditEnabled'}"
    tooltip="{i18n>actionEdit}"
    press=".onEditInvoice"
  />
        </headerContent>
```

In `frontend/src/controller/InvoiceDetail.controller.ts`, add:

```ts
  public async onTogglePaid(): Promise<void> {
    const model = this.state<RevisionDetailView>("detail");
    const invoiceId = model.getProperty("/latestInvoiceId");
    if (invoiceId == null) {
      return;
    }

    if (await this.togglePaid(invoiceId, model.getProperty("/paidOn"))) {
      await this.refresh();
    }
  }
```

`refresh` there is `private`; change it to `protected`.

Do the same in `frontend/src/view/GraveInvoiceDetail.view.xml` — the same three
controls in `headerContent`, with `press=".onEditGraveInvoice"` on the edit
button — and add the identical `onTogglePaid` to
`frontend/src/controller/GraveInvoiceDetail.controller.ts`, changing its
`refresh` to `protected` as well.

- [ ] **Step 5: Run the frontend checks**

Run the frontend checks.
Expected: all three clean.

- [ ] **Step 6: Check the pages by hand against the E2E stack**

Never against the dev stack. Start the E2E stack, mark an invoice from the grave
detail page, and confirm the header of both detail pages follows:

```sh
cd e2e && npx playwright test --project=desktop tests/graves.spec.ts tests/invoices.spec.ts
```

Expected: PASS — Tasks 6 and 7's specs still pass with the detail pages changed.

- [ ] **Step 7: Commit**

```bash
git add frontend/src/view frontend/src/controller frontend/src/model/state.ts
git commit -m "feat(frontend): payment status on the invoice and grave detail pages"
```

---

### Task 9: Bring the specification up to date

**Files:**

- Modify: `docs/spec.md`

**Interfaces:**

- Consumes: the behaviour of Tasks 1–8.
- Produces: nothing code depends on.

- [ ] **Step 1: Add the functional requirements**

In `docs/spec.md`, continue the `FR-` numbering after the last existing entry
(check the file — it currently ends at `FR-068`) and append:

```markdown
- **FR-069**: Users MUST be able to mark any invoice, including a grave invoice,
  as paid, and to take that mark back.
- **FR-070**: The system MUST record the day an invoice was marked paid. The
  user interface MUST show only whether an invoice is paid, never that day.
- **FR-071**: Marking an invoice paid MUST apply to the individual invoice. Each
  recipient of a grave's half-year MUST be markable independently.
- **FR-072**: Taking a payment mark back MUST require a confirmation.
- **FR-073**: The system MUST refuse to revise an invoice that is marked paid.
- **FR-074**: The system MUST refuse to edit a grave invoice while any recipient
  of that grave and half-year is marked paid, and MUST name those recipients.
- **FR-075**: Only the latest revision of an invoice MUST be markable as paid.
- **FR-076**: The invoice list MUST be filterable by payment status.
- **FR-077**: The grave overview MUST show, for the selected half-year, whether
  a grave's invoices are unpaid, partly paid or paid, and MUST be filterable to
  the graves that still have an unpaid invoice in that half-year.
```

- [ ] **Step 2: Check the numbering**

```sh
grep -c "^- \*\*FR-" docs/spec.md
grep -o "FR-[0-9]*" docs/spec.md | sort -u | tail -3
```

Expected: the last three are `FR-075`, `FR-076`, `FR-077`, and no number appears
twice.

- [ ] **Step 3: Commit**

```bash
git add docs/spec.md
git commit -m "docs(spec): record the payment status requirements"
```

---

## Done

The feature is complete when:

- The backend tests pass, including the new `PaymentTests.cs`.
- The frontend checks (`typecheck`, `lint`, `ui5lint`) are clean.
- The full E2E suite passes against its own stack: `cd e2e && npm test`.
- `git log` shows exactly one migration across the whole branch.
