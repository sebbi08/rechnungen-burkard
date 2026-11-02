using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;

namespace Burkard.Invoicing.IntegrationTests;

/// <summary>The cemeteries are kept in the settings; a grave refers to one,
/// which the overview shows and the grave search looks in.</summary>
public class CemeteryTests
{
    private static async Task<long> CreateAsync(HttpClient client, string name)
    {
        var response = await client.PostAsync(
            "/odata/Cemeteries",
            ODataJson.Body(new { Name = name })
        );
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        return json.RootElement.GetProperty("Id").GetInt64();
    }

    private static async Task<string?> ErrorCodeAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        return json.RootElement.GetProperty("error").GetProperty("code").GetString();
    }

    /// <summary>A grave in <paramref name="cemeteryId"/>, saved the way the
    /// editor saves it. Answers the response.</summary>
    private static async Task<HttpResponseMessage> SaveGraveAsync(
        HttpClient client,
        string graveNumber,
        long? cemeteryId,
        string recipientName = "Anna Beispiel"
    )
    {
        await client.PostAsync("/odata/VatRates", ODataJson.Body(new { Rate = 7.0m }));
        var item = await client.PostAsync(
            "/odata/InvoiceItems",
            ODataJson.Body(
                new
                {
                    Number = $"I-{graveNumber}",
                    Description = "Grabpflege",
                    VatRate = 7.0m,
                    DefaultPrice = 120.00m,
                }
            )
        );
        using var itemJson = await ODataJson.ReadAsync(item);
        var recipient = await client.PostAsync(
            "/odata/Recipients",
            ODataJson.Body(new { Name = recipientName, Address = "Hauptstraße 1" })
        );
        using var recipientJson = await ODataJson.ReadAsync(recipient);
        return await client.PostAsync(
            "/odata/SaveGrave",
            ODataJson.Body(
                new
                {
                    graveId = (long?)null,
                    graveNumber,
                    fieldNumber = "F-1",
                    siteName = "Schineller Bäcke",
                    cemeteryId,
                    position = "SingleInside",
                    startDate = "2026-01",
                    endDate = (string?)null,
                    items = new[]
                    {
                        new
                        {
                            itemId = itemJson.RootElement.GetProperty("Id").GetInt64(),
                            count = 1,
                            unitPrice = (decimal?)null,
                        },
                    },
                    recipientIds = new[] { recipientJson.RootElement.GetProperty("Id").GetInt64() },
                }
            )
        );
    }

    private static async Task<long> SaveGraveIdAsync(
        HttpClient client,
        string graveNumber,
        long cemeteryId,
        string recipientName = "Anna Beispiel"
    )
    {
        var response = await SaveGraveAsync(client, graveNumber, cemeteryId, recipientName);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        return json.RootElement.GetProperty("Id").GetInt64();
    }

    [Fact]
    public async Task A_cemetery_is_created_with_its_name_trimmed_and_listed()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();

        var id = await CreateAsync(client, "  Waldfriedhof ");

        using var json = await ODataJson.ReadAsync(
            await client.GetAsync("/odata/Cemeteries?$orderby=Name")
        );
        var names = json
            .RootElement.GetProperty("value")
            .EnumerateArray()
            .Select(c => (c.GetProperty("Id").GetInt64(), c.GetProperty("Name").GetString()))
            .ToList();
        Assert.Equal(new[] { (TestCemetery.Id, TestCemetery.Name), (id, "Waldfriedhof") }, names);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_cemetery_needs_a_name(string name)
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();

        var response = await client.PostAsync(
            "/odata/Cemeteries",
            ODataJson.Body(new { Name = name })
        );

        Assert.Equal("CemeteryNameRequired", await ErrorCodeAsync(response));
    }

    [Theory]
    [InlineData("waldfriedhof")]
    [InlineData(" WALDFRIEDHOF ")]
    public async Task A_name_is_taken_whatever_its_case(string duplicate)
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        await CreateAsync(client, "Waldfriedhof");

        var response = await client.PostAsync(
            "/odata/Cemeteries",
            ODataJson.Body(new { Name = duplicate })
        );

        Assert.Equal("CemeteryNameExists", await ErrorCodeAsync(response));
    }

    [Fact]
    public async Task A_name_is_taken_whatever_the_case_of_its_umlauts()
    {
        // SQLite's NOCASE folds ASCII only, so this is the comparison it misses
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        await CreateAsync(client, "Südfriedhof");

        var response = await client.PostAsync(
            "/odata/Cemeteries",
            ODataJson.Body(new { Name = "SÜDFRIEDHOF" })
        );

        Assert.Equal("CemeteryNameExists", await ErrorCodeAsync(response));
    }

    [Fact]
    public async Task A_rename_applies_to_every_grave_in_the_cemetery()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var id = await CreateAsync(client, "Waldfriedhof");
        var first = await SaveGraveIdAsync(client, "W-1", id, "Anna Beispiel");
        var second = await SaveGraveIdAsync(client, "W-2", id, "Bert Beispiel");

        var renamed = await client.PatchAsync(
            $"/odata/Cemeteries({id})",
            ODataJson.Body(new { Name = " Neuer Waldfriedhof " })
        );
        Assert.True(renamed.IsSuccessStatusCode, renamed.StatusCode.ToString());

        foreach (var graveId in new[] { first, second })
        {
            using var details = await ODataJson.ReadAsync(
                await client.GetAsync($"/odata/Graves({graveId})")
            );
            Assert.Equal(id, details.RootElement.GetProperty("CemeteryId").GetInt64());
            Assert.Equal(
                "Neuer Waldfriedhof",
                details.RootElement.GetProperty("Cemetery").GetProperty("Name").GetString()
            );
        }
    }

    [Fact]
    public async Task A_rename_may_change_the_case_of_its_own_name_but_not_take_another()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var id = await CreateAsync(client, "Waldfriedhof");

        var ownCase = await client.PatchAsync(
            $"/odata/Cemeteries({id})",
            ODataJson.Body(new { Name = "WALDFRIEDHOF" })
        );
        Assert.True(ownCase.IsSuccessStatusCode, ownCase.StatusCode.ToString());

        var taken = await client.PatchAsync(
            $"/odata/Cemeteries({id})",
            ODataJson.Body(new { Name = TestCemetery.Name.ToUpperInvariant() })
        );
        Assert.Equal("CemeteryNameExists", await ErrorCodeAsync(taken));

        var blank = await client.PatchAsync(
            $"/odata/Cemeteries({id})",
            ODataJson.Body(new { Name = " " })
        );
        Assert.Equal("CemeteryNameRequired", await ErrorCodeAsync(blank));

        using var json = await ODataJson.ReadAsync(
            await client.GetAsync($"/odata/Cemeteries({id})")
        );
        Assert.Equal("WALDFRIEDHOF", json.RootElement.GetProperty("Name").GetString());
    }

    [Fact]
    public async Task Delete_is_refused_while_a_grave_lies_in_the_cemetery()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var used = await CreateAsync(client, "Waldfriedhof");
        await SaveGraveIdAsync(client, "W-1", used);
        var unused = await CreateAsync(client, "Bergfriedhof Nord");

        var refused = await client.DeleteAsync($"/odata/Cemeteries({used})");
        Assert.Equal("CemeteryInUse", await ErrorCodeAsync(refused));

        var deleted = await client.DeleteAsync($"/odata/Cemeteries({unused})");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await client.GetAsync($"/odata/Cemeteries({unused})")).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.OK,
            (await client.GetAsync($"/odata/Cemeteries({used})")).StatusCode
        );
    }

    [Fact]
    public async Task Save_grave_requires_a_cemetery_that_exists()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();

        var missing = await SaveGraveAsync(client, "C-1", null);
        Assert.Equal("CemeteryRequired", await ErrorCodeAsync(missing));

        var unknown = await SaveGraveAsync(client, "C-2", 999);
        Assert.Equal("CemeteryNotFound", await ErrorCodeAsync(unknown));
    }

    [Fact]
    public async Task Save_grave_moves_a_grave_to_another_cemetery()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var graveId = await SaveGraveIdAsync(client, "C-1", TestCemetery.Id);
        var other = await CreateAsync(client, "Waldfriedhof");

        using var stored = await ODataJson.ReadAsync(
            await client.GetAsync($"/odata/Graves({graveId})")
        );
        var root = stored.RootElement;
        var resaved = await client.PostAsync(
            "/odata/SaveGrave",
            ODataJson.Body(
                new
                {
                    graveId,
                    graveNumber = "C-1",
                    fieldNumber = "F-1",
                    siteName = "Schineller Bäcke",
                    cemeteryId = other,
                    position = "SingleInside",
                    startDate = "2026-01",
                    endDate = (string?)null,
                    items = root.GetProperty("Items")
                        .EnumerateArray()
                        .Select(i => new
                        {
                            itemId = i.GetProperty("Item").GetProperty("Id").GetInt64(),
                            count = i.GetProperty("Count").GetInt32(),
                            unitPrice = (decimal?)null,
                        })
                        .ToArray(),
                    recipientIds = root.GetProperty("Recipients")
                        .EnumerateArray()
                        .Select(r => r.GetProperty("Recipient").GetProperty("Id").GetInt64())
                        .ToArray(),
                }
            )
        );
        Assert.Equal(HttpStatusCode.OK, resaved.StatusCode);

        using var details = await ODataJson.ReadAsync(
            await client.GetAsync($"/odata/Graves({graveId})")
        );
        Assert.Equal(other, details.RootElement.GetProperty("CemeteryId").GetInt64());
    }

    [Fact]
    public async Task The_overview_rows_carry_the_cemetery()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var wald = await CreateAsync(client, "Waldfriedhof");
        var graveId = await SaveGraveIdAsync(client, "O-1", wald);

        using var json = await ODataJson.ReadAsync(
            await client.GetAsync("/odata/GraveInvoicingStatus?year=2026&half=2")
        );

        var row = json
            .RootElement.GetProperty("value")
            .EnumerateArray()
            .Single(r => r.GetProperty("Id").GetInt64() == graveId);
        Assert.Equal("Waldfriedhof", row.GetProperty("CemeteryName").GetString());
        // the overview's cemetery filter picks by id, as the settings list them
        Assert.Equal(wald, row.GetProperty("CemeteryId").GetInt64());
    }

    [Fact]
    public async Task The_cemetery_is_not_printed_on_or_copied_into_invoices()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var wald = await CreateAsync(client, "Waldfriedhof");
        var graveId = await SaveGraveIdAsync(client, "N-1", wald);

        await GraveGenerationHelper.RunAsync(client, 2026, 2);

        using var json = await ODataJson.ReadAsync(
            await client.GetAsync($"/odata/Invoices?$filter=GraveId eq {graveId}")
        );
        var invoice = Assert.Single(json.RootElement.GetProperty("value").EnumerateArray());
        Assert.DoesNotContain("Waldfriedhof", invoice.GetRawText());
    }
}
