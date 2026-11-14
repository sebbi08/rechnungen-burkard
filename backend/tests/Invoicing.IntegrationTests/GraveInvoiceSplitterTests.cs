using System;
using System.Collections.Generic;
using System.Linq;
using Burkard.Invoicing.Domain;
using Burkard.Invoicing.Domain.Entities;
using Xunit;

namespace Burkard.Invoicing.IntegrationTests;

/// <summary>GraveInvoiceSplitter.Combine only — the legacy path that
/// reconstructs a grave's lines from invoices generated before this feature,
/// when each recipient's invoice stored their own even share of the grave's
/// lines rather than the grave's lines in full. The even split itself no
/// longer exists in the product; these per-recipient inputs are hand-written
/// to stand in for what it once produced.</summary>
public class GraveInvoiceSplitterTests
{
    private static InvoiceLine RecipientLine(
        int count,
        decimal unitPrice,
        decimal vatRate,
        int lineNumber,
        string itemNumber = "1001"
    ) =>
        new()
        {
            ItemId = 1,
            ItemNumber = itemNumber,
            Description = "Grabpflege",
            Count = count,
            UnitPrice = unitPrice,
            VatRate = vatRate,
            LineNumber = lineNumber,
        };

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    public void Combine_reconstructs_the_grave_lines_from_evenly_split_shares(int recipients)
    {
        // Chosen so every recipient count in this theory divides both lines' net
        // cents (600 and 300) exactly, so each recipient's hand-written share
        // needs no rounding — a genuine remainder is covered separately below.
        var perRecipient = Enumerable
            .Range(0, recipients)
            .Select(_ =>
                (IReadOnlyList<InvoiceLine>)
                    new List<InvoiceLine>
                    {
                        RecipientLine(1, 6.00m / recipients, 7m, 1),
                        RecipientLine(2, 1.50m / recipients, 19m, 2, itemNumber: "1002"),
                    }
            )
            .ToList();

        var combined = GraveInvoiceSplitter.Combine(perRecipient);

        Assert.Equal(new[] { 6.00m, 1.50m }, combined.Select(l => l.UnitPrice));
        Assert.Equal(new[] { 1, 2 }, combined.Select(l => l.Count));
        Assert.Equal(new[] { "1001", "1002" }, combined.Select(l => l.ItemNumber));
        Assert.Equal(new[] { 7m, 19m }, combined.Select(l => l.VatRate));
    }

    [Fact]
    public void Combine_is_exact_even_for_amounts_too_small_to_round_cleanly()
    {
        // 1 x 2.60 net (19 % VAT) split three ways the way generation once did
        // it: 87 + 87 + 86 cents, the genuine remainder handed to the first two
        // recipients.
        var perRecipient = new[]
        {
            (IReadOnlyList<InvoiceLine>)
                new List<InvoiceLine> { RecipientLine(1, 0.87m, 19m, 1) },
            new List<InvoiceLine> { RecipientLine(1, 0.87m, 19m, 1) },
            new List<InvoiceLine> { RecipientLine(1, 0.86m, 19m, 1) },
        };

        var combined = Assert.Single(GraveInvoiceSplitter.Combine(perRecipient));

        Assert.Equal(2.60m, combined.UnitPrice);
    }

    [Fact]
    public void Combining_nothing_throws()
    {
        Assert.Throws<ArgumentException>(() =>
            GraveInvoiceSplitter.Combine(Array.Empty<IReadOnlyList<InvoiceLine>>())
        );
    }
}
