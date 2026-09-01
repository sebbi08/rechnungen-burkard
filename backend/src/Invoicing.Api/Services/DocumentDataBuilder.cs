using Burkard.Invoicing.Documents;
using Burkard.Invoicing.Domain.Entities;

namespace Burkard.Invoicing.Api.Services;

public sealed class DocumentDataBuilder
{
    public InvoiceDocumentData Build(Invoice invoice)
    {
        var lines = invoice
            .Lines.OrderBy(l => l.LineNumber)
            .Select(l =>
            {
                var lineTotalCents = Math.Round(
                    l.Count * l.UnitPrice * 100m,
                    0,
                    MidpointRounding.AwayFromZero
                );
                var lineTotal = lineTotalCents / 100m;
                var vatAmount =
                    Math.Round(lineTotalCents * l.VatRate / 100m, 0, MidpointRounding.AwayFromZero)
                    / 100m;
                return new InvoiceDocumentData.LineData(
                    l.LineNumber,
                    l.ItemNumber,
                    l.Description,
                    l.Count,
                    l.UnitPrice,
                    l.VatRate,
                    lineTotal,
                    vatAmount
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

        // the share note is written in the invoice's own language, so a later
        // language switch cannot change what an existing document said
        var texts = DocumentTexts.For(invoice.Language);
        var shareText = invoice.SharePercent is { } percent ? texts.ShareText(percent) : null;

        // A grave invoice generated since the lines became the grave's own: the
        // recipient pays a share of them, and their VAT is worked out from that
        // share rather than from the whole. MakeDivisible guarantees each rate's
        // net divides exactly, so no rounding happens here.
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
            GeneratedAt = DateTime.SpecifyKind(invoice.GeneratedAt, DateTimeKind.Utc),
            RecipientName = invoice.RecipientName,
            RecipientAddress = invoice.RecipientAddress,
            ShareText = shareText,
            EndingText = invoice.EndingText,
            StampWord = invoice.StampWord,
            Language = invoice.Language,
            Lines = lines,
            VatSummaries = summaries,
            NetTotal = netTotalCents / 100m,
            TotalVat = vatTotalCents / 100m,
            GrossTotal = (netTotalCents + vatTotalCents) / 100m,
            Share = share,
        };
    }
}
