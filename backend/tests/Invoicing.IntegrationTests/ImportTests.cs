using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Burkard.Invoicing.IntegrationTests;

public class ImportTests
{
    private const string RecipientsHeader = "RecipientNumber,Name,Address\r\n";
    private const string ItemsHeader = "ItemNumber,Description,VatRate,Price,PriceIncludesVat\r\n";
    private const string GravesHeader = "GraveNumber,StartDate,EndDate\r\n";
    private const string MappingHeader = "Type,GraveNumber,Reference,Count\r\n";

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

    /// <summary>A consistent archive: two recipients, two items, one grave with
    /// both link kinds.</summary>
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
                ItemsHeader + "9001,Grabpflege,7,120.00,\r\n" + "9002,Sonderleistung,7,,true\r\n"
            ),
            ("graves.csv", GravesHeader + "IMP-1,2026-01-01,2040-12-31\r\n"),
            (
                "mapping.csv",
                MappingHeader
                    + "Item,IMP-1,9001,2\r\n"
                    + "Item,IMP-1,9002,1\r\n"
                    + "Recipient,IMP-1,E-1000,\r\n"
                    + "Recipient,IMP-1,E-1001,\r\n"
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

    private static async Task<List<(string File, int? Row, string Code)>> ProblemsAsync(
        HttpResponseMessage response
    )
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
                    p.GetProperty("code").GetString()!
                )
            )
            .ToList();
    }

    private static async Task AssertNothingImportedAsync(ApiTestHost host)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
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
                ("recipients.csv", RecipientsHeader),
                ("items.csv", ItemsHeader),
                ("graves.csv", GravesHeader),
                ("mapping.csv", MappingHeader),
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
        Assert.Equal(2, summary.GetProperty("items").GetInt32());
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
        Assert.NotEqual(default, grave.CreatedAt);
        Assert.Equal(new DateOnly(2026, 1, 1), grave.StartDate);

        // the grave's item uses the imported item's number, text, VAT rate and price
        var pricedLink = grave.Items.Single(i => i.Item.Number == "9001");
        Assert.Equal(2, pricedLink.Count);
        Assert.Equal("Grabpflege", pricedLink.Item.Description);
        Assert.Equal(7m, pricedLink.Item.VatRate);
        Assert.Equal(120.00m, pricedLink.Item.DefaultPrice);
        Assert.Null(grave.Items.Single(i => i.Item.Number == "9002").Item.DefaultPrice);

        // the address keeps the line break the quoted CSV field carried
        var anna = grave.Recipients.Select(r => r.Recipient).Single(r => r.Number == "E-1000");
        Assert.Equal("Hauptstraße 1\n99999 Musterdorf", anna.Address);
    }

    [Fact]
    public async Task An_item_row_carries_its_vat_included_flag()
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
                .InvoiceItems.Where(i => i.Number == "9001")
                .Select(i => i.PriceIncludesVat)
                .SingleAsync()
        );
        Assert.True(
            await db
                .InvoiceItems.Where(i => i.Number == "9002")
                .Select(i => i.PriceIncludesVat)
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
            ("items.csv", ItemsHeader + "9001,Grabpflege,7,120.00,vielleicht\r\n"),
            ("graves.csv", GravesHeader),
            ("mapping.csv", MappingHeader)
        );
        var response = await client.PostAsync("/odata/ImportArchive", Upload(archive));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problems = await ProblemsAsync(response);
        Assert.Contains(
            problems,
            p => p.File == "items.csv" && p.Row == 2 && p.Code == "PriceIncludesVatInvalid"
        );
    }

    /// <summary>An archive built before this feature has an items.csv with the
    /// old four-column header, missing PriceIncludesVat. Regenerating the
    /// archive from the current template is the accepted cost of the new
    /// column, so the old header must be rejected rather than silently read as
    /// if every item were not VAT-included.</summary>
    [Fact]
    public async Task An_items_header_without_the_vat_included_column_is_rejected()
    {
        using var host = new ApiTestHost();
        await ConfigureVatAsync(host, 7m);
        using var client = host.CreateClient();

        var archive = Zip(
            ("recipients.csv", RecipientsHeader),
            (
                "items.csv",
                "ItemNumber,Description,VatRate,Price\r\n" + "9001,Grabpflege,7,120.00\r\n"
            ),
            ("graves.csv", GravesHeader),
            ("mapping.csv", MappingHeader)
        );
        var response = await client.PostAsync("/odata/ImportArchive", Upload(archive));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problems = await ProblemsAsync(response);
        Assert.Contains(
            problems,
            p => p.File == "items.csv" && p.Row == 1 && p.Code == "HeaderInvalid"
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
            ("graves.csv", GravesHeader + "IMP-1,nicht-ein-datum,2040-12-31\r\n"),
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
            ("graves.csv", GravesHeader + "IMP-1,2026-01-01,2040-12-31\r\n"),
            ("mapping.csv", MappingHeader + "Item,IMP-1,GIBTSNICHT,1\r\n")
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
            ("graves.csv", GravesHeader + "IMP-1,2026-01-01,2040-12-31\r\n"),
            ("mapping.csv", MappingHeader + "Recipient,IMP-1,E-1000,\r\n")
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
            ("graves.csv", GravesHeader + "IMP-1,2026-01-01,2040-12-31\r\n"),
            (
                "mapping.csv",
                MappingHeader
                    + "Item,IMP-1,9999,1\r\n"
                    + "Recipient,IMP-1,E-9999,\r\n"
                    + "Recipient,IMP-9,E-1000,\r\n"
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
                GravesHeader + "IMP-1,2026-01-01,2040-12-31\r\n" + "IMP-1,2026-01-01,2040-12-31\r\n"
            ),
            ("mapping.csv", MappingHeader + "Item,IMP-1,9001,1\r\n" + "Item,IMP-1,9001,2\r\n")
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
}
