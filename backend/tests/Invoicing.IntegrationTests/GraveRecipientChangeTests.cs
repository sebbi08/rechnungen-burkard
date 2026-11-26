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

public class GraveRecipientChangeTests
{
    private static async Task<(long GraveId, long ItemId, long AnnaId, long BerndId)> SeedAsync(
        ApiTestHost host
    )
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
        var grave = new Grave
        {
            GraveNumber = "G-400",
            CemeteryId = TestCemetery.Id,
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
        db.GraveRecipients.Add(new GraveRecipient { GraveId = grave.Id, RecipientId = anna.Id });
        await db.SaveChangesAsync();
        return (grave.Id, item.Id, anna.Id, bernd.Id);
    }

    [Fact]
    public async Task Saving_an_uninvoiced_grave_with_a_different_recipient_set_replaces_it()
    {
        using var host = new ApiTestHost();
        var (graveId, itemId, annaId, berndId) = await SeedAsync(host);

        using var client = host.CreateClient();
        var response = await client.PostAsync(
            "/odata/SaveGrave",
            ODataJson.Body(
                new
                {
                    graveId,
                    graveNumber = "G-400",
                    fieldNumber = "F-1",
                    siteName = "Grabstätte Test",
                    cemeteryId = TestCemetery.Id,
                    position = "SingleInside",
                    startDate = "2020-01-01",
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
                    recipientIds = new[] { annaId, berndId },
                }
            )
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(2, await db.GraveRecipients.CountAsync(r => r.GraveId == graveId));
    }

    [Fact]
    public async Task Saving_a_grave_with_the_same_recipients_still_works()
    {
        using var host = new ApiTestHost();
        var (graveId, itemId, annaId, _) = await SeedAsync(host);

        using var client = host.CreateClient();
        var response = await client.PostAsync(
            "/odata/SaveGrave",
            ODataJson.Body(
                new
                {
                    graveId,
                    graveNumber = "G-400-neu",
                    fieldNumber = "F-1",
                    siteName = "Grabstätte Test",
                    cemeteryId = TestCemetery.Id,
                    position = "SingleInside",
                    startDate = "2020-01-01",
                    endDate = "2041-12-31",
                    items = new[]
                    {
                        new
                        {
                            itemId,
                            count = 5,
                            unitPrice = (decimal?)null,
                        },
                    },
                    recipientIds = new[] { annaId },
                }
            )
        );

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
        var response = await client.PostAsync(
            "/odata/SaveGrave",
            ODataJson.Body(
                new
                {
                    graveId = (long?)null,
                    graveNumber = "G-401",
                    fieldNumber = "F-1",
                    siteName = "Grabstätte Test",
                    cemeteryId = TestCemetery.Id,
                    position = "SingleInside",
                    startDate = "2020-01-01",
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
                    recipientIds = new[] { annaId, berndId },
                }
            )
        );

        response.EnsureSuccessStatusCode();
        using var json = await ODataJson.ReadAsync(response);
        var newGraveId = json.RootElement.GetProperty("Id").GetInt64();

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(2, await db.GraveRecipients.CountAsync(r => r.GraveId == newGraveId));
    }

    private static async Task<HttpResponseMessage> SaveRecipientsAsync(
        HttpClient client,
        long graveId,
        long itemId,
        params long[] recipientIds
    ) =>
        await client.PostAsync(
            "/odata/SaveGrave",
            ODataJson.Body(
                new
                {
                    graveId,
                    graveNumber = "G-400",
                    fieldNumber = "F-1",
                    siteName = "Grabstätte Test",
                    cemeteryId = TestCemetery.Id,
                    position = "SingleInside",
                    startDate = "2020-01-01",
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
                    recipientIds,
                }
            )
        );

    [Fact]
    public async Task Saving_an_invoiced_grave_with_another_recipient_set_replaces_it()
    {
        using var host = new ApiTestHost();
        var (graveId, itemId, annaId, berndId) = await SeedAsync(host);

        using var client = host.CreateClient();
        await GraveGenerationHelper.RunAsync(client, 2033, 1);
        var response = await SaveRecipientsAsync(client, graveId, itemId, annaId, berndId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(2, await db.GraveRecipients.CountAsync(r => r.GraveId == graveId));
    }

    [Fact]
    public async Task The_next_generation_bills_the_new_recipients()
    {
        using var host = new ApiTestHost();
        var (graveId, itemId, annaId, berndId) = await SeedAsync(host);

        using var client = host.CreateClient();
        await GraveGenerationHelper.RunAsync(client, 2033, 1);
        (
            await SaveRecipientsAsync(client, graveId, itemId, annaId, berndId)
        ).EnsureSuccessStatusCode();
        await GraveGenerationHelper.RunAsync(client, 2033, 2);

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var invoices = await db
            .Invoices.Where(i => i.GraveId == graveId && i.IsLatestRevision)
            .ToListAsync();

        var first = invoices.Where(i => i.PeriodYear == 2033 && i.PeriodHalf == 1).ToList();
        var onlyFirst = Assert.Single(first);
        Assert.Equal("Anna Beispiel", onlyFirst.RecipientName);
        Assert.Equal(100m, onlyFirst.SharePercent);

        var second = invoices.Where(i => i.PeriodYear == 2033 && i.PeriodHalf == 2).ToList();
        Assert.Equal(2, second.Count);
        Assert.All(second, i => Assert.Equal(50m, i.SharePercent));
        Assert.Equal(
            ["Anna Beispiel", "Bernd Beispiel"],
            second.Select(i => i.RecipientName).OrderBy(n => n).ToArray()
        );
    }

    [Fact]
    public async Task Revising_an_issued_half_year_keeps_its_own_recipients()
    {
        using var host = new ApiTestHost();
        var (graveId, itemId, annaId, berndId) = await SeedAsync(host);

        using var client = host.CreateClient();
        await GraveGenerationHelper.RunAsync(client, 2033, 1);
        (
            await SaveRecipientsAsync(client, graveId, itemId, annaId, berndId)
        ).EnsureSuccessStatusCode();

        long invoiceId;
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            invoiceId = (await db.Invoices.SingleAsync(i => i.GraveId == graveId)).Id;
        }

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
                            count = 2,
                            unitPrice = (decimal?)100m,
                        },
                    },
                }
            )
        );
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var verify = host.Services.CreateScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<AppDbContext>();
        var latest = await verifyDb
            .Invoices.Where(i => i.GraveId == graveId && i.IsLatestRevision)
            .ToListAsync();
        var revision = Assert.Single(latest);
        Assert.Equal("Anna Beispiel", revision.RecipientName);
        Assert.Equal(1, revision.RecipientCount);
        Assert.Equal(2, await verifyDb.Invoices.CountAsync(i => i.GraveId == graveId));
    }
}
