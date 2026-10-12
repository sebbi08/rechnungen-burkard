using System.Linq;
using System.Net;
using System.Net.Http;
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

    private async Task<long> CreateAsync(HttpClient client, object body)
    {
        var response = await client.PostAsync("/odata/Recipients", ODataJson.Body(body));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        return json.RootElement.GetProperty("Id").GetInt64();
    }

    private async Task<string?> EmailOfAsync(HttpClient client, long id)
    {
        using var json = await ODataJson.ReadAsync(
            await client.GetAsync($"/odata/Recipients({id})")
        );
        return json.RootElement.GetProperty("Email").GetString();
    }

    private static Task<HttpResponseMessage> PatchAsync(HttpClient client, long id, object body) =>
        client.SendAsync(
            new HttpRequestMessage(HttpMethod.Patch, $"/odata/Recipients({id})")
            {
                Content = ODataJson.Body(body),
            }
        );

    [Fact]
    public async Task An_email_address_is_stored_trimmed_and_an_empty_one_as_none()
    {
        using var client = _host.CreateClient();
        var withEmail = await CreateAsync(
            client,
            new
            {
                Name = "Mit E-Mail",
                Address = "Hauptstraße 1",
                Email = "  anna@example.de ",
            }
        );
        var blank = await CreateAsync(
            client,
            new
            {
                Name = "Leere E-Mail",
                Address = "Hauptstraße 1",
                Email = " ",
            }
        );
        var without = await CreateAsync(
            client,
            new { Name = "Ohne E-Mail", Address = "Hauptstraße 1" }
        );

        Assert.Equal("anna@example.de", await EmailOfAsync(client, withEmail));
        Assert.Null(await EmailOfAsync(client, blank));
        Assert.Null(await EmailOfAsync(client, without));

        // and a patch can change or clear it
        Assert.True(
            (
                await PatchAsync(client, withEmail, new { Email = "bert@example.de" })
            ).IsSuccessStatusCode
        );
        Assert.Equal("bert@example.de", await EmailOfAsync(client, withEmail));
        Assert.True((await PatchAsync(client, withEmail, new { Email = "" })).IsSuccessStatusCode);
        Assert.Null(await EmailOfAsync(client, withEmail));
    }

    [Theory]
    [InlineData("anna")]
    [InlineData("anna@")]
    [InlineData("@example.de")]
    [InlineData("anna@example")]
    [InlineData("anna@example.")]
    [InlineData("an na@example.de")]
    [InlineData("anna@b@example.de")]
    public async Task A_malformed_email_address_is_rejected(string email)
    {
        using var client = _host.CreateClient();
        var created = await client.PostAsync(
            "/odata/Recipients",
            ODataJson.Body(
                new
                {
                    Name = "Falsche E-Mail",
                    Address = "Hauptstraße 1",
                    Email = email,
                }
            )
        );
        Assert.Equal(HttpStatusCode.BadRequest, created.StatusCode);
        using (var json = await ODataJson.ReadAsync(created))
        {
            Assert.Equal(
                "RecipientEmailInvalid",
                json.RootElement.GetProperty("error").GetProperty("code").GetString()
            );
        }

        var id = await CreateAsync(client, new { Name = "Richtige E-Mail", Address = "Weg 1" });
        var patched = await PatchAsync(client, id, new { Email = email });
        Assert.Equal(HttpStatusCode.BadRequest, patched.StatusCode);
        using var patchJson = await ODataJson.ReadAsync(patched);
        Assert.Equal(
            "RecipientEmailInvalid",
            patchJson.RootElement.GetProperty("error").GetProperty("code").GetString()
        );
        Assert.Null(await EmailOfAsync(client, id));
    }
}
