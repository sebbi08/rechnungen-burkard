using System.Net;
using System.Threading.Tasks;
using Xunit;

namespace Burkard.Invoicing.IntegrationTests;

public class RecipientNumberTests
{
    [Fact]
    public async Task Recipients_created_without_a_number_get_the_next_free_one()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();

        var first = await client.PostAsync("/odata/Recipients",
            ODataJson.Body(new { Name = "Anna", Address = "Hauptstraße 1" }));
        var second = await client.PostAsync("/odata/Recipients",
            ODataJson.Body(new { Name = "Bert", Address = "Nebenweg 2" }));

        using var firstJson = await ODataJson.ReadAsync(first);
        using var secondJson = await ODataJson.ReadAsync(second);
        Assert.Equal("E-0001", firstJson.RootElement.GetProperty("Number").GetString());
        Assert.Equal("E-0002", secondJson.RootElement.GetProperty("Number").GetString());
    }

    [Fact]
    public async Task An_explicit_number_is_kept_and_must_stay_unique()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();

        var created = await client.PostAsync("/odata/Recipients",
            ODataJson.Body(new { Number = "K-77", Name = "Anna", Address = "Hauptstraße 1" }));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var createdJson = await ODataJson.ReadAsync(created);
        Assert.Equal("K-77", createdJson.RootElement.GetProperty("Number").GetString());

        var duplicate = await client.PostAsync("/odata/Recipients",
            ODataJson.Body(new { Number = "K-77", Name = "Bert", Address = "Nebenweg 2" }));
        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
        using var duplicateJson = await ODataJson.ReadAsync(duplicate);
        Assert.Equal("RecipientNumberExists",
            duplicateJson.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Generated_numbers_continue_after_an_explicit_one()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();

        await client.PostAsync("/odata/Recipients",
            ODataJson.Body(new { Number = "E-0042", Name = "Anna", Address = "Hauptstraße 1" }));
        var next = await client.PostAsync("/odata/Recipients",
            ODataJson.Body(new { Name = "Bert", Address = "Nebenweg 2" }));

        using var json = await ODataJson.ReadAsync(next);
        Assert.Equal("E-0043", json.RootElement.GetProperty("Number").GetString());
    }
}
