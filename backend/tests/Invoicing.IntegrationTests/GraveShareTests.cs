using System;
using System.Collections.Generic;
using System.Linq;
using Burkard.Invoicing.Domain;
using Xunit;

namespace Burkard.Invoicing.IntegrationTests;

public class GraveShareTests
{
    private static GraveLine Line(int count, decimal unitPrice, decimal vatRate) =>
        new(1, "1001", "Grabpflege", count, unitPrice, vatRate, false);

    private static long NetCents(GraveLine l) =>
        (long)decimal.Round(l.Count * l.UnitPrice * 100m, 0, MidpointRounding.AwayFromZero);

    [Fact]
    public void Subtotals_that_already_divide_are_left_alone()
    {
        var lines = new[] { Line(1, 99m, 7m) };

        var adjusted = GraveShare.MakeDivisible(lines, 3);

        Assert.Equal(99m, Assert.Single(adjusted).UnitPrice);
    }

    [Fact]
    public void Each_vat_rate_divides_on_its_own()
    {
        // 7%: 118,50 already divides by 3. 19%: 97,70 does not.
        var lines = new[]
        {
            Line(1, 100m, 7m),
            Line(5, 3.70m, 7m),
            Line(2, 24.95m, 19m),
            Line(1, 47.80m, 19m),
        };

        var adjusted = GraveShare.MakeDivisible(lines, 3);

        foreach (var rate in new[] { 7m, 19m })
        {
            var subtotal = adjusted.Where(l => l.VatRate == rate).Sum(NetCents);
            Assert.Equal(0, subtotal % 3);
        }
    }

    [Fact]
    public void The_cheapest_line_takes_the_cents()
    {
        // 19%: 97,70 over three needs one cent. Bumping the count-1 line costs
        // one cent; bumping the count-2 line would cost two.
        var lines = new[] { Line(2, 24.95m, 19m), Line(1, 47.80m, 19m) };

        var adjusted = GraveShare.MakeDivisible(lines, 3);

        Assert.Equal(24.95m, adjusted[0].UnitPrice);
        Assert.Equal(47.81m, adjusted[1].UnitPrice);
    }

    [Fact]
    public void Bumping_a_two_decimal_price_keeps_it_at_two_decimals()
    {
        // A bump adds exactly 0,01, so it cannot introduce a third decimal. It
        // says nothing about prices that arrive with three already: a
        // VAT-included grave line is billed on VatInclusivePrice.NetOf's
        // 3-decimal net, and MakeDivisible carries that precision through
        // untouched — see the design's Limits section.
        var lines = new[] { Line(3, 8.31m, 19m), Line(7, 2.22m, 19m) };

        var adjusted = GraveShare.MakeDivisible(lines, 4);

        Assert.All(adjusted, l => Assert.Equal(l.UnitPrice, decimal.Round(l.UnitPrice, 2)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_non_positive_recipient_count_is_rejected(int recipients)
    {
        var lines = new[] { Line(1, 10.00m, 19m) };

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            GraveShare.MakeDivisible(lines, recipients)
        );
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_non_positive_line_count_is_rejected(int count)
    {
        // Count 0 would give the residue graph a zero-cost self-loop that the
        // lexicographic tie-break keeps taking, so the relaxation would never
        // settle; a negative count would index the cost array negatively. Both
        // are caught up front rather than reached.
        var lines = new[] { Line(1, 10.00m, 19m), Line(count, 5.00m, 19m) };

        Assert.Throws<ArgumentOutOfRangeException>(() => GraveShare.MakeDivisible(lines, 3));
    }

    // Round-robin — add a cent to each line in turn — cannot solve this: one lap
    // adds three cents, which is zero modulo three, so every lap returns to the
    // same residue and only two of the three are ever reachable.
    [Fact]
    public void The_case_round_robin_cannot_solve()
    {
        var lines = new[] { Line(1, 24.95m, 19m), Line(2, 0.01m, 19m) };

        var adjusted = GraveShare.MakeDivisible(lines, 3);

        Assert.Equal(0, adjusted.Sum(NetCents) % 3);
    }

    [Fact]
    public void It_never_lowers_a_price()
    {
        var lines = new[] { Line(1, 33.33m, 19m), Line(2, 11.11m, 7m) };

        var adjusted = GraveShare.MakeDivisible(lines, 3);

        Assert.True(adjusted.Zip(lines).All(p => p.First.UnitPrice >= p.Second.UnitPrice));
    }

    [Fact]
    public void Same_count_lines_break_ties_by_line_number()
    {
        // At 0 % the covering test is satisfied by anything that divides, so
        // this isolates the tie-break; the rates above show the two working
        // together.
        // Both lines have count 2, so a single bump of either moves the
        // subtotal by 2 cents — bumping either is equally cheap.
        // Net cents: 2*10.00*100 + 2*10.00*100 = 4000; 4000 % 3 = 1, so 2 more
        // cents are needed (the next multiple of 3 is 4002), reachable in one
        // bump of either line. The tie-break must land on the lower line
        // number: line 0 gets the cent, line 1 is untouched.
        var lines = new[] { Line(2, 10.00m, 0m), Line(2, 10.00m, 0m) };

        var adjusted = GraveShare.MakeDivisible(lines, 3);

        Assert.Equal(10.01m, adjusted[0].UnitPrice);
        Assert.Equal(10.00m, adjusted[1].UnitPrice);
    }

    [Fact]
    public void Equal_cost_ties_are_broken_by_the_lexicographically_smallest_bump_set()
    {
        // Counts [1, 3] with 4 recipients — the counterexample that defeats a
        // "lower line number wins" rule taken literally. Net cents:
        // 1*10.01*100 + 3*10.00*100 = 1001 + 3000 = 4001; 4001 % 4 = 1, so 3
        // more cents are needed to reach 4004.
        // Two combinations reach exactly 3 cents: one bump of the count-3 line
        // (displacement 3, path [1]), or three bumps of the count-1 line
        // (displacement 1+1+1 = 3, path [0, 0, 0]). Both are minimal — no
        // smaller non-negative displacement is congruent to 3 mod 4.
        // Comparing the sorted bump lists lexicographically, [0, 0, 0] sorts
        // before [1] (their first elements are 0 and 1), so the three bumps of
        // the count-1 line win, not the single bump of the count-3 line.
        // At 0 %, so that the covering test does not raise past the tie.
        var lines = new[] { Line(1, 10.01m, 0m), Line(3, 10.00m, 0m) };

        var adjusted = GraveShare.MakeDivisible(lines, 4);

        Assert.Equal(10.04m, adjusted[0].UnitPrice);
        Assert.Equal(10.00m, adjusted[1].UnitPrice);
    }

    [Fact]
    public void A_subtotal_that_divides_is_still_raised_when_the_shares_would_not_cover_it()
    {
        // The bug this rule was extended for. 100,25 gross at 19 % is a net of
        // 84,24, whose 8 424 cents divide evenly by two — 42,12 each. But each
        // recipient's VAT rounds 8,0028 down to 8,00, where the grave's own
        // 16,0056 rounds up to 16,01, so the two of them would collect 100,24
        // against a bill of 100,25. Dividing is not enough; the subtotal is
        // raised to 84,26, where the shares of 42,13 collect 100,26.
        var lines = new[] { Line(1, 84.24m, 19m) };

        var adjusted = GraveShare.MakeDivisible(lines, 2);

        Assert.Equal(84.26m, Assert.Single(adjusted).UnitPrice);
    }

    [Theory]
    [InlineData(7)]
    [InlineData(19)]
    public void The_shares_always_collect_at_least_the_grave_s_own_bill(decimal rate)
    {
        for (var recipients = 1; recipients <= 8; recipients++)
        {
            for (var cents = 1; cents <= 1500; cents++)
            {
                var adjusted = GraveShare.MakeDivisible(
                    new[] { Line(1, cents / 100m, rate) },
                    recipients
                );
                var subtotal = NetCents(adjusted[0]);
                var share = subtotal / recipients;
                // the bill as entered, not as raised
                var owed = cents + Vat(cents, rate);

                Assert.Equal(0, subtotal % recipients);
                Assert.True(
                    recipients * (share + Vat(share, rate)) >= owed,
                    $"{cents} cents at {rate} % over {recipients} recipients falls short"
                );
                Assert.True(adjusted[0].UnitPrice >= cents / 100m);
            }
        }
    }

    private static long Vat(long netCents, decimal rate) =>
        (long)decimal.Round(netCents * rate / 100m, 0, MidpointRounding.AwayFromZero);

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(7)]
    public void It_terminates_for_every_combination_of_counts(int recipients)
    {
        foreach (var a in new[] { 1, 2, 3, 4, 5, 7, 10 })
        foreach (var b in new[] { 1, 2, 3, 4, 5, 7, 10 })
        {
            var lines = new[] { Line(a, 24.95m, 19m), Line(b, 3.70m, 19m) };

            var adjusted = GraveShare.MakeDivisible(lines, recipients);

            Assert.Equal(0, adjusted.Sum(NetCents) % recipients);
        }
    }
}
