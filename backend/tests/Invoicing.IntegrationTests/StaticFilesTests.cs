using System;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Xunit;

namespace Burkard.Invoicing.IntegrationTests;

/// <summary>The release image serves the frontend's build from wwwroot. The
/// test host has none, so this one is given a small one of its own.</summary>
public class StaticFilesTests : IDisposable
{
    private sealed class HostWithWebRoot : ApiTestHost
    {
        public string WebRoot { get; } =
            Path.Combine(Path.GetTempPath(), $"invoicing-wwwroot-{Guid.NewGuid():N}");

        public HostWithWebRoot()
        {
            Directory.CreateDirectory(Path.Combine(WebRoot, "assets"));
            File.WriteAllText(
                Path.Combine(WebRoot, "index.html"),
                "<!doctype html><script src=\"/assets/index-abc123.js\"></script>"
            );
            File.WriteAllText(Path.Combine(WebRoot, "assets", "index-abc123.js"), "1;");
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseWebRoot(WebRoot);
        }
    }

    private readonly HostWithWebRoot _host = new();

    public void Dispose()
    {
        _host.Dispose();
        Directory.Delete(_host.WebRoot, recursive: true);
    }

    /// <summary>After a release the old index.html names hashed files the new
    /// image no longer has. A browser that kept it -- and without Cache-Control
    /// it may, for a while, by heuristic -- showed a blank page until a hard
    /// reload. So the page is always revalidated.</summary>
    [Theory]
    [InlineData("/")]
    [InlineData("/index.html")]
    [InlineData("/graves/12")]
    public async Task The_page_is_revalidated_on_every_load(string path)
    {
        using var client = _host.CreateClient();

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoCache, "index.html must be revalidated");
    }

    /// <summary>A hashed asset never changes under its name, so it may be kept
    /// for good.</summary>
    [Fact]
    public async Task A_hashed_asset_is_cached_for_good()
    {
        using var client = _host.CreateClient();

        var response = await client.GetAsync("/assets/index-abc123.js");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(TimeSpan.FromDays(365), response.Headers.CacheControl?.MaxAge);
        Assert.Contains(response.Headers.CacheControl!.Extensions, e => e.Name == "immutable");
    }

    /// <summary>A wrong API path still answers 404 rather than the page.</summary>
    [Fact]
    public async Task An_unknown_api_path_is_not_answered_with_the_page()
    {
        using var client = _host.CreateClient();

        var response = await client.GetAsync("/odata/NoSuchSet");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
