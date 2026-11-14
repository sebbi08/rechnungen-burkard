using System;
using System.Collections.Generic;
using System.Linq;
using Burkard.Invoicing.Domain;
using Xunit;

namespace Burkard.Invoicing.IntegrationTests;

/// <summary>The split rule reports what does not come out even; it never adjusts
/// a price itself. Where one has to change, GraveBillingCheck raises it by the
/// cents it needs, and the user confirms the raise before the grave is saved
/// (see GraveBillingCorrectionTests).</summary>
public class GraveShareTests
{
    private static GraveLine Net(int count, decimal unitPrice, decimal vatRate) =>
        new(1, "1001", "Grabpflege", count, unitPrice, vatRate);

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
        Assert.Equal(4780, problem.Amount);
        // one below and one above, so the message names figures to aim at
        // rather than only saying no
        Assert.Equal(new long[] { 4779, 4782 }, problem.Suggestions);
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
        Assert.Equal(9770, problem.Amount);
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

    /// <summary>Asked about lines that do not divide, it used to drop the odd
    /// cents without a word and answer an amount nobody is billed.</summary>
    [Fact]
    public void What_the_recipients_collect_is_not_answered_for_lines_that_do_not_divide()
    {
        Assert.Throws<InvalidOperationException>(() =>
            GraveShare.CollectedCents(new[] { Net(1, 84.25m, 19m) }, 2)
        );
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

    private static long Vat(long netCents, decimal rate) =>
        (long)decimal.Round(netCents * rate / 100m, 0, MidpointRounding.AwayFromZero);
}
