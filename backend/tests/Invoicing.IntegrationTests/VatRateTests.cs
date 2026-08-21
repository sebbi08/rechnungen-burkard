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
        var created = await client.PostAsync("/odata/VatRates", ODataJson.Body(new { Rate = 7.0m }));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var duplicate = await client.PostAsync("/odata/VatRates", ODataJson.Body(new { Rate = 7.0m }));
        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
        using var json = await ODataJson.ReadAsync(duplicate);
        Assert.Equal("VatRateExists", json.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Post_negative_vat_rate_is_rejected()
    {
        using var client = _host.CreateClient();
        var response = await client.PostAsync("/odata/VatRates", ODataJson.Body(new { Rate = -1m }));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
