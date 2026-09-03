using System;
using System.Collections.Generic;
using System.Linq;
using Burkard.Invoicing.Domain;
using Xunit;

namespace Burkard.Invoicing.IntegrationTests;

/// <summary>The split rule reports what does not come out even; it never adjusts
/// a price. Raising prices automatically was tried and abandoned — it put
/// figures on the page nobody had typed, and left the grave's worth and what the
/// recipients paid a cent apart.</summary>
public class GraveShareTests
{
    private static GraveLine Net(int count, decimal unitPrice, decimal vatRate) =>
        new(1, "1001", "Grabpflege", count, unitPrice, vatRate, false);

    private static GraveLine Gross(int count, decimal enteredGross, decimal vatRate) =>
        new(
            1,
            "1001",
            "Grabpflege",
            count,
            VatInclusivePrice.NetOf(enteredGross, vatRate, true),
            vatRate,
            true,
            enteredGross
        );

    [Fact]
    public void A_subtotal_that_divides_is_accepted()
    {
        // 99,00 over three recipients: 9 900 cents, 3 300 each
        Assert.Empty(GraveShare.Problems(new[] { Net(1, 99m, 7m) }, 3));
    }

    [Fact]
    public void A_subtotal_that_does_not_divide_is_reported_with_what_would()
    {
        // 47,80 at 19 % over three: 4 780 cents leaves a remainder of one
        var problem = Assert.Single(GraveShare.Problems(new[] { Net(1, 47.80m, 19m) }, 3));

        Assert.Equal(19m, problem.VatRate);
        Assert.Equal(GraveShareProblemKind.NotDivisible, problem.Kind);
        Assert.Equal(4780, problem.NetSubtotal);
        // the message names a figure to aim at rather than only saying no
        Assert.Equal(new long[] { 4779, 4782 }, problem.NearbyValidSubtotals);
    }

    [Fact]
    public void Each_vat_rate_is_judged_on_its_own()
    {
        // 7 %: 118,50 divides by three. 19 %: 97,70 does not.
        var lines = new[]
        {
            Net(1, 100m, 7m),
            Net(5, 3.70m, 7m),
            Net(2, 24.95m, 19m),
            Net(1, 47.80m, 19m),
        };

        var problem = Assert.Single(GraveShare.Problems(lines, 3));
        Assert.Equal(19m, problem.VatRate);
        Assert.Equal(9770, problem.NetSubtotal);
    }

    [Fact]
    public void A_gross_price_that_cannot_be_billed_exactly_is_reported()
    {
        // The reported case. 100,25 entered as a gross at 19 % is a net of
        // 84,24, whose 8 424 cents divide evenly by two — but each recipient's
        // VAT rounds 8,0028 down to 8,00, so the two of them collect 100,24
        // against the 100,25 that was typed. The old rule raised the price to
        // cover that; this one refuses the grave and says so.
        var problem = Assert.Single(GraveShare.Problems(new[] { Gross(1, 100.25m, 19m) }, 2));

        Assert.Equal(GraveShareProblemKind.DoesNotAddUp, problem.Kind);
        Assert.Equal(10024, problem.Collected);
        Assert.Equal(10025, problem.Owed);
    }

    [Fact]
    public void A_gross_price_that_comes_out_even_is_accepted()
    {
        // 100,24 at 19 % is the same net of 84,24, and two shares of 50,12
        // collect exactly that
        Assert.Empty(GraveShare.Problems(new[] { Gross(1, 100.24m, 19m) }, 2));
    }

    [Fact]
    public void A_net_price_is_not_held_to_a_gross_it_never_promised()
    {
        // 84,24 net at 19 % over two: the shares collect 100,24 while net plus
        // VAT on the whole grave would be 100,25. A net price says what the net
        // is; the gross is derived, and what the recipients collect is simply
        // what the grave is worth. Only a price entered as a gross is a promise.
        Assert.Empty(GraveShare.Problems(new[] { Net(1, 84.24m, 19m) }, 2));
    }

    [Fact]
    public void A_single_recipient_divides_by_definition()
    {
        Assert.Empty(GraveShare.Problems(new[] { Net(1, 33.33m, 19m) }, 1));
    }

    [Fact]
    public void What_the_recipients_collect_is_the_graves_worth()
    {
        // 84,24 over two: 42,12 each plus 8,00 VAT each
        Assert.Equal(10024, GraveShare.CollectedCents(new[] { Net(1, 84.24m, 19m) }, 2));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_non_positive_recipient_count_is_refused(int recipients)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            GraveShare.Problems(new[] { Net(1, 10m, 7m) }, recipients)
        );
    }

    [Fact]
    public void A_non_positive_count_is_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            GraveShare.Problems(new[] { Net(0, 10m, 7m) }, 2)
        );
    }

    [Theory]
    [InlineData(7)]
    [InlineData(19)]
    public void Anything_reported_as_valid_really_does_come_out_even(decimal rate)
    {
        // The guarantee the save relies on: a grave that passes can be billed
        // with every recipient paying the same whole number of cents, and those
        // shares collecting exactly what the lines are worth.
        for (var recipients = 1; recipients <= 7; recipients++)
        {
            for (var cents = 1; cents <= 3000; cents++)
            {
                var line = Net(1, cents / 100m, rate);
                if (GraveShare.Problems(new[] { line }, recipients).Count > 0)
                {
                    continue;
                }

                Assert.Equal(0, cents % recipients);
                Assert.Equal(
                    GraveShare.CollectedCents(new[] { line }, recipients),
                    recipients * (cents / recipients + Vat(cents / recipients, rate))
                );
            }
        }
    }

    [Theory]
    [InlineData(7)]
    [InlineData(19)]
    public void A_gross_price_that_passes_is_billed_to_the_cent(decimal rate)
    {
        for (var recipients = 1; recipients <= 7; recipients++)
        {
            for (var cents = 1; cents <= 3000; cents++)
            {
                var line = Gross(1, cents / 100m, rate);
                if (GraveShare.Problems(new[] { line }, recipients).Count > 0)
                {
                    continue;
                }

                // what was typed is what the recipients collect between them
                Assert.Equal(cents, GraveShare.CollectedCents(new[] { line }, recipients));
            }
        }
    }

    private static long Vat(long netCents, decimal rate) =>
        (long)decimal.Round(netCents * rate / 100m, 0, MidpointRounding.AwayFromZero);
}
