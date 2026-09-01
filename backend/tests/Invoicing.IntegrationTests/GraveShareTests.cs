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
    public void Prices_keep_two_decimals()
    {
        var lines = new[] { Line(3, 8.31m, 19m), Line(7, 2.22m, 19m) };

        var adjusted = GraveShare.MakeDivisible(lines, 4);

        Assert.All(adjusted, l => Assert.Equal(l.UnitPrice, decimal.Round(l.UnitPrice, 2)));
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
        // Both lines have count 2, so a single bump of either moves the
        // subtotal by 2 cents — bumping either is equally cheap.
        // Net cents: 2*10.00*100 + 2*10.00*100 = 4000; 4000 % 3 = 1, so 2 more
        // cents are needed (the next multiple of 3 is 4002), reachable in one
        // bump of either line. The tie-break must land on the lower line
        // number: line 0 gets the cent, line 1 is untouched.
        var lines = new[] { Line(2, 10.00m, 19m), Line(2, 10.00m, 19m) };

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
        var lines = new[] { Line(1, 10.01m, 19m), Line(3, 10.00m, 19m) };

        var adjusted = GraveShare.MakeDivisible(lines, 4);

        Assert.Equal(10.04m, adjusted[0].UnitPrice);
        Assert.Equal(10.00m, adjusted[1].UnitPrice);
    }

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
