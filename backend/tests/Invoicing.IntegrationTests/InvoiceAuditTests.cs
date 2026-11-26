using System.Net;
using System.Text.Json;
using Burkard.Invoicing.Api.Audit;
using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Burkard.Invoicing.IntegrationTests;

/// <summary>Every write path of an invoice leaves an entry on its chain, grave
/// invoices one on their grave as well, and a refused write leaves none.</summary>
public class InvoiceAuditTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private sealed record Seed(long ItemId, long AnnaId, long BerndId);

    private static async Task<Seed> SeedAsync(ApiTestHost host)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.VatRates.Add(new VatRate { Rate = 7m });
        var item = new InvoiceItem
        {
            Number = "1001",
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
        db.InvoiceItems.Add(item);
        db.Recipients.AddRange(anna, bernd);
        await db.SaveChangesAsync();
        return new Seed(item.Id, anna.Id, bernd.Id);
    }

    /// <summary>A grave billing the item twice at 100.00, shared by Anna and
    /// Bernd, invoiced for 2031 H1: each pays 100.00 net, 107.00 gross.</summary>
    private static async Task<(long GraveId, List<Invoice> Invoices)> SeedGraveInvoicesAsync(
        ApiTestHost host,
        Seed seed
    )
    {
        long graveId;
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var grave = new Grave
            {
                GraveNumber = "G-300",
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
                    ItemId = seed.ItemId,
                    Count = 2,
                }
            );
            db.GraveRecipients.AddRange(
                new GraveRecipient { GraveId = grave.Id, RecipientId = seed.AnnaId },
                new GraveRecipient { GraveId = grave.Id, RecipientId = seed.BerndId }
            );
            await db.SaveChangesAsync();
            graveId = grave.Id;
        }

        using var client = host.CreateClient();
        await GraveGenerationHelper.RunAsync(client, 2031, 1);
        return (graveId, await InvoicesAsync(host));
    }

    private static async Task<List<Invoice>> InvoicesAsync(ApiTestHost host)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Invoices.AsNoTracking().OrderBy(i => i.Id).ToListAsync();
    }

    private static object Lines(long itemId, int count) =>
        new[]
        {
            new
            {
                itemId,
                count,
                unitPrice = (decimal?)null,
            },
        };

    private static async Task<long> CreateInvoiceAsync(
        HttpClient client,
        long recipientId,
        long itemId,
        int count,
        string? introText = null
    )
    {
        var response = await client.PostAsync(
            "/odata/CreateInvoice",
            ODataJson.Body(
                new
                {
                    recipientId,
                    introText,
                    lines = Lines(itemId, count),
                }
            )
        );
        return await CreatedIdAsync(response);
    }

    private static async Task<long> ReviseAsync(
        HttpClient client,
        long invoiceId,
        long recipientId,
        long itemId,
        int count
    ) =>
        await CreatedIdAsync(
            await client.PostAsync(
                "/odata/CreateRevision",
                ODataJson.Body(
                    new
                    {
                        invoiceId,
                        recipientId,
                        lines = Lines(itemId, count),
                    }
                )
            )
        );

    private static Task<HttpResponseMessage> ReviseGraveInvoiceAsync(
        HttpClient client,
        long invoiceId,
        long itemId,
        int count
    ) =>
        client.PostAsync(
            "/odata/CreateGraveInvoiceRevision",
            ODataJson.Body(new { invoiceId, lines = Lines(itemId, count) })
        );

    private static async Task SetPaidAsync(HttpClient client, long invoiceId, bool paid) =>
        (
            await client.PostAsync("/odata/SetInvoicePaid", ODataJson.Body(new { invoiceId, paid }))
        ).EnsureSuccessStatusCode();

    private static async Task<long> CreatedIdAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        return json.RootElement.GetProperty("Id").GetInt64();
    }

    private static async Task<List<AuditEntry>> EntriesAsync(ApiTestHost host)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.AuditEntries.AsNoTracking().OrderBy(e => e.Id).ToListAsync();
    }

    private static List<AuditChange> Changes(AuditEntry entry) =>
        JsonSerializer.Deserialize<List<AuditChange>>(entry.ChangesJson, Json)!;

    private static Dictionary<string, string> Details(AuditEntry entry) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(entry.DetailsJson!, Json)!;

    private static AuditChange Change(AuditEntry entry, string field) =>
        Assert.Single(Changes(entry), c => c.Field == field);

    [Fact]
    public async Task Creating_an_invoice_records_recipient_lines_and_total()
    {
        using var host = new ApiTestHost();
        var seed = await SeedAsync(host);
        using var client = host.CreateClient();

        var id = await CreateInvoiceAsync(client, seed.AnnaId, seed.ItemId, 2, "Für Mai");

        var entry = Assert.Single(await EntriesAsync(host));
        Assert.Equal(AuditActions.InvoiceCreated, entry.Action);
        Assert.Equal(id, entry.InvoiceRootId);
        Assert.Null(entry.GraveId);
        Assert.Equal("E-0001 Anna Beispiel", Change(entry, "recipient").New);
        Assert.Equal("Für Mai", Change(entry, "introText").New);
        Assert.Equal("1001 Grabpflege", Change(entry, "lineAdded").Label);
        var count = Change(entry, "lineCount");
        Assert.Equal(("1001 Grabpflege", null, "2"), (count.Label, count.Old, count.New));
        var price = Change(entry, "linePrice");
        Assert.Equal(("100.00", "money"), (price.New, price.Type));
        var total = Change(entry, "total");
        Assert.Equal((null, "214.00", "money"), (total.Old, total.New, total.Type));
        Assert.DoesNotContain(Changes(entry), c => c.Field is "lineVatIncluded" or "lineComment");
    }

    [Fact]
    public async Task A_revision_records_what_changed()
    {
        using var host = new ApiTestHost();
        var seed = await SeedAsync(host);
        using var client = host.CreateClient();
        var rootId = await CreateInvoiceAsync(client, seed.AnnaId, seed.ItemId, 1);

        var firstRevisionId = await ReviseAsync(client, rootId, seed.BerndId, seed.ItemId, 3);
        await ReviseAsync(client, firstRevisionId, seed.BerndId, seed.ItemId, 3);

        var entries = await EntriesAsync(host);
        Assert.Equal(3, entries.Count);
        Assert.All(entries, e => Assert.Equal(rootId, e.InvoiceRootId));
        var revised = entries[1];
        Assert.Equal(AuditActions.InvoiceRevised, revised.Action);
        var recipient = Change(revised, "recipient");
        Assert.Equal(
            ("E-0001 Anna Beispiel", "E-0002 Bernd Beispiel"),
            (recipient.Old, recipient.New)
        );
        var count = Change(revised, "lineCount");
        Assert.Equal(("1001 Grabpflege", "1", "3"), (count.Label, count.Old, count.New));
        var total = Change(revised, "total");
        Assert.Equal(("107.00", "321.00"), (total.Old, total.New));
        Assert.EndsWith("-R1", Change(revised, "invoiceNumber").New);
        Assert.Equal(4, Changes(revised).Count);

        // unchanged but for its number
        var again = entries[2];
        Assert.Equal(AuditActions.InvoiceRevised, again.Action);
        var number = Assert.Single(Changes(again));
        Assert.Equal("invoiceNumber", number.Field);
        Assert.EndsWith("-R1", number.Old);
        Assert.EndsWith("-R2", number.New);
    }

    [Fact]
    public async Task Converting_a_quote_records_the_quote_number()
    {
        using var host = new ApiTestHost();
        var seed = await SeedAsync(host);
        using var client = host.CreateClient();
        var created = await client.PostAsync(
            "/odata/CreateQuote",
            ODataJson.Body(new { recipientId = seed.AnnaId, lines = Lines(seed.ItemId, 1) })
        );
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        long quoteId;
        string quoteNumber;
        using (var json = await ODataJson.ReadAsync(created))
        {
            quoteId = json.RootElement.GetProperty("Id").GetInt64();
            quoteNumber = json.RootElement.GetProperty("QuoteNumber").GetString()!;
        }

        var invoiceId = await CreatedIdAsync(
            await client.PostAsync("/odata/ConvertQuote", ODataJson.Body(new { quoteId }))
        );

        var entry = Assert.Single(await EntriesAsync(host));
        Assert.Equal(AuditActions.InvoiceCreatedFromQuote, entry.Action);
        Assert.Equal(invoiceId, entry.InvoiceRootId);
        Assert.Equal(quoteNumber, Details(entry)["quote"]);
        Assert.Equal("E-0001 Anna Beispiel", Change(entry, "recipient").New);
        Assert.Equal("107.00", Change(entry, "total").New);
    }

    [Fact]
    public async Task Paying_and_unpaying_are_recorded()
    {
        using var host = new ApiTestHost();
        var seed = await SeedAsync(host);
        using var client = host.CreateClient();
        var id = await CreateInvoiceAsync(client, seed.AnnaId, seed.ItemId, 1);

        await SetPaidAsync(client, id, true);
        // already paid: nothing changes, so nothing is recorded
        await SetPaidAsync(client, id, true);
        await SetPaidAsync(client, id, false);

        var entries = await EntriesAsync(host);
        Assert.Equal(3, entries.Count);
        var paid = entries[1];
        Assert.Equal(AuditActions.InvoicePaid, paid.Action);
        Assert.Equal(id, paid.InvoiceRootId);
        var change = Assert.Single(Changes(paid));
        Assert.Equal(
            ("paid", "false", "true", "bool"),
            (change.Field, change.Old, change.New, change.Type)
        );
        var unpaid = entries[2];
        Assert.Equal(AuditActions.InvoiceUnpaid, unpaid.Action);
        var back = Assert.Single(Changes(unpaid));
        Assert.Equal(("true", "false"), (back.Old, back.New));
    }

    [Fact]
    public async Task Paying_a_grave_invoice_records_on_its_grave_too()
    {
        using var host = new ApiTestHost();
        var seed = await SeedAsync(host);
        var (graveId, invoices) = await SeedGraveInvoicesAsync(host, seed);
        var before = (await EntriesAsync(host)).Count;
        using var client = host.CreateClient();

        await SetPaidAsync(client, invoices[0].Id, true);

        var added = (await EntriesAsync(host)).Skip(before).ToList();
        Assert.Equal(2, added.Count);
        var chain = Assert.Single(added, e => e.InvoiceRootId == invoices[0].RootId);
        Assert.Equal(AuditActions.InvoicePaid, chain.Action);
        var grave = Assert.Single(added, e => e.GraveId == graveId);
        Assert.Equal(AuditActions.InvoicePaid, grave.Action);
        var change = Assert.Single(Changes(grave));
        Assert.Equal(
            ("paid", invoices[0].InvoiceNumber, "false", "true"),
            (change.Field, change.Label, change.Old, change.New)
        );
    }

    [Fact]
    public async Task Marking_grave_invoices_paid_records_on_invoices_and_grave()
    {
        using var host = new ApiTestHost();
        var seed = await SeedAsync(host);
        var (graveId, invoices) = await SeedGraveInvoicesAsync(host, seed);
        var before = (await EntriesAsync(host)).Count;
        using var client = host.CreateClient();

        var response = await client.PostAsync(
            "/odata/MarkInvoicesPaid",
            ODataJson.Body(new { invoiceIds = invoices.Select(i => i.Id).ToArray() })
        );
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var added = (await EntriesAsync(host)).Skip(before).ToList();
        Assert.Equal(3, added.Count);
        Assert.All(added, e => Assert.Equal(AuditActions.InvoicePaid, e.Action));
        foreach (var invoice in invoices)
        {
            var chain = Assert.Single(added, e => e.InvoiceRootId == invoice.RootId);
            Assert.Equal("true", Change(chain, "paid").New);
        }

        var grave = Assert.Single(added, e => e.GraveId == graveId);
        var changes = Changes(grave);
        Assert.Equal(
            invoices.Select(i => i.InvoiceNumber).Order(),
            changes.Select(c => c.Label).Order()
        );
        Assert.All(changes, c => Assert.Equal(("paid", "false", "true"), (c.Field, c.Old, c.New)));
    }

    [Fact]
    public async Task Generating_records_on_grave_and_invoices()
    {
        using var host = new ApiTestHost();
        var seed = await SeedAsync(host);

        var (graveId, invoices) = await SeedGraveInvoicesAsync(host, seed);

        Assert.Equal(2, invoices.Count);
        var entries = await EntriesAsync(host);
        Assert.Equal(3, entries.Count);
        var grave = Assert.Single(entries, e => e.GraveId == graveId);
        Assert.Equal(AuditActions.GraveInvoicesGenerated, grave.Action);
        Assert.Equal(
            new Dictionary<string, string> { ["year"] = "2031", ["half"] = "1" },
            Details(grave)
        );
        Assert.Equal(
            invoices.Select(i => $"{i.RootId}:{i.InvoiceNumber}"),
            Changes(grave).Select(c => c.New)
        );
        Assert.All(Changes(grave), c => Assert.Equal(("invoice", "invoice"), (c.Field, c.Type)));

        var labels = new[] { "E-0001 Anna Beispiel", "E-0002 Bernd Beispiel" };
        for (var i = 0; i < invoices.Count; i++)
        {
            var chain = Assert.Single(entries, e => e.InvoiceRootId == invoices[i].RootId);
            Assert.Equal(AuditActions.InvoiceCreated, chain.Action);
            Assert.Null(chain.DetailsJson);
            Assert.Equal(labels[i], Change(chain, "recipient").New);
            // the recipient's share, as the invoice list shows it
            Assert.Equal("107.00", Change(chain, "total").New);
        }
    }

    [Fact]
    public async Task Revising_a_grave_invoice_records_every_chain()
    {
        using var host = new ApiTestHost();
        var seed = await SeedAsync(host);
        var (graveId, invoices) = await SeedGraveInvoicesAsync(host, seed);
        var before = (await EntriesAsync(host)).Count;
        using var client = host.CreateClient();

        var response = await ReviseGraveInvoiceAsync(client, invoices[1].Id, seed.ItemId, 3);
        response.EnsureSuccessStatusCode();

        var revisions = (await InvoicesAsync(host)).Where(i => i.RevisionNumber == 1).ToList();
        Assert.Equal(2, revisions.Count);
        var added = (await EntriesAsync(host)).Skip(before).ToList();
        Assert.Equal(3, added.Count);
        var grave = Assert.Single(added, e => e.GraveId == graveId);
        Assert.Equal(AuditActions.GraveInvoiceRevised, grave.Action);
        Assert.Equal(
            new Dictionary<string, string> { ["year"] = "2031", ["half"] = "1" },
            Details(grave)
        );
        Assert.Equal(
            revisions.Select(r => $"{r.RootId}:{r.InvoiceNumber}"),
            Changes(grave).Select(c => c.New)
        );

        foreach (var invoice in invoices)
        {
            var chain = Assert.Single(added, e => e.InvoiceRootId == invoice.RootId);
            Assert.Equal(AuditActions.InvoiceRevised, chain.Action);
            Assert.Null(chain.DetailsJson);
            var number = Change(chain, "invoiceNumber");
            Assert.Equal(
                (invoice.InvoiceNumber, $"{invoice.InvoiceNumber}-R1"),
                (number.Old, number.New)
            );
            var count = Change(chain, "lineCount");
            Assert.Equal(("2", "3"), (count.Old, count.New));
            // 300.00 net shared by two: 150.00 net and 10.50 VAT each
            var total = Change(chain, "total");
            Assert.Equal(("107.00", "160.50"), (total.Old, total.New));
            Assert.Equal(3, Changes(chain).Count);
        }
    }

    [Fact]
    public async Task A_refused_revision_writes_nothing()
    {
        using var host = new ApiTestHost();
        var seed = await SeedAsync(host);
        var (_, invoices) = await SeedGraveInvoicesAsync(host, seed);
        using var client = host.CreateClient();
        await SetPaidAsync(client, invoices[0].Id, true);
        var before = (await EntriesAsync(host)).Count;

        var response = await ReviseGraveInvoiceAsync(client, invoices[1].Id, seed.ItemId, 3);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using (var json = await ODataJson.ReadAsync(response))
        {
            Assert.Equal(
                "GraveInvoicePaid",
                json.RootElement.GetProperty("error").GetProperty("code").GetString()
            );
        }

        Assert.Equal(before, (await EntriesAsync(host)).Count);
    }
}
