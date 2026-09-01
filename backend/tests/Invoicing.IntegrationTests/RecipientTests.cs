using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace Burkard.Invoicing.IntegrationTests;

public class RecipientTests : IClassFixture<ApiTestHost>
{
    private readonly ApiTestHost _host;

    public RecipientTests(ApiTestHost host) => _host = host;

    [Fact]
    public async Task Create_recipient_and_query_with_filter()
    {
        using var client = _host.CreateClient();
        var created = await client.PostAsync(
            "/odata/Recipients",
            ODataJson.Body(
                new { Name = "Familie Mahr", Address = "Musterstraße 1\n12345 Musterstadt" }
            )
        );
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var list = await client.GetAsync("/odata/Recipients?$filter=contains(Name,'Mahr')");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        using var json = await ODataJson.ReadAsync(list);
        var names = json
            .RootElement.GetProperty("value")
            .EnumerateArray()
            .Select(e => e.GetProperty("Name").GetString())
            .ToArray();
        Assert.Contains("Familie Mahr", names);
    }

    [Fact]
    public async Task Create_recipient_without_address_is_rejected()
    {
        using var client = _host.CreateClient();
        var response = await client.PostAsync(
            "/odata/Recipients",
            ODataJson.Body(new { Name = "No Address" })
        );
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        Assert.Equal(
            "RecipientAddressRequired",
            json.RootElement.GetProperty("error").GetProperty("code").GetString()
        );
    }

    /// <summary>Several clients creating a recipient at once each read the next
    /// free number before any of them has saved, and the unique index on
    /// Recipients.Number rejects all but the first. Every request must still end
    /// up with a recipient of its own: before the retry the losers of the race
    /// came back as unhandled 500s.</summary>
    [Fact]
    public async Task Concurrent_creates_each_get_their_own_number()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();

        var responses = await Task.WhenAll(
            Enumerable
                .Range(0, 8)
                .Select(i =>
                    client.PostAsync(
                        "/odata/Recipients",
                        ODataJson.Body(
                            new { Name = $"Gleichzeitig {i}", Address = "Hauptstraße 1" }
                        )
                    )
                )
        );

        Assert.All(
            responses,
            response => Assert.Equal(HttpStatusCode.Created, response.StatusCode)
        );

        var list = await client.GetAsync(
            "/odata/Recipients?$filter=startswith(Name,'Gleichzeitig')&$select=Number"
        );
        list.EnsureSuccessStatusCode();
        using var json = await ODataJson.ReadAsync(list);
        var numbers = json
            .RootElement.GetProperty("value")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("Number").GetString())
            .ToList();
        Assert.Equal(8, numbers.Count);
        Assert.Equal(8, numbers.Distinct().Count());
    }
}
