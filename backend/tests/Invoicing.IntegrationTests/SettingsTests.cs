using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Xunit;

namespace Burkard.Invoicing.IntegrationTests;

public class SettingsTests : IClassFixture<ApiTestHost>
{
    private readonly ApiTestHost _host;

    public SettingsTests(ApiTestHost host) => _host = host;

    [Fact]
    public async Task Get_settings_returns_seeded_defaults()
    {
        using var client = _host.CreateClient();
        var response = await client.GetAsync("/odata/Settings");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        Assert.Equal("Kopie", json.RootElement.GetProperty("StampWord").GetString());
    }

    [Fact]
    public async Task Patch_empty_stamp_word_is_rejected()
    {
        using var client = _host.CreateClient();
        var response = await client.PatchAsync("/odata/Settings", ODataJson.Body(new { StampWord = "" }));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        Assert.Equal("StampWordRequired", json.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Patch_ending_text_preserves_line_breaks()
    {
        using var client = _host.CreateClient();
        const string text = "Line one\nLine two";
        var response = await client.PatchAsync("/odata/Settings", ODataJson.Body(new { EndingText = text }));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var get = await client.GetAsync("/odata/Settings");
        using var json = await ODataJson.ReadAsync(get);
        Assert.Equal(text, json.RootElement.GetProperty("EndingText").GetString());
    }
}
