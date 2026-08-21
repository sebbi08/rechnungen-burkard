using System.Net;
using System.Threading.Tasks;
using Xunit;

namespace Burkard.Invoicing.IntegrationTests;

public class HealthTests : IClassFixture<ApiTestHost>
{
    private readonly ApiTestHost _host;

    public HealthTests(ApiTestHost host) => _host = host;

    [Fact]
    public async Task Healthz_returns_ok()
    {
        using var client = _host.CreateClient();
        var response = await client.GetAsync("/healthz");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"ok\"", body);
    }
}
