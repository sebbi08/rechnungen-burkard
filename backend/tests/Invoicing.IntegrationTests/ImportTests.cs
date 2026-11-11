using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Domain;
using Burkard.Invoicing.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Burkard.Invoicing.IntegrationTests;

public class ImportTests
{
    // Recipients and mapping without their optional last column (Email,
    // UnitPrice), as archives were written before those existed: most tests
    // need neither, and these still have to import.
    private const string RecipientsHeader = "RecipientNumber,Name,Address\r\n";
    private const string ItemsHeader = "ItemNumber,Description,VatRate,Price,Quarter\r\n";
    private const string GravesHeader =
        "GraveNumber,FieldNumber,SiteName,Cemetery,Position,StartDate,EndDate\r\n";
    private const string MappingHeader = "Type,GraveNumber,Reference,Count,PriceIncludesVat\r\n";

    // the full headers, as the template has them
    private const string FullGravesHeader =
        "GraveNumber,FieldNumber,SiteName,Cemetery,Position,StartDate,EndDate,Cooperative,ContractNumber\r\n";

    // the graves header v3.2.0 shipped, one column short of the template's
    private const string EightColumnGravesHeader =
        "GraveNumber,FieldNumber,SiteName,Cemetery,Position,StartDate,EndDate,Cooperative\r\n";
    private const string FullRecipientsHeader = "RecipientNumber,Name,Address,Email\r\n";
    private const string FullMappingHeader =
        "Type,GraveNumber,Reference,Count,PriceIncludesVat,UnitPrice\r\n";

    private static async Task ConfigureVatAsync(ApiTestHost host, params decimal[] rates)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        foreach (var rate in rates)
        {
            db.VatRates.Add(new VatRate { Rate = rate });
        }

        await db.SaveChangesAsync();
    }

    private static byte[] Zip(params (string Name, string Content)[] files)
    {
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in files)
            {
                var entry = zip.CreateEntry(name);
                using var stream = entry.Open();
                using var writer = new StreamWriter(stream, new UTF8Encoding(false));
                writer.Write(content);
            }
        }

        return memory.ToArray();
    }

    /// <summary>Like <see cref="Zip"/>, but with each file's bytes as given, for
    /// files that are not UTF-8.</summary>
    private static byte[] ZipBytes(params (string Name, byte[] Content)[] files)
    {
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in files)
            {
                using var stream = zip.CreateEntry(name).Open();
                stream.Write(content);
            }
        }

        return memory.ToArray();
    }

    private static byte[] Utf8(string text) => new UTF8Encoding(false).GetBytes(text);

    /// <summary>A consistent archive: two recipients, three items (one of them
    /// without a price, which no grave links), one grave with both link kinds.
    /// The gross-priced 107.00 is 100.00 net at 7 %, so the grave divides.</summary>
    private static byte[] ValidArchive() =>
        Zip(
            (
                "recipients.csv",
                RecipientsHeader
                    + "E-1000,Anna Beispiel,\"Hauptstraße 1\n99999 Musterdorf\"\r\n"
                    + "E-1001,Bert Beispiel,\"Nebenweg 2\n99999 Musterdorf\"\r\n"
            ),
            (
                "items.csv",
                ItemsHeader
                    + "9001,Grabpflege,7,120.00,\r\n"
                    + "9002,Sonderleistung,7,107.00,\r\n"
                    + "9003,Extra,7,,\r\n"
            ),
            (
                "graves.csv",
                GravesHeader
                    + "IMP-1,27,Grabstätte Import,Bergfriedhof,DoubleOutside,2026-01-01,2040-12-31\r\n"
            ),
            (
                "mapping.csv",
                MappingHeader
                    + "Item,IMP-1,9001,2,\r\n"
                    + "Item,IMP-1,9002,1,true\r\n"
                    + "Recipient,IMP-1,E-1000,,\r\n"
                    + "Recipient,IMP-1,E-1001,,\r\n"
            )
        );

    /// <summary>An archive whose one grave cannot be split: 120.01 net at 19 % over
    /// two recipients is 12 001 cents, which is odd.</summary>
    private static byte[] UnsplittableArchive() =>
        Zip(
            (
                "recipients.csv",
                RecipientsHeader
                    + "E-1000,Anna Beispiel,Hauptstraße 1\r\n"
                    + "E-1001,Bert Beispiel,Nebenweg 2\r\n"
            ),
            ("items.csv", ItemsHeader + "9001,Grabpflege,19,120.01,\r\n"),
            (
                "graves.csv",
                GravesHeader
                    + "IMP-1,27,Grabstätte Import,Bergfriedhof,DoubleOutside,2026-01-01,2040-12-31\r\n"
            ),
            (
                "mapping.csv",
                MappingHeader
                    + "Item,IMP-1,9001,1,\r\n"
                    + "Recipient,IMP-1,E-1000,,\r\n"
                    + "Recipient,IMP-1,E-1001,,\r\n"
            )
        );

    private static MultipartFormDataContent Upload(byte[] archive)
    {
        var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(archive);
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(
            "application/zip"
        );
        content.Add(file, "file", "import.zip");
        return content;
    }

    private static async Task<
        List<(string File, int? Row, string Code, string Message)>
    > ProblemsAsync(HttpResponseMessage response)
    {
        using var json = await ODataJson.ReadAsync(response);
        return json
            .RootElement.GetProperty("problems")
            .EnumerateArray()
            .Select(p =>
                (
                    p.GetProperty("file").GetString()!,
                    p.GetProperty("row").ValueKind == JsonValueKind.Null
                        ? (int?)null
                        : p.GetProperty("row").GetInt32(),
                    p.GetProperty("code").GetString()!,
                    p.GetProperty("message").GetString()!
                )
            )
            .ToList();
    }

    /// <summary>The refusal has to be the whole archive: recipients and items are
    /// written before graves, so they are what a partial write would leave.</summary>
    private static async Task AssertNothingImportedAsync(ApiTestHost host)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Empty(await db.Recipients.ToListAsync());
        Assert.Empty(await db.InvoiceItems.ToListAsync());
        Assert.Empty(await db.Graves.ToListAsync());
        Assert.Empty(await db.GraveItems.ToListAsync());
        Assert.Empty(await db.GraveRecipients.ToListAsync());
    }

    [Fact]
    public async Task Template_contains_the_four_files_with_their_header_rows()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();

        var response = await client.GetAsync("/odata/ImportTemplate");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/zip", response.Content.Headers.ContentType?.MediaType);

        using var zip = new ZipArchive(
            await response.Content.ReadAsStreamAsync(),
            ZipArchiveMode.Read
        );
        var names = zip.Entries.Select(e => e.FullName).OrderBy(n => n).ToArray();
        Assert.Equal(new[] { "graves.csv", "items.csv", "mapping.csv", "recipients.csv" }, names);

        foreach (
            var (name, header) in new[]
            {
                ("recipients.csv", FullRecipientsHeader),
                ("items.csv", ItemsHeader),
                ("graves.csv", FullGravesHeader),
                ("mapping.csv", FullMappingHeader),
            }
        )
        {
            using var reader = new StreamReader(zip.GetEntry(name)!.Open(), Encoding.UTF8, true);
            Assert.Equal(
                header.TrimEnd('\r', '\n'),
                reader.ReadToEnd().Trim('﻿').TrimEnd('\r', '\n')
            );
        }
    }

    [Fact]
    public async Task A_downloaded_template_imports_as_no_data()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();

        var template = await (
            await client.GetAsync("/odata/ImportTemplate")
        ).Content.ReadAsByteArrayAsync();
        var response = await client.PostAsync("/odata/ImportArchive", Upload(template));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        var summary = json.RootElement.GetProperty("summary");
        Assert.Equal(0, summary.GetProperty("recipients").GetInt32());
        Assert.Equal(0, summary.GetProperty("graves").GetInt32());
    }

    [Fact]
    public async Task Valid_archive_creates_everything_and_reports_counts()
    {
        using var host = new ApiTestHost();
        await ConfigureVatAsync(host, 7m);
        using var client = host.CreateClient();

        var response = await client.PostAsync("/odata/ImportArchive", Upload(ValidArchive()));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var json = await ODataJson.ReadAsync(response);
        var summary = json.RootElement.GetProperty("summary");
        Assert.Equal(2, summary.GetProperty("recipients").GetInt32());
        Assert.Equal(3, summary.GetProperty("items").GetInt32());
        Assert.Equal(1, summary.GetProperty("graves").GetInt32());
        Assert.Equal(2, summary.GetProperty("itemLinks").GetInt32());
        Assert.Equal(2, summary.GetProperty("recipientLinks").GetInt32());

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var grave = await db
            .Graves.Include(g => g.Items)
                .ThenInclude(i => i.Item)
            .Include(g => g.Recipients)
                .ThenInclude(r => r.Recipient)
            .SingleAsync();

        Assert.Equal("IMP-1", grave.GraveNumber);
        // the day of the import by the app's clock, not by the UTC day
        Assert.Equal(DateOnly.FromDateTime(host.Now), grave.CreatedAt);
        Assert.Equal(new DateOnly(2026, 1, 1), grave.StartDate);

        // the grave's item uses the imported item's number, text, VAT rate and price
        var pricedLink = grave.Items.Single(i => i.Item.Number == "9001");
        Assert.Equal(2, pricedLink.Count);
        Assert.Equal("Grabpflege", pricedLink.Item.Description);
        Assert.Equal(7m, pricedLink.Item.VatRate);
        Assert.Equal(120.00m, pricedLink.Item.DefaultPrice);
        Assert.Equal(107.00m, grave.Items.Single(i => i.Item.Number == "9002").Item.DefaultPrice);

        // an item without a price is imported as it is, as long as no grave links it
        Assert.Null((await db.InvoiceItems.SingleAsync(i => i.Number == "9003")).DefaultPrice);

        // the address keeps the line break the quoted CSV field carried
        var anna = grave.Recipients.Select(r => r.Recipient).Single(r => r.Number == "E-1000");
        Assert.Equal("Hauptstraße 1\n99999 Musterdorf", anna.Address);

        // a file without the Email and UnitPrice columns has neither
        Assert.Null(anna.Email);
        Assert.All(grave.Items, link => Assert.Null(link.UnitPrice));
    }

    [Fact]
    public async Task An_item_link_carries_its_vat_included_flag()
    {
        using var host = new ApiTestHost();
        await ConfigureVatAsync(host, 7m);
        using var client = host.CreateClient();

        var response = await client.PostAsync("/odata/ImportArchive", Upload(ValidArchive()));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(
            await db
                .GraveItems.Include(l => l.Item)
                .Where(l => l.Item.Number == "9001")
                .Select(l => l.PriceIncludesVat)
                .SingleAsync()
        );
        Assert.True(
            await db
                .GraveItems.Include(l => l.Item)
                .Where(l => l.Item.Number == "9002")
                .Select(l => l.PriceIncludesVat)
                .SingleAsync()
        );
    }

    [Fact]
    public async Task An_unreadable_vat_included_cell_is_reported()
    {
        using var host = new ApiTestHost();
        await ConfigureVatAsync(host, 7m);
        using var client = host.CreateClient();

        var archive = Zip(
            ("recipients.csv", RecipientsHeader),
            ("items.csv", ItemsHeader),
            ("graves.csv", GravesHeader),
            ("mapping.csv", MappingHeader + "Item,IMP-1,9001,1,vielleicht\r\n")
        );
        var response = await client.PostAsync("/odata/ImportArchive", Upload(archive));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problems = await ProblemsAsync(response);
        Assert.Contains(
            problems,
            p => p.File == "mapping.csv" && p.Row == 2 && p.Code == "PriceIncludesVatInvalid"
        );
    }

    /// <summary>An archive built before this feature has a mapping.csv with the
    /// old four-column header, missing PriceIncludesVat. Regenerating the
    /// archive from the current template is the accepted cost of the new
    /// column, so the old header must be rejected rather than silently read as
    /// if every item link were not VAT-included.</summary>
    [Fact]
    public async Task A_mapping_header_without_the_vat_included_column_is_rejected()
    {
        using var host = new ApiTestHost();
        await ConfigureVatAsync(host, 7m);
        using var client = host.CreateClient();

        var archive = Zip(
            ("recipients.csv", RecipientsHeader),
            ("items.csv", ItemsHeader),
            ("graves.csv", GravesHeader),
            ("mapping.csv", "Type,GraveNumber,Reference,Count\r\n")
        );
        var response = await client.PostAsync("/odata/ImportArchive", Upload(archive));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problems = await ProblemsAsync(response);
        Assert.Contains(
            problems,
            p => p.File == "mapping.csv" && p.Row == 1 && p.Code == "HeaderInvalid"
        );
        await AssertNothingImportedAsync(host);
    }

    [Fact]
    public async Task Missing_file_is_reported_and_nothing_is_imported()
    {
        using var host = new ApiTestHost();
        await ConfigureVatAsync(host, 7m);
        using var client = host.CreateClient();

        var archive = Zip(
            ("recipients.csv", RecipientsHeader),
            ("items.csv", ItemsHeader),
            ("graves.csv", GravesHeader)
        );
        var response = await client.PostAsync("/odata/ImportArchive", Upload(archive));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problems = await ProblemsAsync(response);
        Assert.Contains(problems, p => p.File == "mapping.csv" && p.Code == "FileMissing");
        await AssertNothingImportedAsync(host);
    }

    [Fact]
    public async Task Wrong_header_row_is_reported_with_its_file()
    {
        using var host = new ApiTestHost();
        await ConfigureVatAsync(host, 7m);
        using var client = host.CreateClient();

        var archive = Zip(
            ("recipients.csv", "Nummer,Name,Adresse\r\n"),
            ("items.csv", ItemsHeader),
            ("graves.csv", GravesHeader),
            ("mapping.csv", MappingHeader)
        );
        var response = await client.PostAsync("/odata/ImportArchive", Upload(archive));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problems = await ProblemsAsync(response);
        Assert.Contains(
            problems,
            p => p.File == "recipients.csv" && p.Row == 1 && p.Code == "HeaderInvalid"
        );
        await AssertNothingImportedAsync(host);
    }

    [Fact]
    public async Task Invalid_row_is_reported_with_its_row_number()
    {
        using var host = new ApiTestHost();
        await ConfigureVatAsync(host, 7m);
        using var client = host.CreateClient();

        var archive = Zip(
            ("recipients.csv", RecipientsHeader + "E-1000,,Hauptstraße 1\r\n"),
            ("items.csv", ItemsHeader + "9001,Grabpflege,abc,10,\r\n"),
            (
                "graves.csv",
                GravesHeader
                    + "IMP-1,27,Grabstätte Import,Bergfriedhof,DoubleOutside,nicht-ein-datum,2040-12-31\r\n"
            ),
            ("mapping.csv", MappingHeader)
        );
        var response = await client.PostAsync("/odata/ImportArchive", Upload(archive));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problems = await ProblemsAsync(response);
        Assert.Contains(
            problems,
            p => p.File == "recipients.csv" && p.Row == 2 && p.Code == "FieldRequired"
        );
        Assert.Contains(
            problems,
            p => p.File == "items.csv" && p.Row == 2 && p.Code == "VatRateInvalid"
        );
        Assert.Contains(
            problems,
            p => p.File == "graves.csv" && p.Row == 2 && p.Code == "DateInvalid"
        );
        await AssertNothingImportedAsync(host);
    }

    [Fact]
    public async Task Every_problem_in_the_archive_is_reported_in_one_pass()
    {
        using var host = new ApiTestHost();
        await ConfigureVatAsync(host, 7m);
        using var client = host.CreateClient();

        // a row-level problem in one file must not hide the reference problem in
        // another: users fix an archive in one pass, not one error at a time
        var archive = Zip(
            ("recipients.csv", RecipientsHeader + "E-1000,,Hauptstraße 1\r\n"),
            ("items.csv", ItemsHeader + "9001,Grabpflege,7,120,\r\n"),
            (
                "graves.csv",
                GravesHeader
                    + "IMP-1,27,Grabstätte Import,Bergfriedhof,DoubleOutside,2026-01-01,2040-12-31\r\n"
            ),
            ("mapping.csv", MappingHeader + "Item,IMP-1,GIBTSNICHT,1,\r\n")
        );
        var response = await client.PostAsync("/odata/ImportArchive", Upload(archive));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problems = await ProblemsAsync(response);
        Assert.Contains(
            problems,
            p => p.File == "recipients.csv" && p.Row == 2 && p.Code == "FieldRequired"
        );
        Assert.Contains(
            problems,
            p => p.File == "mapping.csv" && p.Row == 2 && p.Code == "ItemNotDefined"
        );
    }

    [Fact]
    public async Task A_mapping_row_pointing_at_a_rejected_row_is_not_reported_twice()
    {
        using var host = new ApiTestHost();
        await ConfigureVatAsync(host, 7m);
        using var client = host.CreateClient();

        // the recipient row is rejected for its missing name; the mapping row that
        // references it must not add a second, misleading "not defined" problem
        var archive = Zip(
            ("recipients.csv", RecipientsHeader + "E-1000,,Hauptstraße 1\r\n"),
            ("items.csv", ItemsHeader),
            (
                "graves.csv",
                GravesHeader
                    + "IMP-1,27,Grabstätte Import,Bergfriedhof,DoubleOutside,2026-01-01,2040-12-31\r\n"
            ),
            ("mapping.csv", MappingHeader + "Recipient,IMP-1,E-1000,,\r\n")
        );
        var response = await client.PostAsync("/odata/ImportArchive", Upload(archive));

        var problems = await ProblemsAsync(response);
        Assert.DoesNotContain(problems, p => p.Code == "RecipientNotDefined");
        Assert.Contains(problems, p => p.File == "recipients.csv" && p.Code == "FieldRequired");
    }

    [Fact]
    public async Task Mapping_reference_that_is_not_defined_is_reported()
    {
        using var host = new ApiTestHost();
        await ConfigureVatAsync(host, 7m);
        using var client = host.CreateClient();

        var archive = Zip(
            ("recipients.csv", RecipientsHeader + "E-1000,Anna,Hauptstraße 1\r\n"),
            ("items.csv", ItemsHeader + "9001,Grabpflege,7,120,\r\n"),
            (
                "graves.csv",
                GravesHeader
                    + "IMP-1,27,Grabstätte Import,Bergfriedhof,DoubleOutside,2026-01-01,2040-12-31\r\n"
            ),
            (
                "mapping.csv",
                MappingHeader
                    + "Item,IMP-1,9999,1,\r\n"
                    + "Recipient,IMP-1,E-9999,,\r\n"
                    + "Recipient,IMP-9,E-1000,,\r\n"
            )
        );
        var response = await client.PostAsync("/odata/ImportArchive", Upload(archive));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problems = await ProblemsAsync(response);
        Assert.Contains(
            problems,
            p => p.File == "mapping.csv" && p.Row == 2 && p.Code == "ItemNotDefined"
        );
        Assert.Contains(
            problems,
            p => p.File == "mapping.csv" && p.Row == 3 && p.Code == "RecipientNotDefined"
        );
        Assert.Contains(
            problems,
            p => p.File == "mapping.csv" && p.Row == 4 && p.Code == "GraveNotDefined"
        );
        await AssertNothingImportedAsync(host);
    }

    [Fact]
    public async Task Duplicate_numbers_and_duplicate_links_are_reported()
    {
        using var host = new ApiTestHost();
        await ConfigureVatAsync(host, 7m);
        using var client = host.CreateClient();

        var archive = Zip(
            (
                "recipients.csv",
                RecipientsHeader + "E-1000,Anna,Hauptstraße 1\r\n" + "E-1000,Bert,Nebenweg 2\r\n"
            ),
            ("items.csv", ItemsHeader + "9001,Grabpflege,7,120,\r\n" + "9001,Doppelt,7,10,\r\n"),
            (
                "graves.csv",
                GravesHeader
                    + "IMP-1,27,Grabstätte Import,Bergfriedhof,DoubleOutside,2026-01-01,2040-12-31\r\n"
                    + "IMP-1,27,Grabstätte Import,Bergfriedhof,DoubleOutside,2026-01-01,2040-12-31\r\n"
            ),
            ("mapping.csv", MappingHeader + "Item,IMP-1,9001,1,\r\n" + "Item,IMP-1,9001,2,\r\n")
        );
        var response = await client.PostAsync("/odata/ImportArchive", Upload(archive));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problems = await ProblemsAsync(response);
        Assert.Contains(
            problems,
            p => p.File == "recipients.csv" && p.Row == 3 && p.Code == "DuplicateNumber"
        );
        Assert.Contains(
            problems,
            p => p.File == "items.csv" && p.Row == 3 && p.Code == "DuplicateNumber"
        );
        Assert.Contains(
            problems,
            p => p.File == "graves.csv" && p.Row == 3 && p.Code == "DuplicateNumber"
        );
        Assert.Contains(
            problems,
            p => p.File == "mapping.csv" && p.Row == 3 && p.Code == "DuplicateLink"
        );
        await AssertNothingImportedAsync(host);
    }

    [Fact]
    public async Task Identifiers_that_already_exist_are_rejected_rather_than_updated()
    {
        using var host = new ApiTestHost();
        await ConfigureVatAsync(host, 7m);
        using var client = host.CreateClient();

        // the same archive twice: the second run collides with everything
        Assert.Equal(
            HttpStatusCode.OK,
            (await client.PostAsync("/odata/ImportArchive", Upload(ValidArchive()))).StatusCode
        );
        var response = await client.PostAsync("/odata/ImportArchive", Upload(ValidArchive()));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problems = await ProblemsAsync(response);
        Assert.Contains(problems, p => p.File == "recipients.csv" && p.Code == "AlreadyExists");
        Assert.Contains(problems, p => p.File == "items.csv" && p.Code == "AlreadyExists");
        Assert.Contains(problems, p => p.File == "graves.csv" && p.Code == "AlreadyExists");

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Single(await db.Graves.ToListAsync());
        Assert.Equal(2, await db.Recipients.CountAsync());
    }

    [Fact]
    public async Task Vat_rate_that_is_not_configured_is_reported()
    {
        using var host = new ApiTestHost();
        await ConfigureVatAsync(host, 7m);
        using var client = host.CreateClient();

        var archive = Zip(
            ("recipients.csv", RecipientsHeader),
            ("items.csv", ItemsHeader + "9001,Grabpflege,19,120,\r\n"),
            ("graves.csv", GravesHeader),
            ("mapping.csv", MappingHeader)
        );
        var response = await client.PostAsync("/odata/ImportArchive", Upload(archive));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problems = await ProblemsAsync(response);
        Assert.Contains(
            problems,
            p => p.File == "items.csv" && p.Row == 2 && p.Code == "VatRateNotConfigured"
        );
    }

    [Fact]
    public async Task Extra_files_in_the_archive_are_ignored()
    {
        using var host = new ApiTestHost();
        await ConfigureVatAsync(host, 7m);
        using var client = host.CreateClient();

        var archive = Zip(
            ("recipients.csv", RecipientsHeader + "E-1000,Anna,Hauptstraße 1\r\n"),
            ("items.csv", ItemsHeader),
            ("graves.csv", GravesHeader),
            ("mapping.csv", MappingHeader),
            ("liesmich.txt", "nur eine Notiz"),
            ("bilder/grab.csv", "irrelevant")
        );
        var response = await client.PostAsync("/odata/ImportArchive", Upload(archive));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        Assert.Equal(
            1,
            json.RootElement.GetProperty("summary").GetProperty("recipients").GetInt32()
        );
    }

    [Fact]
    public async Task A_file_that_is_not_a_zip_is_rejected()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();

        var response = await client.PostAsync(
            "/odata/ImportArchive",
            Upload(Encoding.UTF8.GetBytes("das ist kein ZIP"))
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problems = await ProblemsAsync(response);
        Assert.Contains(problems, p => p.Code == "ArchiveUnreadable");
    }

    [Fact]
    public async Task Upload_without_a_file_is_rejected()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();

        var response = await client.PostAsync(
            "/odata/ImportArchive",
            new MultipartFormDataContent()
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        Assert.Equal(
            "ArchiveRequired",
            json.RootElement.GetProperty("error").GetProperty("code").GetString()
        );
    }

    /// <summary>A grave whose prices do not split evenly across its recipients
    /// imports, with the rounding corrections the dialog would have confirmed
    /// stored next to it.</summary>
    [Fact]
    public async Task A_grave_whose_prices_do_not_divide_is_imported_with_corrections()
    {
        using var host = new ApiTestHost();
        await ConfigureVatAsync(host, 19m);
        using var client = host.CreateClient();

        var response = await client.PostAsync(
            "/odata/ImportArchive",
            Upload(UnsplittableArchive())
        );

        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        using var json = await ODataJson.ReadAsync(response);
        Assert.Equal(
            1,
            json.RootElement.GetProperty("summary").GetProperty("correctedGraves").GetInt32()
        );

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var grave = await db.Graves.SingleAsync();
        var corrections = await db.GraveBillingCorrections.ToListAsync();
        Assert.All(corrections, c => Assert.Equal(grave.Id, c.GraveId));
        Assert.All(corrections, c => Assert.Equal(120.02m, c.CorrectedPrice));
        Assert.Equal(
            new[]
            {
                BillingExampleKind.Start,
                BillingExampleKind.FullFirstHalf,
                BillingExampleKind.FullSecondHalf,
                BillingExampleKind.End,
            }.Order(),
            corrections.Select(c => c.Kind).Order()
        );
    }

    /// <summary>The imported corrections are what the precheck of a half-year run
    /// compares against, so the run is accepted.</summary>
    [Fact]
    public async Task Generation_accepts_the_corrections_an_import_stored()
    {
        using var host = new ApiTestHost();
        await ConfigureVatAsync(host, 19m);
        using var client = host.CreateClient();
        var import = await client.PostAsync("/odata/ImportArchive", Upload(UnsplittableArchive()));
        Assert.True(import.IsSuccessStatusCode, await import.Content.ReadAsStringAsync());

        var response = await client.PostAsync(
            "/odata/GenerateGraveInvoices",
            ODataJson.Body(new { year = 2026, half = 1 })
        );

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    [Fact]
    public async Task An_items_quarter_is_imported()
    {
        using var host = new ApiTestHost();
        await ConfigureVatAsync(host, 19m);
        using var client = host.CreateClient();
        var archive = Zip(
            ("recipients.csv", RecipientsHeader),
            (
                "items.csv",
                ItemsHeader + "9001,Sommerbepflanzung,19,40,Summer\r\n9002,Grabpflege,19,60,\r\n"
            ),
            ("graves.csv", GravesHeader),
            ("mapping.csv", MappingHeader)
        );

        var response = await client.PostAsync("/odata/ImportArchive", Upload(archive));

        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(
            BillingQuarter.Summer,
            (await db.InvoiceItems.SingleAsync(i => i.Number == "9001")).Quarter
        );
        Assert.Null((await db.InvoiceItems.SingleAsync(i => i.Number == "9002")).Quarter);
    }

    [Fact]
    public async Task An_unknown_quarter_is_refused()
    {
        using var host = new ApiTestHost();
        await ConfigureVatAsync(host, 19m);
        using var client = host.CreateClient();
        var archive = Zip(
            ("recipients.csv", RecipientsHeader),
            ("items.csv", ItemsHeader + "9001,Sommerbepflanzung,19,40,Sommer\r\n"),
            ("graves.csv", GravesHeader),
            ("mapping.csv", MappingHeader)
        );

        var response = await client.PostAsync("/odata/ImportArchive", Upload(archive));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(
            await ProblemsAsync(response),
            p => p.File == "items.csv" && p.Row == 2 && p.Code == "QuarterInvalid"
        );
    }

    [Fact]
    public async Task A_grave_whose_only_linked_item_has_a_quarter_is_refused()
    {
        using var host = new ApiTestHost();
        await ConfigureVatAsync(host, 19m);
        using var client = host.CreateClient();
        var archive = Zip(
            ("recipients.csv", RecipientsHeader + "E-1,Name 1,Weg 1\r\n"),
            ("items.csv", ItemsHeader + "9001,Sommerbepflanzung,19,40,Summer\r\n"),
            (
                "graves.csv",
                GravesHeader + "IMP-6,27,Grabstätte,Bergfriedhof,DoubleOutside,2026-01,\r\n"
            ),
            ("mapping.csv", MappingHeader + "Item,IMP-6,9001,1,\r\n" + "Recipient,IMP-6,E-1,,\r\n")
        );

        var response = await client.PostAsync("/odata/ImportArchive", Upload(archive));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(
            await ProblemsAsync(response),
            p => p.File == "graves.csv" && p.Row == 2 && p.Code == "AllYearItemRequired"
        );
    }

    [Fact]
    public async Task A_grave_with_five_recipients_is_refused()
    {
        using var host = new ApiTestHost();
        await ConfigureVatAsync(host, 19m);
        using var client = host.CreateClient();
        var recipients = string.Concat(
            Enumerable.Range(0, 5).Select(i => $"E-{i},Name {i},Weg {i}\r\n")
        );
        var links = string.Concat(
            Enumerable.Range(0, 5).Select(i => $"Recipient,IMP-5,E-{i},,\r\n")
        );
        var archive = Zip(
            ("recipients.csv", RecipientsHeader + recipients),
            ("items.csv", ItemsHeader + "9001,Grabpflege,19,120.00,\r\n"),
            (
                "graves.csv",
                GravesHeader + "IMP-5,27,Grabstätte,Bergfriedhof,DoubleOutside,2026-01,\r\n"
            ),
            ("mapping.csv", MappingHeader + "Item,IMP-5,9001,1,\r\n" + links)
        );

        var response = await client.PostAsync("/odata/ImportArchive", Upload(archive));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(
            await ProblemsAsync(response),
            p => p.File == "graves.csv" && p.Row == 2 && p.Code == "TooManyRecipients"
        );
    }

    /// <summary>An item may come without a price (FR-051), but a grave that links
    /// it cannot be billed: the dialog refuses to save one, and generation would
    /// skip it. The import refuses the link, on the mapping row, so the item
    /// itself stays importable.</summary>
    [Fact]
    public async Task A_link_to_an_item_without_a_price_is_refused()
    {
        using var host = new ApiTestHost();
        await ConfigureVatAsync(host, 7m);
        using var client = host.CreateClient();

        var archive = Zip(
            ("recipients.csv", RecipientsHeader + "E-1000,Anna Beispiel,Hauptstraße 1\r\n"),
            ("items.csv", ItemsHeader + "9002,Sonderleistung,7,,\r\n"),
            (
                "graves.csv",
                GravesHeader
                    + "IMP-1,27,Grabstätte Import,Bergfriedhof,DoubleOutside,2026-01-01,2040-12-31\r\n"
            ),
            (
                "mapping.csv",
                MappingHeader + "Item,IMP-1,9002,1,\r\n" + "Recipient,IMP-1,E-1000,,\r\n"
            )
        );
        var response = await client.PostAsync("/odata/ImportArchive", Upload(archive));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problems = await ProblemsAsync(response);
        Assert.Contains(
            problems,
            p => p.File == "mapping.csv" && p.Row == 2 && p.Code == "PriceRequired"
        );
        await AssertNothingImportedAsync(host);
    }

    /// <summary>Prices are cents. A third decimal would be judged rounded by the
    /// split check and billed unrounded by generation, so it is refused where it
    /// comes in, as the dialogs refuse it.</summary>
    [Fact]
    public async Task A_price_with_more_than_two_decimals_is_refused()
    {
        using var host = new ApiTestHost();
        await ConfigureVatAsync(host, 7m);
        using var client = host.CreateClient();

        var archive = Zip(
            ("recipients.csv", RecipientsHeader + "E-1000,Anna Beispiel,Hauptstraße 1\r\n"),
            ("items.csv", ItemsHeader + "9001,Grabpflege,7,120.004,\r\n"),
            (
                "graves.csv",
                GravesHeader
                    + "IMP-1,27,Grabstätte Import,Bergfriedhof,DoubleOutside,2026-01-01,2040-12-31\r\n"
            ),
            (
                "mapping.csv",
                MappingHeader + "Item,IMP-1,9001,3,\r\n" + "Recipient,IMP-1,E-1000,,\r\n"
            )
        );
        var response = await client.PostAsync("/odata/ImportArchive", Upload(archive));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problems = await ProblemsAsync(response);
        Assert.Contains(
            problems,
            p => p.File == "items.csv" && p.Row == 2 && p.Code == "PriceInvalid"
        );
        await AssertNothingImportedAsync(host);
    }

    /// <summary>Excel in German saves CSV with semicolons, "CSV UTF-8" included,
    /// and a German decimal comma then needs no quotes. A file whose header row
    /// is the expected one, separated by semicolons, is read that way.</summary>
    [Fact]
    public async Task A_semicolon_separated_archive_imports_like_a_comma_separated_one()
    {
        using var host = new ApiTestHost();
        await ConfigureVatAsync(host, 7m);
        using var client = host.CreateClient();

        static string Semicolons(string header) => header.Replace(',', ';');
        var archive = Zip(
            (
                "recipients.csv",
                Semicolons(RecipientsHeader)
                    + "E-1000;Anna Beispiel;\"Hauptstraße 1; Hinterhaus\"\r\n"
            ),
            ("items.csv", Semicolons(ItemsHeader) + "9001;Grabpflege;7;120,50;\r\n"),
            (
                "graves.csv",
                Semicolons(GravesHeader)
                    + "IMP-1;27;Grabstätte Import;Bergfriedhof;DoubleOutside;2026-01;\r\n"
            ),
            (
                "mapping.csv",
                Semicolons(MappingHeader)
                    + "Item;IMP-1;9001;2;\r\n"
                    + "Recipient;IMP-1;E-1000;;\r\n"
            )
        );
        var response = await client.PostAsync("/odata/ImportArchive", Upload(archive));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(120.50m, (await db.InvoiceItems.SingleAsync()).DefaultPrice);
        Assert.Equal("Hauptstraße 1; Hinterhaus", (await db.Recipients.SingleAsync()).Address);
        Assert.Equal(2, (await db.GraveItems.SingleAsync()).Count);
    }

    /// <summary>A spreadsheet leaves an empty row as ",,,," and a text editor as
    /// an empty line. Neither is data, but both are rows the user counts, so
    /// the problems after one still name the row the user sees.</summary>
    [Fact]
    public async Task A_problem_after_a_blank_row_names_the_row_the_user_sees()
    {
        using var host = new ApiTestHost();
        await ConfigureVatAsync(host, 7m);
        using var client = host.CreateClient();

        var archive = Zip(
            ("recipients.csv", RecipientsHeader),
            (
                "items.csv",
                ItemsHeader
                    + "9001,Grabpflege,7,120,\r\n"
                    + ",,,,\r\n"
                    + "\r\n"
                    + "9002,Gießdienst,abc,10,\r\n"
            ),
            ("graves.csv", GravesHeader),
            ("mapping.csv", MappingHeader)
        );
        var response = await client.PostAsync("/odata/ImportArchive", Upload(archive));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = Assert.Single(await ProblemsAsync(response));
        Assert.Equal(("items.csv", 5, "VatRateInvalid"), (problem.File, problem.Row, problem.Code));
    }

    /// <summary>Excel's plain "CSV" on Windows saves in Windows-1252. Read as
    /// UTF-8, every umlaut became U+FFFD and was imported -- and printed on
    /// invoices -- as such, with nothing reported.</summary>
    [Fact]
    public async Task A_file_that_is_not_utf8_is_refused_by_name()
    {
        using var host = new ApiTestHost();
        await ConfigureVatAsync(host, 7m);
        using var client = host.CreateClient();

        // "Müller" in Windows-1252: the ü is the single byte 0xFC
        var recipients = Utf8(RecipientsHeader + "E-1000,M")
            .Concat(new byte[] { 0xFC })
            .Concat(Utf8("ller,Weg 1\r\n"))
            .ToArray();
        var archive = ZipBytes(
            ("recipients.csv", recipients),
            ("items.csv", Utf8(ItemsHeader)),
            ("graves.csv", Utf8(GravesHeader)),
            ("mapping.csv", Utf8(MappingHeader))
        );
        var response = await client.PostAsync("/odata/ImportArchive", Upload(archive));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = Assert.Single(await ProblemsAsync(response));
        Assert.Equal(("recipients.csv", "EncodingInvalid"), (problem.File, problem.Code));
        await AssertNothingImportedAsync(host);
    }

    /// <summary>The upload is capped, but a compressed entry is not: a few
    /// megabytes of archive can unpack to far more than the app can hold.</summary>
    [Fact]
    public async Task A_file_too_large_to_be_a_template_is_refused_by_name()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();

        var archive = Zip(
            ("recipients.csv", RecipientsHeader + new string('a', 17 * 1024 * 1024)),
            ("items.csv", ItemsHeader),
            ("graves.csv", GravesHeader),
            ("mapping.csv", MappingHeader)
        );
        var response = await client.PostAsync("/odata/ImportArchive", Upload(archive));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = Assert.Single(await ProblemsAsync(response));
        Assert.Equal(("recipients.csv", "FileTooLarge"), (problem.File, problem.Code));
    }

    /// <summary>Templates are filled in by hand, and a German hand writes
    /// "120,50" -- quoted, since the comma is also the file's separator. Read
    /// with the invariant culture's thousands separator allowed, that was
    /// 12 050: a price a hundred times too high that passed every later check
    /// and was billed.</summary>
    [Theory]
    [InlineData("120.50", "120.50")]
    [InlineData("\"120,50\"", "120.50")]
    [InlineData("\"12,5\"", "12.5")]
    [InlineData("120", "120")]
    public async Task A_price_is_read_at_its_value_in_either_spelling(string raw, string expected)
    {
        using var host = new ApiTestHost();
        await ConfigureVatAsync(host, 7m);
        using var client = host.CreateClient();

        var archive = Zip(
            ("recipients.csv", RecipientsHeader),
            ("items.csv", ItemsHeader + $"9001,Grabpflege,7,{raw},\r\n"),
            ("graves.csv", GravesHeader),
            ("mapping.csv", MappingHeader)
        );
        var response = await client.PostAsync("/odata/ImportArchive", Upload(archive));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var item = await db.InvoiceItems.SingleAsync();
        Assert.Equal(decimal.Parse(expected, CultureInfo.InvariantCulture), item.DefaultPrice);
    }

    /// <summary>A thousands separator is ambiguous between the two spellings
    /// ("1.234,50" and "1,234.50" are the same amount), so neither is read.</summary>
    [Theory]
    [InlineData("\"1.234,50\"")]
    [InlineData("\"1,234.50\"")]
    [InlineData("\"1,234\"")]
    public async Task A_price_with_a_thousands_separator_is_refused(string raw)
    {
        using var host = new ApiTestHost();
        await ConfigureVatAsync(host, 7m);
        using var client = host.CreateClient();

        var archive = Zip(
            ("recipients.csv", RecipientsHeader),
            ("items.csv", ItemsHeader + $"9001,Grabpflege,7,{raw},\r\n"),
            ("graves.csv", GravesHeader),
            ("mapping.csv", MappingHeader)
        );
        var response = await client.PostAsync("/odata/ImportArchive", Upload(archive));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problems = await ProblemsAsync(response);
        Assert.Contains(
            problems,
            p => p.File == "items.csv" && p.Row == 2 && p.Code == "PriceInvalid"
        );
        await AssertNothingImportedAsync(host);
    }

    /// <summary>The code alone does not say which sentence it was -- a duplicate
    /// number may be a recipient's, an item's or a grave's -- and the values sit
    /// inside the English message. Key and arguments let a frontend say it in
    /// German.</summary>
    [Fact]
    public async Task A_problem_carries_a_message_key_and_its_values()
    {
        using var host = new ApiTestHost();
        await ConfigureVatAsync(host, 19m);
        using var client = host.CreateClient();

        var archive = Zip(
            ("recipients.csv", RecipientsHeader),
            ("items.csv", ItemsHeader),
            (
                "graves.csv",
                GravesHeader
                    + "IMP-1,27,Grabstätte Import,Bergfriedhof,DoubleOutside,2026-01-01,2040-12-31\r\n"
                    + "IMP-1,27,Grabstätte Import,Bergfriedhof,DoubleOutside,2026-01-01,2040-12-31\r\n"
            ),
            ("mapping.csv", MappingHeader)
        );
        var response = await client.PostAsync("/odata/ImportArchive", Upload(archive));
        using var duplicate = await ODataJson.ReadAsync(response);
        var duplicateProblem = duplicate
            .RootElement.GetProperty("problems")
            .EnumerateArray()
            .Single(p => p.GetProperty("code").GetString() == "DuplicateNumber");
        Assert.Equal(
            "DuplicateGraveNumber",
            duplicateProblem.GetProperty("messageKey").GetString()
        );
        Assert.Equal(
            new[] { "IMP-1", "2" },
            duplicateProblem
                .GetProperty("messageArgs")
                .EnumerateArray()
                .Select(a => a.GetString())
                .ToArray()
        );
    }

    [Fact]
    public async Task Graves_import_with_site_fields_month_terms_and_an_open_end()
    {
        using var host = new ApiTestHost();
        await ConfigureVatAsync(host, 7m);
        using var client = host.CreateClient();

        var archive = Zip(
            ("recipients.csv", RecipientsHeader + "E-1000,Anna Beispiel,Hauptstraße 1\r\n"),
            ("items.csv", ItemsHeader + "9001,Grabpflege,7,120.00,\r\n"),
            (
                "graves.csv",
                GravesHeader + "IMP-1,27,Schineller Bäcke,Bergfriedhof,DoubleOutside,2026-03,\r\n"
            ),
            (
                "mapping.csv",
                MappingHeader + "Item,IMP-1,9001,1,\r\n" + "Recipient,IMP-1,E-1000,,\r\n"
            )
        );
        var response = await client.PostAsync("/odata/ImportArchive", Upload(archive));
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var grave = await db.Graves.SingleAsync();
        Assert.Equal("27", grave.FieldNumber);
        Assert.Equal("Schineller Bäcke", grave.SiteName);
        Assert.Equal(GravePosition.DoubleOutside, grave.Position);
        Assert.Equal(new DateOnly(2026, 3, 1), grave.StartDate);
        Assert.Null(grave.EndDate);
    }

    [Theory]
    [InlineData("IMP-1,27,Grabstätte,Bergfriedhof,Sideways,2026-01,", "PositionInvalid")]
    [InlineData("IMP-1,,Grabstätte,Bergfriedhof,DoubleOutside,2026-01,", "FieldRequired")]
    [InlineData("IMP-1,27,,Bergfriedhof,DoubleOutside,2026-01,", "FieldRequired")]
    [InlineData("IMP-1,27,Grabstätte,Bergfriedhof,,2026-01,", "FieldRequired")]
    [InlineData("IMP-1,27,Grabstätte,,DoubleOutside,2026-01,", "FieldRequired")]
    [InlineData("IMP-1,27,Grabstätte,Bergfriedhof,DoubleOutside,2026-13,", "DateInvalid")]
    [InlineData(
        "IMP-1,27,Grabstätte,Bergfriedhof,DoubleOutside,2026-05,2026-04",
        "DateRangeInvalid"
    )]
    public async Task Invalid_grave_rows_are_reported(string row, string code)
    {
        using var host = new ApiTestHost();
        await ConfigureVatAsync(host, 7m);
        using var client = host.CreateClient();

        var archive = Zip(
            ("recipients.csv", RecipientsHeader),
            ("items.csv", ItemsHeader),
            ("graves.csv", GravesHeader + row + "\r\n"),
            ("mapping.csv", MappingHeader)
        );
        var response = await client.PostAsync("/odata/ImportArchive", Upload(archive));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problems = await ProblemsAsync(response);
        Assert.Contains(problems, p => p.File == "graves.csv" && p.Row == 2 && p.Code == code);
    }

    [Fact]
    public async Task An_archive_with_the_graves_file_from_before_cemeteries_is_refused()
    {
        // the cemetery is required, so it cannot be an optional trailing column
        using var host = new ApiTestHost();
        await ConfigureVatAsync(host, 7m);
        using var client = host.CreateClient();

        var archive = Zip(
            ("recipients.csv", RecipientsHeader),
            ("items.csv", ItemsHeader),
            (
                "graves.csv",
                "GraveNumber,FieldNumber,SiteName,Position,StartDate,EndDate\r\n"
                    + "IMP-1,27,Grabstätte,DoubleOutside,2026-01,\r\n"
            ),
            ("mapping.csv", MappingHeader)
        );
        var response = await client.PostAsync("/odata/ImportArchive", Upload(archive));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problems = await ProblemsAsync(response);
        var problem = Assert.Single(problems);
        Assert.Equal(("graves.csv", 1, "HeaderInvalid"), (problem.File, problem.Row, problem.Code));
        Assert.Contains("SiteName, Cemetery, Position", problem.Message);
    }

    /// <summary>One grave in <paramref name="cemetery"/>, billable as it is.</summary>
    private static byte[] ArchiveInCemetery(string cemetery) =>
        Zip(
            ("recipients.csv", RecipientsHeader + "E-1000,Anna Beispiel,Weg 1\r\n"),
            ("items.csv", ItemsHeader + "9001,Grabpflege,7,120.00,\r\n"),
            (
                "graves.csv",
                GravesHeader + $"IMP-1,27,Grabstätte,{cemetery},DoubleOutside,2026-01,\r\n"
            ),
            (
                "mapping.csv",
                MappingHeader + "Item,IMP-1,9001,1,\r\n" + "Recipient,IMP-1,E-1000,,\r\n"
            )
        );

    [Fact]
    public async Task A_grave_lies_in_the_cemetery_it_names_whatever_the_case()
    {
        using var host = new ApiTestHost();
        await ConfigureVatAsync(host, 7m);
        using var client = host.CreateClient();
        long südfriedhof;
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var cemetery = new Cemetery { Name = "Südfriedhof" };
            db.Cemeteries.Add(cemetery);
            await db.SaveChangesAsync();
            südfriedhof = cemetery.Id;
        }

        // the umlaut differs in case too, which SQLite's NOCASE would not fold
        var response = await client.PostAsync(
            "/odata/ImportArchive",
            Upload(ArchiveInCemetery(" SÜDFRIEDHOF "))
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(südfriedhof, (await db.Graves.SingleAsync()).CemeteryId);
            // linked, not created
            Assert.Equal(2, await db.Cemeteries.CountAsync());
        }
    }

    [Fact]
    public async Task A_cemetery_that_is_not_in_the_settings_is_reported_and_not_created()
    {
        using var host = new ApiTestHost();
        await ConfigureVatAsync(host, 7m);
        using var client = host.CreateClient();

        var response = await client.PostAsync(
            "/odata/ImportArchive",
            Upload(ArchiveInCemetery("Waldfriedhof"))
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        var problem = Assert.Single(json.RootElement.GetProperty("problems").EnumerateArray());
        Assert.Equal("graves.csv", problem.GetProperty("file").GetString());
        Assert.Equal(2, problem.GetProperty("row").GetInt32());
        Assert.Equal("CemeteryNotDefined", problem.GetProperty("code").GetString());
        Assert.Equal("CemeteryNotDefined", problem.GetProperty("messageKey").GetString());
        Assert.Equal(
            new[] { "Waldfriedhof" },
            problem.GetProperty("messageArgs").EnumerateArray().Select(a => a.GetString())
        );
        await AssertNothingImportedAsync(host);
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(
            new[] { TestCemetery.Name },
            await db.Cemeteries.Select(c => c.Name).ToListAsync()
        );
    }

    [Fact]
    public async Task An_archive_with_the_old_three_column_graves_file_is_refused()
    {
        using var host = new ApiTestHost();
        await ConfigureVatAsync(host, 7m);
        using var client = host.CreateClient();

        var archive = Zip(
            ("recipients.csv", RecipientsHeader),
            ("items.csv", ItemsHeader),
            ("graves.csv", "GraveNumber,StartDate,EndDate\r\nIMP-1,2026-01-01,2040-12-31\r\n"),
            ("mapping.csv", MappingHeader)
        );
        var response = await client.PostAsync("/odata/ImportArchive", Upload(archive));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problems = await ProblemsAsync(response);
        Assert.Contains(problems, p => p.File == "graves.csv" && p.Code == "HeaderInvalid");
    }

    /// <summary>One grave with a recipient and an item at 7 %; the mapping's
    /// item row is <paramref name="itemLink"/>, under the full header.</summary>
    private static byte[] ArchiveWithItemLink(string items, string itemLink, int recipients = 1) =>
        Zip(
            (
                "recipients.csv",
                FullRecipientsHeader
                    + string.Concat(
                        Enumerable
                            .Range(0, recipients)
                            .Select(i => $"E-100{i},Empfänger {i},Weg {i},\r\n")
                    )
            ),
            ("items.csv", ItemsHeader + items),
            (
                "graves.csv",
                GravesHeader
                    + "IMP-1,27,Grabstätte Import,Bergfriedhof,DoubleOutside,2026-01,2040-12\r\n"
            ),
            (
                "mapping.csv",
                FullMappingHeader
                    + itemLink
                    + string.Concat(
                        Enumerable
                            .Range(0, recipients)
                            .Select(i => $"Recipient,IMP-1,E-100{i},,,\r\n")
                    )
            )
        );

    /// <summary>One grave under the given graves header, an unpriced all-year
    /// item linked to it and <paramref name="recipients"/> recipient links.</summary>
    private static byte[] CooperativeArchive(string header, string graveRow, int recipients = 0) =>
        Zip(
            (
                "recipients.csv",
                RecipientsHeader
                    + string.Concat(
                        Enumerable
                            .Range(0, recipients)
                            .Select(i => $"E-100{i},Empfänger {i},Weg {i}\r\n")
                    )
            ),
            ("items.csv", ItemsHeader + "9001,Grabpflege,19,,\r\n"),
            ("graves.csv", header + graveRow),
            (
                "mapping.csv",
                MappingHeader
                    + "Item,IMP-1,9001,1,\r\n"
                    + string.Concat(
                        Enumerable
                            .Range(0, recipients)
                            .Select(i => $"Recipient,IMP-1,E-100{i},,\r\n")
                    )
            )
        );

    private const string CooperativeRow =
        "IMP-1,27,Grabstätte Import,Bergfriedhof,DoubleOutside,2026-01,2040-12,";

    [Fact]
    public async Task A_cooperative_grave_imports_without_recipient_or_price()
    {
        using var host = new ApiTestHost();
        await ConfigureVatAsync(host, 19m);
        using var client = host.CreateClient();

        var response = await client.PostAsync(
            "/odata/ImportArchive",
            Upload(CooperativeArchive(FullGravesHeader, CooperativeRow + "true,2023029\r\n"))
        );

        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.True((await db.Graves.SingleAsync(g => g.GraveNumber == "IMP-1")).IsCooperative);
        Assert.Empty(await db.GraveRecipients.ToListAsync());
        Assert.Empty(await db.GraveBillingCorrections.ToListAsync());
    }

    [Fact]
    public async Task An_archive_without_the_Cooperative_column_imports_ordinary_graves()
    {
        using var host = new ApiTestHost();
        await ConfigureVatAsync(host, 7m, 19m);
        using var client = host.CreateClient();

        var ordinary = await client.PostAsync("/odata/ImportArchive", Upload(ValidArchive()));
        Assert.True(ordinary.IsSuccessStatusCode, await ordinary.Content.ReadAsStringAsync());
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.False((await db.Graves.SingleAsync()).IsCooperative);
        }
    }

    private async Task<(ApiTestHost Host, HttpResponseMessage Response)> ImportCooperativeAsync(
        string header,
        string graveRow
    )
    {
        var host = new ApiTestHost();
        await ConfigureVatAsync(host, 19m);
        using var client = host.CreateClient();
        var response = await client.PostAsync(
            "/odata/ImportArchive",
            Upload(CooperativeArchive(header, graveRow))
        );
        return (host, response);
    }

    [Fact]
    public async Task A_cooperative_row_imports_its_contract_number()
    {
        var (host, response) = await ImportCooperativeAsync(
            FullGravesHeader,
            CooperativeRow + "true,2023029\r\n"
        );
        using var _ = host;

        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal("2023029", (await db.Graves.SingleAsync()).ContractNumber);
    }

    [Fact]
    public async Task A_cooperative_row_without_contract_number_is_a_problem()
    {
        var (host, response) = await ImportCooperativeAsync(
            FullGravesHeader,
            CooperativeRow + "true,\r\n"
        );
        using var _ = host;

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        var problem = json
            .RootElement.GetProperty("problems")
            .EnumerateArray()
            .Single(p => p.GetProperty("code").GetString() == "ContractNumberRequired");
        Assert.Equal("graves.csv", problem.GetProperty("file").GetString());
        Assert.Equal(2, problem.GetProperty("row").GetInt32());
        Assert.Equal("ContractNumberRequired", problem.GetProperty("messageKey").GetString());
        Assert.Equal("IMP-1", problem.GetProperty("messageArgs")[0].GetString());
        await AssertNothingImportedAsync(host);
    }

    [Fact]
    public async Task An_eight_column_archive_still_reads()
    {
        using var host = new ApiTestHost();
        await ConfigureVatAsync(host, 19m);
        using (var client = host.CreateClient())
        {
            var archive = Zip(
                ("recipients.csv", RecipientsHeader),
                ("items.csv", ItemsHeader + "9001,Grabpflege,19,120.00,\r\n"),
                ("graves.csv", EightColumnGravesHeader + CooperativeRow + "false\r\n"),
                ("mapping.csv", MappingHeader + "Item,IMP-1,9001,1,\r\n")
            );
            var response = await client.PostAsync("/odata/ImportArchive", Upload(archive));
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            using var scope = host.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var grave = await db.Graves.SingleAsync();
            Assert.False(grave.IsCooperative);
            Assert.Null(grave.ContractNumber);
        }

        var (other, refused) = await ImportCooperativeAsync(
            EightColumnGravesHeader,
            CooperativeRow + "true\r\n"
        );
        using (other)
        {
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
            Assert.Contains(
                await ProblemsAsync(refused),
                p => p.File == "graves.csv" && p.Row == 2 && p.Code == "ContractNumberRequired"
            );
        }
    }

    private static byte[] OrdinaryArchive(string items, string mapping) =>
        Zip(
            ("recipients.csv", RecipientsHeader),
            ("items.csv", ItemsHeader + items),
            (
                "graves.csv",
                GravesHeader
                    + "IMP-1,27,Grabstätte Import,Bergfriedhof,DoubleOutside,2026-01,2040-12\r\n"
            ),
            ("mapping.csv", MappingHeader + mapping)
        );

    [Fact]
    public async Task An_ordinary_grave_without_recipient_imports()
    {
        using var host = new ApiTestHost();
        await ConfigureVatAsync(host, 19m);
        using var client = host.CreateClient();

        var response = await client.PostAsync(
            "/odata/ImportArchive",
            Upload(OrdinaryArchive("9001,Grabpflege,19,120.00,\r\n", "Item,IMP-1,9001,1,\r\n"))
        );

        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Single(await db.Graves.ToListAsync());
        Assert.Single(await db.GraveItems.ToListAsync());
        Assert.Empty(await db.GraveRecipients.ToListAsync());
    }

    [Fact]
    public async Task An_ordinary_grave_without_items_imports()
    {
        using var host = new ApiTestHost();
        await ConfigureVatAsync(host, 19m);
        using var client = host.CreateClient();

        var response = await client.PostAsync(
            "/odata/ImportArchive",
            Upload(OrdinaryArchive("", ""))
        );

        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Single(await db.Graves.ToListAsync());
        Assert.Empty(await db.GraveItems.ToListAsync());
        Assert.Empty(await db.GraveRecipients.ToListAsync());
    }

    [Fact]
    public async Task Item_links_without_a_recipient_are_still_checked()
    {
        using var host = new ApiTestHost();
        await ConfigureVatAsync(host, 19m);
        using var client = host.CreateClient();

        var response = await client.PostAsync(
            "/odata/ImportArchive",
            Upload(OrdinaryArchive("9001,Grabpflege,19,,\r\n", "Item,IMP-1,9001,1,\r\n"))
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(
            await ProblemsAsync(response),
            p => p.File == "mapping.csv" && p.Code == "PriceRequired"
        );
        await AssertNothingImportedAsync(host);
    }

    [Fact]
    public async Task A_Cooperative_value_that_is_no_boolean_is_a_problem()
    {
        using var host = new ApiTestHost();
        await ConfigureVatAsync(host, 19m);
        using var client = host.CreateClient();

        var response = await client.PostAsync(
            "/odata/ImportArchive",
            Upload(CooperativeArchive(FullGravesHeader, CooperativeRow + "x,2023029\r\n"))
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        var problem = json
            .RootElement.GetProperty("problems")
            .EnumerateArray()
            .Single(p => p.GetProperty("code").GetString() == "CooperativeInvalid");
        Assert.Equal("graves.csv", problem.GetProperty("file").GetString());
        Assert.Equal(2, problem.GetProperty("row").GetInt32());
        Assert.Equal("CooperativeInvalid", problem.GetProperty("messageKey").GetString());
        Assert.Equal("x", problem.GetProperty("messageArgs")[0].GetString());
        await AssertNothingImportedAsync(host);
    }

    [Fact]
    public async Task A_cooperative_grave_keeps_the_recipient_cap()
    {
        using var host = new ApiTestHost();
        await ConfigureVatAsync(host, 19m);
        using var client = host.CreateClient();

        var response = await client.PostAsync(
            "/odata/ImportArchive",
            Upload(CooperativeArchive(FullGravesHeader, CooperativeRow + "true,2023029\r\n", 5))
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(
            await ProblemsAsync(response),
            p => p.File == "graves.csv" && p.Row == 2 && p.Code == "TooManyRecipients"
        );
    }

    [Fact]
    public async Task A_recipients_email_address_is_imported_and_a_blank_one_is_none()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();

        var archive = Zip(
            (
                "recipients.csv",
                FullRecipientsHeader
                    + "E-1000,Anna Beispiel,Hauptstraße 1, anna@example.de \r\n"
                    + "E-1001,Bert Beispiel,Nebenweg 2,\r\n"
            ),
            ("items.csv", ItemsHeader),
            ("graves.csv", GravesHeader),
            ("mapping.csv", FullMappingHeader)
        );
        var response = await client.PostAsync("/odata/ImportArchive", Upload(archive));
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(
            "anna@example.de",
            (await db.Recipients.SingleAsync(r => r.Number == "E-1000")).Email
        );
        Assert.Null((await db.Recipients.SingleAsync(r => r.Number == "E-1001")).Email);
    }

    [Fact]
    public async Task A_malformed_email_address_is_reported_with_its_row()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();

        var archive = Zip(
            (
                "recipients.csv",
                FullRecipientsHeader
                    + "E-1000,Anna Beispiel,Hauptstraße 1,anna@example.de\r\n"
                    + "E-1001,Bert Beispiel,Nebenweg 2,bert at example.de\r\n"
            ),
            ("items.csv", ItemsHeader),
            ("graves.csv", GravesHeader),
            ("mapping.csv", FullMappingHeader)
        );
        var response = await client.PostAsync("/odata/ImportArchive", Upload(archive));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        var problem = Assert.Single(json.RootElement.GetProperty("problems").EnumerateArray());
        Assert.Equal("recipients.csv", problem.GetProperty("file").GetString());
        Assert.Equal(3, problem.GetProperty("row").GetInt32());
        Assert.Equal("EmailInvalid", problem.GetProperty("code").GetString());
        Assert.Equal("EmailInvalid", problem.GetProperty("messageKey").GetString());
        Assert.Equal("bert at example.de", problem.GetProperty("messageArgs")[0].GetString());
        await AssertNothingImportedAsync(host);
    }

    /// <summary>The header decides how many columns a row has: under the full
    /// header a row without the Email cell is short.</summary>
    [Fact]
    public async Task A_row_without_the_optional_column_under_the_full_header_is_reported()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();

        var archive = Zip(
            ("recipients.csv", FullRecipientsHeader + "E-1000,Anna Beispiel,Hauptstraße 1\r\n"),
            ("items.csv", ItemsHeader),
            ("graves.csv", GravesHeader),
            ("mapping.csv", FullMappingHeader)
        );
        var response = await client.PostAsync("/odata/ImportArchive", Upload(archive));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problems = await ProblemsAsync(response);
        Assert.Contains(
            problems,
            p => p.File == "recipients.csv" && p.Row == 2 && p.Code == "ColumnCount"
        );
    }

    [Fact]
    public async Task An_item_links_unit_price_is_stored_and_a_blank_one_bills_the_items_price()
    {
        using var host = new ApiTestHost();
        await ConfigureVatAsync(host, 7m);
        using var client = host.CreateClient();

        var archive = ArchiveWithItemLink(
            "9001,Grabpflege,7,120.00,\r\n9002,Gießen,7,30.00,\r\n",
            "Item,IMP-1,9001,1,,\"80,50\"\r\nItem,IMP-1,9002,1,,\r\n"
        );
        var response = await client.PostAsync("/odata/ImportArchive", Upload(archive));
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var links = await db.GraveItems.Include(l => l.Item).ToListAsync();
        Assert.Equal(80.50m, links.Single(l => l.Item.Number == "9001").UnitPrice);
        Assert.Null(links.Single(l => l.Item.Number == "9002").UnitPrice);
    }

    /// <summary>An item without a price of its own is fine for a grave that
    /// brings its own; another grave linking it without one is refused.</summary>
    [Fact]
    public async Task An_item_without_a_price_needs_one_on_every_link()
    {
        using var host = new ApiTestHost();
        await ConfigureVatAsync(host, 7m);
        using var client = host.CreateClient();

        var priced = ArchiveWithItemLink("9001,Grabpflege,7,,\r\n", "Item,IMP-1,9001,1,,95.00\r\n");
        var response = await client.PostAsync("/odata/ImportArchive", Upload(priced));
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Null((await db.InvoiceItems.SingleAsync()).DefaultPrice);
            Assert.Equal(95.00m, (await db.GraveItems.SingleAsync()).UnitPrice);
        }

        using var other = new ApiTestHost();
        await ConfigureVatAsync(other, 7m);
        using var otherClient = other.CreateClient();
        var archive = Zip(
            ("recipients.csv", FullRecipientsHeader + "E-1000,Anna Beispiel,Weg 1,\r\n"),
            ("items.csv", ItemsHeader + "9001,Grabpflege,7,,\r\n"),
            (
                "graves.csv",
                GravesHeader
                    + "IMP-1,27,Grabstätte,Bergfriedhof,DoubleOutside,2026-01,\r\n"
                    + "IMP-2,28,Grabstätte,Bergfriedhof,DoubleOutside,2026-01,\r\n"
            ),
            (
                "mapping.csv",
                FullMappingHeader
                    + "Item,IMP-1,9001,1,,95.00\r\n"
                    + "Item,IMP-2,9001,1,,\r\n"
                    + "Recipient,IMP-1,E-1000,,,\r\n"
                    + "Recipient,IMP-2,E-1000,,,\r\n"
            )
        );
        var refused = await otherClient.PostAsync("/odata/ImportArchive", Upload(archive));

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        var problem = Assert.Single(await ProblemsAsync(refused));
        Assert.Equal(
            ("mapping.csv", (int?)3, "PriceRequired"),
            (problem.File, problem.Row, problem.Code)
        );
    }

    [Theory]
    [InlineData("-1", "PriceInvalid")]
    [InlineData("zehn", "PriceInvalid")]
    [InlineData("80.505", "PriceInvalid")]
    public async Task An_invalid_unit_price_is_reported(string raw, string code)
    {
        using var host = new ApiTestHost();
        await ConfigureVatAsync(host, 7m);
        using var client = host.CreateClient();

        var archive = ArchiveWithItemLink(
            "9001,Grabpflege,7,120.00,\r\n",
            $"Item,IMP-1,9001,1,,{raw}\r\n"
        );
        var response = await client.PostAsync("/odata/ImportArchive", Upload(archive));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problems = await ProblemsAsync(response);
        Assert.Contains(problems, p => p.File == "mapping.csv" && p.Row == 2 && p.Code == code);
        await AssertNothingImportedAsync(host);
    }

    [Theory]
    [InlineData("Recipient,IMP-1,E-1000,,,10.00", "UnitPrice")]
    [InlineData("Recipient,IMP-1,E-1000,1,,", "Count")]
    [InlineData("Recipient,IMP-1,E-1000,,true,", "PriceIncludesVat")]
    public async Task A_recipient_link_with_an_item_links_value_is_reported(
        string row,
        string column
    )
    {
        using var host = new ApiTestHost();
        await ConfigureVatAsync(host, 7m);
        using var client = host.CreateClient();

        var archive = Zip(
            ("recipients.csv", FullRecipientsHeader + "E-1000,Anna Beispiel,Weg 1,\r\n"),
            ("items.csv", ItemsHeader + "9001,Grabpflege,7,120.00,\r\n"),
            (
                "graves.csv",
                GravesHeader + "IMP-1,27,Grabstätte,Bergfriedhof,DoubleOutside,2026-01,\r\n"
            ),
            ("mapping.csv", FullMappingHeader + "Item,IMP-1,9001,1,,\r\n" + row + "\r\n")
        );
        var response = await client.PostAsync("/odata/ImportArchive", Upload(archive));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        var problem = json
            .RootElement.GetProperty("problems")
            .EnumerateArray()
            .Single(p => p.GetProperty("code").GetString() == "ValueNotAllowed");
        Assert.Equal(3, problem.GetProperty("row").GetInt32());
        Assert.Equal("RecipientLinkValue", problem.GetProperty("messageKey").GetString());
        Assert.Equal(column, problem.GetProperty("messageArgs")[0].GetString());
    }

    /// <summary>The split is checked, and corrected, at the price the grave is
    /// billed: the link's own where it has one. 120.01 net at 19 % does not
    /// divide by two, 120.00 does -- whichever of the two the item has.</summary>
    [Fact]
    public async Task The_split_is_corrected_at_the_links_unit_price()
    {
        using var host = new ApiTestHost();
        await ConfigureVatAsync(host, 19m);
        using var client = host.CreateClient();

        var raised = ArchiveWithItemLink(
            "9001,Grabpflege,19,120.00,\r\n",
            "Item,IMP-1,9001,1,,120.01\r\n",
            recipients: 2
        );
        var response = await client.PostAsync("/odata/ImportArchive", Upload(raised));
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        using (var json = await ODataJson.ReadAsync(response))
        {
            Assert.Equal(
                1,
                json.RootElement.GetProperty("summary").GetProperty("correctedGraves").GetInt32()
            );
        }

        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var corrections = await db.GraveBillingCorrections.ToListAsync();
            Assert.NotEmpty(corrections);
            Assert.All(corrections, c => Assert.Equal(120.01m, c.EnteredPrice));
            Assert.All(corrections, c => Assert.Equal(120.02m, c.CorrectedPrice));
        }

        // the generation's precheck agrees with what the import stored
        var generate = await client.PostAsync(
            "/odata/GenerateGraveInvoices",
            ODataJson.Body(new { year = 2026, half = 1 })
        );
        Assert.Equal(HttpStatusCode.Accepted, generate.StatusCode);

        using var other = new ApiTestHost();
        await ConfigureVatAsync(other, 19m);
        using var otherClient = other.CreateClient();
        var even = ArchiveWithItemLink(
            "9001,Grabpflege,19,120.01,\r\n",
            "Item,IMP-1,9001,1,,120.00\r\n",
            recipients: 2
        );
        var evenResponse = await otherClient.PostAsync("/odata/ImportArchive", Upload(even));
        Assert.True(
            evenResponse.IsSuccessStatusCode,
            await evenResponse.Content.ReadAsStringAsync()
        );
        using var evenJson = await ODataJson.ReadAsync(evenResponse);
        Assert.Equal(
            0,
            evenJson.RootElement.GetProperty("summary").GetProperty("correctedGraves").GetInt32()
        );
    }

    [Fact]
    public async Task Every_grave_position_imports_by_its_name()
    {
        using var host = new ApiTestHost();
        await ConfigureVatAsync(host, 7m);
        using var client = host.CreateClient();

        var positions = Enum.GetNames<GravePosition>();
        var archive = Zip(
            ("recipients.csv", RecipientsHeader + "E-1000,Anna Beispiel,Weg 1\r\n"),
            ("items.csv", ItemsHeader + "9001,Grabpflege,7,120.00,\r\n"),
            (
                "graves.csv",
                GravesHeader
                    + string.Concat(
                        positions.Select(p =>
                            $"IMP-{p},27,Grabstätte,Bergfriedhof,{p},2026-01,\r\n"
                        )
                    )
            ),
            (
                "mapping.csv",
                MappingHeader
                    + string.Concat(
                        positions.Select(p =>
                            $"Item,IMP-{p},9001,1,\r\nRecipient,IMP-{p},E-1000,,\r\n"
                        )
                    )
            )
        );
        var response = await client.PostAsync("/odata/ImportArchive", Upload(archive));
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await db.Graves.ToDictionaryAsync(g => g.GraveNumber, g => g.Position);
        Assert.Equal(36, stored.Count);
        Assert.Equal(GravePosition.LawnUrnOutside, stored["IMP-LawnUrnOutside"]);
        Assert.Equal(GravePosition.Unknown, stored["IMP-Unknown"]);
    }
}
