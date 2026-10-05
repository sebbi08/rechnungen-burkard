using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Domain;
using Burkard.Invoicing.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Burkard.Invoicing.IntegrationTests;

/// <summary>A half-year bills the months of the grave's term it covers, and a
/// half-year outside the term bills nothing.</summary>
public class GraveTermGenerationTests
{
    private static async Task<long> SeedAsync(
        ApiTestHost host,
        DateOnly start,
        DateOnly? end,
        params (string Number, BillingQuarter? Quarter, decimal Price)[] items
    )
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (!await db.VatRates.AnyAsync())
        {
            db.VatRates.Add(new VatRate { Rate = 19m });
        }

        var recipient = new Recipient
        {
            Number = $"E-{Guid.NewGuid():N}",
            Name = "Anna",
            Address = "Weg 1",
        };
        var grave = new Grave
        {
            GraveNumber = $"T-{Guid.NewGuid():N}",
            StartDate = start,
            EndDate = end,
            CreatedAt = DateOnly.FromDateTime(DateTime.UtcNow),
        };
        db.Recipients.Add(recipient);
        db.Graves.Add(grave);
        var entities = items
            .Select(i => new InvoiceItem
            {
                Number = $"{i.Number}-{Guid.NewGuid():N}",
                Description = i.Number,
                VatRate = 19m,
                DefaultPrice = i.Price,
                Quarter = i.Quarter,
            })
            .ToList();
        db.InvoiceItems.AddRange(entities);
        await db.SaveChangesAsync();

        foreach (var item in entities)
        {
            db.GraveItems.Add(
                new GraveItem
                {
                    GraveId = grave.Id,
                    ItemId = item.Id,
                    Count = 1,
                }
            );
        }

        db.GraveRecipients.Add(
            new GraveRecipient { GraveId = grave.Id, RecipientId = recipient.Id }
        );
        await db.SaveChangesAsync();
        return grave.Id;
    }

    /// <summary>Runs a generation and returns the job's result.</summary>
    private static async Task<JsonElement> GenerateAsync(HttpClient client, int year, int half)
    {
        var start = await client.PostAsync(
            "/odata/GenerateGraveInvoices",
            ODataJson.Body(new { year, half })
        );
        Assert.Equal(HttpStatusCode.Accepted, start.StatusCode);
        using var startJson = await ODataJson.ReadAsync(start);
        var jobId = startJson.RootElement.GetProperty("Id").GetInt64();

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(20);
        while (DateTime.UtcNow < deadline)
        {
            using var json = await ODataJson.ReadAsync(
                await client.GetAsync($"/odata/Jobs({jobId})")
            );
            var status = json.RootElement.GetProperty("Status").GetString();
            Assert.NotEqual("Failed", status);
            if (status == "Succeeded")
            {
                return JsonDocument
                    .Parse(json.RootElement.GetProperty("ResultJson").GetString()!)
                    .RootElement.Clone();
            }

            await Task.Delay(100);
        }

        throw new TimeoutException("generation did not finish");
    }

    private static async Task<InvoiceLine[]> LinesAsync(
        ApiTestHost host,
        long graveId,
        int year,
        int half
    )
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db
            .Invoices.Where(i =>
                i.GraveId == graveId && i.PeriodYear == year && i.PeriodHalf == half
            )
            .SelectMany(i => i.Lines)
            .OrderBy(l => l.LineNumber)
            .ToArrayAsync();
    }

    [Fact]
    public async Task Generation_prorates_the_start_and_bills_the_touched_quarter()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var graveId = await SeedAsync(
            host,
            new DateOnly(2026, 5, 1),
            null,
            ("Pflege", null, 60m),
            ("Sommer", BillingQuarter.Summer, 40m),
            ("Fruehling", BillingQuarter.Spring, 30m)
        );

        await GenerateAsync(client, 2026, 1);

        var lines = await LinesAsync(host, graveId, 2026, 1);
        Assert.Equal(2, lines.Length);
        Assert.Equal("Pflege, anteilig 05–06/2026 (2 von 6 Monaten)", lines[0].Description);
        Assert.Equal(20.00m, lines[0].UnitPrice);
        Assert.Equal("Sommer", lines[1].Description);
        Assert.Equal(40.00m, lines[1].UnitPrice);
    }

    [Fact]
    public async Task Generation_prorates_a_term_inside_one_half_year()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var graveId = await SeedAsync(
            host,
            new DateOnly(2026, 2, 1),
            new DateOnly(2026, 4, 1),
            ("Pflege", null, 60m)
        );

        await GenerateAsync(client, 2026, 1);

        var line = Assert.Single(await LinesAsync(host, graveId, 2026, 1));
        Assert.Equal(30.00m, line.UnitPrice);
        Assert.Equal("Pflege, anteilig 02–04/2026 (3 von 6 Monaten)", line.Description);
    }

    [Fact]
    public async Task A_half_year_outside_the_term_is_neither_billed_nor_skipped()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var graveId = await SeedAsync(host, new DateOnly(2026, 5, 1), null, ("Pflege", null, 60m));

        var result = await GenerateAsync(client, 2025, 2);

        Assert.Empty(await LinesAsync(host, graveId, 2025, 2));
        Assert.Equal(0, result.GetProperty("InvoicedGraves").GetInt32());
        Assert.Empty(result.GetProperty("SkippedGraves").EnumerateArray());
    }

    [Fact]
    public async Task A_half_year_with_nothing_to_bill_is_not_due_and_so_not_skipped()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var graveId = await SeedAsync(
            host,
            new DateOnly(2026, 1, 1),
            null,
            ("Herbst", BillingQuarter.Autumn, 30m)
        );

        var result = await GenerateAsync(client, 2026, 1);

        Assert.Empty(await LinesAsync(host, graveId, 2026, 1));
        Assert.Equal(0, result.GetProperty("InvoicedGraves").GetInt32());
        Assert.Empty(result.GetProperty("SkippedGraves").EnumerateArray());
    }

    [Fact]
    public async Task Generation_bills_a_prorated_gross_line_as_a_gross()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var graveId = await SeedAsync(host, new DateOnly(2026, 5, 1), null, ("Pflege", null, 119m));
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            foreach (var link in db.GraveItems.Where(l => l.GraveId == graveId))
            {
                link.PriceIncludesVat = true;
            }

            await db.SaveChangesAsync();
        }

        await GenerateAsync(client, 2026, 1);

        var line = Assert.Single(await LinesAsync(host, graveId, 2026, 1));
        Assert.Equal("Pflege, anteilig 05–06/2026 (2 von 6 Monaten)", line.Description);
        Assert.True(line.PriceIncludesVat);
        Assert.Equal(VatInclusivePrice.NetOf(39.67m, 19m, true), line.UnitPrice);
    }

    private static async Task<long[]> StatusIdsAsync(HttpClient client, int year, int half)
    {
        using var json = await ODataJson.ReadAsync(
            await client.GetAsync($"/odata/GraveInvoicingStatus?year={year}&half={half}")
        );
        return json
            .RootElement.GetProperty("value")
            .EnumerateArray()
            .Select(g => g.GetProperty("Id").GetInt64())
            .ToArray();
    }

    [Fact]
    public async Task The_status_leaves_out_graves_with_nothing_to_bill_in_the_half_year()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var graveId = await SeedAsync(
            host,
            new DateOnly(2026, 1, 1),
            null,
            ("Herbst", BillingQuarter.Autumn, 30m)
        );

        Assert.DoesNotContain(graveId, await StatusIdsAsync(client, 2026, 1));
        Assert.Contains(graveId, await StatusIdsAsync(client, 2026, 2));
    }

    [Fact]
    public async Task The_status_keeps_a_grave_invoiced_for_a_half_year_it_would_not_be_due_in()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var graveId = await SeedAsync(
            host,
            new DateOnly(2026, 1, 1),
            null,
            ("Herbst", BillingQuarter.Autumn, 30m)
        );
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.GraveInvoiceRuns.Add(
                new GraveInvoiceRun
                {
                    GraveId = graveId,
                    Year = 2026,
                    Half = 1,
                    CreatedAt = DateTime.UtcNow,
                }
            );
            await db.SaveChangesAsync();
        }

        Assert.Contains(graveId, await StatusIdsAsync(client, 2026, 1));
    }

    [Fact]
    public async Task The_status_leaves_out_graves_outside_the_term()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var graveId = await SeedAsync(host, new DateOnly(2026, 5, 1), null, ("Pflege", null, 60m));

        using var before = await ODataJson.ReadAsync(
            await client.GetAsync("/odata/GraveInvoicingStatus?year=2025&half=2")
        );
        using var during = await ODataJson.ReadAsync(
            await client.GetAsync("/odata/GraveInvoicingStatus?year=2026&half=1")
        );

        Assert.DoesNotContain(
            before.RootElement.GetProperty("value").EnumerateArray(),
            g => g.GetProperty("Id").GetInt64() == graveId
        );
        Assert.Contains(
            during.RootElement.GetProperty("value").EnumerateArray(),
            g => g.GetProperty("Id").GetInt64() == graveId
        );
    }
}
