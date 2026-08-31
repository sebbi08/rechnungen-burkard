using System;
using System.Linq;
using Burkard.Invoicing.Domain;
using Xunit;

namespace Burkard.Invoicing.IntegrationTests;

public class GraveInvoiceSplitterTests
{
    private static GraveLine Line(
        int count,
        decimal unitPrice,
        decimal vatRate,
        bool priceIncludesVat = false
    ) =>
        new(
            ItemId: 1,
            ItemNumber: "1001",
            Description: "Grabpflege",
            count,
            unitPrice,
            vatRate,
            priceIncludesVat
        );

    [Fact]
    public void One_recipient_gets_the_grave_line_unchanged()
    {
        var split = GraveInvoiceSplitter.Split(new[] { Line(2, 120m, 7m) }, 1);

        var line = Assert.Single(Assert.Single(split));
        Assert.Equal(2, line.Count);
        Assert.Equal(120m, line.UnitPrice);
        Assert.Equal(7m, line.VatRate);
        Assert.Equal(1, line.LineNumber);
        Assert.Equal("1001", line.ItemNumber);
    }

    [Fact]
    public void Every_line_is_split_on_its_own_so_proportions_survive()
    {
        var split = GraveInvoiceSplitter.Split(new[] { Line(2, 120m, 7m), Line(1, 50m, 19m) }, 2);

        Assert.Equal(2, split.Count);
        Assert.All(split, lines => Assert.Equal(2, lines.Count));
        Assert.All(split, lines => Assert.Equal(60m, lines[0].UnitPrice));
        Assert.All(split, lines => Assert.Equal(25m, lines[1].UnitPrice));
        // the count stays the grave's count; only the price is a share
        Assert.All(split, lines => Assert.Equal(2, lines[0].Count));
        Assert.All(split, lines => Assert.Equal(new[] { 1, 2 }, lines.Select(l => l.LineNumber)));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    public void Combine_is_the_inverse_of_split(int recipients)
    {
        var lines = new[] { Line(2, 120m, 7m), Line(3, 33.33m, 19m), Line(1, 1000m, 7m) };

        var combined = GraveInvoiceSplitter.Combine(GraveInvoiceSplitter.Split(lines, recipients));

        Assert.Equal(lines.Select(l => l.UnitPrice), combined.Select(l => l.UnitPrice));
        Assert.Equal(lines.Select(l => l.Count), combined.Select(l => l.Count));
        Assert.Equal(lines.Select(l => l.ItemNumber), combined.Select(l => l.ItemNumber));
        Assert.Equal(lines.Select(l => l.VatRate), combined.Select(l => l.VatRate));
    }

    /// <summary>The net cents a stored line actually bills: what
    /// DocumentDataBuilder derives from the persisted count and unit price.</summary>
    private static long BilledNetCents(Domain.Entities.InvoiceLine line) =>
        (long)Math.Round(line.Count * line.UnitPrice * 100m, 0, MidpointRounding.AwayFromZero);

    // The fee being divided is the net one, so the net is what has to divide
    // exactly: every recipient's invoice then adds up on its own, and together
    // they bill the grave's net to the cent (SC-004).
    [Fact]
    public void Net_shares_sum_exactly_to_the_grave_net()
    {
        var split = GraveInvoiceSplitter.Split(new[] { Line(1, 2.60m, 19m) }, 3);

        var billed = split.Sum(lines => BilledNetCents(lines[0]));
        Assert.Equal(260, billed);
    }

    [Fact]
    public void Combine_is_exact_even_for_amounts_too_small_to_round_cleanly()
    {
        var lines = new[] { Line(1, 2.60m, 19m) };

        var combined = Assert.Single(
            GraveInvoiceSplitter.Combine(GraveInvoiceSplitter.Split(lines, 3))
        );

        Assert.Equal(2.60m, combined.UnitPrice);
    }

    // A documented boundary, not a wish. A line stores a count and a unit price,
    // and the invoice prints that price with at most three decimals, so the
    // smallest net step a line can express is count x 0.1 cents. From a count of
    // ten upwards the shares are therefore not always placeable to the cent.
    // Storing more decimals would make the printed price stop reproducing the
    // printed line total, which is the worse defect.
    [Fact]
    public void Split_cannot_place_every_cent_once_the_count_reaches_ten()
    {
        // grave net 40.02 over two recipients: 20.01 each, which 20 units cannot
        // express in three decimals (1.0005 rounds to 1.001, billing 20.02)
        var split = GraveInvoiceSplitter.Split(new[] { Line(20, 2.001m, 19m) }, 2);

        var billed = split.Sum(lines => BilledNetCents(lines[0]));
        Assert.Equal(4004, billed);
    }

    [Fact]
    public void Zero_recipients_throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            GraveInvoiceSplitter.Split(new[] { Line(1, 100m, 7m) }, 0)
        );
    }

    [Fact]
    public void Combining_nothing_throws()
    {
        Assert.Throws<ArgumentException>(() =>
            GraveInvoiceSplitter.Combine(
                Array.Empty<System.Collections.Generic.IReadOnlyList<Domain.Entities.InvoiceLine>>()
            )
        );
    }

    [Fact]
    public void Every_recipient_line_remembers_how_the_grave_price_was_entered()
    {
        var lines = new[] { new GraveLine(1, "1001", "Grabpflege", 1, 100.840m, 19m, true) };

        var split = GraveInvoiceSplitter.Split(lines, 2);

        Assert.All(split, recipient => Assert.True(Assert.Single(recipient).PriceIncludesVat));
        Assert.True(Assert.Single(GraveInvoiceSplitter.Combine(split)).PriceIncludesVat);
    }
}
