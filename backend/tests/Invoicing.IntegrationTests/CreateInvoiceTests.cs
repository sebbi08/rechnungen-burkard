using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Api.Services;
using Burkard.Invoicing.Domain.Entities;
using Burkard.Invoicing.Documents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Burkard.Invoicing.IntegrationTests;

public class CreateInvoiceTests
{
    private static async Task<(long ItemId, long RecipientId)> SeedAsync(ApiTestHost host)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.VatRates.Add(new VatRate { Rate = 7m });
        var item = new InvoiceItem
        {
            Number = "4001",
            Description = "Grabpflege",
            VatRate = 7m,
            DefaultPrice = 120.00m,
        };
        var recipient = new Recipient
        {
            Number = "E-0004",
            Name = "Familie Mahr",
            Address = "Musterstraße 1\n12345 Musterstadt",
        };
        db.InvoiceItems.Add(item);
        db.Recipients.Add(recipient);
        await db.SaveChangesAsync();
        return (item.Id, recipient.Id);
    }

    [Fact]
    public async Task Create_invoice_persists_snapshots_and_both_documents()
    {
        using var host = new ApiTestHost();
        var (itemId, recipientId) = await SeedAsync(host);
        using var client = host.CreateClient();

        var response = await client.PostAsync("/odata/CreateInvoice", ODataJson.Body(new
        {
            recipientId,
            lines = new[]
            {
                new { itemId, count = 2, unitPrice = (decimal?)null },
            },
        }));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var json = await ODataJson.ReadAsync(response);
        var number = json.RootElement.GetProperty("InvoiceNumber").GetString();
        Assert.Matches(new Regex(@"^\d{4}-\d{4}$"), number);
        var id = json.RootElement.GetProperty("Id").GetInt64();

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var invoice = await db.Invoices
            .Include(i => i.Lines).Include(i => i.Documents)
            .SingleAsync(i => i.Id == id);

        Assert.Equal("Familie Mahr", invoice.RecipientName);
        Assert.Contains("Musterstraße 1", invoice.RecipientAddress);
        Assert.Equal("Kopie", invoice.StampWord);
        var line = Assert.Single(invoice.Lines);
        Assert.Equal(2, line.Count);
        Assert.Equal(120.00m, line.UnitPrice);
        Assert.Equal(7m, line.VatRate);
        Assert.Equal(2, invoice.Documents.Count);
        Assert.Contains(invoice.Documents, d => d.Kind == DocumentKind.Normal);
        Assert.Contains(invoice.Documents, d => d.Kind == DocumentKind.Copy);
        Assert.All(invoice.Documents, d => Assert.NotEmpty(d.Content));
    }

    [Fact]
    public async Task Create_invoice_with_item_without_price_and_no_unit_price_fails()
    {
        using var host = new ApiTestHost();
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.VatRates.Add(new VatRate { Rate = 19m });
        var item = new InvoiceItem { Number = "4002", Description = "No price", VatRate = 19m, DefaultPrice = null };
        var recipient = new Recipient { Number = "E-0001", Name = "X", Address = "Y" };
        db.InvoiceItems.Add(item);
        db.Recipients.Add(recipient);
        await db.SaveChangesAsync();

        using var client = host.CreateClient();
        var response = await client.PostAsync("/odata/CreateInvoice", ODataJson.Body(new
        {
            recipientId = recipient.Id,
            lines = new[] { new { itemId = item.Id, count = 1, unitPrice = (decimal?)null } },
        }));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        Assert.Equal("PriceRequired", json.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Create_invoice_with_recipient_without_address_fails()
    {
        using var host = new ApiTestHost();
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.VatRates.Add(new VatRate { Rate = 19m });
        var item = new InvoiceItem { Number = "4003", Description = "X", VatRate = 19m, DefaultPrice = 1m };
        var recipient = new Recipient { Number = "E-0002", Name = "No address", Address = "" };
        db.InvoiceItems.Add(item);
        db.Recipients.Add(recipient);
        await db.SaveChangesAsync();

        using var client = host.CreateClient();
        var response = await client.PostAsync("/odata/CreateInvoice", ODataJson.Body(new
        {
            recipientId = recipient.Id,
            lines = new[] { new { itemId = item.Id, count = 1, unitPrice = (decimal?)1m } },
        }));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        Assert.Equal("RecipientAddressRequired", json.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Concurrent_creates_get_distinct_numbers()
    {
        using var host = new ApiTestHost();
        var (itemId, recipientId) = await SeedAsync(host);
        using var client = host.CreateClient();

        var tasks = Enumerable.Range(0, 5)
            .Select(_ => Task.Run(async () =>
            {
                // each request gets its own body — reusing one StringContent across
                // parallel requests races on its underlying stream
                var own = ODataJson.Body(new
                {
                    recipientId,
                    lines = new[] { new { itemId, count = 1, unitPrice = (decimal?)10m } },
                });
                return await client.PostAsync("/odata/CreateInvoice", own);
            }));
        var responses = await Task.WhenAll(tasks);
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));

        var numbers = new System.Collections.Generic.List<string>();
        foreach (var r in responses)
        {
            using var json = await ODataJson.ReadAsync(r);
            numbers.Add(json.RootElement.GetProperty("InvoiceNumber").GetString());
        }

        Assert.Equal(5, numbers.Distinct().Count());
    }

    [Fact]
    public async Task Failed_copy_generation_persists_no_half_generated_pair()
    {
        var host = new FailingDocsHost();
        try
        {
            long itemId;
            long recipientId;
            using (var scope = host.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                db.VatRates.Add(new VatRate { Rate = 7m });
                var item = new InvoiceItem { Number = "4004", Description = "X", VatRate = 7m, DefaultPrice = 1m };
                var recipient = new Recipient { Number = "E-0003", Name = "X", Address = "Y" };
                db.InvoiceItems.Add(item);
                db.Recipients.Add(recipient);
                await db.SaveChangesAsync();
                itemId = item.Id;
                recipientId = recipient.Id;
            }

            using var client = host.CreateClient();
            var response = await client.PostAsync("/odata/CreateInvoice", ODataJson.Body(new
            {
                recipientId,
                lines = new[] { new { itemId, count = 1, unitPrice = (decimal?)1m } },
            }));
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);

            using var verifyScope = host.Services.CreateScope();
            var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Empty(await verifyDb.Invoices.ToListAsync());
            Assert.Empty(await verifyDb.Documents.ToListAsync());
        }
        finally
        {
            host.Dispose();
        }
    }

    private sealed class FailingDocumentGenerator : IDocumentGenerator
    {
        public byte[] Generate(InvoiceDocumentData data, bool isCopy)
        {
            if (isCopy)
            {
                throw new InvalidOperationException("copy generation failed");
            }

            return DocumentService.GeneratePdf(data, isCopy: false);
        }
    }

    private sealed class FailingDocsHost : ApiTestHost
    {
        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services =>
            {
                var descriptor = services.Single(d => d.ServiceType == typeof(IDocumentGenerator));
                services.Remove(descriptor);
                services.AddSingleton<IDocumentGenerator, FailingDocumentGenerator>();
            });
        }
    }
}
