using System;
using System.Net;
using System.Threading.Tasks;
using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Burkard.Invoicing.IntegrationTests;

/// <summary>What is issued here is dated and numbered by the day here, in
/// Europe/Berlin -- not by the UTC day, which until one or two in the morning
/// is still yesterday. Both come from the app's clock, so a clock standing at
/// 00:30 on New Year's Day gives the first number of the new year and a date in
/// it, not 2026-0001 dated 31.12.2025.</summary>
public class IssueDateTests
{
    private static readonly DateTime NewYearLocal = new(2026, 1, 1, 0, 30, 0);
    private static readonly DateTime NewYearUtc = new(2025, 12, 31, 23, 30, 0);
    private static readonly DateOnly NewYear = new(2026, 1, 1);

    private static ApiTestHost AfterMidnight() => new() { Now = NewYearLocal, UtcNow = NewYearUtc };

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
            Name = "Anna Beispiel",
            Address = "Hauptstraße 1\n99999 Musterdorf",
        };
        db.InvoiceItems.Add(item);
        db.Recipients.Add(recipient);
        await db.SaveChangesAsync();
        return (item.Id, recipient.Id);
    }

    private static Task<HttpResponseMessage> SaveGraveAsync(
        System.Net.Http.HttpClient client,
        long itemId,
        long recipientId
    ) =>
        client.PostAsync(
            "/odata/SaveGrave",
            ODataJson.Body(
                new
                {
                    graveId = (long?)null,
                    graveNumber = "S-001",
                    fieldNumber = "F-1",
                    siteName = "Grabstätte Test",
                    cemeteryId = TestCemetery.Id,
                    position = "SingleInside",
                    startDate = "2026-01-01",
                    endDate = "2040-12-31",
                    items = new[]
                    {
                        new
                        {
                            itemId,
                            count = 1,
                            unitPrice = (decimal?)null,
                        },
                    },
                    recipientIds = new[] { recipientId },
                }
            )
        );

    [Fact]
    public async Task An_invoice_created_after_midnight_is_numbered_and_dated_in_the_local_day()
    {
        using var host = AfterMidnight();
        var (itemId, recipientId) = await SeedAsync(host);
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
                            itemId,
                            count = 1,
                            unitPrice = (decimal?)null,
                        },
                    },
                }
            )
        );
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var invoice = await db.Invoices.SingleAsync();
        Assert.Equal("2026-0001", invoice.InvoiceNumber);
        Assert.Equal(NewYear, DateOnly.FromDateTime(invoice.GeneratedAt));
    }

    [Fact]
    public async Task A_grave_saved_after_midnight_is_created_on_the_local_day()
    {
        using var host = AfterMidnight();
        var (itemId, recipientId) = await SeedAsync(host);
        using var client = host.CreateClient();

        var response = await SaveGraveAsync(client, itemId, recipientId);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var grave = await db.Graves.SingleAsync();
        Assert.Equal(NewYear, grave.CreatedAt);
    }

    [Fact]
    public async Task Grave_invoices_generated_after_midnight_are_dated_in_the_local_day()
    {
        using var host = AfterMidnight();
        var (itemId, recipientId) = await SeedAsync(host);
        using var client = host.CreateClient();
        var saved = await SaveGraveAsync(client, itemId, recipientId);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);

        await GraveGenerationHelper.RunAsync(client, 2026, 1);

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var invoice = await db.Invoices.SingleAsync();
        Assert.StartsWith("2026-", invoice.InvoiceNumber);
        Assert.Equal(NewYear, DateOnly.FromDateTime(invoice.GeneratedAt));
    }
}
