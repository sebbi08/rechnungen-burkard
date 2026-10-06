using System;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Domain.Entities;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Burkard.Invoicing.IntegrationTests;

/// <summary>The grave editor asks for every invoice the grave's term will
/// produce while the user types: what each bills, whether it divides, and which
/// price on which line would fix it.</summary>
public class GraveBillingPreviewTests
{
    private static async Task<(long Care, long Summer)> SeedAsync(ApiTestHost host)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.VatRates.Add(new VatRate { Rate = 19m });
        var care = new InvoiceItem
        {
            Number = "100",
            Description = "Grabpflege",
            VatRate = 19m,
            DefaultPrice = 60m,
        };
        var summer = new InvoiceItem
        {
            Number = "210",
            Description = "Sommerbepflanzung",
            VatRate = 19m,
            DefaultPrice = 40m,
            Quarter = BillingQuarter.Summer,
        };
        db.InvoiceItems.AddRange(care, summer);
        await db.SaveChangesAsync();
        return (care.Id, summer.Id);
    }

    private static object Line(long itemId, decimal? price) =>
        new
        {
            ItemId = itemId,
            Count = 1,
            UnitPrice = price,
            PriceIncludesVat = false,
        };

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, object body) =>
        await client.PostAsJsonAsync("/odata/PreviewGraveBilling", body);

    private static async Task<JsonElement> PreviewAsync(HttpClient client, object body)
    {
        var response = await PostAsync(client, body);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    }

    [Fact]
    public async Task The_preview_lists_the_terms_invoices_with_their_lines()
    {
        // a clock before the term: its start block is still ahead
        using var host = new ApiTestHost { Now = new DateTime(2025, 10, 6) };
        var (care, summer) = await SeedAsync(host);
        using var client = host.CreateClient();

        var body = await PreviewAsync(
            client,
            new
            {
                Recipients = 2,
                StartMonth = "2026-05",
                EndMonth = "2030-08",
                Lines = new[] { Line(care, null), Line(summer, null) },
            }
        );

        Assert.True(body.GetProperty("Ok").GetBoolean());
        Assert.Equal(2, body.GetProperty("Recipients").GetInt32());
        var invoices = body.GetProperty("Invoices").EnumerateArray().ToArray();
        Assert.Equal(
            new[] { "Start", "FullFirstHalf", "FullSecondHalf", "End" },
            invoices.Select(i => i.GetProperty("Kind").GetString())
        );

        var start = invoices[0];
        Assert.Equal(2026, start.GetProperty("Year").GetInt32());
        Assert.Equal(1, start.GetProperty("Half").GetInt32());
        Assert.Equal(2, start.GetProperty("Months").GetInt32());
        Assert.Equal(60.00m, start.GetProperty("NetTotal").GetDecimal());
        var lines = start.GetProperty("Lines").EnumerateArray().ToArray();
        Assert.Equal(new[] { 0, 1 }, lines.Select(l => l.GetProperty("LineIndex").GetInt32()));
        Assert.Equal(
            "Grabpflege, anteilig 05–06/2026 (2 von 6 Monaten)",
            lines[0].GetProperty("Description").GetString()
        );
        Assert.Equal(20.00m, lines[0].GetProperty("Price").GetDecimal());

        // the full H2 carries no summer item
        Assert.Equal(JsonValueKind.Null, invoices[2].GetProperty("Year").ValueKind);
        Assert.Single(invoices[2].GetProperty("Lines").EnumerateArray());

        // 60,00 net at 19 % over two: 30,00 + 5,70 each
        Assert.Equal(71.40m, invoices[2].GetProperty("GrossTotal").GetDecimal());
        Assert.Equal(35.70m, invoices[2].GetProperty("PerRecipient").GetDecimal());
    }

    [Fact]
    public async Task The_start_block_of_a_term_that_has_begun_is_not_previewed()
    {
        // the fixed clock stands in H2 2026: a start in 05/2026 has begun, one in
        // 01/2027 has not
        using var host = new ApiTestHost();
        var (care, _) = await SeedAsync(host);
        using var client = host.CreateClient();

        async Task<string?[]> KindsAsync(string startMonth)
        {
            var body = await PreviewAsync(
                client,
                new
                {
                    Recipients = 2,
                    StartMonth = startMonth,
                    Lines = new[] { Line(care, 60.02m) },
                }
            );
            return body.GetProperty("Invoices")
                .EnumerateArray()
                .Select(i => i.GetProperty("Kind").GetString())
                .ToArray();
        }

        Assert.Equal(new[] { "FullFirstHalf", "FullSecondHalf" }, await KindsAsync("2026-05"));
        Assert.Equal(
            new[] { "Start", "FullFirstHalf", "FullSecondHalf" },
            await KindsAsync("2027-05")
        );
    }

    [Fact]
    public async Task A_block_that_does_not_divide_comes_back_corrected()
    {
        // a clock before the term: its start block is still ahead
        using var host = new ApiTestHost { Now = new DateTime(2025, 10, 6) };
        var (care, _) = await SeedAsync(host);
        using var client = host.CreateClient();

        var body = await PreviewAsync(
            client,
            new
            {
                Recipients = 2,
                StartMonth = "2026-05",
                Lines = new[] { Line(care, 60.01m) },
            }
        );

        Assert.True(body.GetProperty("Ok").GetBoolean());
        Assert.False(body.TryGetProperty("LineSuggestions", out _));

        var full = body.GetProperty("Invoices")[1];
        Assert.Equal("FullFirstHalf", full.GetProperty("Kind").GetString());
        var correction = Assert.Single(full.GetProperty("Corrections").EnumerateArray().ToArray());
        Assert.Equal("FullFirstHalf", correction.GetProperty("Kind").GetString());
        Assert.Equal(JsonValueKind.Null, correction.GetProperty("Year").ValueKind);
        Assert.Equal(1, correction.GetProperty("Half").GetInt32());
        Assert.Equal(0, correction.GetProperty("LineIndex").GetInt32());
        Assert.Equal(19m, correction.GetProperty("VatRate").GetDecimal());
        Assert.Equal("100", correction.GetProperty("ItemNumber").GetString());
        Assert.Equal(60.01m, correction.GetProperty("EnteredPrice").GetDecimal());
        Assert.Equal(60.02m, correction.GetProperty("CorrectedPrice").GetDecimal());

        var line = Assert.Single(full.GetProperty("Lines").EnumerateArray().ToArray());
        Assert.Equal(60.02m, line.GetProperty("Price").GetDecimal());
        Assert.Equal(60.01m, line.GetProperty("EnteredPrice").GetDecimal());
        Assert.True(line.GetProperty("Corrected").GetBoolean());
        Assert.NotEqual(JsonValueKind.Null, full.GetProperty("GrossTotal").ValueKind);

        var start = body.GetProperty("Invoices")[0];
        Assert.Equal(20.00m, start.GetProperty("NetTotal").GetDecimal());
        Assert.Empty(start.GetProperty("Corrections").EnumerateArray());
        Assert.False(start.GetProperty("Lines")[0].GetProperty("Corrected").GetBoolean());
    }

    [Theory]
    [InlineData(0, "NoRecipients")]
    [InlineData(5, "TooManyRecipients")]
    public async Task Preview_refuses_a_recipient_count_out_of_range(int recipients, string code)
    {
        using var host = new ApiTestHost();
        var (care, _) = await SeedAsync(host);
        using var client = host.CreateClient();

        var response = await PostAsync(
            client,
            new { Recipients = recipients, Lines = new[] { Line(care, null) } }
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(code, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Preview_refuses_an_end_before_the_start()
    {
        using var host = new ApiTestHost();
        var (care, _) = await SeedAsync(host);
        using var client = host.CreateClient();

        var response = await PostAsync(
            client,
            new
            {
                Recipients = 1,
                StartMonth = "2026-05",
                EndMonth = "2026-04",
                Lines = new[] { Line(care, null) },
            }
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("DateRangeInvalid", await response.Content.ReadAsStringAsync());
    }
}
