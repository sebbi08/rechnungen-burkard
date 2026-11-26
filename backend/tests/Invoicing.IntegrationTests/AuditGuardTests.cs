using System.Net;
using Burkard.Invoicing.Api.Audit;
using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Burkard.Invoicing.IntegrationTests;

/// <summary>The audit log's storage and its guard: an armed scope refuses a
/// change to a grave or an invoice that no audit action declared, and an action
/// writes its entries in the transaction of the change.</summary>
public class AuditGuardTests
{
    private static Grave NewGrave(string number = "G-1") =>
        new()
        {
            GraveNumber = number,
            FieldNumber = "F-1",
            SiteName = "Alt",
            CemeteryId = TestCemetery.Id,
            StartDate = new DateOnly(2026, 1, 1),
            CreatedAt = new DateOnly(2026, 1, 1),
        };

    private static async Task<long> SeedGraveAsync(ApiTestHost host)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var grave = NewGrave();
        db.Graves.Add(grave);
        await db.SaveChangesAsync();
        return grave.Id;
    }

    private static IReadOnlyList<AuditChange> Paid() =>
        [new AuditChange("paid", null, "false", "true", "bool")];

    [Fact]
    public void An_armed_scope_refuses_a_grave_change_without_an_action()
    {
        using var host = new ApiTestHost();
        using var scope = host.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<AuditScope>().Arm();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        db.Graves.Add(NewGrave());

        var error = Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
        Assert.Contains("without a declared audit action", error.Message);
        Assert.Contains("Grave", error.Message);
    }

    [Fact]
    public void An_unarmed_scope_seeds_freely()
    {
        using var host = new ApiTestHost();
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        db.Graves.Add(NewGrave());
        db.SaveChanges();

        Assert.Equal(1, db.Graves.Count());
        Assert.Empty(db.AuditEntries);
    }

    [Fact]
    public async Task Record_mode_writes_an_unattributed_entry()
    {
        using var host = new ApiTestHost
        {
            ConfigureTestServices = s =>
                s.AddSingleton(typeof(AuditGuardMode), AuditGuardMode.Record),
        };
        var graveId = await SeedGraveAsync(host);

        using (var scope = host.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<AuditScope>().Arm();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var grave = await db.Graves.SingleAsync(g => g.Id == graveId);
            grave.SiteName = "Neu";
            await db.SaveChangesAsync();
        }

        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal("Neu", (await db.Graves.SingleAsync()).SiteName);
            var entry = Assert.Single(await db.AuditEntries.ToListAsync());
            Assert.Equal(AuditActions.Unattributed, entry.Action);
            Assert.Equal("Unattributed", entry.Action);
            Assert.Equal(graveId, entry.GraveId);
            Assert.Null(entry.InvoiceRootId);
        }
    }

    [Fact]
    public async Task An_action_must_complete_inside_a_transaction()
    {
        using var host = new ApiTestHost();
        using var scope = host.Services.CreateScope();
        var audit = scope.ServiceProvider.GetRequiredService<AuditScope>();

        using var action = audit.Begin(AuditActions.InvoicePaid);
        action.Record(new AuditSubject.Invoice(1), Paid());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            action.CompleteAsync(CancellationToken.None)
        );
    }

    [Fact]
    public async Task A_rolled_back_action_leaves_no_entry()
    {
        using var host = new ApiTestHost();
        using (var scope = host.Services.CreateScope())
        {
            var audit = scope.ServiceProvider.GetRequiredService<AuditScope>();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await using var transaction = await db.Database.BeginTransactionAsync();
            using (var action = audit.Begin(AuditActions.InvoicePaid))
            {
                action.Record(new AuditSubject.Invoice(1), Paid());
                await action.CompleteAsync(CancellationToken.None);
            }

            // written, but only inside the transaction
            Assert.Equal(1, await db.AuditEntries.CountAsync());
            await transaction.RollbackAsync();
        }

        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Empty(await db.AuditEntries.ToListAsync());
        }
    }

    [Fact]
    public async Task The_entries_are_read_by_grave_and_by_invoice()
    {
        using var host = new ApiTestHost();
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.AuditEntries.AddRange(
                new AuditEntry
                {
                    OccurredAt = new DateTime(2026, 11, 1, 8, 0, 0, DateTimeKind.Utc),
                    Action = AuditActions.GraveCreated,
                    GraveId = 5,
                },
                new AuditEntry
                {
                    OccurredAt = new DateTime(2026, 11, 2, 8, 0, 0, DateTimeKind.Utc),
                    Action = AuditActions.GraveEdited,
                    GraveId = 5,
                },
                new AuditEntry
                {
                    OccurredAt = new DateTime(2026, 11, 3, 8, 0, 0, DateTimeKind.Utc),
                    Action = AuditActions.InvoicePaid,
                    InvoiceRootId = 5,
                }
            );
            await db.SaveChangesAsync();
        }

        using var client = host.CreateClient();

        var byGrave = await client.GetAsync(
            "/odata/AuditEntries?$filter=GraveId eq 5&$orderby=OccurredAt desc"
        );
        Assert.Equal(HttpStatusCode.OK, byGrave.StatusCode);
        using (var json = await ODataJson.ReadAsync(byGrave))
        {
            var actions = json
                .RootElement.GetProperty("value")
                .EnumerateArray()
                .Select(e => e.GetProperty("Action").GetString())
                .ToList();
            Assert.Equal([AuditActions.GraveEdited, AuditActions.GraveCreated], actions);
        }

        using (
            var json = await ODataJson.ReadAsync(
                await client.GetAsync("/odata/AuditEntries?$filter=InvoiceRootId eq 5")
            )
        )
        {
            var entry = Assert.Single(json.RootElement.GetProperty("value").EnumerateArray());
            Assert.Equal(AuditActions.InvoicePaid, entry.GetProperty("Action").GetString());
            Assert.Equal("[]", entry.GetProperty("ChangesJson").GetString());
        }
    }

    [Fact]
    public async Task The_entries_take_no_writes()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();

        var response = await client.PostAsync(
            "/odata/AuditEntries",
            ODataJson.Body(new { Action = "GraveEdited", GraveId = 5 })
        );

        Assert.False(response.IsSuccessStatusCode);
    }

    [Fact]
    public async Task An_open_action_refuses_a_grave_it_does_not_watch()
    {
        using var host = new ApiTestHost();
        var graveId = await SeedGraveAsync(host);
        using var scope = host.Services.CreateScope();
        var audit = scope.ServiceProvider.GetRequiredService<AuditScope>();
        audit.Arm();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        using var action = audit.Begin(AuditActions.GraveEdited);
        (await db.Graves.SingleAsync(g => g.Id == graveId)).SiteName = "Neu";
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());

        // an added grave counts as watched only when it was handed to the action
        db.ChangeTracker.Clear();
        db.Graves.Add(NewGrave("G-2"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task A_watched_grave_records_what_changed_and_nothing_when_unchanged()
    {
        using var host = new ApiTestHost();
        var changedId = await SeedGraveAsync(host);
        var unchangedId = await SeedGraveAsync(host);

        using (var scope = host.Services.CreateScope())
        {
            var audit = scope.ServiceProvider.GetRequiredService<AuditScope>();
            audit.Arm();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await using var transaction = await db.Database.BeginTransactionAsync();
            using (var action = audit.Begin(AuditActions.GraveEdited))
            {
                await action.WatchGravesAsync([changedId, unchangedId], CancellationToken.None);
                var created = NewGrave("G-3");
                action.WatchNewGrave(created, AuditActions.GraveCreated);
                db.Graves.Add(created);
                (await db.Graves.SingleAsync(g => g.Id == changedId)).SiteName = "Neu";
                // a replaced row with the same values is no change
                var unchanged = await db.Graves.SingleAsync(g => g.Id == unchangedId);
                unchanged.SiteName = "Zwischendurch";
                await db.SaveChangesAsync();
                unchanged.SiteName = "Alt";
                await db.SaveChangesAsync();
                await action.CompleteAsync(CancellationToken.None);
            }

            await transaction.CommitAsync();
        }

        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var entries = await db.AuditEntries.OrderBy(e => e.Id).ToListAsync();
            Assert.Equal(2, entries.Count);
            Assert.Equal(AuditActions.GraveEdited, entries[0].Action);
            Assert.Equal(changedId, entries[0].GraveId);
            Assert.Contains("\"field\":\"siteName\"", entries[0].ChangesJson);
            Assert.Contains("\"old\":\"Alt\"", entries[0].ChangesJson);
            Assert.Contains("\"new\":\"Neu\"", entries[0].ChangesJson);
            Assert.Equal(new DateTime(2026, 10, 6, 12, 0, 0), entries[0].OccurredAt);
            Assert.Equal(AuditActions.GraveCreated, entries[1].Action);
            Assert.Contains("\"new\":\"G-3\"", entries[1].ChangesJson);
        }
    }

    [Fact]
    public async Task An_armed_action_refuses_an_invoice_change_it_did_not_record()
    {
        using var host = new ApiTestHost();
        using var scope = host.Services.CreateScope();
        var audit = scope.ServiceProvider.GetRequiredService<AuditScope>();
        audit.Arm();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync();

        using var action = audit.Begin(AuditActions.InvoiceCreated);
        var invoice = new Invoice
        {
            Year = 2026,
            Sequence = 1,
            InvoiceNumber = "R2026-0001",
            RecipientName = "Anna Beispiel",
        };
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync();
        invoice.RootId = invoice.Id;
        await db.SaveChangesAsync();
        action.Record(new AuditSubject.Invoice(invoice.Id + 1), Paid());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            action.CompleteAsync(CancellationToken.None)
        );
    }

    [Fact]
    public async Task Records_for_one_subject_and_action_merge_into_one_entry()
    {
        using var host = new ApiTestHost();
        using var scope = host.Services.CreateScope();
        var audit = scope.ServiceProvider.GetRequiredService<AuditScope>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync();

        using (var action = audit.Begin(AuditActions.GraveInvoicesGenerated))
        {
            action.Record(
                new AuditSubject.Grave(5),
                [new AuditChange("invoice", null, null, "1:R2026-0001", "invoice")],
                details: new Dictionary<string, string> { ["period"] = "2026-2" }
            );
            action.Record(
                new AuditSubject.Grave(5),
                [new AuditChange("invoice", null, null, "2:R2026-0002", "invoice")]
            );
            action.Record(new AuditSubject.Invoice(1), [], action: AuditActions.InvoiceCreated);
            Assert.Throws<InvalidOperationException>(() => audit.Begin(AuditActions.GraveEdited));
            await action.CompleteAsync(CancellationToken.None);
        }

        Assert.Null(audit.Current);
        var entries = await db.AuditEntries.OrderBy(e => e.Id).ToListAsync();
        Assert.Equal(2, entries.Count);
        Assert.Equal(5, entries[0].GraveId);
        Assert.Contains("1:R2026-0001", entries[0].ChangesJson);
        Assert.Contains("2:R2026-0002", entries[0].ChangesJson);
        Assert.Equal("{\"period\":\"2026-2\"}", entries[0].DetailsJson);
        Assert.Equal(AuditActions.InvoiceCreated, entries[1].Action);
        Assert.Equal(1, entries[1].InvoiceRootId);
        Assert.Equal("[]", entries[1].ChangesJson);
    }
}
