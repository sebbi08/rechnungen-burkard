using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Burkard.Invoicing.Api.OData;
using Microsoft.AspNetCore.Hosting;
using Xunit;

namespace Burkard.Invoicing.IntegrationTests;

/// <summary>The grave link collections can be written to directly, and POST
/// /odata/Graves takes nested links along with the grave. Either builds a grave
/// without the check that its prices divide evenly across its recipients. Such a
/// grave saves and then never bills — generation skips it.
///
/// Nothing in the application writes through them; the E2E suite does, for
/// fixtures. So they are offered only where an instance says it exists to be
/// seeded, and a deployment answers as if they were never mapped.</summary>
public class SeedingEndpointTests
{
    [Theory]
    [InlineData("/odata/GraveItems")]
    [InlineData("/odata/GraveRecipients")]
    public async Task Writing_a_link_is_not_offered_without_the_seeding_flag(string path)
    {
        using var host = ApiTestHost.WithoutSeedingEndpoints();
        using var client = host.CreateClient();

        var created = await client.PostAsync(path, ODataJson.Body(new { GraveId = 1, ItemId = 1 }));
        Assert.Equal(HttpStatusCode.NotFound, created.StatusCode);
    }

    [Theory]
    [InlineData("/odata/GraveItems")]
    [InlineData("/odata/GraveRecipients")]
    public async Task Removing_a_link_is_not_offered_at_all(string path)
    {
        // Not gated -- gone. Nothing ever called it: the dialog sends a grave's
        // full set of links and GraveService replaces them wholesale, so
        // deletion happens inside the service. The GraveItems one had no check
        // of its own either, and would have unpicked an invoiced grave.
        //
        // On a link that exists: a missing row was 404 from the old action too,
        // so only a surviving row shows the route itself is gone.
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var linkId = await SeedLinkAsync(client, path);

        var deleted = await client.DeleteAsync($"{path}({linkId})");

        Assert.Equal(HttpStatusCode.NotFound, deleted.StatusCode);
        var remaining = await client.GetAsync($"{path}?$filter=Id eq {linkId}");
        using var json = await ODataJson.ReadAsync(remaining);
        Assert.Single(json.RootElement.GetProperty("value").EnumerateArray());
    }

    /// <summary>A grave with one link of the kind the path names, built through
    /// the seeding routes; answers the link's id.</summary>
    private static async Task<long> SeedLinkAsync(HttpClient client, string path)
    {
        await client.PostAsync("/odata/VatRates", ODataJson.Body(new { Rate = 7.0m }));
        var graveId = await SeedGraveAsync(client);
        object link;
        if (path == "/odata/GraveItems")
        {
            var item = await client.PostAsync(
                "/odata/InvoiceItems",
                ODataJson.Body(
                    new
                    {
                        Number = "9001",
                        Description = "Grabpflege",
                        VatRate = 7.0m,
                        DefaultPrice = 100.00m,
                    }
                )
            );
            link = new
            {
                GraveId = graveId,
                ItemId = await IdAsync(item),
                Count = 1,
            };
        }
        else
        {
            var recipient = await client.PostAsync(
                "/odata/Recipients",
                ODataJson.Body(new { Name = "Anna Beispiel", Address = "Hauptstraße 1" })
            );
            link = new { GraveId = graveId, RecipientId = await IdAsync(recipient) };
        }
        var created = await client.PostAsync(path, ODataJson.Body(link));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        return await IdAsync(created);
    }

    private static async Task<long> IdAsync(HttpResponseMessage response)
    {
        using var json = await ODataJson.ReadAsync(response);
        return json.RootElement.GetProperty("Id").GetInt64();
    }

    [Theory]
    [InlineData("/odata/GraveItems")]
    [InlineData("/odata/GraveRecipients")]
    public async Task Reading_the_links_stays_open(string path)
    {
        // the export and the suite's own self-healing read them, and reading
        // cannot put a grave together that will not bill
        using var host = ApiTestHost.WithoutSeedingEndpoints();
        using var client = host.CreateClient();

        var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("/odata/GraveItems")]
    [InlineData("/odata/GraveRecipients")]
    public async Task A_seeding_instance_still_answers(string path)
    {
        // not a 404: the request gets as far as the controller's own validation,
        // which is what a seeding instance is for
        using var host = new ApiTestHost();
        using var client = host.CreateClient();

        var response = await client.PostAsync(
            path,
            ODataJson.Body(new { GraveId = 1, ItemId = 1 })
        );
        Assert.NotEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_hidden_route_answers_404_whatever_the_request_looks_like()
    {
        // gone from routing, not merely refused after the body was read: a
        // charset the formatters do not speak would otherwise be told 415
        using var host = ApiTestHost.WithoutSeedingEndpoints();
        using var client = host.CreateClient();

        var body = new StringContent(
            "{\"GraveId\":1,\"ItemId\":1}",
            Encoding.Latin1,
            "application/json"
        );
        var created = await client.PostAsync("/odata/GraveItems", body);

        Assert.Equal(HttpStatusCode.NotFound, created.StatusCode);
    }

    /// <summary>A host whose flag is set to something that is not a boolean.</summary>
    private sealed class MistypedFlagHost : ApiTestHost
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting(SeedingOnlyAttribute.ConfigurationKey, "yes");
        }
    }

    [Fact]
    public void A_flag_that_is_not_a_boolean_stops_the_host_from_starting()
    {
        // read once at startup, so a mistyped value fails the boot rather than
        // every seeding write after it
        using var host = new MistypedFlagHost();

        Assert.ThrowsAny<Exception>(() => host.CreateClient());
    }

    [Fact]
    public async Task Writing_a_grave_is_not_offered_without_the_seeding_flag()
    {
        // POST /odata/Graves takes the whole graph: nested Items and Recipients
        // are written along with the grave, which puts one together without
        // the split check exactly as the link collections do.
        using var host = ApiTestHost.WithoutSeedingEndpoints();
        using var client = host.CreateClient();

        var created = await client.PostAsync("/odata/Graves", GraveBody("G-1"));
        Assert.Equal(HttpStatusCode.NotFound, created.StatusCode);
    }

    [Fact]
    public async Task Changing_a_grave_directly_is_not_offered_at_all()
    {
        // Nothing calls PATCH on /odata/Graves: the dialog goes through
        // SaveGrave. A nested Items or Recipients set would be written without
        // any check, even on an invoiced grave. Gone, like the link deletes.
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var id = await SeedGraveAsync(client);

        var patched = await client.PatchAsync(
            $"/odata/Graves({id})",
            ODataJson.Body(new { GraveNumber = "G-2" })
        );

        Assert.Equal(HttpStatusCode.NotFound, patched.StatusCode);
        Assert.Equal("G-1", await GraveNumberAsync(client, id));
    }

    [Fact]
    public async Task Removing_a_grave_is_not_offered_at_all()
    {
        // Nothing calls DELETE on /odata/Graves either, and it would leave the
        // grave's invoices and runs pointing at nothing.
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var id = await SeedGraveAsync(client);

        var deleted = await client.DeleteAsync($"/odata/Graves({id})");

        Assert.Equal(HttpStatusCode.NotFound, deleted.StatusCode);
        Assert.Equal("G-1", await GraveNumberAsync(client, id));
    }

    private static StringContent GraveBody(string number) =>
        ODataJson.Body(
            new
            {
                GraveNumber = number,
                StartDate = "2026-01-01",
                EndDate = "2040-12-31",
            }
        );

    private static async Task<long> SeedGraveAsync(HttpClient client)
    {
        var created = await client.PostAsync("/odata/Graves", GraveBody("G-1"));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var json = await ODataJson.ReadAsync(created);
        return json.RootElement.GetProperty("Id").GetInt64();
    }

    private static async Task<string?> GraveNumberAsync(HttpClient client, long id)
    {
        var response = await client.GetAsync($"/odata/Graves({id})");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        return json.RootElement.GetProperty("GraveNumber").GetString();
    }
}
