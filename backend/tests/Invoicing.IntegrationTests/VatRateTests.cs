using System.Net;
using System.Threading.Tasks;
using Xunit;

namespace Burkard.Invoicing.IntegrationTests;

public class VatRateTests : IClassFixture<ApiTestHost>
{
    private readonly ApiTestHost _host;

    public VatRateTests(ApiTestHost host) => _host = host;

    [Fact]
    public async Task Post_vat_rate_creates_and_duplicate_is_rejected()
    {
        using var client = _host.CreateClient();
        var created = await client.PostAsync(
            "/odata/VatRates",
            ODataJson.Body(new { Rate = 7.0m })
        );
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var duplicate = await client.PostAsync(
            "/odata/VatRates",
            ODataJson.Body(new { Rate = 7.0m })
        );
        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
        using var json = await ODataJson.ReadAsync(duplicate);
        Assert.Equal(
            "VatRateExists",
            json.RootElement.GetProperty("error").GetProperty("code").GetString()
        );
    }

    [Fact]
    public async Task Post_negative_vat_rate_is_rejected()
    {
        using var client = _host.CreateClient();
        var response = await client.PostAsync(
            "/odata/VatRates",
            ODataJson.Body(new { Rate = -1m })
        );
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Post_scale_equivalent_rate_is_rejected_after_normalization()
    {
        using var client = _host.CreateClient();
        var first = await client.PostAsync("/odata/VatRates", ODataJson.Body(new { Rate = 19.0m }));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        // 19 == 19.0 after normalization: the duplicate must be detected
        var duplicate = await client.PostAsync(
            "/odata/VatRates",
            ODataJson.Body(new { Rate = 19m })
        );
        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
        using var json = await ODataJson.ReadAsync(duplicate);
        Assert.Equal(
            "VatRateExists",
            json.RootElement.GetProperty("error").GetProperty("code").GetString()
        );
    }

    [Theory]
    [InlineData(100)]
    [InlineData(250)]
    public async Task Post_vat_rate_of_100_or_more_is_rejected(decimal rate)
    {
        using var client = _host.CreateClient();
        var response = await client.PostAsync(
            "/odata/VatRates",
            ODataJson.Body(new { Rate = rate })
        );
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        Assert.Equal(
            "InvalidVatRate",
            json.RootElement.GetProperty("error").GetProperty("code").GetString()
        );
    }

    [Fact]
    public async Task Delete_is_refused_while_an_item_uses_the_rate_and_allowed_after()
    {
        // with the rate gone the item could no longer be saved
        using var client = _host.CreateClient();
        var created = await client.PostAsync(
            "/odata/VatRates",
            ODataJson.Body(new { Rate = 11.5m })
        );
        using var rateJson = await ODataJson.ReadAsync(created);
        var rateId = rateJson.RootElement.GetProperty("Id").GetInt64();
        var itemResponse = await client.PostAsync(
            "/odata/InvoiceItems",
            ODataJson.Body(
                new
                {
                    Number = "VR-115",
                    Description = "Uses 11.5",
                    VatRate = 11.5m,
                    DefaultPrice = 10m,
                }
            )
        );
        Assert.Equal(HttpStatusCode.Created, itemResponse.StatusCode);

        var refused = await client.DeleteAsync($"/odata/VatRates({rateId})");
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        using var json = await ODataJson.ReadAsync(refused);
        Assert.Equal(
            "VatRateInUse",
            json.RootElement.GetProperty("error").GetProperty("code").GetString()
        );

        var unused = await client.PostAsync(
            "/odata/VatRates",
            ODataJson.Body(new { Rate = 12.5m })
        );
        using var unusedJson = await ODataJson.ReadAsync(unused);
        var deleted = await client.DeleteAsync(
            $"/odata/VatRates({unusedJson.RootElement.GetProperty("Id").GetInt64()})"
        );
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
    }
}
