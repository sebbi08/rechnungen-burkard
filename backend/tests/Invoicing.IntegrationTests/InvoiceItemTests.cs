using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace Burkard.Invoicing.IntegrationTests;

public class InvoiceItemTests : IClassFixture<ApiTestHost>
{
    private readonly ApiTestHost _host;

    public InvoiceItemTests(ApiTestHost host) => _host = host;

    private async Task SeedVatRateAsync(HttpClient client) =>
        await client.PostAsync("/odata/VatRates", ODataJson.Body(new { Rate = 7.0m }));

    [Fact]
    public async Task Create_item_without_default_price_stores_null_price()
    {
        using var client = _host.CreateClient();
        await SeedVatRateAsync(client);

        var response = await client.PostAsync(
            "/odata/InvoiceItems",
            ODataJson.Body(
                new
                {
                    Number = "1001",
                    Description = "Grabpflege",
                    VatRate = 7.0m,
                    DefaultPrice = (decimal?)null,
                }
            )
        );
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("DefaultPrice").ValueKind);
    }

    [Fact]
    public async Task Create_item_with_unknown_vat_rate_is_rejected()
    {
        using var client = _host.CreateClient();
        var response = await client.PostAsync(
            "/odata/InvoiceItems",
            ODataJson.Body(
                new
                {
                    Number = "1002",
                    Description = "Unknown rate",
                    VatRate = 99m,
                }
            )
        );
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        Assert.Equal(
            "VatRateNotConfigured",
            json.RootElement.GetProperty("error").GetProperty("code").GetString()
        );
    }

    [Fact]
    public async Task Create_item_with_duplicate_number_is_rejected()
    {
        using var client = _host.CreateClient();
        await SeedVatRateAsync(client);
        var first = await client.PostAsync(
            "/odata/InvoiceItems",
            ODataJson.Body(
                new
                {
                    Number = "2001",
                    Description = "First",
                    VatRate = 7.0m,
                }
            )
        );
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var duplicate = await client.PostAsync(
            "/odata/InvoiceItems",
            ODataJson.Body(
                new
                {
                    Number = "2001",
                    Description = "Second",
                    VatRate = 7.0m,
                }
            )
        );
        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
        using var json = await ODataJson.ReadAsync(duplicate);
        Assert.Equal(
            "ItemNumberExists",
            json.RootElement.GetProperty("error").GetProperty("code").GetString()
        );
    }

    [Fact]
    public async Task Patch_item_with_empty_description_is_rejected()
    {
        using var client = _host.CreateClient();
        await SeedVatRateAsync(client);
        var created = await client.PostAsync(
            "/odata/InvoiceItems",
            ODataJson.Body(
                new
                {
                    Number = "3001",
                    Description = "Grabpflege",
                    VatRate = 7.0m,
                }
            )
        );
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var id = await GetCreatedIdAsync(created);
        var patch = new HttpRequestMessage(HttpMethod.Patch, $"/odata/InvoiceItems({id})")
        {
            Content = ODataJson.Body(new { Description = "" }),
        };
        var response = await client.SendAsync(patch);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        Assert.Equal(
            "ItemDescriptionRequired",
            json.RootElement.GetProperty("error").GetProperty("code").GetString()
        );
    }

    [Fact]
    public async Task Patch_item_with_unconfigured_vat_rate_is_rejected()
    {
        using var client = _host.CreateClient();
        await SeedVatRateAsync(client);
        var created = await client.PostAsync(
            "/odata/InvoiceItems",
            ODataJson.Body(
                new
                {
                    Number = "3002",
                    Description = "Grabpflege",
                    VatRate = 7.0m,
                }
            )
        );
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var id = await GetCreatedIdAsync(created);
        var patch = new HttpRequestMessage(HttpMethod.Patch, $"/odata/InvoiceItems({id})")
        {
            Content = ODataJson.Body(new { VatRate = 99m }),
        };
        var response = await client.SendAsync(patch);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        Assert.Equal(
            "VatRateNotConfigured",
            json.RootElement.GetProperty("error").GetProperty("code").GetString()
        );
    }

    [Fact]
    public async Task Create_item_with_scale_equivalent_vat_rate_is_accepted()
    {
        using var client = _host.CreateClient();
        await client.PostAsync("/odata/VatRates", ODataJson.Body(new { Rate = 19.0m }));

        // only "19" is configured; "19.0" must be normalized and accepted
        var response = await client.PostAsync(
            "/odata/InvoiceItems",
            ODataJson.Body(
                new
                {
                    Number = "4001",
                    Description = "Grabpflege",
                    VatRate = 19.0m,
                }
            )
        );
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        Assert.Equal(19m, json.RootElement.GetProperty("VatRate").GetDecimal());
    }

    private static async Task<long> GetCreatedIdAsync(HttpResponseMessage created)
    {
        using var json = await ODataJson.ReadAsync(created);
        return json.RootElement.GetProperty("Id").GetInt64();
    }

    [Fact]
    public async Task An_item_is_priced_net_unless_it_says_otherwise()
    {
        using var client = _host.CreateClient();
        await SeedVatRateAsync(client);

        var response = await client.PostAsync(
            "/odata/InvoiceItems",
            ODataJson.Body(
                new
                {
                    Number = "1101",
                    Description = "Netto",
                    VatRate = 7.0m,
                }
            )
        );
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        Assert.False(json.RootElement.GetProperty("PriceIncludesVat").GetBoolean());
    }

    [Fact]
    public async Task An_item_can_be_marked_and_unmarked_as_priced_vat_included()
    {
        using var client = _host.CreateClient();
        await SeedVatRateAsync(client);

        var created = await client.PostAsync(
            "/odata/InvoiceItems",
            ODataJson.Body(
                new
                {
                    Number = "1102",
                    Description = "Brutto",
                    VatRate = 7.0m,
                    DefaultPrice = 107.00m,
                    PriceIncludesVat = true,
                }
            )
        );
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var createdJson = await ODataJson.ReadAsync(created);
        var id = createdJson.RootElement.GetProperty("Id").GetInt64();
        Assert.True(createdJson.RootElement.GetProperty("PriceIncludesVat").GetBoolean());

        var patched = await client.PatchAsync(
            $"/odata/InvoiceItems({id})",
            ODataJson.Body(new { PriceIncludesVat = false })
        );
        // Updated() answers 204 or 200 depending on the Prefer header, so the
        // status is only checked for success; the re-read below is the assertion
        Assert.True(patched.IsSuccessStatusCode, $"PATCH returned {patched.StatusCode}");

        var reread = await client.GetAsync($"/odata/InvoiceItems({id})");
        using var rereadJson = await ODataJson.ReadAsync(reread);
        Assert.False(rereadJson.RootElement.GetProperty("PriceIncludesVat").GetBoolean());
    }
}
