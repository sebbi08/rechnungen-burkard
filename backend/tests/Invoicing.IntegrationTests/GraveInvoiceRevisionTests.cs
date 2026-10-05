using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Api.Services;
using Burkard.Invoicing.Domain;
using Burkard.Invoicing.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Burkard.Invoicing.IntegrationTests;

public class GraveInvoiceRevisionTests
{
    /// <summary>A grave with one item (count 2 at 100.00, 7% VAT) and two
    /// recipients, invoiced for 2031 H1.</summary>
    private static async Task<(long GraveId, long ItemId, long InvoiceId)> SeedAsync(
        ApiTestHost host
    )
    {
        long graveId,
            itemId;
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.VatRates.Add(new VatRate { Rate = 7m });
            var item = new InvoiceItem
            {
                Number = "2001",
                Description = "Grabpflege",
                VatRate = 7m,
                DefaultPrice = 100m,
            };
            var anna = new Recipient
            {
                Number = "E-0001",
                Name = "Anna Beispiel",
                Address = "A 1",
            };
            var bernd = new Recipient
            {
                Number = "E-0002",
                Name = "Bernd Beispiel",
                Address = "B 2",
            };
            db.InvoiceItems.Add(item);
            db.Recipients.AddRange(anna, bernd);
            var grave = new Grave
            {
                GraveNumber = "G-300",
                StartDate = new DateOnly(2020, 1, 1),
                EndDate = new DateOnly(2040, 12, 31),
                CreatedAt = DateOnly.FromDateTime(DateTime.UtcNow),
            };
            db.Graves.Add(grave);
            await db.SaveChangesAsync();
            db.GraveItems.Add(
                new GraveItem
                {
                    GraveId = grave.Id,
                    ItemId = item.Id,
                    Count = 2,
                }
            );
            db.GraveRecipients.Add(
                new GraveRecipient { GraveId = grave.Id, RecipientId = anna.Id }
            );
            db.GraveRecipients.Add(
                new GraveRecipient { GraveId = grave.Id, RecipientId = bernd.Id }
            );
            await db.SaveChangesAsync();
            (graveId, itemId) = (grave.Id, item.Id);
        }

        using var client = host.CreateClient();
        await GraveGenerationHelper.RunAsync(client, 2031, 1);

        using var verify = host.Services.CreateScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<AppDbContext>();
        var invoiceId = (
            await verifyDb.Invoices.OrderBy(i => i.Id).FirstAsync(i => i.GraveId == graveId)
        ).Id;
        return (graveId, itemId, invoiceId);
    }

    /// <summary>A grave with one item (count 1 at 10.01, 7% VAT — chosen so the
    /// net does not divide evenly across two recipients, so generation raises it
    /// by a cent) and two recipients linked in reverse-alphabetical order
    /// (Zimmermann first, then Abel), invoiced for 2032 H1.</summary>
    private static async Task<(
        long GraveId,
        long ItemId,
        long InvoiceId
    )> SeedReverseAlphabeticalAsync(ApiTestHost host)
    {
        long graveId,
            itemId;
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.VatRates.Add(new VatRate { Rate = 7m });
            var item = new InvoiceItem
            {
                Number = "2003",
                Description = "Grabpflege",
                VatRate = 7m,
                // 10,02 rather than 10,01: 1 002 net cents divide evenly across
                // the two recipients. At 10,01 the grave could not be saved --
                // nothing raises a price to make it fit any more.
                DefaultPrice = 10.02m,
            };
            var zimmermann = new Recipient
            {
                Number = "E-0010",
                Name = "Zimmermann Zora",
                Address = "Z 1",
            };
            var abel = new Recipient
            {
                Number = "E-0011",
                Name = "Abel Anton",
                Address = "A 2",
            };
            db.InvoiceItems.Add(item);
            db.Recipients.AddRange(zimmermann, abel);
            var grave = new Grave
            {
                GraveNumber = "G-301",
                StartDate = new DateOnly(2020, 1, 1),
                EndDate = new DateOnly(2040, 12, 31),
                CreatedAt = DateOnly.FromDateTime(DateTime.UtcNow),
            };
            db.Graves.Add(grave);
            await db.SaveChangesAsync();
            db.GraveItems.Add(
                new GraveItem
                {
                    GraveId = grave.Id,
                    ItemId = item.Id,
                    Count = 1,
                }
            );
            // linked in reverse-alphabetical order, so a query that (wrongly)
            // ordered siblings by RecipientName instead of Id would process them
            // in a different order than generation did.
            db.GraveRecipients.Add(
                new GraveRecipient { GraveId = grave.Id, RecipientId = zimmermann.Id }
            );
            db.GraveRecipients.Add(
                new GraveRecipient { GraveId = grave.Id, RecipientId = abel.Id }
            );
            await db.SaveChangesAsync();
            (graveId, itemId) = (grave.Id, item.Id);
        }

        using var client = host.CreateClient();
        await GraveGenerationHelper.RunAsync(client, 2032, 1);

        using var verify = host.Services.CreateScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<AppDbContext>();
        var invoiceId = (
            await verifyDb.Invoices.OrderBy(i => i.Id).FirstAsync(i => i.GraveId == graveId)
        ).Id;
        return (graveId, itemId, invoiceId);
    }

    [Fact]
    public async Task A_revision_of_a_new_form_group_bills_every_recipient_identically()
    {
        using var host = new ApiTestHost();
        var (graveId, itemId, invoiceId) = await SeedReverseAlphabeticalAsync(host);

        // count 1 at 10.02 with 7 % VAT nets 1 002 cents, which divides evenly
        // across two recipients. Both are billed the grave's own price in full;
        // there is no split position for a remainder cent to land on unevenly.
        Dictionary<string, decimal> before;
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            before = await db
                .Invoices.Include(i => i.Lines)
                .Where(i => i.GraveId == graveId)
                .ToDictionaryAsync(i => i.RecipientName, i => i.Lines.Single().UnitPrice);
        }
        Assert.Equal(2, before.Count);
        Assert.All(before.Values, price => Assert.Equal(10.02m, price));

        using var client = host.CreateClient();
        var response = await client.PostAsync(
            "/odata/CreateGraveInvoiceRevision",
            ODataJson.Body(
                new
                {
                    invoiceId,
                    // the same amounts generation already used, submitted unchanged
                    lines = new[]
                    {
                        new
                        {
                            itemId,
                            count = 1,
                            unitPrice = (decimal?)10.02m,
                        },
                    },
                }
            )
        );
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var verify = host.Services.CreateScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<AppDbContext>();
        var after = await verifyDb
            .Invoices.Include(i => i.Lines)
            .Where(i => i.GraveId == graveId && i.IsLatestRevision)
            .ToListAsync();

        Assert.Equal(2, after.Count);
        // and the revision leaves them on the same identical amount
        Assert.All(after, r => Assert.Equal(10.02m, r.Lines.Single().UnitPrice));
        Assert.All(after, r => Assert.Equal(2, r.RecipientCount));
    }

    /// <summary>The most important guarantee in this feature: a grave invoiced
    /// before recipients were billed the grave's own lines directly must go on
    /// rendering exactly as it always did, forever — even once the group it
    /// belongs to is revised into the new form.</summary>
    [Fact]
    public async Task Revising_a_pre_change_group_leaves_the_superseded_invoices_building_their_original_totals()
    {
        using var host = new ApiTestHost();

        long graveId,
            itemId,
            annaId,
            berndId,
            oldAnnaInvoiceId,
            oldBerndInvoiceId;
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var documentFactory =
                scope.ServiceProvider.GetRequiredService<InvoiceDocumentFactory>();
            db.VatRates.Add(new VatRate { Rate = 7m });
            var item = new InvoiceItem
            {
                Number = "2004",
                Description = "Grabpflege",
                VatRate = 7m,
                DefaultPrice = 100m,
            };
            var anna = new Recipient
            {
                Number = "E-0030",
                Name = "Anna Alt",
                Address = "A 1",
            };
            var bernd = new Recipient
            {
                Number = "E-0031",
                Name = "Bernd Alt",
                Address = "B 2",
            };
            db.InvoiceItems.Add(item);
            db.Recipients.AddRange(anna, bernd);
            var grave = new Grave
            {
                GraveNumber = "G-500",
                StartDate = new DateOnly(2020, 1, 1),
                EndDate = new DateOnly(2040, 12, 31),
                CreatedAt = DateOnly.FromDateTime(DateTime.UtcNow),
            };
            db.Graves.Add(grave);
            await db.SaveChangesAsync();
            db.GraveItems.Add(
                new GraveItem
                {
                    GraveId = grave.Id,
                    ItemId = item.Id,
                    Count = 2,
                }
            );
            db.GraveRecipients.Add(
                new GraveRecipient { GraveId = grave.Id, RecipientId = anna.Id }
            );
            db.GraveRecipients.Add(
                new GraveRecipient { GraveId = grave.Id, RecipientId = bernd.Id }
            );
            await db.SaveChangesAsync();
            (graveId, itemId, annaId, berndId) = (grave.Id, item.Id, anna.Id, bernd.Id);

            // Hand-built exactly as the pre-change generator produced it: each
            // recipient billed their own 50 % share (count 2 @ 50.00 net, half of
            // the grave's 2 x 100.00), the grave's pre-split price remembered on
            // GraveUnitPrice, and RecipientCount left null — the form every grave
            // invoice had before this feature.
            var now = DateTime.UtcNow;
            var invoiceIds = new List<long>();
            var seq = 1;
            foreach (var recipient in new[] { anna, bernd })
            {
                var invoice = new Invoice
                {
                    RevisionNumber = 0,
                    Year = 2033,
                    Sequence = seq,
                    InvoiceNumber = InvoiceNumberFormatter.ForOriginal(2033, seq),
                    GraveId = grave.Id,
                    PeriodYear = 2033,
                    PeriodHalf = 1,
                    SharePercent = 50m,
                    RecipientCount = null,
                    RecipientId = recipient.Id,
                    RecipientName = recipient.Name,
                    RecipientAddress = recipient.Address,
                    EndingText = "Danke",
                    StampWord = "Kopie",
                    GeneratedAt = now,
                    Lines = new List<InvoiceLine>
                    {
                        new()
                        {
                            ItemId = item.Id,
                            ItemNumber = item.Number,
                            Description = item.Description,
                            Count = 2,
                            UnitPrice = 50m,
                            GraveUnitPrice = 100m,
                            VatRate = 7m,
                            LineNumber = 1,
                        },
                    },
                };
                db.Invoices.Add(invoice);
                await db.SaveChangesAsync();
                invoice.RootId = invoice.Id;
                invoice.Documents = documentFactory.Build(invoice, now);
                await db.SaveChangesAsync();
                invoiceIds.Add(invoice.Id);
                seq++;
            }

            (oldAnnaInvoiceId, oldBerndInvoiceId) = (invoiceIds[0], invoiceIds[1]);
        }

        Dictionary<long, decimal> originalNetTotals;
        Dictionary<long, Dictionary<DocumentKind, byte[]>> originalDocumentContent;
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var originals = await db
                .Invoices.Include(i => i.Lines)
                .Include(i => i.Documents)
                .Where(i => i.GraveId == graveId)
                .ToListAsync();

            var builder = new DocumentDataBuilder();
            originalNetTotals = originals.ToDictionary(i => i.Id, i => builder.Build(i).NetTotal);
            // both the normal document and the copy are persisted, and both must
            // reproduce — comparing only one would miss a bug that regenerates or
            // corrupts just the other
            originalDocumentContent = originals.ToDictionary(
                i => i.Id,
                i => i.Documents.ToDictionary(d => d.Kind, d => d.Content)
            );
        }
        // both original invoices bill 2 x 50.00 = 100.00 net each — the OLD
        // split's per-recipient share, not the grave's full 200.00
        Assert.All(originalNetTotals.Values, total => Assert.Equal(100.00m, total));

        using var client = host.CreateClient();
        var response = await client.PostAsync(
            "/odata/CreateGraveInvoiceRevision",
            ODataJson.Body(
                new
                {
                    invoiceId = oldAnnaInvoiceId,
                    lines = new[]
                    {
                        new
                        {
                            itemId,
                            count = 3,
                            unitPrice = (decimal?)100m,
                        },
                    },
                }
            )
        );
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var verify = host.Services.CreateScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<AppDbContext>();

        // the new revisions are in the new form: identical, full-price lines and
        // RecipientCount set
        var revisions = await verifyDb
            .Invoices.Include(i => i.Lines)
            .Where(i => i.GraveId == graveId && i.IsLatestRevision)
            .ToListAsync();
        Assert.Equal(2, revisions.Count);
        Assert.All(revisions, r => Assert.Equal(2, r.RecipientCount));
        Assert.All(
            revisions,
            r =>
            {
                var line = Assert.Single(r.Lines);
                Assert.Equal(3, line.Count);
                Assert.Equal(100m, line.UnitPrice);
            }
        );

        // the superseded, pre-change invoices are untouched: same lines, still
        // RecipientCount null, and they still build the exact totals — and the
        // exact document bytes — they always did
        var superseded = await verifyDb
            .Invoices.Include(i => i.Lines)
            .Include(i => i.Documents)
            .Where(i => i.Id == oldAnnaInvoiceId || i.Id == oldBerndInvoiceId)
            .ToListAsync();
        Assert.Equal(2, superseded.Count);
        Assert.All(superseded, i => Assert.False(i.IsLatestRevision));
        Assert.All(superseded, i => Assert.Null(i.RecipientCount));
        Assert.All(
            superseded,
            i =>
            {
                var line = Assert.Single(i.Lines);
                Assert.Equal(2, line.Count);
                Assert.Equal(50m, line.UnitPrice);
                Assert.Equal(100m, line.GraveUnitPrice);
            }
        );

        var builderAfter = new DocumentDataBuilder();
        Assert.All(
            superseded,
            i => Assert.Equal(originalNetTotals[i.Id], builderAfter.Build(i).NetTotal)
        );
        Assert.All(
            superseded,
            i =>
            {
                var before = originalDocumentContent[i.Id];
                Assert.Equal(2, i.Documents.Count);
                Assert.All(
                    i.Documents,
                    document => Assert.Equal(before[document.Kind], document.Content)
                );
            }
        );
    }

    [Fact]
    public async Task Editing_one_invoice_revises_every_recipient()
    {
        using var host = new ApiTestHost();
        var (graveId, itemId, invoiceId) = await SeedAsync(host);

        using var client = host.CreateClient();
        var response = await client.PostAsync(
            "/odata/CreateGraveInvoiceRevision",
            ODataJson.Body(
                new
                {
                    invoiceId,
                    lines = new[]
                    {
                        new
                        {
                            itemId,
                            count = 3,
                            unitPrice = (decimal?)200m,
                        },
                    },
                }
            )
        );
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var invoices = await db
            .Invoices.Include(i => i.Lines)
            .Include(i => i.Documents)
            .Where(i => i.GraveId == graveId)
            .ToListAsync();

        // two originals plus one revision each
        Assert.Equal(4, invoices.Count);
        var revisions = invoices.Where(i => i.RevisionNumber == 1).ToList();
        Assert.Equal(2, revisions.Count);
        Assert.All(revisions, r => Assert.True(r.IsLatestRevision));
        Assert.All(revisions, r => Assert.EndsWith("-R1", r.InvoiceNumber));
        Assert.All(
            invoices.Where(i => i.RevisionNumber == 0),
            o => Assert.False(o.IsLatestRevision)
        );

        // each revision carries the grave's new line in full, identically, and
        // is marked as billing a share of it rather than the whole
        Assert.All(
            revisions,
            r =>
            {
                var line = Assert.Single(r.Lines);
                Assert.Equal(3, line.Count);
                Assert.Equal(200m, line.UnitPrice);
                Assert.Equal(2, r.RecipientCount);
            }
        );

        // both recipients keep their own invoice and get both documents again
        Assert.Equal(
            new[] { "Anna Beispiel", "Bernd Beispiel" },
            revisions.Select(r => r.RecipientName).OrderBy(n => n)
        );
        Assert.All(revisions, r => Assert.Equal(2, r.Documents.Count));
        Assert.All(revisions, r => Assert.All(r.Documents, d => Assert.NotEmpty(d.Content)));
    }

    [Fact]
    public async Task The_revisions_keep_the_grave_period_and_share()
    {
        using var host = new ApiTestHost();
        var (graveId, itemId, invoiceId) = await SeedAsync(host);

        using var client = host.CreateClient();
        var response = await client.PostAsync(
            "/odata/CreateGraveInvoiceRevision",
            ODataJson.Body(
                new
                {
                    invoiceId,
                    lines = new[]
                    {
                        new
                        {
                            itemId,
                            count = 2,
                            unitPrice = (decimal?)100m,
                        },
                    },
                }
            )
        );
        response.EnsureSuccessStatusCode();

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var revisions = await db
            .Invoices.Where(i => i.GraveId == graveId && i.RevisionNumber == 1)
            .ToListAsync();

        Assert.All(revisions, r => Assert.Equal(graveId, r.GraveId));
        Assert.All(revisions, r => Assert.Equal(2031, r.PeriodYear));
        Assert.All(revisions, r => Assert.Equal(1, r.PeriodHalf));
        Assert.All(revisions, r => Assert.Equal(50m, r.SharePercent));
    }

    [Fact]
    public async Task Editing_twice_keeps_the_chains_in_step()
    {
        using var host = new ApiTestHost();
        var (graveId, itemId, invoiceId) = await SeedAsync(host);
        using var client = host.CreateClient();

        for (var count = 3; count <= 4; count++)
        {
            var response = await client.PostAsync(
                "/odata/CreateGraveInvoiceRevision",
                ODataJson.Body(
                    new
                    {
                        invoiceId,
                        lines = new[]
                        {
                            new
                            {
                                itemId,
                                count,
                                unitPrice = (decimal?)100m,
                            },
                        },
                    }
                )
            );
            response.EnsureSuccessStatusCode();
        }

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var latest = await db
            .Invoices.Where(i => i.GraveId == graveId && i.IsLatestRevision)
            .ToListAsync();
        Assert.Equal(2, latest.Count);
        Assert.All(latest, i => Assert.Equal(2, i.RevisionNumber));
    }

    [Fact]
    public async Task Rejects_a_non_grave_invoice()
    {
        using var host = new ApiTestHost();
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.VatRates.Add(new VatRate { Rate = 7m });
        var item = new InvoiceItem
        {
            Number = "2002",
            Description = "X",
            VatRate = 7m,
            DefaultPrice = 10m,
        };
        var recipient = new Recipient
        {
            Number = "E-0009",
            Name = "Solo",
            Address = "S 1",
        };
        db.InvoiceItems.Add(item);
        db.Recipients.Add(recipient);
        await db.SaveChangesAsync();

        using var client = host.CreateClient();
        var created = await client.PostAsync(
            "/odata/CreateInvoice",
            ODataJson.Body(
                new
                {
                    recipientId = recipient.Id,
                    lines = new[]
                    {
                        new
                        {
                            itemId = item.Id,
                            count = 1,
                            unitPrice = (decimal?)null,
                        },
                    },
                }
            )
        );
        created.EnsureSuccessStatusCode();
        using var createdJson = await ODataJson.ReadAsync(created);

        var response = await client.PostAsync(
            "/odata/CreateGraveInvoiceRevision",
            ODataJson.Body(
                new
                {
                    invoiceId = createdJson.RootElement.GetProperty("Id").GetInt64(),
                    lines = new[]
                    {
                        new
                        {
                            itemId = item.Id,
                            count = 1,
                            unitPrice = (decimal?)10m,
                        },
                    },
                }
            )
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        Assert.Equal(
            "NotAGraveInvoice",
            json.RootElement.GetProperty("error").GetProperty("code").GetString()
        );
    }

    [Fact]
    public async Task Rejects_an_empty_line_list_without_touching_anything()
    {
        using var host = new ApiTestHost();
        var (graveId, _, invoiceId) = await SeedAsync(host);

        using var client = host.CreateClient();
        var response = await client.PostAsync(
            "/odata/CreateGraveInvoiceRevision",
            ODataJson.Body(new { invoiceId, lines = Array.Empty<object>() })
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        Assert.Equal(
            "NoLines",
            json.RootElement.GetProperty("error").GetProperty("code").GetString()
        );

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(2, await db.Invoices.CountAsync(i => i.GraveId == graveId));
    }

    [Fact]
    public async Task Rejects_prices_that_do_not_split_across_the_recipients_without_touching_anything()
    {
        using var host = new ApiTestHost();
        var (graveId, itemId, invoiceId) = await SeedAsync(host);

        // 1 × 100.01 net cannot be split between the grave's two recipients
        using var client = host.CreateClient();
        var response = await client.PostAsync(
            "/odata/CreateGraveInvoiceRevision",
            ODataJson.Body(
                new
                {
                    invoiceId,
                    lines = new[]
                    {
                        new
                        {
                            itemId,
                            count = 1,
                            unitPrice = (decimal?)100.01m,
                        },
                    },
                }
            )
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        Assert.Equal(
            "PricesDoNotDivide",
            json.RootElement.GetProperty("error").GetProperty("code").GetString()
        );

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(2, await db.Invoices.CountAsync(i => i.GraveId == graveId));
    }

    [Fact]
    public async Task A_revision_keeps_the_prorated_description()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var (graveId, itemId) = await SeedProratedGraveAsync(host);
        await GraveGenerationHelper.RunAsync(client, 2026, 1);
        var invoiceId = await FirstInvoiceIdAsync(host, graveId);

        var response = await client.PostAsync(
            "/odata/CreateGraveInvoiceRevision",
            ODataJson.Body(
                new
                {
                    invoiceId,
                    lines = new[]
                    {
                        new
                        {
                            itemId,
                            count = 1,
                            unitPrice = (decimal?)21.00m,
                        },
                    },
                }
            )
        );
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var line = await db
            .Invoices.Where(i => i.GraveId == graveId && i.IsLatestRevision)
            .SelectMany(i => i.Lines)
            .SingleAsync();
        Assert.Equal("Grabpflege, anteilig 05–06/2026 (2 von 6 Monaten)", line.Description);
        Assert.Equal(21.00m, line.UnitPrice);
    }

    /// <summary>A grave starting 2026-05 with one recipient and one all-year item
    /// at 60.00, so generation bills 2026 H1 prorated.</summary>
    private static async Task<(long GraveId, long ItemId)> SeedProratedGraveAsync(ApiTestHost host)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.VatRates.Add(new VatRate { Rate = 7m });
        var item = new InvoiceItem
        {
            Number = "2010",
            Description = "Grabpflege",
            VatRate = 7m,
            DefaultPrice = 60m,
        };
        var anna = new Recipient
        {
            Number = "E-0020",
            Name = "Anna Beispiel",
            Address = "A 1",
        };
        db.InvoiceItems.Add(item);
        db.Recipients.Add(anna);
        var grave = new Grave
        {
            GraveNumber = "G-310",
            StartDate = new DateOnly(2026, 5, 1),
            EndDate = new DateOnly(2040, 12, 31),
            CreatedAt = DateOnly.FromDateTime(DateTime.UtcNow),
        };
        db.Graves.Add(grave);
        await db.SaveChangesAsync();
        db.GraveItems.Add(
            new GraveItem
            {
                GraveId = grave.Id,
                ItemId = item.Id,
                Count = 1,
            }
        );
        db.GraveRecipients.Add(new GraveRecipient { GraveId = grave.Id, RecipientId = anna.Id });
        await db.SaveChangesAsync();
        return (grave.Id, item.Id);
    }

    private static async Task<long> FirstInvoiceIdAsync(ApiTestHost host, long graveId)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return (await db.Invoices.OrderBy(i => i.Id).FirstAsync(i => i.GraveId == graveId)).Id;
    }
}
