using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Domain;
using Burkard.Invoicing.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Burkard.Invoicing.IntegrationTests;

/// <summary>The grave dialog collects the grave, its items and its recipients
/// client-side and saves them in one request.</summary>
public class SaveGraveTests
{
    private async Task<(
        long PricedItemId,
        long UnpricedItemId,
        long RecipientId,
        long OtherRecipientId
    )> SeedAsync(HttpClient client)
    {
        await client.PostAsync("/odata/VatRates", ODataJson.Body(new { Rate = 7.0m }));
        var priced = await ReadIdAsync(
            client.PostAsync(
                "/odata/InvoiceItems",
                ODataJson.Body(
                    new
                    {
                        Number = "4001",
                        Description = "Grabpflege",
                        VatRate = 7.0m,
                        DefaultPrice = 120.00m,
                    }
                )
            )
        );
        var unpriced = await ReadIdAsync(
            client.PostAsync(
                "/odata/InvoiceItems",
                ODataJson.Body(
                    new
                    {
                        Number = "4002",
                        Description = "Sonderleistung",
                        VatRate = 7.0m,
                        DefaultPrice = (decimal?)null,
                    }
                )
            )
        );
        var recipient = await ReadIdAsync(
            client.PostAsync(
                "/odata/Recipients",
                ODataJson.Body(
                    new { Name = "Anna Beispiel", Address = "Hauptstraße 1\n99999 Musterdorf" }
                )
            )
        );
        var other = await ReadIdAsync(
            client.PostAsync(
                "/odata/Recipients",
                ODataJson.Body(
                    new { Name = "Bert Beispiel", Address = "Nebenweg 2\n99999 Musterdorf" }
                )
            )
        );
        return (priced, unpriced, recipient, other);
    }

    private static async Task<long> ReadIdAsync(Task<HttpResponseMessage> call)
    {
        using var json = await ODataJson.ReadAsync(await call);
        return json.RootElement.GetProperty("Id").GetInt64();
    }

    [Fact]
    public async Task Save_creates_a_grave_with_its_items_and_recipients_in_one_call()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var (pricedItem, _, recipient, other) = await SeedAsync(client);

        var response = await client.PostAsync(
            "/odata/SaveGrave",
            ODataJson.Body(
                new
                {
                    graveId = (long?)null,
                    graveNumber = "S-001",
                    fieldNumber = "F-1",
                    siteName = "Grabstätte Test",
                    position = "SingleInside",
                    startDate = "2026-01-01",
                    endDate = "2040-12-31",
                    items = new[]
                    {
                        new
                        {
                            itemId = pricedItem,
                            count = 2,
                            unitPrice = (decimal?)null,
                        },
                    },
                    recipientIds = new[] { recipient, other },
                }
            )
        );
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        var graveId = json.RootElement.GetProperty("Id").GetInt64();

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var grave = await db
            .Graves.Include(g => g.Items)
            .Include(g => g.Recipients)
            .SingleAsync(g => g.Id == graveId);
        Assert.Equal("S-001", grave.GraveNumber);
        Assert.NotEqual(default, grave.CreatedAt);
        var item = Assert.Single(grave.Items);
        Assert.Equal(pricedItem, item.ItemId);
        Assert.Equal(2, item.Count);
        Assert.Null(item.UnitPrice);
        Assert.Equal(
            new[] { recipient, other }.OrderBy(id => id),
            grave.Recipients.Select(r => r.RecipientId).OrderBy(id => id)
        );
    }

    [Fact]
    public async Task Save_rejects_a_new_grave_without_recipients()
    {
        // recipients are fixed at creation, so a grave created without any could
        // never be billed: generation would skip it and none could be added later
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var (pricedItem, _, _, _) = await SeedAsync(client);

        var response = await client.PostAsync(
            "/odata/SaveGrave",
            ODataJson.Body(
                new
                {
                    graveId = (long?)null,
                    graveNumber = "S-002",
                    fieldNumber = "F-1",
                    siteName = "Grabstätte Test",
                    position = "SingleInside",
                    startDate = "2026-01-01",
                    endDate = "2040-12-31",
                    items = new[]
                    {
                        new
                        {
                            itemId = pricedItem,
                            count = 1,
                            unitPrice = (decimal?)null,
                        },
                    },
                    recipientIds = new List<long>(),
                }
            )
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        Assert.Equal(
            "NoRecipients",
            json.RootElement.GetProperty("error").GetProperty("code").GetString()
        );
    }

    [Fact]
    public async Task Save_rejects_a_unit_price_with_more_than_two_decimals()
    {
        // prices are cents: a third decimal would pass the split check rounded
        // and be billed unrounded
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var (pricedItem, _, recipient, _) = await SeedAsync(client);

        var response = await client.PostAsync(
            "/odata/SaveGrave",
            ODataJson.Body(
                new
                {
                    graveId = (long?)null,
                    graveNumber = "S-004",
                    fieldNumber = "F-1",
                    siteName = "Grabstätte Test",
                    position = "SingleInside",
                    startDate = "2026-01-01",
                    endDate = "2040-12-31",
                    items = new[]
                    {
                        new
                        {
                            itemId = pricedItem,
                            count = 1,
                            unitPrice = (decimal?)120.004m,
                        },
                    },
                    recipientIds = new[] { recipient },
                }
            )
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        Assert.Equal(
            "InvalidPrice",
            json.RootElement.GetProperty("error").GetProperty("code").GetString()
        );
    }

    [Fact]
    public async Task Save_still_edits_an_existing_grave_that_has_no_recipients()
    {
        // a grave that predates the rule keeps its recipient set, empty as it
        // is; refusing the save would leave it uneditable
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var (pricedItem, _, _, _) = await SeedAsync(client);
        long graveId;
        using (var scope = host.Services.CreateScope())
        {
            // nothing can create such a grave any more; this one is written as
            // the data of an older version would have left it
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var grave = new Burkard.Invoicing.Domain.Entities.Grave
            {
                GraveNumber = "S-003",
                StartDate = new DateOnly(2026, 1, 1),
                EndDate = new DateOnly(2040, 12, 31),
                CreatedAt = DateOnly.FromDateTime(DateTime.UtcNow),
            };
            db.Graves.Add(grave);
            await db.SaveChangesAsync();
            graveId = grave.Id;
        }

        var response = await client.PostAsync(
            "/odata/SaveGrave",
            ODataJson.Body(
                new
                {
                    graveId,
                    graveNumber = "S-003",
                    fieldNumber = "F-1",
                    siteName = "Grabstätte Test",
                    position = "SingleInside",
                    startDate = "2026-01-01",
                    endDate = "2041-12-31",
                    items = new[]
                    {
                        new
                        {
                            itemId = pricedItem,
                            count = 1,
                            unitPrice = (decimal?)null,
                        },
                    },
                    recipientIds = new List<long>(),
                }
            )
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Save_replaces_the_items_of_an_existing_grave()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var (pricedItem, unpricedItem, recipient, _) = await SeedAsync(client);

        var created = await client.PostAsync(
            "/odata/SaveGrave",
            ODataJson.Body(
                new
                {
                    graveNumber = "S-002",
                    fieldNumber = "F-1",
                    siteName = "Grabstätte Test",
                    position = "SingleInside",
                    startDate = "2026-01-01",
                    endDate = "2040-12-31",
                    items = new[]
                    {
                        new
                        {
                            itemId = pricedItem,
                            count = 1,
                            unitPrice = (decimal?)null,
                        },
                    },
                    recipientIds = new[] { recipient },
                }
            )
        );
        using var createdJson = await ODataJson.ReadAsync(created);
        var graveId = createdJson.RootElement.GetProperty("Id").GetInt64();

        // the same item stays (so the unique (GraveId, ItemId) index sees a re-add),
        // the second one is new; the recipient set is unchanged, since a grave's
        // recipients are fixed once it exists (see GraveRecipientImmutabilityTests)
        var updated = await client.PostAsync(
            "/odata/SaveGrave",
            ODataJson.Body(
                new
                {
                    graveId,
                    graveNumber = "S-002b",
                    fieldNumber = "F-1",
                    siteName = "Grabstätte Test",
                    position = "SingleInside",
                    startDate = "2027-01-01",
                    endDate = "2041-12-31",
                    items = new object[]
                    {
                        new
                        {
                            itemId = pricedItem,
                            count = 3,
                            unitPrice = (decimal?)null,
                        },
                        new
                        {
                            itemId = unpricedItem,
                            count = 1,
                            unitPrice = (decimal?)45.50m,
                        },
                    },
                    recipientIds = new[] { recipient },
                }
            )
        );
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var grave = await db
            .Graves.Include(g => g.Items)
            .Include(g => g.Recipients)
            .SingleAsync(g => g.Id == graveId);
        Assert.Equal("S-002b", grave.GraveNumber);
        Assert.Equal(2, grave.Items.Count);
        Assert.Equal(3, grave.Items.Single(i => i.ItemId == pricedItem).Count);
        Assert.Equal(45.50m, grave.Items.Single(i => i.ItemId == unpricedItem).UnitPrice);
        Assert.Equal(recipient, Assert.Single(grave.Recipients).RecipientId);
        // the replaced item links are gone, not orphaned
        Assert.Equal(2, await db.GraveItems.CountAsync());
        Assert.Equal(1, await db.GraveRecipients.CountAsync());
    }

    [Fact]
    public async Task Save_rejects_an_item_that_has_neither_a_link_price_nor_a_default_price()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var (_, unpricedItem, recipient, _) = await SeedAsync(client);

        var response = await client.PostAsync(
            "/odata/SaveGrave",
            ODataJson.Body(
                new
                {
                    graveNumber = "S-003",
                    fieldNumber = "F-1",
                    siteName = "Grabstätte Test",
                    position = "SingleInside",
                    startDate = "2026-01-01",
                    endDate = "2040-12-31",
                    items = new[]
                    {
                        new
                        {
                            itemId = unpricedItem,
                            count = 1,
                            unitPrice = (decimal?)null,
                        },
                    },
                    recipientIds = new[] { recipient },
                }
            )
        );
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        Assert.Equal(
            "PriceRequired",
            json.RootElement.GetProperty("error").GetProperty("code").GetString()
        );

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.Graves.AnyAsync(g => g.GraveNumber == "S-003"));
    }

    [Fact]
    public async Task Save_accepts_an_unpriced_item_when_the_link_carries_a_price()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var (_, unpricedItem, recipient, _) = await SeedAsync(client);

        var response = await client.PostAsync(
            "/odata/SaveGrave",
            ODataJson.Body(
                new
                {
                    graveNumber = "S-004",
                    fieldNumber = "F-1",
                    siteName = "Grabstätte Test",
                    position = "SingleInside",
                    startDate = "2026-01-01",
                    endDate = "2040-12-31",
                    items = new[]
                    {
                        new
                        {
                            itemId = unpricedItem,
                            count = 1,
                            unitPrice = (decimal?)33.00m,
                        },
                    },
                    recipientIds = new[] { recipient },
                }
            )
        );
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var link = await db.GraveItems.SingleAsync();
        Assert.Equal(33.00m, link.UnitPrice);
    }

    [Theory]
    [InlineData("", "F-1", "Grabstätte", "SingleInside", "2026-01", "GraveNumberRequired")]
    [InlineData("S-005", "", "Grabstätte", "SingleInside", "2026-01", "FieldNumberRequired")]
    [InlineData("S-005", "F-1", " ", "SingleInside", "2026-01", "SiteNameRequired")]
    [InlineData("S-005", "F-1", "Grabstätte", null, "2026-01", "PositionRequired")]
    [InlineData("S-005", "F-1", "Grabstätte", "Sideways", "2026-01", "PositionRequired")]
    [InlineData("S-005", "F-1", "Grabstätte", "3", "2026-01", "PositionRequired")]
    [InlineData("S-005", "F-1", "Grabstätte", "SingleInside", "not-a-date", "InvalidDate")]
    [InlineData("S-005", "F-1", "Grabstätte", "SingleInside", "", "InvalidDate")]
    public async Task Save_rejects_invalid_grave_fields(
        string graveNumber,
        string fieldNumber,
        string siteName,
        string? position,
        string startDate,
        string expectedCode
    )
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        await SeedAsync(client);

        var response = await client.PostAsync(
            "/odata/SaveGrave",
            ODataJson.Body(
                new
                {
                    graveNumber,
                    fieldNumber,
                    siteName,
                    position,
                    startDate,
                    endDate = (string?)null,
                    items = new List<object>(),
                    recipientIds = new List<long>(),
                }
            )
        );
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        Assert.Equal(
            expectedCode,
            json.RootElement.GetProperty("error").GetProperty("code").GetString()
        );
    }

    [Fact]
    public async Task Save_rejects_a_non_positive_count()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var (pricedItem, _, recipient, _) = await SeedAsync(client);

        var response = await client.PostAsync(
            "/odata/SaveGrave",
            ODataJson.Body(
                new
                {
                    graveNumber = "S-006",
                    fieldNumber = "F-1",
                    siteName = "Grabstätte Test",
                    position = "SingleInside",
                    startDate = "2026-01-01",
                    endDate = "2040-12-31",
                    items = new[]
                    {
                        new
                        {
                            itemId = pricedItem,
                            count = 0,
                            unitPrice = (decimal?)null,
                        },
                    },
                    recipientIds = new[] { recipient },
                }
            )
        );
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        Assert.Equal(
            "InvalidCount",
            json.RootElement.GetProperty("error").GetProperty("code").GetString()
        );
    }

    [Fact]
    public async Task Save_rejects_the_same_item_twice()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var (pricedItem, _, recipient, _) = await SeedAsync(client);

        var response = await client.PostAsync(
            "/odata/SaveGrave",
            ODataJson.Body(
                new
                {
                    graveNumber = "S-007",
                    fieldNumber = "F-1",
                    siteName = "Grabstätte Test",
                    position = "SingleInside",
                    startDate = "2026-01-01",
                    endDate = "2040-12-31",
                    items = new[]
                    {
                        new
                        {
                            itemId = pricedItem,
                            count = 1,
                            unitPrice = (decimal?)null,
                        },
                        new
                        {
                            itemId = pricedItem,
                            count = 2,
                            unitPrice = (decimal?)null,
                        },
                    },
                    recipientIds = new[] { recipient },
                }
            )
        );
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        Assert.Equal(
            "GraveItemLinkExists",
            json.RootElement.GetProperty("error").GetProperty("code").GetString()
        );
    }

    [Fact]
    public async Task Grave_details_expose_the_link_price_for_the_dialog()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var (pricedItem, _, recipient, _) = await SeedAsync(client);

        var created = await client.PostAsync(
            "/odata/SaveGrave",
            ODataJson.Body(
                new
                {
                    graveNumber = "S-008",
                    fieldNumber = "F-1",
                    siteName = "Grabstätte Test",
                    position = "SingleInside",
                    startDate = "2026-01-01",
                    endDate = "2040-12-31",
                    items = new[]
                    {
                        new
                        {
                            itemId = pricedItem,
                            count = 1,
                            unitPrice = (decimal?)99.00m,
                        },
                    },
                    recipientIds = new[] { recipient },
                }
            )
        );
        using var createdJson = await ODataJson.ReadAsync(created);
        var graveId = createdJson.RootElement.GetProperty("Id").GetInt64();

        var details = await client.GetAsync($"/odata/Graves({graveId})");
        Assert.Equal(HttpStatusCode.OK, details.StatusCode);
        using var detailsJson = await ODataJson.ReadAsync(details);
        var link = detailsJson.RootElement.GetProperty("Items")[0];
        Assert.Equal(99.00m, link.GetProperty("UnitPrice").GetDecimal());
        Assert.Equal(120.00m, link.GetProperty("Item").GetProperty("DefaultPrice").GetDecimal());
    }

    [Fact]
    public async Task Save_rejects_an_end_date_before_the_start_date()
    {
        // the import refuses the same row as DateRangeInvalid
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var (pricedItem, _, recipient, _) = await SeedAsync(client);

        var response = await client.PostAsync(
            "/odata/SaveGrave",
            ODataJson.Body(
                new
                {
                    graveId = (long?)null,
                    graveNumber = "S-DATE",
                    fieldNumber = "F-1",
                    siteName = "Grabstätte Test",
                    position = "SingleInside",
                    startDate = "2030-12-31",
                    endDate = "2026-01-01",
                    items = new[]
                    {
                        new
                        {
                            itemId = pricedItem,
                            count = 1,
                            unitPrice = (decimal?)null,
                        },
                    },
                    recipientIds = new List<long> { recipient },
                }
            )
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        Assert.Equal(
            "DateRangeInvalid",
            json.RootElement.GetProperty("error").GetProperty("code").GetString()
        );
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.Graves.AnyAsync(g => g.GraveNumber == "S-DATE"));
    }

    private static Task<HttpResponseMessage> PostGraveAsync(
        HttpClient client,
        string graveNumber,
        string startDate,
        string? endDate,
        long itemId,
        long recipientId,
        long? graveId = null,
        string fieldNumber = "27",
        string siteName = "Schineller Bäcke",
        string position = "DoubleOutside"
    ) =>
        client.PostAsync(
            "/odata/SaveGrave",
            ODataJson.Body(
                new
                {
                    graveId,
                    graveNumber,
                    fieldNumber,
                    siteName,
                    position,
                    startDate,
                    endDate,
                    items = new[]
                    {
                        new
                        {
                            itemId,
                            count = 1,
                            unitPrice = (decimal?)null,
                        },
                    },
                    recipientIds = new[] { recipientId },
                }
            )
        );

    private static async Task<(string? Code, string[] Args)> ErrorOfAsync(
        HttpResponseMessage response
    )
    {
        using var json = await ODataJson.ReadAsync(response);
        var error = json.RootElement.GetProperty("error");
        var args = error.TryGetProperty("args", out var list)
            ? list.EnumerateArray().Select(a => a.GetString() ?? "").ToArray()
            : Array.Empty<string>();
        return (error.GetProperty("code").GetString(), args);
    }

    [Fact]
    public async Task Save_stores_the_site_fields_and_the_term_as_months()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var (item, _, recipient, _) = await SeedAsync(client);

        var response = await PostGraveAsync(client, "M-1", "2026-03-15", null, item, recipient);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var created = await ODataJson.ReadAsync(response);
        var id = created.RootElement.GetProperty("Id").GetInt64();

        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var grave = await db.Graves.SingleAsync(g => g.Id == id);
            Assert.Equal(new DateOnly(2026, 3, 1), grave.StartDate);
            Assert.Null(grave.EndDate);
            Assert.Equal("27", grave.FieldNumber);
            Assert.Equal("Schineller Bäcke", grave.SiteName);
            Assert.Equal(GravePosition.DoubleOutside, grave.Position);
        }

        using var details = await ODataJson.ReadAsync(
            await client.GetAsync($"/odata/Graves({id})")
        );
        Assert.Equal("27", details.RootElement.GetProperty("FieldNumber").GetString());
        Assert.Equal("Schineller Bäcke", details.RootElement.GetProperty("SiteName").GetString());
        Assert.Equal("DoubleOutside", details.RootElement.GetProperty("Position").GetString());

        using var status = await ODataJson.ReadAsync(
            await client.GetAsync("/odata/GraveInvoicingStatus?year=2026&half=1")
        );
        var row = status.RootElement.GetProperty("value")[0];
        Assert.Equal("Schineller Bäcke", row.GetProperty("SiteName").GetString());
        Assert.Equal("27", row.GetProperty("FieldNumber").GetString());

        using var search = await ODataJson.ReadAsync(
            await client.GetAsync("/odata/SearchGraves?term=bäcke")
        );
        Assert.Equal(1, search.RootElement.GetProperty("value").GetArrayLength());
    }

    [Fact]
    public async Task Save_compares_the_end_month_with_the_start_month()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var (item, _, recipient, _) = await SeedAsync(client);

        var before = await PostGraveAsync(client, "R-1", "2026-05", "2026-04", item, recipient);
        Assert.Equal(HttpStatusCode.BadRequest, before.StatusCode);
        Assert.Equal("DateRangeInvalid", (await ErrorOfAsync(before)).Code);

        // one month long: start and end are the same month
        var same = await PostGraveAsync(client, "R-2", "2026-05", "2026-05", item, recipient);
        Assert.Equal(HttpStatusCode.OK, same.StatusCode);
    }

    [Fact]
    public async Task A_grave_number_cannot_be_used_twice_in_overlapping_terms()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var (item, _, recipient, _) = await SeedAsync(client);
        Assert.Equal(
            HttpStatusCode.OK,
            (await PostGraveAsync(client, "A-1", "2020-01", "2030-12", item, recipient)).StatusCode
        );

        // the last month overlaps; trimmed and case-insensitive, like the import
        var clash = await PostGraveAsync(client, " a-1 ", "2030-12", null, item, recipient);
        Assert.Equal(HttpStatusCode.BadRequest, clash.StatusCode);
        var (code, args) = await ErrorOfAsync(clash);
        Assert.Equal("GraveNumberInUse", code);
        Assert.Equal(new[] { "A-1", "01/2020", "12/2030" }, args);

        // the month after the end is free again
        var next = await PostGraveAsync(client, "A-1", "2031-01", null, item, recipient);
        Assert.Equal(HttpStatusCode.OK, next.StatusCode);

        // that grave has no end, so every later term overlaps it
        var later = await PostGraveAsync(client, "A-1", "2045-01", "2045-12", item, recipient);
        var (laterCode, laterArgs) = await ErrorOfAsync(later);
        Assert.Equal("GraveNumberInUse", laterCode);
        Assert.Equal(new[] { "A-1", "01/2031" }, laterArgs);
    }

    [Fact]
    public async Task Resaving_a_grave_does_not_collide_with_itself()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var (item, _, recipient, _) = await SeedAsync(client);
        using var created = await ODataJson.ReadAsync(
            await PostGraveAsync(client, "B-1", "2020-01", null, item, recipient)
        );
        var id = created.RootElement.GetProperty("Id").GetInt64();

        var resaved = await PostGraveAsync(client, "B-1", "2020-01", null, item, recipient, id);
        Assert.Equal(HttpStatusCode.OK, resaved.StatusCode);
    }

    [Fact]
    public async Task Editing_a_grave_onto_another_graves_number_is_refused()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var (item, _, recipient, _) = await SeedAsync(client);
        Assert.Equal(
            HttpStatusCode.OK,
            (await PostGraveAsync(client, "D-1", "2020-01", null, item, recipient)).StatusCode
        );
        using var second = await ODataJson.ReadAsync(
            await PostGraveAsync(client, "D-2", "2020-01", null, item, recipient)
        );
        var id = second.RootElement.GetProperty("Id").GetInt64();

        var renamed = await PostGraveAsync(client, "D-1", "2020-01", null, item, recipient, id);

        Assert.Equal(HttpStatusCode.BadRequest, renamed.StatusCode);
        var (code, args) = await ErrorOfAsync(renamed);
        Assert.Equal("GraveNumberInUse", code);
        Assert.Equal(new[] { "D-1", "01/2020" }, args);
    }

    [Fact]
    public async Task Two_graves_may_share_a_field_number()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var (item, _, recipient, _) = await SeedAsync(client);

        var first = await PostGraveAsync(
            client,
            "C-1",
            "2020-01",
            null,
            item,
            recipient,
            fieldNumber: "27"
        );
        var second = await PostGraveAsync(
            client,
            "C-2",
            "2020-01",
            null,
            item,
            recipient,
            fieldNumber: "27"
        );
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
    }

    private static object GraveBody(
        string number,
        string start,
        string? end,
        long itemId,
        decimal price,
        IEnumerable<long> recipientIds
    ) =>
        new
        {
            graveId = (long?)null,
            graveNumber = number,
            fieldNumber = "1",
            siteName = "Schineller Bäcke",
            position = "SingleInside",
            startDate = start,
            endDate = end,
            items = new[]
            {
                new
                {
                    itemId,
                    count = 1,
                    unitPrice = (decimal?)price,
                    priceIncludesVat = false,
                },
            },
            recipientIds,
        };

    private static async Task<long[]> RecipientsAsync(HttpClient client, int count)
    {
        var ids = new long[count];
        for (var i = 0; i < count; i++)
        {
            ids[i] = await ReadIdAsync(
                client.PostAsync(
                    "/odata/Recipients",
                    ODataJson.Body(new { Name = $"Empfänger {i}", Address = $"Weg {i}" })
                )
            );
        }

        return ids;
    }

    [Fact]
    public async Task Saving_refuses_more_than_four_recipients()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var (priced, _, _, _) = await SeedAsync(client);
        var recipients = await RecipientsAsync(client, 5);

        // 120,00 divides by five, so only the cap can refuse it
        var response = await client.PostAsync(
            "/odata/SaveGrave",
            ODataJson.Body(GraveBody("TM-1", "2026-01", null, priced, 120.00m, recipients))
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        Assert.Equal(
            "TooManyRecipients",
            json.RootElement.GetProperty("error").GetProperty("code").GetString()
        );
    }

    [Fact]
    public async Task Saving_a_grave_whose_start_invoice_needs_a_correction_asks_for_it()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var (priced, _, first, second) = await SeedAsync(client);

        // 60,02 divides by two; May and June of it are 20,01, which does not and
        // is raised to 20,02 -- a correction the request has not confirmed
        var response = await client.PostAsync(
            "/odata/SaveGrave",
            ODataJson.Body(
                GraveBody("SD-1", "2026-05", null, priced, 60.02m, new[] { first, second })
            )
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        var error = json.RootElement.GetProperty("error");
        Assert.Equal("CorrectionsChanged", error.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Saving_accepts_a_grave_whose_every_invoice_divides()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var (priced, _, first, second) = await SeedAsync(client);

        var response = await client.PostAsync(
            "/odata/SaveGrave",
            ODataJson.Body(
                GraveBody("SD-2", "2026-05", "2030-08", priced, 60.00m, new[] { first, second })
            )
        );

        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
    }

    private static async Task<long> SeedQuarterItemAsync(HttpClient client)
    {
        return await ReadIdAsync(
            client.PostAsync(
                "/odata/InvoiceItems",
                ODataJson.Body(
                    new
                    {
                        Number = "4003",
                        Description = "Sommerbepflanzung",
                        VatRate = 7.0m,
                        DefaultPrice = 40.00m,
                        Quarter = "Summer",
                    }
                )
            )
        );
    }

    private static object AllYearBody(long[] itemIds, long recipient) =>
        new
        {
            graveId = (long?)null,
            graveNumber = "AY-1",
            fieldNumber = "F-1",
            siteName = "Grabstätte Test",
            position = "SingleInside",
            startDate = "2026-01-01",
            endDate = "2040-12-31",
            items = itemIds.Select(id => new
            {
                itemId = id,
                count = 1,
                unitPrice = (decimal?)null,
            }),
            recipientIds = new[] { recipient },
        };

    [Fact]
    public async Task Save_rejects_a_grave_whose_only_item_has_a_quarter()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var (_, _, recipient, _) = await SeedAsync(client);
        var summer = await SeedQuarterItemAsync(client);

        var response = await client.PostAsync(
            "/odata/SaveGrave",
            ODataJson.Body(AllYearBody(new[] { summer }, recipient))
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        Assert.Equal(
            "AllYearItemRequired",
            json.RootElement.GetProperty("error").GetProperty("code").GetString()
        );
    }

    [Fact]
    public async Task Save_accepts_a_grave_with_an_all_year_item_and_a_quarter_item()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var (priced, _, recipient, _) = await SeedAsync(client);
        var summer = await SeedQuarterItemAsync(client);

        var response = await client.PostAsync(
            "/odata/SaveGrave",
            ODataJson.Body(AllYearBody(new[] { priced, summer }, recipient))
        );

        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
    }

    private static async Task<long> SeedItemAt19Async(HttpClient client)
    {
        await client.PostAsync("/odata/VatRates", ODataJson.Body(new { Rate = 19.0m }));
        return await ReadIdAsync(
            client.PostAsync(
                "/odata/InvoiceItems",
                ODataJson.Body(
                    new
                    {
                        Number = "4010",
                        Description = "Grabpflege 19",
                        VatRate = 19.0m,
                        DefaultPrice = (decimal?)null,
                    }
                )
            )
        );
    }

    private static object CorrectedBody(
        long? graveId,
        long itemId,
        decimal price,
        IEnumerable<long> recipientIds,
        object[] corrections
    ) =>
        new
        {
            graveId,
            graveNumber = "K-1",
            fieldNumber = "1",
            siteName = "Schineller Bäcke",
            position = "SingleInside",
            startDate = "2026-03",
            endDate = "2028-10",
            items = new[]
            {
                new
                {
                    itemId,
                    count = 1,
                    unitPrice = (decimal?)price,
                    priceIncludesVat = false,
                },
            },
            recipientIds,
            corrections,
        };

    private static object Correction(
        string kind,
        int? year,
        int half,
        long itemId,
        decimal price
    ) =>
        new
        {
            kind,
            year,
            half,
            vatRate = 19.0m,
            itemId,
            correctedPrice = price,
        };

    private static async Task<string> ErrorCodeAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        return json.RootElement.GetProperty("error").GetProperty("code").GetString()!;
    }

    // 3 recipients, care 60,00 all year, term 03/2026-10/2028: start H1 2026 and
    // end H2 2028 bill 40,00, raised to 40,02
    [Fact]
    public async Task Saving_without_the_needed_corrections_is_refused()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var item = await SeedItemAt19Async(client);
        var recipients = await RecipientsAsync(client, 3);

        var response = await client.PostAsync(
            "/odata/SaveGrave",
            ODataJson.Body(CorrectedBody(null, item, 60.00m, recipients, []))
        );

        Assert.Equal("CorrectionsChanged", await ErrorCodeAsync(response));
    }

    [Fact]
    public async Task Saving_with_outdated_corrections_is_refused()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var item = await SeedItemAt19Async(client);
        var recipients = await RecipientsAsync(client, 3);

        var response = await client.PostAsync(
            "/odata/SaveGrave",
            ODataJson.Body(
                CorrectedBody(
                    null,
                    item,
                    60.00m,
                    recipients,
                    [
                        Correction("Start", 2026, 1, item, 40.01m),
                        Correction("End", 2028, 2, item, 40.02m),
                    ]
                )
            )
        );

        Assert.Equal("CorrectionsChanged", await ErrorCodeAsync(response));
    }

    // 100,25 gross at 19 % needs a correction even for one recipient, which the
    // editor previews; without a recipient the save has nothing to evaluate and
    // must say so instead of calling the corrections changed
    [Fact]
    public async Task A_new_grave_without_recipients_is_refused_for_that_not_for_its_corrections()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var item = await SeedItemAt19Async(client);

        var response = await client.PostAsync(
            "/odata/SaveGrave",
            ODataJson.Body(
                new
                {
                    graveId = (long?)null,
                    graveNumber = "K-2",
                    fieldNumber = "1",
                    siteName = "Schineller Bäcke",
                    position = "SingleInside",
                    startDate = "2026-03",
                    endDate = "2028-10",
                    items = new[]
                    {
                        new
                        {
                            itemId = item,
                            count = 1,
                            unitPrice = (decimal?)100.25m,
                            priceIncludesVat = true,
                        },
                    },
                    recipientIds = Array.Empty<long>(),
                    corrections = new[] { Correction("Start", 2026, 1, item, 100.26m) },
                }
            )
        );

        Assert.Equal("NoRecipients", await ErrorCodeAsync(response));
    }

    [Fact]
    public async Task Saving_with_an_unknown_correction_kind_is_refused()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var item = await SeedItemAt19Async(client);
        var recipients = await RecipientsAsync(client, 3);

        var response = await client.PostAsync(
            "/odata/SaveGrave",
            ODataJson.Body(
                CorrectedBody(
                    null,
                    item,
                    60.00m,
                    recipients,
                    [
                        Correction("Nonsense", 2026, 1, item, 40.02m),
                        Correction("End", 2028, 2, item, 40.02m),
                    ]
                )
            )
        );

        Assert.Equal("CorrectionsChanged", await ErrorCodeAsync(response));
    }

    [Fact]
    public async Task Saving_with_the_confirmed_corrections_stores_them()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var item = await SeedItemAt19Async(client);
        var recipients = await RecipientsAsync(client, 3);

        var response = await client.PostAsync(
            "/odata/SaveGrave",
            ODataJson.Body(
                CorrectedBody(
                    null,
                    item,
                    60.00m,
                    recipients,
                    [
                        Correction("Start", 2026, 1, item, 40.02m),
                        Correction("End", 2028, 2, item, 40.02m),
                    ]
                )
            )
        );

        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        using var json = await ODataJson.ReadAsync(response);
        var graveId = json.RootElement.GetProperty("Id").GetInt64();
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rows = await db
            .GraveBillingCorrections.Where(c => c.GraveId == graveId)
            .OrderBy(c => c.Year)
            .ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Equal(40.00m, r.EnteredPrice));
        Assert.All(rows, r => Assert.Equal(40.02m, r.CorrectedPrice));
        Assert.All(rows, r => Assert.Equal(19.0m, r.VatRate));
        Assert.All(rows, r => Assert.Equal(item, r.ItemId));
        Assert.Equal(BillingExampleKind.Start, rows[0].Kind);
        Assert.Equal((2026, 1), (rows[0].Year, rows[0].Half));
        Assert.Equal(BillingExampleKind.End, rows[1].Kind);
        Assert.Equal((2028, 2), (rows[1].Year, rows[1].Half));
    }

    [Fact]
    public async Task Saving_replaces_the_stored_corrections()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var item = await SeedItemAt19Async(client);
        var recipients = await RecipientsAsync(client, 3);
        var first = await client.PostAsync(
            "/odata/SaveGrave",
            ODataJson.Body(
                CorrectedBody(
                    null,
                    item,
                    60.00m,
                    recipients,
                    [
                        Correction("Start", 2026, 1, item, 40.02m),
                        Correction("End", 2028, 2, item, 40.02m),
                    ]
                )
            )
        );
        Assert.True(first.IsSuccessStatusCode, await first.Content.ReadAsStringAsync());
        using var created = await ODataJson.ReadAsync(first);
        var graveId = created.RootElement.GetProperty("Id").GetInt64();

        // 60,03 divides by three, and 4/6 of it, 40,02, divides as well
        var second = await client.PostAsync(
            "/odata/SaveGrave",
            ODataJson.Body(CorrectedBody(graveId, item, 60.03m, recipients, []))
        );

        Assert.True(second.IsSuccessStatusCode, await second.Content.ReadAsStringAsync());
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Empty(
            await db.GraveBillingCorrections.Where(c => c.GraveId == graveId).ToListAsync()
        );
    }

    [Fact]
    public async Task A_grave_needing_no_correction_saves_without_any()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var (priced, _, first, second) = await SeedAsync(client);

        var response = await client.PostAsync(
            "/odata/SaveGrave",
            ODataJson.Body(CorrectedBody(null, priced, 60.00m, new[] { first, second }, []))
        );

        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        using var json = await ODataJson.ReadAsync(response);
        var graveId = json.RootElement.GetProperty("Id").GetInt64();
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Empty(
            await db.GraveBillingCorrections.Where(c => c.GraveId == graveId).ToListAsync()
        );
    }
}
