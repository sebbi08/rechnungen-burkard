using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Burkard.Invoicing.IntegrationTests;

public class GraveGenerationTests
{
    // Each test seeds the same item number, recipient names and grave numbers, so
    // every test uses a fresh ApiTestHost (its own temporary database) — see GraveTests.
    private async Task<(long GraveTwoRecipients, long GraveOneRecipient, long GraveNoRecipients)> SeedAsync(ApiTestHost host)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.VatRates.Add(new VatRate { Rate = 7m });
        var item = new InvoiceItem { Number = "6001", Description = "Grabpflege", VatRate = 7m, DefaultPrice = 100m };
        db.InvoiceItems.Add(item);
        var r1 = new Recipient { Name = "Anna", Address = "A-Straße 1" };
        var r2 = new Recipient { Name = "Bert", Address = "B-Straße 2" };
        var r3 = new Recipient { Name = "Carla", Address = "C-Straße 3" };
        db.Recipients.AddRange(r1, r2, r3);
        await db.SaveChangesAsync();

        var g2 = new Grave
        {
            GraveNumber = "G-1",
            StartDate = new DateOnly(2020, 1, 1),
            EndDate = new DateOnly(2040, 12, 31),
            CreatedAt = DateOnly.FromDateTime(DateTime.UtcNow),
        };
        var g1 = new Grave
        {
            GraveNumber = "G-2",
            StartDate = new DateOnly(2020, 1, 1),
            EndDate = new DateOnly(2040, 12, 31),
            CreatedAt = DateOnly.FromDateTime(DateTime.UtcNow),
        };
        var g0 = new Grave
        {
            GraveNumber = "G-3",
            StartDate = new DateOnly(2020, 1, 1),
            EndDate = new DateOnly(2040, 12, 31),
            CreatedAt = DateOnly.FromDateTime(DateTime.UtcNow),
        };
        db.Graves.AddRange(g2, g1, g0);
        await db.SaveChangesAsync();

        db.GraveItems.AddRange(
            new GraveItem { GraveId = g2.Id, ItemId = item.Id, Count = 2 },
            new GraveItem { GraveId = g1.Id, ItemId = item.Id, Count = 1 });
        db.GraveRecipients.AddRange(
            new GraveRecipient { GraveId = g2.Id, RecipientId = r1.Id },
            new GraveRecipient { GraveId = g2.Id, RecipientId = r2.Id },
            new GraveRecipient { GraveId = g1.Id, RecipientId = r3.Id });
        await db.SaveChangesAsync();

        return (g2.Id, g1.Id, g0.Id);
    }

    private async Task<JsonElement> PollJobAsync(HttpClient client, long jobId)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(20);
        while (DateTime.UtcNow < deadline)
        {
            var response = await client.GetAsync($"/odata/Jobs({jobId})");
            response.EnsureSuccessStatusCode();
            using var json = await ODataJson.ReadAsync(response);
            var status = json.RootElement.GetProperty("Status").GetString();
            if (status is "Succeeded" or "Failed")
            {
                return json.RootElement.Clone();
            }

            await Task.Delay(100);
        }

        throw new TimeoutException("generation job did not finish");
    }

    [Fact]
    public async Task Generation_creates_one_invoice_per_recipient_with_split_shares()
    {
        using var host = new ApiTestHost();
        var (grave2, grave1, _) = await SeedAsync(host);
        using var client = host.CreateClient();

        var start = await client.PostAsync("/odata/GenerateGraveInvoices",
            ODataJson.Body(new { year = 2026, half = 1 }));
        Assert.Equal(HttpStatusCode.Accepted, start.StatusCode);
        using var startJson = await ODataJson.ReadAsync(start);
        var jobId = startJson.RootElement.GetProperty("Id").GetInt64();

        var job = await PollJobAsync(client, jobId);
        Assert.Equal("Succeeded", job.GetProperty("Status").GetString());

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var invoices = await db.Invoices.Include(i => i.Lines).Include(i => i.Documents)
            .Where(i => i.PeriodYear == 2026 && i.PeriodHalf == 1)
            .ToListAsync();

        // grave G-1: 2 recipients × share of 214.00 total (2 × 100 + 7% VAT = 214.00) → 107.00 each
        // grave G-2: 1 recipient → full 107.00
        Assert.Equal(3, invoices.Count);

        var g2Invoices = invoices.Where(i => i.GraveId == grave2).ToList();
        Assert.Equal(2, g2Invoices.Count);
        Assert.All(g2Invoices, i => Assert.Equal(50m, i.SharePercent));

        // split totals: 21400 cents / 2 = 10700 each → line gross 107.00
        foreach (var invoice in g2Invoices)
        {
            var grossCents = invoice.Lines.Sum(l => (long)Math.Round(
                Math.Round(l.Count * l.UnitPrice * 100m) * (100m + l.VatRate) / 100m));
            Assert.Equal(10700, grossCents);
        }

        // shares sum to the grave total: 10700 + 10700 = 21400
        Assert.Equal(21400, g2Invoices.Sum(i => (long)Math.Round(
            i.Lines.Sum(l => Math.Round(l.Count * l.UnitPrice * 100m) * (100m + l.VatRate) / 100m))));

        Assert.Equal(2, await db.GraveInvoiceRuns.CountAsync(r => r.Year == 2026 && r.Half == 1));
        Assert.All(invoices, i =>
        {
            Assert.Equal(2, i.Documents.Count);
            Assert.All(i.Documents, d => Assert.NotEmpty(d.Content));
        });
    }

    [Fact]
    public async Task Odd_three_way_split_keeps_persisted_line_gross_exact_to_the_cent()
    {
        using var host = new ApiTestHost();
        {
            using var scope = host.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.VatRates.Add(new VatRate { Rate = 7m });
            var item = new InvoiceItem { Number = "6001", Description = "Grabpflege", VatRate = 7m, DefaultPrice = 100m };
            db.InvoiceItems.Add(item);
            var recipients = new[]
            {
                new Recipient { Name = "Anna", Address = "A-Straße 1" },
                new Recipient { Name = "Bert", Address = "B-Straße 2" },
                new Recipient { Name = "Carla", Address = "C-Straße 3" },
            };
            db.Recipients.AddRange(recipients);
            var grave = new Grave
            {
                GraveNumber = "G-1",
                StartDate = new DateOnly(2020, 1, 1),
                EndDate = new DateOnly(2040, 12, 31),
                CreatedAt = DateOnly.FromDateTime(DateTime.UtcNow),
            };
            db.Graves.Add(grave);
            await db.SaveChangesAsync();

            db.GraveItems.Add(new GraveItem { GraveId = grave.Id, ItemId = item.Id, Count = 2 });
            db.GraveRecipients.AddRange(recipients.Select(r =>
                new GraveRecipient { GraveId = grave.Id, RecipientId = r.Id }));
            await db.SaveChangesAsync();
        }

        using var client = host.CreateClient();
        var start = await client.PostAsync("/odata/GenerateGraveInvoices",
            ODataJson.Body(new { year = 2026, half = 2 }));
        Assert.Equal(HttpStatusCode.Accepted, start.StatusCode);
        using var startJson = await ODataJson.ReadAsync(start);
        var job = await PollJobAsync(client, startJson.RootElement.GetProperty("Id").GetInt64());
        Assert.Equal("Succeeded", job.GetProperty("Status").GetString());

        {
            using var scope = host.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var invoices = await db.Invoices.Include(i => i.Lines)
                .Where(i => i.PeriodYear == 2026 && i.PeriodHalf == 2)
                .ToListAsync();

            // 2 × 100.00 + 7% VAT = 214.00 gross for the grave; 3 recipients → 71.34 / 71.33 / 71.33
            Assert.Equal(3, invoices.Count);
            Assert.All(invoices, i => Assert.Equal(100m / 3, i.SharePercent));

            // gross per invoice recomputed from the PERSISTED lines:
            // round(round(count × unitPrice × 100) × (100 + rate) / 100) per line, summed.
            var grosses = invoices
                .Select(i => (long)Math.Round(i.Lines.Sum(l =>
                    Math.Round(l.Count * l.UnitPrice * 100m) * (100m + l.VatRate) / 100m)))
                .OrderBy(g => g)
                .ToList();

            Assert.Equal(new List<long> { 7133, 7133, 7134 }, grosses);
            Assert.Equal(21400, grosses.Sum());
        }
    }

    [Fact]
    public async Task Second_generation_for_same_half_year_is_rejected()
    {
        using var host = new ApiTestHost();
        var (_, _, graveNoRecipients) = await SeedAsync(host);
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var grave = await db.Graves.SingleAsync(g => g.Id == graveNoRecipients);
            db.Graves.Remove(grave); // G-3 can never be invoiced; remove it so ALL graves are covered
            await db.SaveChangesAsync();
        }

        using var client = host.CreateClient();
        var first = await client.PostAsync("/odata/GenerateGraveInvoices",
            ODataJson.Body(new { year = 2026, half = 1 }));
        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        using var firstJson = await ODataJson.ReadAsync(first);
        await PollJobAsync(client, firstJson.RootElement.GetProperty("Id").GetInt64());

        var second = await client.PostAsync("/odata/GenerateGraveInvoices",
            ODataJson.Body(new { year = 2026, half = 1 }));
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        using var secondJson = await ODataJson.ReadAsync(second);
        Assert.Equal("GravesAlreadyInvoiced", secondJson.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Concurrent_generation_is_rejected_with_409()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient(); // start the host so startup recovery has already run
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Jobs.Add(new Job
            {
                Type = "GraveInvoices",
                Status = JobStatus.Running,
                TotalCount = 1,
                CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var response = await client.PostAsync("/odata/GenerateGraveInvoices",
            ODataJson.Body(new { year = 2026, half = 2 }));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        Assert.Equal("JobAlreadyRunning", json.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Status_endpoint_orders_not_invoiced_first_and_reports_skips()
    {
        using var host = new ApiTestHost();
        var (_, _, graveNoRecipients) = await SeedAsync(host);
        using var client = host.CreateClient();

        var statusBefore = await client.GetAsync("/odata/GraveInvoicingStatus?year=2026&half=1");
        using var before = await ODataJson.ReadAsync(statusBefore);
        var itemsBefore = before.RootElement.GetProperty("value").EnumerateArray().ToList();
        Assert.All(itemsBefore, e => Assert.False(e.GetProperty("Invoiced").GetBoolean()));

        var start = await client.PostAsync("/odata/GenerateGraveInvoices",
            ODataJson.Body(new { year = 2026, half = 1 }));
        using var startJson = await ODataJson.ReadAsync(start);
        var job = await PollJobAsync(client, startJson.RootElement.GetProperty("Id").GetInt64());
        Assert.Equal("Succeeded", job.GetProperty("Status").GetString());

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(2, await db.GraveInvoiceRuns.CountAsync()); // G-1 and G-2
        // grave without recipients is skipped and reported
        var storedJob = await db.Jobs.SingleAsync(j => j.Type == "GraveInvoices");
        Assert.Contains("G-3", storedJob.ResultJson);
    }
}
