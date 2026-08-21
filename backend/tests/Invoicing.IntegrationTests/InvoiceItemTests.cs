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

        var response = await client.PostAsync("/odata/InvoiceItems", ODataJson.Body(new
        {
            Number = "1001",
            Description = "Grabpflege",
            VatRate = 7.0m,
            DefaultPrice = (decimal?)null,
        }));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("DefaultPrice").ValueKind);
    }

    [Fact]
    public async Task Create_item_with_unknown_vat_rate_is_rejected()
    {
        using var client = _host.CreateClient();
        var response = await client.PostAsync("/odata/InvoiceItems", ODataJson.Body(new
        {
            Number = "1002",
            Description = "Unknown rate",
            VatRate = 99m,
        }));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        Assert.Equal("VatRateNotConfigured", json.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Create_item_with_duplicate_number_is_rejected()
    {
        using var client = _host.CreateClient();
        await SeedVatRateAsync(client);
        var first = await client.PostAsync("/odata/InvoiceItems", ODataJson.Body(new
        {
            Number = "2001",
            Description = "First",
            VatRate = 7.0m,
        }));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var duplicate = await client.PostAsync("/odata/InvoiceItems", ODataJson.Body(new
        {
            Number = "2001",
            Description = "Second",
            VatRate = 7.0m,
        }));
        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
        using var json = await ODataJson.ReadAsync(duplicate);
        Assert.Equal("ItemNumberExists", json.RootElement.GetProperty("error").GetProperty("code").GetString());
    }
}
