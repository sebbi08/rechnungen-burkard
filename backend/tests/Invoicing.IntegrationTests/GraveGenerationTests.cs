using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Api.Services;
using Burkard.Invoicing.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Burkard.Invoicing.IntegrationTests;

public class GraveGenerationTests
{
    // Each test seeds the same item number, recipient names and grave numbers, so
    // every test uses a fresh ApiTestHost (its own temporary database) — see GraveTests.
    private async Task<(
        long GraveTwoRecipients,
        long GraveOneRecipient,
        long GraveNoRecipients
    )> SeedAsync(ApiTestHost host)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.VatRates.Add(new VatRate { Rate = 7m });
        var item = new InvoiceItem
        {
            Number = "6001",
            Description = "Grabpflege",
            VatRate = 7m,
            DefaultPrice = 100m,
        };
        db.InvoiceItems.Add(item);
        var r1 = new Recipient
        {
            Number = "E-0001",
            Name = "Anna",
            Address = "A-Straße 1",
        };
        var r2 = new Recipient
        {
            Number = "E-0002",
            Name = "Bert",
            Address = "B-Straße 2",
        };
        var r3 = new Recipient
        {
            Number = "E-0003",
            Name = "Carla",
            Address = "C-Straße 3",
        };
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
            new GraveItem
            {
                GraveId = g2.Id,
                ItemId = item.Id,
                Count = 2,
            },
            new GraveItem
            {
                GraveId = g1.Id,
                ItemId = item.Id,
                Count = 1,
            },
            // G-3 bills something but has no recipient, so it is due and skipped
            new GraveItem
            {
                GraveId = g0.Id,
                ItemId = item.Id,
                Count = 1,
            }
        );
        db.GraveRecipients.AddRange(
            new GraveRecipient { GraveId = g2.Id, RecipientId = r1.Id },
            new GraveRecipient { GraveId = g2.Id, RecipientId = r2.Id },
            new GraveRecipient { GraveId = g1.Id, RecipientId = r3.Id }
        );
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
    public async Task Generation_creates_one_invoice_per_recipient_with_a_document_share()
    {
        using var host = new ApiTestHost();
        var (grave2, grave1, _) = await SeedAsync(host);
        using var client = host.CreateClient();

        var start = await client.PostAsync(
            "/odata/GenerateGraveInvoices",
            ODataJson.Body(new { year = 2026, half = 1 })
        );
        Assert.Equal(HttpStatusCode.Accepted, start.StatusCode);
        using var startJson = await ODataJson.ReadAsync(start);
        var jobId = startJson.RootElement.GetProperty("Id").GetInt64();

        var job = await PollJobAsync(client, jobId);
        Assert.Equal("Succeeded", job.GetProperty("Status").GetString());

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var invoices = await db
            .Invoices.Include(i => i.Lines)
            .Include(i => i.Documents)
            .Where(i => i.PeriodYear == 2026 && i.PeriodHalf == 1)
            .ToListAsync();

        // grave G-1 (variable grave2): 2 recipients, item count 2 @ 100.00 @ 7 %.
        // Net cents 2*100*100=20000 already divide evenly by 2, so MakeDivisible
        // bumps nothing: every recipient is billed the 2 units in full, and their
        // share is 20000/2=10000 net cents = 100.00, VAT round(100.00 x 7 / 100, 2)
        // = 7.00, gross 107.00 — the same amount the old per-line split produced,
        // now worked out by the document instead of the persisted line.
        // grave G-2 (variable grave1): 1 recipient → RecipientCount 1, so its share
        // equals its own 100 % total: 107.00.
        Assert.Equal(3, invoices.Count);

        var g2Invoices = invoices.Where(i => i.GraveId == grave2).ToList();
        Assert.Equal(2, g2Invoices.Count);
        Assert.All(g2Invoices, i => Assert.Equal(50m, i.SharePercent));
        Assert.All(g2Invoices, i => Assert.Equal(2, i.RecipientCount));

        var builder = new DocumentDataBuilder();
        foreach (var invoice in g2Invoices)
        {
            Assert.Equal(107.00m, builder.Build(invoice).Share!.GrossTotal);
        }

        // shares sum to the grave total: 107.00 + 107.00 = 214.00
        Assert.Equal(214.00m, g2Invoices.Sum(i => builder.Build(i).Share!.GrossTotal));

        Assert.Equal(2, await db.GraveInvoiceRuns.CountAsync(r => r.Year == 2026 && r.Half == 1));
        Assert.All(
            invoices,
            i =>
            {
                Assert.Equal(2, i.Documents.Count);
                Assert.All(i.Documents, d => Assert.NotEmpty(d.Content));
            }
        );
    }

    [Fact]
    public async Task A_grave_whose_prices_do_not_divide_is_skipped_rather_than_billed_wrong()
    {
        using var host = new ApiTestHost();
        {
            using var scope = host.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.VatRates.Add(new VatRate { Rate = 7m });
            var item = new InvoiceItem
            {
                Number = "6001",
                Description = "Grabpflege",
                VatRate = 7m,
                DefaultPrice = 100m,
            };
            db.InvoiceItems.Add(item);
            var recipients = new[]
            {
                new Recipient
                {
                    Number = "E-0004",
                    Name = "Anna",
                    Address = "A-Straße 1",
                },
                new Recipient
                {
                    Number = "E-0005",
                    Name = "Bert",
                    Address = "B-Straße 2",
                },
                new Recipient
                {
                    Number = "E-0006",
                    Name = "Carla",
                    Address = "C-Straße 3",
                },
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

            db.GraveItems.Add(
                new GraveItem
                {
                    GraveId = grave.Id,
                    ItemId = item.Id,
                    Count = 2,
                }
            );
            db.GraveRecipients.AddRange(
                recipients.Select(r => new GraveRecipient
                {
                    GraveId = grave.Id,
                    RecipientId = r.Id,
                })
            );
            await db.SaveChangesAsync();
        }

        using var client = host.CreateClient();
        var start = await client.PostAsync(
            "/odata/GenerateGraveInvoices",
            ODataJson.Body(new { year = 2026, half = 2 })
        );
        Assert.Equal(HttpStatusCode.Accepted, start.StatusCode);
        using var startJson = await ODataJson.ReadAsync(start);
        var job = await PollJobAsync(client, startJson.RootElement.GetProperty("Id").GetInt64());
        Assert.Equal("Succeeded", job.GetProperty("Status").GetString());

        // 2 x 100.00 = 200.00 net at 7 %, or 20 000 cents, and 20 000 % 3 = 2:
        // the three recipients cannot be billed the same amount. Nothing here
        // adjusts the price to make it fit -- saving such a grave is refused in
        // the first place, and one that reaches generation anyway (saved before
        // the rule existed, say) is skipped rather than billed wrong.
        Assert.Contains("PricesDoNotDivide", job.GetRawText());

        {
            using var scope = host.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Empty(
                await db
                    .Invoices.Where(i => i.PeriodYear == 2026 && i.PeriodHalf == 2)
                    .ToListAsync()
            );
        }
    }

    [Fact]
    public async Task Generation_bills_the_link_price_instead_of_the_items_default_price()
    {
        using var host = new ApiTestHost();
        {
            using var scope = host.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.VatRates.Add(new VatRate { Rate = 7m });
            // the item itself has no price at all: only the link makes the grave billable
            var item = new InvoiceItem
            {
                Number = "6002",
                Description = "Sonderpflege",
                VatRate = 7m,
                DefaultPrice = null,
            };
            var priced = new InvoiceItem
            {
                Number = "6003",
                Description = "Grabpflege",
                VatRate = 7m,
                DefaultPrice = 100m,
            };
            db.InvoiceItems.AddRange(item, priced);
            var recipient = new Recipient
            {
                Number = "E-0007",
                Name = "Anna",
                Address = "A-Straße 1",
            };
            db.Recipients.Add(recipient);
            var grave = new Grave
            {
                GraveNumber = "G-4",
                StartDate = new DateOnly(2020, 1, 1),
                EndDate = new DateOnly(2040, 12, 31),
                CreatedAt = DateOnly.FromDateTime(DateTime.UtcNow),
            };
            db.Graves.Add(grave);
            await db.SaveChangesAsync();

            db.GraveItems.AddRange(
                new GraveItem
                {
                    GraveId = grave.Id,
                    ItemId = item.Id,
                    Count = 1,
                    UnitPrice = 40m,
                },
                // an explicit price also overrides an item that has a default
                new GraveItem
                {
                    GraveId = grave.Id,
                    ItemId = priced.Id,
                    Count = 1,
                    UnitPrice = 60m,
                }
            );
            db.GraveRecipients.Add(
                new GraveRecipient { GraveId = grave.Id, RecipientId = recipient.Id }
            );
            await db.SaveChangesAsync();
        }

        using var client = host.CreateClient();
        var start = await client.PostAsync(
            "/odata/GenerateGraveInvoices",
            ODataJson.Body(new { year = 2029, half = 1 })
        );
        Assert.Equal(HttpStatusCode.Accepted, start.StatusCode);
        using var startJson = await ODataJson.ReadAsync(start);
        var job = await PollJobAsync(client, startJson.RootElement.GetProperty("Id").GetInt64());
        Assert.Equal("Succeeded", job.GetProperty("Status").GetString());

        {
            using var scope = host.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var invoice = await db
                .Invoices.Include(i => i.Lines)
                .SingleAsync(i => i.PeriodYear == 2029 && i.PeriodHalf == 1);

            // 40.00 + 60.00 net, each +7% VAT → 42.80 + 64.20 = 107.00 gross
            var grossCents = (long)
                Math.Round(
                    invoice.Lines.Sum(l =>
                        Math.Round(l.Count * l.UnitPrice * 100m) * (100m + l.VatRate) / 100m
                    )
                );
            Assert.Equal(10700, grossCents);
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
        var first = await client.PostAsync(
            "/odata/GenerateGraveInvoices",
            ODataJson.Body(new { year = 2026, half = 1 })
        );
        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        using var firstJson = await ODataJson.ReadAsync(first);
        await PollJobAsync(client, firstJson.RootElement.GetProperty("Id").GetInt64());

        var second = await client.PostAsync(
            "/odata/GenerateGraveInvoices",
            ODataJson.Body(new { year = 2026, half = 1 })
        );
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        using var secondJson = await ODataJson.ReadAsync(second);
        Assert.Equal(
            "GravesAlreadyInvoiced",
            secondJson.RootElement.GetProperty("error").GetProperty("code").GetString()
        );
    }

    [Fact]
    public async Task Concurrent_generation_is_rejected_with_409()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient(); // start the host so startup recovery has already run
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Jobs.Add(
                new Job
                {
                    Type = "GraveInvoices",
                    Status = JobStatus.Running,
                    TotalCount = 1,
                    CreatedAt = DateTime.UtcNow,
                }
            );
            await db.SaveChangesAsync();
        }

        var response = await client.PostAsync(
            "/odata/GenerateGraveInvoices",
            ODataJson.Body(new { year = 2026, half = 2 })
        );
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        Assert.Equal(
            "JobAlreadyRunning",
            json.RootElement.GetProperty("error").GetProperty("code").GetString()
        );
    }

    [Fact]
    public async Task Status_endpoint_lists_graves_still_open_for_the_half_year_first()
    {
        using var host = new ApiTestHost();
        await SeedAsync(host);
        using var client = host.CreateClient();

        var start = await client.PostAsync(
            "/odata/GenerateGraveInvoices",
            ODataJson.Body(new { year = 2026, half = 1 })
        );
        using var startJson = await ODataJson.ReadAsync(start);
        var job = await PollJobAsync(client, startJson.RootElement.GetProperty("Id").GetInt64());
        Assert.Equal("Succeeded", job.GetProperty("Status").GetString());

        var status = await client.GetAsync("/odata/GraveInvoicingStatus?year=2026&half=1");
        using var json = await ODataJson.ReadAsync(status);
        var rows = json.RootElement.GetProperty("value").EnumerateArray().ToList();
        var invoiced = rows.Select(r => r.GetProperty("Invoiced").GetBoolean()).ToList();

        // G-3 has no recipients, so it stays open while G-1 and G-2 were invoiced
        Assert.Contains(false, invoiced);
        Assert.Contains(true, invoiced);
        Assert.Equal(invoiced.OrderBy(i => i), invoiced);
        Assert.False(invoiced[0]);
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

        var start = await client.PostAsync(
            "/odata/GenerateGraveInvoices",
            ODataJson.Body(new { year = 2026, half = 1 })
        );
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
