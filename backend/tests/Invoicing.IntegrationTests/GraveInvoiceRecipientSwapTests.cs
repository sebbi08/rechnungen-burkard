using System.Net;
using System.Text.Json;
using Burkard.Invoicing.Api.Audit;
using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Burkard.Invoicing.IntegrationTests;

/// <summary>Revising a grave invoice may give one invoice of the half-year to
/// another recipient: the split and the grave's own recipients stay.</summary>
public class GraveInvoiceRecipientSwapTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private sealed record Seed(long GraveId, long ItemId, long AnnaId, long BerndId, long CarlaId);

    /// <summary>A grave billing the item twice at 100.00 for Anna and Bernd,
    /// invoiced for 2031 H1. Carla has an address, Dora none.</summary>
    private static async Task<(Seed Seed, long DoraId)> SeedAsync(ApiTestHost host)
    {
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.VatRates.Add(new VatRate { Rate = 7m });
            var item = new InvoiceItem
            {
                Number = "2001",
                Description = "Grabpflege",
                VatRate = 7m,
                DefaultPrice = 100m,
            };
            var anna = new Recipient
            {
                Number = "E-0001",
                Name = "Anna Beispiel",
                Address = "A 1",
            };
            var bernd = new Recipient
            {
                Number = "E-0002",
                Name = "Bernd Beispiel",
                Address = "B 2",
            };
            var carla = new Recipient
            {
                Number = "E-0003",
                Name = "Carla Beispiel",
                Address = "C 3",
            };
            var dora = new Recipient { Number = "E-0004", Name = "Dora Beispiel" };
            db.InvoiceItems.Add(item);
            db.Recipients.AddRange(anna, bernd, carla, dora);
            var grave = new Grave
            {
                GraveNumber = "G-400",
                CemeteryId = TestCemetery.Id,
                StartDate = new DateOnly(2020, 1, 1),
                EndDate = new DateOnly(2040, 12, 31),
                CreatedAt = new DateOnly(2026, 1, 1),
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
            db.GraveRecipients.AddRange(
                new GraveRecipient { GraveId = grave.Id, RecipientId = anna.Id },
                new GraveRecipient { GraveId = grave.Id, RecipientId = bernd.Id }
            );
            await db.SaveChangesAsync();
            using var client = host.CreateClient();
            await GraveGenerationHelper.RunAsync(client, 2031, 1);
            return (new Seed(grave.Id, item.Id, anna.Id, bernd.Id, carla.Id), dora.Id);
        }
    }

    private static async Task<List<Invoice>> InvoicesAsync(ApiTestHost host)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Invoices.AsNoTracking().OrderBy(i => i.Id).ToListAsync();
    }

    private static async Task<List<AuditEntry>> EntriesAsync(ApiTestHost host)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.AuditEntries.AsNoTracking().OrderBy(e => e.Id).ToListAsync();
    }

    private static Task<HttpResponseMessage> ReviseAsync(
        HttpClient client,
        long invoiceId,
        long itemId,
        params (long InvoiceId, long RecipientId)[] replacements
    ) =>
        client.PostAsync(
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
                            count = 2,
                            unitPrice = (decimal?)null,
                        },
                    },
                    replacements = replacements.Select(r => new
                    {
                        invoiceId = r.InvoiceId,
                        recipientId = r.RecipientId,
                    }),
                }
            )
        );

    private static async Task AssertRefusedAsync(HttpResponseMessage response, string code)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        Assert.Equal(code, json.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    private static async Task AssertRefusedWritingNothingAsync(
        ApiTestHost host,
        Func<HttpClient, Task<HttpResponseMessage>> request,
        string code
    )
    {
        var invoices = (await InvoicesAsync(host)).Count;
        var entries = (await EntriesAsync(host)).Count;
        using var client = host.CreateClient();

        await AssertRefusedAsync(await request(client), code);

        Assert.Equal(invoices, (await InvoicesAsync(host)).Count);
        Assert.Equal(entries, (await EntriesAsync(host)).Count);
    }

    [Fact]
    public async Task A_swap_gives_that_invoice_the_new_recipient()
    {
        using var host = new ApiTestHost();
        var (seed, _) = await SeedAsync(host);
        var originals = await InvoicesAsync(host);
        var anna = originals.Single(i => i.RecipientId == seed.AnnaId);
        var bernd = originals.Single(i => i.RecipientId == seed.BerndId);
        using var client = host.CreateClient();

        var response = await ReviseAsync(client, anna.Id, seed.ItemId, (bernd.Id, seed.CarlaId));
        response.EnsureSuccessStatusCode();

        var revisions = (await InvoicesAsync(host)).Where(i => i.IsLatestRevision).ToList();
        Assert.Equal(2, revisions.Count);
        Assert.All(revisions, r => Assert.EndsWith("-R1", r.InvoiceNumber));
        var carla = Assert.Single(revisions, r => r.RootId == bernd.RootId);
        Assert.Equal(
            (seed.CarlaId, "Carla Beispiel", "C 3", "E-0003"),
            (carla.RecipientId, carla.RecipientName, carla.RecipientAddress, carla.RecipientNumber)
        );
        Assert.Equal((50m, 2), (carla.SharePercent, carla.RecipientCount));
        var kept = Assert.Single(revisions, r => r.RootId == anna.RootId);
        Assert.Equal(
            (seed.AnnaId, "Anna Beispiel", "A 1"),
            (kept.RecipientId, kept.RecipientName, kept.RecipientAddress)
        );

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var linked = await db
            .GraveRecipients.Where(g => g.GraveId == seed.GraveId)
            .Select(g => g.RecipientId)
            .OrderBy(id => id)
            .ToListAsync();
        Assert.Equal(new[] { seed.AnnaId, seed.BerndId }, linked);
    }

    [Fact]
    public async Task A_swap_to_a_recipient_already_in_the_group_is_refused()
    {
        using var host = new ApiTestHost();
        var (seed, _) = await SeedAsync(host);
        var originals = await InvoicesAsync(host);
        var bernd = originals.Single(i => i.RecipientId == seed.BerndId);

        await AssertRefusedWritingNothingAsync(
            host,
            client => ReviseAsync(client, bernd.Id, seed.ItemId, (bernd.Id, seed.AnnaId)),
            "GraveRecipientLinkExists"
        );
    }

    [Fact]
    public async Task A_swap_to_a_recipient_without_address_is_refused()
    {
        using var host = new ApiTestHost();
        var (seed, doraId) = await SeedAsync(host);
        var originals = await InvoicesAsync(host);
        var bernd = originals.Single(i => i.RecipientId == seed.BerndId);

        await AssertRefusedWritingNothingAsync(
            host,
            client => ReviseAsync(client, bernd.Id, seed.ItemId, (bernd.Id, doraId)),
            "RecipientAddressRequired"
        );
    }

    [Fact]
    public async Task A_swap_to_a_missing_recipient_is_refused()
    {
        using var host = new ApiTestHost();
        var (seed, _) = await SeedAsync(host);
        var originals = await InvoicesAsync(host);
        var bernd = originals.Single(i => i.RecipientId == seed.BerndId);

        await AssertRefusedWritingNothingAsync(
            host,
            client => ReviseAsync(client, bernd.Id, seed.ItemId, (bernd.Id, 99999)),
            "RecipientNotFound"
        );
    }

    [Fact]
    public async Task A_swap_naming_an_invoice_outside_the_group_is_refused()
    {
        using var host = new ApiTestHost();
        var (seed, _) = await SeedAsync(host);
        var originals = await InvoicesAsync(host);
        var anna = originals.Single(i => i.RecipientId == seed.AnnaId);
        var bernd = originals.Single(i => i.RecipientId == seed.BerndId);

        // an invoice of no grave, and the same invoice named twice
        long other;
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var invoice = new Invoice
            {
                InvoiceNumber = "X-1",
                RecipientId = seed.CarlaId,
                RecipientName = "Carla Beispiel",
                RecipientAddress = "C 3",
                IsLatestRevision = true,
            };
            db.Invoices.Add(invoice);
            await db.SaveChangesAsync();
            other = invoice.Id;
        }

        await AssertRefusedWritingNothingAsync(
            host,
            client => ReviseAsync(client, anna.Id, seed.ItemId, (other, seed.CarlaId)),
            "ReplacementNotInGroup"
        );
        await AssertRefusedWritingNothingAsync(
            host,
            client =>
                ReviseAsync(
                    client,
                    anna.Id,
                    seed.ItemId,
                    (bernd.Id, seed.CarlaId),
                    (bernd.Id, seed.CarlaId)
                ),
            "ReplacementNotInGroup"
        );
    }

    [Fact]
    public async Task A_swap_is_refused_while_an_invoice_is_paid()
    {
        using var host = new ApiTestHost();
        var (seed, _) = await SeedAsync(host);
        var originals = await InvoicesAsync(host);
        var anna = originals.Single(i => i.RecipientId == seed.AnnaId);
        var bernd = originals.Single(i => i.RecipientId == seed.BerndId);
        using (var client = host.CreateClient())
        {
            (
                await client.PostAsync(
                    "/odata/SetInvoicePaid",
                    ODataJson.Body(new { invoiceId = anna.Id, paid = true })
                )
            ).EnsureSuccessStatusCode();
        }

        await AssertRefusedWritingNothingAsync(
            host,
            client => ReviseAsync(client, bernd.Id, seed.ItemId, (bernd.Id, seed.CarlaId)),
            "GraveInvoicePaid"
        );
    }

    [Fact]
    public async Task A_swap_is_recorded()
    {
        using var host = new ApiTestHost();
        var (seed, _) = await SeedAsync(host);
        var originals = await InvoicesAsync(host);
        var anna = originals.Single(i => i.RecipientId == seed.AnnaId);
        var bernd = originals.Single(i => i.RecipientId == seed.BerndId);
        var before = (await EntriesAsync(host)).Count;
        using var client = host.CreateClient();

        (
            await ReviseAsync(client, anna.Id, seed.ItemId, (bernd.Id, seed.CarlaId))
        ).EnsureSuccessStatusCode();

        var added = (await EntriesAsync(host)).Skip(before).ToList();
        Assert.Equal(3, added.Count);

        var chain = Assert.Single(added, e => e.InvoiceRootId == bernd.RootId);
        var chainChanges = Changes(chain);
        var recipient = Assert.Single(chainChanges, c => c.Field == "recipient");
        Assert.Equal(
            ("E-0002 Bernd Beispiel", "E-0003 Carla Beispiel"),
            (recipient.Old, recipient.New)
        );
        // recorded once on the chain, by the diff alone
        Assert.DoesNotContain(chainChanges, c => c.Field == "recipientSwapped");

        var untouched = Assert.Single(added, e => e.InvoiceRootId == anna.RootId);
        Assert.DoesNotContain(Changes(untouched), c => c.Field == "recipient");

        var grave = Assert.Single(added, e => e.GraveId == seed.GraveId);
        var swapped = Assert.Single(Changes(grave), c => c.Field == "recipientSwapped");
        Assert.Equal(
            ($"{bernd.InvoiceNumber}-R1", "E-0002 Bernd Beispiel", "E-0003 Carla Beispiel"),
            (swapped.Label, swapped.Old, swapped.New)
        );
    }

    private static List<AuditChange> Changes(AuditEntry entry) =>
        JsonSerializer.Deserialize<List<AuditChange>>(entry.ChangesJson, Json)!;
}
