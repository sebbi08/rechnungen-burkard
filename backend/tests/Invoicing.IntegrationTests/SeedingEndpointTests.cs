using System.Net;
using System.Threading.Tasks;
using Xunit;

namespace Burkard.Invoicing.IntegrationTests;

/// <summary>The grave link collections can be written to directly, which builds
/// a grave without the check that its prices divide evenly across its
/// recipients. Such a grave saves and then never bills — generation skips it.
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
        using var host = new ApiTestHost();
        using var client = host.CreateClient();

        var deleted = await client.DeleteAsync($"{path}(1)");
        Assert.Equal(HttpStatusCode.NotFound, deleted.StatusCode);
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
}
