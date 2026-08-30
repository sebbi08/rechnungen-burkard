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
    public async Task Saving_a_grave_with_a_different_recipient_set_is_rejected()
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

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        Assert.Equal(
            "GraveRecipientsImmutable",
            json.RootElement.GetProperty("error").GetProperty("code").GetString()
        );

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
        var response = await client.PostAsync(
            "/odata/SaveGrave",
            ODataJson.Body(
                new
                {
                    graveId,
                    graveNumber = "G-400-neu",
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

    [Fact]
    public async Task Linking_a_recipient_to_a_never_invoiced_grave_still_works()
    {
        using var host = new ApiTestHost();
        var (graveId, _, _, berndId) = await SeedAsync(host);

        using var client = host.CreateClient();
        var response = await client.PostAsync(
            "/odata/GraveRecipients",
            ODataJson.Body(new { GraveId = graveId, RecipientId = berndId })
        );

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Linking_or_unlinking_after_the_grave_was_invoiced_is_rejected()
    {
        using var host = new ApiTestHost();
        var (graveId, _, _, berndId) = await SeedAsync(host);

        using var client = host.CreateClient();
        await GraveGenerationHelper.RunAsync(client, 2033, 1);

        var added = await client.PostAsync(
            "/odata/GraveRecipients",
            ODataJson.Body(new { GraveId = graveId, RecipientId = berndId })
        );
        Assert.Equal(HttpStatusCode.BadRequest, added.StatusCode);
        using var addedJson = await ODataJson.ReadAsync(added);
        Assert.Equal(
            "GraveRecipientsImmutable",
            addedJson.RootElement.GetProperty("error").GetProperty("code").GetString()
        );

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
