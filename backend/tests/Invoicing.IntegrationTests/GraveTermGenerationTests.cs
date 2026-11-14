using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Api.Services;
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
            CemeteryId = TestCemetery.Id,
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

    /// <summary>A grave starting 2026-03 with care at 60,00 all year and three
    /// recipients, which needs a rounding correction. Stores the corrections the
    /// rule computes, the first one raised by <paramref name="drift"/>.</summary>
    private static async Task<long> SeedCorrectedAsync(
        ApiTestHost host,
        bool storeCorrections,
        decimal drift = 0m,
        DateOnly? end = null,
        decimal price = 60m,
        DateOnly? start = null,
        (int Year, int Half)? current = null
    )
    {
        var graveId = await SeedAsync(
            host,
            start ?? new DateOnly(2026, 3, 1),
            end,
            ("Pflege", null, price)
        );
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var others = new[] { "Bert", "Carla" }
            .Select(name => new Recipient
            {
                Number = $"E-{Guid.NewGuid():N}",
                Name = name,
                Address = "Weg 2",
            })
            .ToList();
        db.Recipients.AddRange(others);
        await db.SaveChangesAsync();
        db.GraveRecipients.AddRange(
            others.Select(r => new GraveRecipient { GraveId = graveId, RecipientId = r.Id })
        );
        await db.SaveChangesAsync();

        if (storeCorrections)
        {
            var grave = await db.Graves.SingleAsync(g => g.Id == graveId);
            var links = (await GraveBilling.LinksByGraveAsync(db))[graveId];
            var keys = GraveBillingCheck
                .Evaluate(links, grave.StartDate, grave.EndDate, 3, current ?? (2026, 2))
                .CorrectionKeys;
            Assert.NotEmpty(keys);
            var first = true;
            foreach (var key in keys)
            {
                db.GraveBillingCorrections.Add(
                    new GraveBillingCorrection
                    {
                        GraveId = graveId,
                        Kind = key.Kind,
                        Year = key.Year,
                        Half = key.Half,
                        VatRate = key.VatRate,
                        ItemId = key.ItemId,
                        EnteredPrice = 0m,
                        CorrectedPrice = key.CorrectedPrice + (first ? drift : 0m),
                    }
                );
                first = false;
            }

            await db.SaveChangesAsync();
        }

        return graveId;
    }

    private static async Task<JsonElement> RefusedGenerationAsync(HttpClient client)
    {
        var response = await client.PostAsync(
            "/odata/GenerateGraveInvoices",
            ODataJson.Body(new { year = 2026, half = 1 })
        );
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        return json.RootElement.GetProperty("error").Clone();
    }

    private static async Task<string> GraveNumberAsync(ApiTestHost host, long graveId)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return (await db.Graves.SingleAsync(g => g.Id == graveId)).GraveNumber;
    }

    [Fact]
    public async Task Generation_bills_the_stored_correction()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var graveId = await SeedCorrectedAsync(host, storeCorrections: true);

        await GenerateAsync(client, 2026, 1);

        var lines = await LinesAsync(host, graveId, 2026, 1);
        Assert.Equal(3, lines.Length);
        Assert.All(
            lines,
            l =>
            {
                Assert.Equal(40.02m, l.UnitPrice);
                Assert.StartsWith("Pflege, anteilig 03–06/2026", l.Description);
            }
        );
    }

    [Fact]
    public async Task Generation_stops_when_a_grave_has_no_stored_correction_it_needs()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var graveId = await SeedCorrectedAsync(host, storeCorrections: false);

        var error = await RefusedGenerationAsync(client);

        Assert.Equal("CorrectionsOutdated", error.GetProperty("code").GetString());
        Assert.Contains(
            await GraveNumberAsync(host, graveId),
            error.GetProperty("args")[0].GetString()
        );
        Assert.Empty(await LinesAsync(host, graveId, 2026, 1));
    }

    [Fact]
    public async Task Generation_stops_when_a_stored_correction_differs()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var graveId = await SeedCorrectedAsync(host, storeCorrections: true, drift: 0.01m);

        var error = await RefusedGenerationAsync(client);

        Assert.Equal("CorrectionsOutdated", error.GetProperty("code").GetString());
        Assert.Contains(
            await GraveNumberAsync(host, graveId),
            error.GetProperty("args")[0].GetString()
        );
        Assert.Empty(await LinesAsync(host, graveId, 2026, 1));
    }

    [Fact]
    public async Task The_precheck_ignores_a_stored_start_row_before_the_generation_window()
    {
        // the fixed clock stands in H2 2026, generation reaches back to H2 2025:
        // the start block of 2025 H1 is not checked, so its stored row, wrong as
        // it is, blocks nothing
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        await SeedCorrectedAsync(
            host,
            storeCorrections: true,
            drift: 0.01m,
            start: new DateOnly(2025, 3, 1),
            current: (2025, 1)
        );

        var response = await client.PostAsync(
            "/odata/GenerateGraveInvoices",
            ODataJson.Body(new { year = 2026, half = 1 })
        );

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    [Fact]
    public async Task A_start_in_the_current_half_year_bills_its_confirmed_correction()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var start = new DateOnly(2026, 11, 1);
        var graveId = await SeedCorrectedAsync(
            host,
            storeCorrections: true,
            start: start,
            price: 100.02m
        );
        decimal corrected;
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var links = (await GraveBilling.LinksByGraveAsync(db))[graveId];
            corrected = GraveBillingCheck
                .Evaluate(links, start, null, 3, (2026, 2))
                .CorrectionKeys.Single(k => k.Kind == BillingExampleKind.Start)
                .CorrectedPrice;
        }

        Assert.NotEqual(33.34m, corrected);

        await GenerateAsync(client, 2026, 2);

        var lines = await LinesAsync(host, graveId, 2026, 2);
        Assert.NotEmpty(lines);
        // November and December of 100,02 are 33,34, which does not divide by three
        Assert.All(lines, l => Assert.Equal(corrected, l.UnitPrice));
    }

    [Fact]
    public async Task Generation_refuses_a_half_year_more_than_two_back()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();

        var response = await client.PostAsync(
            "/odata/GenerateGraveInvoices",
            ODataJson.Body(new { year = 2025, half = 1 })
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        var error = json.RootElement.GetProperty("error");
        Assert.Equal("HalfYearTooOld", error.GetProperty("code").GetString());
        Assert.Equal(
            "Invoices can be generated at most two half-years back.",
            error.GetProperty("message").GetString()
        );
        Assert.Equal(
            new[] { "H2 2025" },
            error.GetProperty("args").EnumerateArray().Select(a => a.GetString())
        );
    }

    [Theory]
    [InlineData(2025, 2)]
    [InlineData(2026, 1)]
    [InlineData(2026, 2)]
    [InlineData(2027, 1)]
    public async Task Generation_accepts_the_current_half_year_and_two_back(int year, int half)
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();

        var response = await client.PostAsync(
            "/odata/GenerateGraveInvoices",
            ODataJson.Body(new { year, half })
        );

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    [Fact]
    public async Task An_invoiced_grave_with_outdated_corrections_does_not_block_the_run()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var invoiced = await SeedCorrectedAsync(host, storeCorrections: false);
        await SeedCorrectedAsync(host, storeCorrections: true);
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.GraveInvoiceRuns.Add(
                new GraveInvoiceRun
                {
                    GraveId = invoiced,
                    Year = 2026,
                    Half = 1,
                    CreatedAt = DateTime.UtcNow,
                }
            );
            await db.SaveChangesAsync();
        }

        var response = await client.PostAsync(
            "/odata/GenerateGraveInvoices",
            ODataJson.Body(new { year = 2026, half = 1 })
        );

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    [Fact]
    public async Task Generation_bills_each_block_its_own_correction()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var end = new DateOnly(2028, 8, 1);
        var graveId = await SeedCorrectedAsync(
            host,
            storeCorrections: true,
            end: end,
            price: 30.04m
        );

        List<CorrectionKey> keys;
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var links = (await GraveBilling.LinksByGraveAsync(db))[graveId];
            keys = GraveBillingCheck
                .Evaluate(links, new DateOnly(2026, 3, 1), end, 3, (2026, 2))
                .CorrectionKeys.ToList();
        }

        decimal PriceOf(BillingExampleKind kind) => keys.Single(k => k.Kind == kind).CorrectedPrice;

        // the blocks need different corrections, so a block taking another
        // block's row would bill a visibly different price
        var distinct = new[]
        {
            PriceOf(BillingExampleKind.Start),
            PriceOf(BillingExampleKind.FullSecondHalf),
            PriceOf(BillingExampleKind.End),
        };
        Assert.Equal(3, distinct.Distinct().Count());

        await GenerateAsync(client, 2026, 1);
        await GenerateAsync(client, 2027, 2);
        await GenerateAsync(client, 2028, 2);

        Assert.All(
            await LinesAsync(host, graveId, 2026, 1),
            l => Assert.Equal(distinct[0], l.UnitPrice)
        );
        Assert.All(
            await LinesAsync(host, graveId, 2027, 2),
            l => Assert.Equal(distinct[1], l.UnitPrice)
        );
        Assert.All(
            await LinesAsync(host, graveId, 2028, 2),
            l => Assert.Equal(distinct[2], l.UnitPrice)
        );
    }
}
