using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Burkard.Invoicing.IntegrationTests;

/// <summary>The grave list's "mark paid" action: several invoices at once, all
/// or none, and the count of a grave's open invoices that decides whether the
/// list offers it.</summary>
public class MarkInvoicesPaidTests
{
    /// <summary>A grave with one item (100.00 at 7% VAT) and two recipients,
    /// invoiced for both halves of 2031. Returns the grave and its four
    /// invoices, ordered as generation created them: H1 first, then H2.</summary>
    private static async Task<(long GraveId, long ItemId, List<long> InvoiceIds)> SeedAsync(
        ApiTestHost host,
        HttpClient client
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
                Number = "7101",
                Description = "Grabpflege",
                VatRate = 7m,
                DefaultPrice = 100m,
            };
            var anna = new Recipient
            {
                Number = "E-0171",
                Name = "Anna Offen",
                Address = "A 1",
            };
            var bernd = new Recipient
            {
                Number = "E-0172",
                Name = "Bernd Offen",
                Address = "B 2",
            };
            db.InvoiceItems.Add(item);
            db.Recipients.AddRange(anna, bernd);
            var grave = new Grave
            {
                GraveNumber = "G-710",
                CemeteryId = TestCemetery.Id,
                StartDate = new DateOnly(2031, 1, 1),
                EndDate = new DateOnly(2032, 12, 31),
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
            db.GraveRecipients.Add(
                new GraveRecipient { GraveId = grave.Id, RecipientId = anna.Id }
            );
            db.GraveRecipients.Add(
                new GraveRecipient { GraveId = grave.Id, RecipientId = bernd.Id }
            );
            await db.SaveChangesAsync();
            (graveId, itemId) = (grave.Id, item.Id);
        }

        await GraveGenerationHelper.RunAsync(client, 2031, 1);
        await GraveGenerationHelper.RunAsync(client, 2031, 2);

        using var verify = host.Services.CreateScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<AppDbContext>();
        var ids = await verifyDb
            .Invoices.Where(i => i.GraveId == graveId)
            .OrderBy(i => i.Id)
            .Select(i => i.Id)
            .ToListAsync();
        Assert.Equal(4, ids.Count);
        return (graveId, itemId, ids);
    }

    private static Task<HttpResponseMessage> MarkAsync(
        HttpClient client,
        params long[] invoiceIds
    ) => client.PostAsync("/odata/MarkInvoicesPaid", ODataJson.Body(new { invoiceIds }));

    private static async Task<Dictionary<long, DateOnly?>> PaidOnAsync(
        ApiTestHost host,
        IEnumerable<long> invoiceIds
    )
    {
        var ids = invoiceIds.ToList();
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db
            .Invoices.Where(i => ids.Contains(i.Id))
            .ToDictionaryAsync(i => i.Id, i => i.PaidOn);
    }

    private static async Task AssertRefusedAsync(HttpResponseMessage response, string code)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        Assert.Equal(code, json.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Marks_every_given_invoice_paid_today_and_leaves_the_others_open()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var (_, _, ids) = await SeedAsync(host, client);

        var response = await MarkAsync(client, ids[0], ids[2], ids[3]);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var paidOn = await PaidOnAsync(host, ids);
        var today = DateOnly.FromDateTime(host.Now);
        Assert.Equal(today, paidOn[ids[0]]);
        Assert.Null(paidOn[ids[1]]);
        Assert.Equal(today, paidOn[ids[2]]);
        Assert.Equal(today, paidOn[ids[3]]);
    }

    [Fact]
    public async Task An_invoice_already_paid_keeps_its_recorded_day()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var (_, _, ids) = await SeedAsync(host, client);
        var earlier = new DateOnly(2031, 3, 4);
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.Invoices.SingleAsync(i => i.Id == ids[0])).PaidOn = earlier;
            await db.SaveChangesAsync();
        }

        (await MarkAsync(client, ids[0], ids[1])).EnsureSuccessStatusCode();

        var paidOn = await PaidOnAsync(host, ids);
        Assert.Equal(earlier, paidOn[ids[0]]);
        Assert.Equal(DateOnly.FromDateTime(host.Now), paidOn[ids[1]]);
    }

    [Fact]
    public async Task A_superseded_revision_among_them_refuses_all_of_them()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var (_, itemId, ids) = await SeedAsync(host, client);
        // revising one H1 invoice revises both recipients' H1 invoices
        (
            await client.PostAsync(
                "/odata/CreateGraveInvoiceRevision",
                ODataJson.Body(
                    new
                    {
                        invoiceId = ids[0],
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
            )
        ).EnsureSuccessStatusCode();

        await AssertRefusedAsync(await MarkAsync(client, ids[2], ids[0]), "NotLatestRevision");

        Assert.All(await PaidOnAsync(host, ids), pair => Assert.Null(pair.Value));
    }

    [Fact]
    public async Task An_unknown_invoice_among_them_refuses_all_of_them()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var (_, _, ids) = await SeedAsync(host, client);

        await AssertRefusedAsync(await MarkAsync(client, ids[0], 987654), "InvoiceNotFound");

        Assert.Null((await PaidOnAsync(host, ids))[ids[0]]);
    }

    [Fact]
    public async Task Invoices_of_a_deleted_grave_are_refused()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var (graveId, _, ids) = await SeedAsync(host, client);
        (
            await client.PostAsync("/odata/DeleteGrave", ODataJson.Body(new { graveId }))
        ).EnsureSuccessStatusCode();

        await AssertRefusedAsync(await MarkAsync(client, ids[0]), "GraveDeleted");

        Assert.Null((await PaidOnAsync(host, ids))[ids[0]]);
    }

    [Fact]
    public async Task An_empty_list_is_refused()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();

        await AssertRefusedAsync(await MarkAsync(client), "InvalidRequest");
    }

    [Fact]
    public async Task A_null_body_is_refused()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();

        await AssertRefusedAsync(
            await client.PostAsync(
                "/odata/MarkInvoicesPaid",
                new StringContent("null", Encoding.UTF8, "application/json")
            ),
            "InvalidRequest"
        );
    }

    [Fact]
    public async Task Grave_status_counts_the_open_invoices_of_every_half_year()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var (graveId, _, ids) = await SeedAsync(host, client);
        (await MarkAsync(client, ids[0])).EnsureSuccessStatusCode();

        // whichever half-year the list shows, the count covers all of them —
        // including one the grave has no invoice for yet
        foreach (var (year, half) in new[] { (2031, 1), (2031, 2), (2032, 1) })
        {
            var status = await client.GetAsync(
                $"/odata/GraveInvoicingStatus?year={year}&half={half}"
            );
            status.EnsureSuccessStatusCode();
            using var json = await ODataJson.ReadAsync(status);
            var row = json
                .RootElement.GetProperty("value")
                .EnumerateArray()
                .Single(g => g.GetProperty("Id").GetInt64() == graveId);
            Assert.Equal(3, row.GetProperty("OpenInvoiceCount").GetInt32());
        }
    }
}
