using Burkard.Invoicing.Domain.Entities;
using Burkard.Invoicing.Documents;

namespace Burkard.Invoicing.Api.Services;

public sealed class DocumentDataBuilder
{
    public InvoiceDocumentData Build(Invoice invoice, string? shareText)
    {
        var lines = invoice.Lines
            .OrderBy(l => l.LineNumber)
            .Select(l =>
            {
                var lineTotalCents = Math.Round(l.Count * l.UnitPrice * 100m, 0, MidpointRounding.AwayFromZero);
                var lineTotal = lineTotalCents / 100m;
                var vatAmount = Math.Round(lineTotalCents * l.VatRate / 100m, 0, MidpointRounding.AwayFromZero) / 100m;
                return new InvoiceDocumentData.LineData(
                    l.LineNumber, l.ItemNumber, l.Description, l.Count,
                    l.UnitPrice, l.VatRate, lineTotal, vatAmount);
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
            .Select(g => new InvoiceDocumentData.VatSummary(g.Rate, g.Net / 100m, g.Vat / 100m, (g.Net + g.Vat) / 100m))
            .ToArray();

        var netTotalCents = summaries.Sum(s => Math.Round(s.NetTotal * 100m, 0, MidpointRounding.AwayFromZero));
        var vatTotalCents = summaries.Sum(s => Math.Round(s.VatAmount * 100m, 0, MidpointRounding.AwayFromZero));

        return new InvoiceDocumentData
        {
            InvoiceNumber = invoice.InvoiceNumber,
            GeneratedAt = DateTime.SpecifyKind(invoice.GeneratedAt, DateTimeKind.Utc),
            RecipientName = invoice.RecipientName,
            RecipientAddress = invoice.RecipientAddress,
            ShareText = shareText,
            EndingText = invoice.EndingText,
            StampWord = invoice.StampWord,
            Lines = lines,
            VatSummaries = summaries,
            NetTotal = netTotalCents / 100m,
            TotalVat = vatTotalCents / 100m,
            GrossTotal = (netTotalCents + vatTotalCents) / 100m,
        };
    }
}
