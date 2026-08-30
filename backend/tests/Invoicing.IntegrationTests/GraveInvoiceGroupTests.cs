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
    private static async Task<(long GraveId, long InvoiceId, long ItemId)> SeedInvoicedGraveAsync(
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
                Number = "3001",
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
            var grave = new Grave
            {
                GraveNumber = "G-200",
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
            db.GraveRecipients.Add(
                new GraveRecipient { GraveId = grave.Id, RecipientId = anna.Id }
            );
            db.GraveRecipients.Add(
                new GraveRecipient { GraveId = grave.Id, RecipientId = bernd.Id }
            );
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

        var names = root.GetProperty("Recipients")
            .EnumerateArray()
            .Select(r => r.GetProperty("Name").GetString())
            .ToList();
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
        var item = new InvoiceItem
        {
            Number = "3002",
            Description = "X",
            VatRate = 7m,
            DefaultPrice = 10m,
        };
        var recipient = new Recipient
        {
            Number = "E-0009",
            Name = "Solo",
            Address = "S 1",
        };
        db.InvoiceItems.Add(item);
        db.Recipients.Add(recipient);
        await db.SaveChangesAsync();

        using var client = host.CreateClient();
        var created = await client.PostAsync(
            "/odata/CreateInvoice",
            ODataJson.Body(
                new
                {
                    recipientId = recipient.Id,
                    lines = new[]
                    {
                        new
                        {
                            itemId = item.Id,
                            count = 1,
                            unitPrice = (decimal?)null,
                        },
                    },
                }
            )
        );
        created.EnsureSuccessStatusCode();
        using var createdJson = await ODataJson.ReadAsync(created);
        var invoiceId = createdJson.RootElement.GetProperty("Id").GetInt64();

        var response = await client.GetAsync($"/odata/GraveInvoiceGroup?invoiceId={invoiceId}");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        Assert.Equal(
            "NotAGraveInvoice",
            json.RootElement.GetProperty("error").GetProperty("code").GetString()
        );
    }

    [Fact]
    public async Task Reports_an_unknown_invoice()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();

        var response = await client.GetAsync("/odata/GraveInvoiceGroup?invoiceId=987654");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        Assert.Equal(
            "InvoiceNotFound",
            json.RootElement.GetProperty("error").GetProperty("code").GetString()
        );
    }

    [Fact]
    public async Task Rejects_a_group_whose_siblings_carry_different_line_counts()
    {
        using var host = new ApiTestHost();
        var (graveId, invoiceId, _) = await SeedInvoicedGraveAsync(host);

        // Simulates a sibling revised on its own outside of generation: it gains a
        // line the rest of the group does not have.
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var siblingInvoiceId = await db
                .Invoices.Where(i => i.GraveId == graveId && i.Id != invoiceId)
                .Select(i => i.Id)
                .FirstAsync();
            var template = await db.InvoiceLines.FirstAsync(l => l.InvoiceId == siblingInvoiceId);
            db.InvoiceLines.Add(
                new InvoiceLine
                {
                    InvoiceId = siblingInvoiceId,
                    ItemId = template.ItemId,
                    ItemNumber = template.ItemNumber,
                    Description = template.Description,
                    Count = 1,
                    UnitPrice = 5m,
                    VatRate = template.VatRate,
                    LineNumber = template.LineNumber + 1,
                }
            );
            await db.SaveChangesAsync();
        }

        using var client = host.CreateClient();
        var response = await client.GetAsync($"/odata/GraveInvoiceGroup?invoiceId={invoiceId}");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        Assert.Equal(
            "GraveInvoiceGroupInconsistent",
            json.RootElement.GetProperty("error").GetProperty("code").GetString()
        );
    }

    [Fact]
    public async Task Falls_back_to_reconstruction_when_one_sibling_lacks_a_grave_unit_price()
    {
        using var host = new ApiTestHost();
        var (graveId, invoiceId, _) = await SeedInvoicedGraveAsync(host);

        // Simulates a sibling revised on its own through the ordinary CreateRevision
        // path, which never sets GraveUnitPrice: line counts still agree, so the
        // group is consistent, but the stored price can no longer be trusted for
        // the whole group and reconstruction from the recipients' shares kicks in.
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var siblingInvoiceId = await db
                .Invoices.Where(i => i.GraveId == graveId && i.Id != invoiceId)
                .Select(i => i.Id)
                .FirstAsync();
            var line = await db.InvoiceLines.SingleAsync(l => l.InvoiceId == siblingInvoiceId);
            line.GraveUnitPrice = null;
            await db.SaveChangesAsync();
        }

        using var client = host.CreateClient();
        var response = await client.GetAsync($"/odata/GraveInvoiceGroup?invoiceId={invoiceId}");
        response.EnsureSuccessStatusCode();
        using var json = await ODataJson.ReadAsync(response);

        var line2 = Assert.Single(json.RootElement.GetProperty("Lines").EnumerateArray().ToList());
        Assert.Equal(2, line2.GetProperty("Count").GetInt32());
        Assert.Equal(100m, line2.GetProperty("UnitPrice").GetDecimal());
    }

    [Fact]
    public async Task Runs_are_readable_per_grave()
    {
        using var host = new ApiTestHost();
        var (graveId, _, _) = await SeedInvoicedGraveAsync(host);

        using var client = host.CreateClient();
        var response = await client.GetAsync(
            $"/odata/GraveInvoiceRuns?$filter=GraveId eq {graveId}"
        );
        response.EnsureSuccessStatusCode();
        using var json = await ODataJson.ReadAsync(response);

        var run = Assert.Single(json.RootElement.GetProperty("value").EnumerateArray().ToList());
        Assert.Equal(2029, run.GetProperty("Year").GetInt32());
        Assert.Equal(1, run.GetProperty("Half").GetInt32());
    }
}
