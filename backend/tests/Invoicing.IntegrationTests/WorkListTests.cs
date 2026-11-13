using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Api.Services;
using Burkard.Invoicing.Documents;
using Burkard.Invoicing.Domain.Entities;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Burkard.Invoicing.IntegrationTests;

/// <summary>Which graves and services the care and planting lists hold, in
/// which order and at what amounts, and how the lists are served.</summary>
public class WorkListTests
{
    private static DateOnly M(int year, int month) => new(year, month, 1);

    private sealed record Svc(
        BillingQuarter? Quarter,
        int Count = 1,
        decimal? UnitPrice = null,
        decimal? DefaultPrice = null,
        decimal Vat = 7m,
        bool Gross = false
    );

    private static long _next;

    private static long Next() => System.Threading.Interlocked.Increment(ref _next);

    private static long Seed(
        ApiTestHost host,
        string number,
        DateOnly start,
        DateOnly? end,
        long cemeteryId = TestCemetery.Id,
        bool cooperative = false,
        bool deleted = false,
        string? contract = null,
        params Svc[] services
    )
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var grave = new Grave
        {
            GraveNumber = number,
            FieldNumber = "F-1",
            SiteName = "Grabstätte " + number,
            CemeteryId = cemeteryId,
            Position = GravePosition.SingleInside,
            StartDate = start,
            EndDate = end,
            CreatedAt = start,
            IsCooperative = cooperative,
            ContractNumber = contract,
            DeletedAt = deleted ? new DateTime(2026, 1, 1) : null,
        };
        foreach (var s in services)
        {
            var n = Next();
            grave.Items.Add(
                new GraveItem
                {
                    Count = s.Count,
                    UnitPrice = s.UnitPrice,
                    PriceIncludesVat = s.Gross,
                    Item = new InvoiceItem
                    {
                        Number = $"L-{n}",
                        Description = $"Leistung {n}",
                        VatRate = s.Vat,
                        DefaultPrice = s.DefaultPrice,
                        Quarter = s.Quarter,
                    },
                }
            );
        }

        db.Graves.Add(grave);
        db.SaveChanges();
        return grave.Id;
    }

    private static async Task<WorkListData?> CareAsync(
        ApiTestHost host,
        int year,
        CareListServices services
    )
    {
        using var scope = host.Services.CreateScope();
        return await scope
            .ServiceProvider.GetRequiredService<WorkListService>()
            .CareListAsync(year, services);
    }

    private static async Task<WorkListData?> PlantingAsync(
        ApiTestHost host,
        int year,
        BillingQuarter quarter
    )
    {
        using var scope = host.Services.CreateScope();
        return await scope
            .ServiceProvider.GetRequiredService<WorkListService>()
            .PlantingListAsync(year, quarter);
    }

    private static string[] Numbers(WorkListData? data) =>
        data!.Cemeteries.SelectMany(c => c.Graves).Select(g => g.GraveNumber).ToArray();

    [Fact]
    public async Task The_care_list_holds_the_graves_running_in_the_year()
    {
        using var host = new ApiTestHost();
        Seed(host, "ends-in", M(2020, 1), M(2027, 1));
        Seed(host, "starts-in", M(2027, 12), null);
        Seed(host, "ended-out", M(2020, 1), M(2026, 12));
        Seed(host, "starts-out", M(2028, 1), null);
        Seed(host, "open", M(2020, 1), null);
        Seed(host, "deleted", M(2020, 1), null, deleted: true);
        Seed(host, "coop", M(2020, 1), null, cooperative: true, contract: "V-77");

        var data = await CareAsync(host, 2027, CareListServices.None);

        Assert.Equal(
            new[] { "coop", "ends-in", "open", "starts-in" },
            Numbers(data).Order().ToArray()
        );
        Assert.Equal(
            "V-77",
            data!
                .Cemeteries.SelectMany(c => c.Graves)
                .Single(g => g.GraveNumber == "coop")
                .ContractNumber
        );
        Assert.Equal(WorkListKind.CareGravesOnly, data.Kind);
        Assert.Equal(2027, data.Year);
        Assert.Equal(new DateOnly(2026, 10, 6), data.MadeOn);
    }

    [Fact]
    public async Task The_planting_list_holds_graves_with_a_service_of_the_quarter()
    {
        using var host = new ApiTestHost();
        Seed(host, "spring", M(2027, 3), null, services: new Svc(BillingQuarter.Spring));
        Seed(host, "autumn", M(2020, 1), null, services: new Svc(BillingQuarter.Autumn));
        Seed(
            host,
            "spring-ended",
            M(2020, 1),
            M(2026, 12),
            services: new Svc(BillingQuarter.Spring)
        );
        Seed(
            host,
            "spring-ends-jan",
            M(2020, 1),
            M(2027, 1),
            services: new Svc(BillingQuarter.Spring)
        );
        Seed(host, "no-service", M(2020, 1), null);

        var data = await PlantingAsync(host, 2027, BillingQuarter.Spring);

        Assert.Equal(new[] { "spring", "spring-ends-jan" }, Numbers(data).Order().ToArray());
        Assert.Equal(WorkListKind.Planting, data!.Kind);
        Assert.Equal(BillingQuarter.Spring, data.Quarter);
    }

    [Fact]
    public async Task Cemeteries_and_graves_are_in_order()
    {
        using var host = new ApiTestHost();
        long alt;
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var c = new Cemetery { Name = "Altfriedhof" };
            db.Cemeteries.Add(c);
            db.SaveChanges();
            alt = c.Id;
        }

        Seed(host, "A-10", M(2020, 1), null);
        Seed(host, "A-2", M(2020, 1), null);
        Seed(host, "B-1", M(2020, 1), null, cemeteryId: alt);
        Seed(host, "H-1", M(2020, 1), M(2027, 5));
        Seed(host, "H-1", M(2027, 6), null);

        var data = await CareAsync(host, 2027, CareListServices.None);

        Assert.Equal(new[] { "Altfriedhof", "Bergfriedhof" }, data!.Cemeteries.Select(c => c.Name));
        var bergfriedhof = data.Cemeteries[1].Graves;
        Assert.Equal(
            new[] { "A-2", "A-10", "H-1", "H-1" },
            bergfriedhof.Select(g => g.GraveNumber)
        );
        Assert.Equal(M(2020, 1), bergfriedhof[2].Start);
        Assert.Equal(M(2027, 6), bergfriedhof[3].Start);
    }

    [Fact]
    public async Task Cemeteries_without_graves_on_the_list_are_left_out()
    {
        using var host = new ApiTestHost();
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Cemeteries.Add(new Cemetery { Name = "Altfriedhof" });
            db.SaveChanges();
        }

        Seed(host, "A-1", M(2020, 1), null);

        var data = await CareAsync(host, 2027, CareListServices.None);

        Assert.Equal(new[] { "Bergfriedhof" }, data!.Cemeteries.Select(c => c.Name));
    }

    [Fact]
    public async Task The_lines_follow_the_variant()
    {
        using var host = new ApiTestHost();
        Seed(
            host,
            "G-1",
            M(2020, 1),
            M(2027, 3),
            services:
            [
                new Svc(null, DefaultPrice: 10m),
                new Svc(BillingQuarter.Spring, DefaultPrice: 10m),
                new Svc(BillingQuarter.Autumn, DefaultPrice: 10m),
            ]
        );

        var none = await CareAsync(host, 2027, CareListServices.None);
        var care = await CareAsync(host, 2027, CareListServices.Care);
        var all = await CareAsync(host, 2027, CareListServices.All);
        var spring = await PlantingAsync(host, 2027, BillingQuarter.Spring);

        Assert.Empty(none!.Cemeteries[0].Graves[0].Lines);
        Assert.Equal(WorkListKind.CareGravesOnly, none.Kind);

        Assert.Equal(WorkListKind.CareWithServices, care!.Kind);
        Assert.Equal(
            new BillingQuarter?[] { null },
            care.Cemeteries[0].Graves[0].Lines.Select(l => l.Quarter)
        );

        Assert.Equal(WorkListKind.CareWithServices, all!.Kind);
        Assert.Equal(
            new BillingQuarter?[] { null, BillingQuarter.Spring },
            all.Cemeteries[0].Graves[0].Lines.Select(l => l.Quarter)
        );

        Assert.Equal(
            new BillingQuarter?[] { BillingQuarter.Spring },
            spring!.Cemeteries[0].Graves[0].Lines.Select(l => l.Quarter)
        );
    }

    [Fact]
    public async Task Amounts_are_count_times_gross()
    {
        using var host = new ApiTestHost();
        Seed(
            host,
            "G-1",
            M(2020, 1),
            null,
            services:
            [
                new Svc(null, Count: 2, UnitPrice: 45.00m, Vat: 7m),
                new Svc(null, UnitPrice: 48.15m, Vat: 7m, Gross: true),
                new Svc(null, DefaultPrice: 10.00m, Vat: 19m),
            ]
        );

        var data = await CareAsync(host, 2027, CareListServices.Care);

        Assert.Equal(
            new decimal?[] { 96.30m, 48.15m, 11.90m },
            data!.Cemeteries[0].Graves[0].Lines.Select(l => l.Amount)
        );
    }

    [Fact]
    public async Task The_price_on_the_grave_wins_over_the_item_default()
    {
        using var host = new ApiTestHost();
        Seed(
            host,
            "G-1",
            M(2020, 1),
            null,
            services: new Svc(null, Count: 2, UnitPrice: 20.00m, DefaultPrice: 10.00m, Vat: 19m)
        );

        var data = await CareAsync(host, 2027, CareListServices.Care);

        Assert.Equal(47.60m, data!.Cemeteries[0].Graves[0].Lines.Single().Amount);
    }

    [Fact]
    public async Task A_service_without_any_price_has_no_amount()
    {
        using var host = new ApiTestHost();
        Seed(host, "G-1", M(2020, 1), null, services: new Svc(null));

        var data = await CareAsync(host, 2027, CareListServices.Care);

        Assert.Null(data!.Cemeteries[0].Graves[0].Lines.Single().Amount);
    }

    [Fact]
    public async Task Start_and_end_in_the_period_are_flagged()
    {
        using var host = new ApiTestHost();
        Seed(host, "starts", M(2027, 11), null);
        Seed(host, "ends", M(2020, 1), M(2027, 3));
        Seed(host, "old", M(2015, 1), null);

        var care = await CareAsync(host, 2027, CareListServices.None);
        WorkListGrave Grave(string n) =>
            care!.Cemeteries.SelectMany(c => c.Graves).Single(g => g.GraveNumber == n);

        Assert.True(Grave("starts").StartInPeriod);
        Assert.False(Grave("starts").EndInPeriod);
        Assert.True(Grave("ends").EndInPeriod);
        Assert.False(Grave("ends").StartInPeriod);
        Assert.False(Grave("old").StartInPeriod);
        Assert.False(Grave("old").EndInPeriod);

        Seed(host, "p-ends", M(2020, 1), M(2027, 5), services: new Svc(BillingQuarter.Spring));
        var planting = await PlantingAsync(host, 2027, BillingQuarter.Spring);
        Assert.False(planting!.Cemeteries[0].Graves.Single().EndInPeriod);
    }

    [Fact]
    public async Task An_empty_list_is_refused()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();

        var care = await client.GetAsync("/odata/CareList?year=2000&services=none");
        Assert.Equal(HttpStatusCode.BadRequest, care.StatusCode);
        using (var json = await ODataJson.ReadAsync(care))
        {
            var error = json.RootElement.GetProperty("error");
            Assert.Equal("NoGravesOnList", error.GetProperty("code").GetString());
            Assert.Equal("No grave is on this list.", error.GetProperty("message").GetString());
            Assert.Equal(
                new[] { "2000" },
                error.GetProperty("args").EnumerateArray().Select(a => a.GetString())
            );
        }

        var planting = await client.GetAsync("/odata/PlantingList?year=2000&quarter=Spring");
        Assert.Equal(HttpStatusCode.BadRequest, planting.StatusCode);
        using (var json = await ODataJson.ReadAsync(planting))
        {
            var error = json.RootElement.GetProperty("error");
            Assert.Equal("NoGravesOnList", error.GetProperty("code").GetString());
            Assert.Equal(
                new[] { "2000", "Frühling" },
                error.GetProperty("args").EnumerateArray().Select(a => a.GetString())
            );
        }
    }

    [Fact]
    public async Task The_lists_download_as_pdf_with_their_names()
    {
        using var host = new ApiTestHost();
        Seed(host, "G-1", M(2020, 1), null, services: new Svc(BillingQuarter.Spring));
        using var client = host.CreateClient();

        var care = await client.GetAsync("/odata/CareList?year=2027&services=all");
        var planting = await client.GetAsync("/odata/PlantingList?year=2027&quarter=Spring");

        foreach (
            var (response, name) in new[]
            {
                (care, "Pflegeliste-2027.pdf"),
                (planting, "Pflanzliste-2027-Fruehling.pdf"),
            }
        )
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
            var bytes = await response.Content.ReadAsByteArrayAsync();
            Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
            var disposition = response.Content.Headers.ContentDisposition;
            Assert.Equal("attachment", disposition?.DispositionType);
            Assert.Equal(name, disposition?.FileNameStar ?? disposition?.FileName?.Trim('"'));
        }
    }

    [Theory]
    [InlineData("/odata/CareList?services=none")]
    [InlineData("/odata/CareList?year=abc&services=none")]
    [InlineData("/odata/CareList?year=1999&services=none")]
    [InlineData("/odata/CareList?year=2101&services=none")]
    [InlineData("/odata/CareList?year=2027")]
    [InlineData("/odata/CareList?year=2027&services=some")]
    [InlineData("/odata/PlantingList?year=2027")]
    [InlineData("/odata/PlantingList?year=2027&quarter=5")]
    [InlineData("/odata/PlantingList?year=2027&quarter=Fall")]
    [InlineData("/odata/PlantingList?year=2027&quarter=Spring,Summer")]
    [InlineData("/odata/PlantingList?year=2027&quarter=Spring,1")]
    [InlineData("/odata/PlantingList?quarter=Spring")]
    public async Task Bad_parameters_are_refused(string url)
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();

        var response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        Assert.Equal(
            "ValidationFailed",
            json.RootElement.GetProperty("error").GetProperty("code").GetString()
        );
    }

    [Fact]
    public async Task Parameter_values_ignore_case()
    {
        using var host = new ApiTestHost();
        Seed(host, "G-1", M(2020, 1), null, services: new Svc(BillingQuarter.Spring));
        using var client = host.CreateClient();

        var planting = await client.GetAsync("/odata/PlantingList?year=2027&quarter=spring");
        var care = await client.GetAsync("/odata/CareList?year=2027&services=ALL");

        Assert.Equal(HttpStatusCode.OK, planting.StatusCode);
        Assert.Equal(HttpStatusCode.OK, care.StatusCode);
    }
}
