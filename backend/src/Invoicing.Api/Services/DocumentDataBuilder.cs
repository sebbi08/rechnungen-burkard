using Burkard.Invoicing.Documents;
using Burkard.Invoicing.Domain.Entities;

namespace Burkard.Invoicing.Api.Services;

public sealed class DocumentDataBuilder
{
    public InvoiceDocumentData Build(Invoice invoice)
    {
        var (lines, summaries, netTotalCents, vatTotalCents) = Calculate(
            invoice.Lines.Select(l => new LineSource(
                l.LineNumber,
                l.ItemNumber,
                l.Description,
                l.Count,
                l.UnitPrice,
                l.VatRate,
                l.PriceIncludesVat,
                l.GrossUnitPrice,
                l.Comment
            ))
        );

        var shareText = invoice.SharePercent is { } percent
            ? DocumentTexts.ShareText(percent)
            : null;

        // A grave invoice generated since the lines became the grave's own: the
        // recipient pays a share of them, and their VAT is worked out from that
        // share rather than from the whole. The prices were corrected until each
        // rate's net divides exactly before anything was billed, so no rounding
        // happens here.
        InvoiceDocumentData.ShareData? share = null;
        if (invoice.RecipientCount is { } recipients && recipients > 0)
        {
            var shareSummaries = summaries
                .Select(s =>
                {
                    var net = s.NetTotal / recipients;
                    var vat = Math.Round(net * s.Rate / 100m, 2, MidpointRounding.AwayFromZero);
                    return new InvoiceDocumentData.VatSummary(s.Rate, net, vat, net + vat);
                })
                .ToArray();
            var shareNet = shareSummaries.Sum(s => s.NetTotal);
            var shareVat = shareSummaries.Sum(s => s.VatAmount);
            share = new InvoiceDocumentData.ShareData(
                100m / recipients,
                shareNet,
                shareSummaries,
                shareVat,
                shareNet + shareVat
            );
        }

        return new InvoiceDocumentData
        {
            InvoiceNumber = invoice.InvoiceNumber,
            // GeneratedAt is the wall-clock here, in Europe/Berlin: the day it
            // names is the issue date the document prints. It is handed on
            // marked UTC so the PDF's metadata timestamp is written as it is on
            // any machine rather than shifted by that machine's time zone -- a
            // document has to come out byte for byte the same wherever it is
            // rebuilt.
            GeneratedAt = DateTime.SpecifyKind(invoice.GeneratedAt, DateTimeKind.Utc),
            RecipientName = invoice.RecipientName,
            RecipientAddress = invoice.RecipientAddress,
            RecipientNumber = invoice.RecipientNumber,
            GraveNumber = invoice.GraveNumber,
            GraveSiteName = invoice.GraveSiteName,
            GraveFieldNumber = invoice.GraveFieldNumber,
            GravePosition = invoice.GravePosition?.ToString(),
            ShareText = shareText,
            IntroText = invoice.IntroText,
            EndingText = invoice.EndingText,
            StampWord = invoice.StampWord,
            Lines = lines,
            VatSummaries = summaries,
            NetTotal = netTotalCents / 100m,
            TotalVat = vatTotalCents / 100m,
            GrossTotal = (netTotalCents + vatTotalCents) / 100m,
            Share = share,
        };
    }

    /// <summary>A quote prints as an invoice titled Angebot: the same lines, VAT
    /// block and totals, worked out by the same calculation, and never a stamp,
    /// since a quote has no copy.</summary>
    public InvoiceDocumentData Build(Quote quote)
    {
        var (lines, summaries, netTotalCents, vatTotalCents) = Calculate(
            quote.Lines.Select(l => new LineSource(
                l.LineNumber,
                l.ItemNumber,
                l.Description,
                l.Count,
                l.UnitPrice,
                l.VatRate,
                l.PriceIncludesVat,
                l.GrossUnitPrice,
                l.Comment
            ))
        );

        return new InvoiceDocumentData
        {
            InvoiceNumber = quote.QuoteNumber,
            IsQuote = true,
            // the wall-clock marked UTC, for the reason given in Build(Invoice)
            GeneratedAt = DateTime.SpecifyKind(quote.GeneratedAt, DateTimeKind.Utc),
            RecipientName = quote.RecipientName,
            RecipientAddress = quote.RecipientAddress,
            RecipientNumber = quote.RecipientNumber,
            IntroText = quote.IntroText,
            EndingText = quote.EndingText,
            StampWord = "",
            Lines = lines,
            VatSummaries = summaries,
            NetTotal = netTotalCents / 100m,
            TotalVat = vatTotalCents / 100m,
            GrossTotal = (netTotalCents + vatTotalCents) / 100m,
        };
    }

    /// <summary>What both kinds of line have in common for the document.</summary>
    private readonly record struct LineSource(
        int LineNumber,
        string ItemNumber,
        string Description,
        int Count,
        decimal UnitPrice,
        decimal VatRate,
        bool PriceIncludesVat,
        decimal? GrossUnitPrice,
        string Comment
    );

    /// <summary>The printed lines, the VAT block and the totals in cents: each
    /// line rounded to the cent, its VAT rounded from that, and the block summed
    /// from the rounded lines. A line with an entered gross is the exception: its
    /// gross is count x gross, its VAT is taken out of that, and its net is what
    /// remains.</summary>
    private static (
        InvoiceDocumentData.LineData[] Lines,
        InvoiceDocumentData.VatSummary[] Summaries,
        decimal NetTotalCents,
        decimal VatTotalCents
    ) Calculate(IEnumerable<LineSource> source)
    {
        var lines = source
            .OrderBy(l => l.LineNumber)
            .Select(l =>
            {
                var (lineTotalCents, vatCents) = LineAmounts.Cents(
                    l.Count,
                    l.UnitPrice,
                    l.VatRate,
                    l.GrossUnitPrice
                );
                return new InvoiceDocumentData.LineData(
                    l.LineNumber,
                    l.ItemNumber,
                    l.Description,
                    l.Count,
                    l.UnitPrice,
                    l.VatRate,
                    lineTotalCents / 100m,
                    vatCents / 100m,
                    l.PriceIncludesVat,
                    l.Comment,
                    l.GrossUnitPrice
                );
            })
            .ToArray();

        var summaries = lines
            .GroupBy(l => l.VatRate)
            .Select(g => new
            {
                Rate = g.Key,
                Net = g.Sum(l => Math.Round(l.LineTotal * 100m, 0, MidpointRounding.AwayFromZero)),
                Vat = g.Sum(l => Math.Round(l.VatAmount * 100m, 0, MidpointRounding.AwayFromZero)),
            })
            .OrderBy(g => g.Rate)
            .Select(g => new InvoiceDocumentData.VatSummary(
                g.Rate,
                g.Net / 100m,
                g.Vat / 100m,
                (g.Net + g.Vat) / 100m
            ))
            .ToArray();

        var netTotalCents = summaries.Sum(s =>
            Math.Round(s.NetTotal * 100m, 0, MidpointRounding.AwayFromZero)
        );
        var vatTotalCents = summaries.Sum(s =>
            Math.Round(s.VatAmount * 100m, 0, MidpointRounding.AwayFromZero)
        );

        return (lines, summaries, netTotalCents, vatTotalCents);
    }
}
