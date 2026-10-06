using System;
using System.Linq;
using Burkard.Invoicing.Domain;
using Burkard.Invoicing.Domain.Entities;
using Xunit;

namespace Burkard.Invoicing.IntegrationTests;

/// <summary>Which months of a half-year a grave's term covers, and what that
/// bills: all-year items pro rata, quarter items in full when their quarter is
/// touched.</summary>
public class HalfYearBillingTests
{
    private static DateOnly M(int year, int month) => new(year, month, 1);

    // a current half-year before every term these tests start: nothing is left out
    private static readonly (int Year, int Half) EarlierHalf = (2025, 2);

    private static BillableLink Care(decimal price = 60m, bool gross = false) =>
        new(1, "100", "Grabpflege", 19m, null, 1, price, gross);

    private static BillableLink Seasonal(BillingQuarter quarter, decimal price = 40m) =>
        new(2, "210", $"Bepflanzung {quarter}", 19m, quarter, 1, price, false);

    [Fact]
    public void PeriodIn_cuts_the_first_half_year_at_the_start()
    {
        Assert.Equal(
            new BillingPeriod(2026, 1, 5, 6),
            HalfYearBilling.PeriodIn(M(2026, 5), null, 2026, 1)
        );
        Assert.Equal(
            new BillingPeriod(2026, 2, 7, 12),
            HalfYearBilling.PeriodIn(M(2026, 5), null, 2026, 2)
        );
        Assert.Null(HalfYearBilling.PeriodIn(M(2026, 5), null, 2025, 2));
    }

    [Fact]
    public void PeriodIn_cuts_the_last_half_year_at_the_end()
    {
        Assert.Equal(
            new BillingPeriod(2030, 2, 7, 8),
            HalfYearBilling.PeriodIn(M(2026, 5), M(2030, 8), 2030, 2)
        );
        Assert.Null(HalfYearBilling.PeriodIn(M(2026, 5), M(2030, 8), 2031, 1));
    }

    [Fact]
    public void PeriodIn_ignores_the_day()
    {
        // tests seed ends like 2040-12-31; the term is counted in months
        Assert.Equal(
            new BillingPeriod(2040, 2, 7, 12),
            HalfYearBilling.PeriodIn(new DateOnly(2020, 1, 1), new DateOnly(2040, 12, 31), 2040, 2)
        );
        Assert.Equal(
            new BillingPeriod(2026, 1, 6, 6),
            HalfYearBilling.PeriodIn(new DateOnly(2026, 6, 15), null, 2026, 1)
        );
    }

    [Fact]
    public void An_all_year_item_is_prorated_with_a_note()
    {
        var line = Assert.Single(
            HalfYearBilling.Lines(new[] { Care() }, new BillingPeriod(2026, 1, 5, 6))
        );

        Assert.Equal(20.00m, line.UnitPrice);
        Assert.Equal("Grabpflege, anteilig 05–06/2026 (2 von 6 Monaten)", line.Description);
        Assert.Equal(1, line.Count);
    }

    [Fact]
    public void A_single_month_is_named_once()
    {
        var line = Assert.Single(
            HalfYearBilling.Lines(new[] { Care() }, new BillingPeriod(2026, 1, 6, 6))
        );

        Assert.Equal(10.00m, line.UnitPrice);
        Assert.Equal("Grabpflege, anteilig 06/2026 (1 von 6 Monaten)", line.Description);
    }

    [Fact]
    public void A_full_half_year_bills_the_price_without_a_note()
    {
        var line = Assert.Single(HalfYearBilling.Lines(new[] { Care() }, BillingPeriod.Full(2)));

        Assert.Equal(60.00m, line.UnitPrice);
        Assert.Equal("Grabpflege", line.Description);
    }

    [Fact]
    public void A_prorated_price_is_rounded_to_the_cent()
    {
        // 50,00 over five months is 41,666…
        var line = Assert.Single(
            HalfYearBilling.Lines(new[] { Care(50m) }, new BillingPeriod(2026, 1, 2, 6))
        );
        Assert.Equal(41.67m, line.UnitPrice);
    }

    [Fact]
    public void A_gross_price_is_prorated_as_a_gross()
    {
        var billed = Assert.Single(
            HalfYearBilling.Bill(
                new[] { Care(119m, gross: true) },
                new BillingPeriod(2026, 1, 4, 6)
            )
        );

        Assert.Equal(59.50m, billed.Price);
        Assert.Equal(59.50m, billed.Line.EnteredGross);
        Assert.Equal(VatInclusivePrice.NetOf(59.50m, 19m, true), billed.Line.UnitPrice);
    }

    [Theory]
    [InlineData(BillingQuarter.Summer, 5, true)] // May/June touch Apr–Jun
    [InlineData(BillingQuarter.Spring, 5, false)] // Jan–Mar is over by May
    [InlineData(BillingQuarter.Spring, 3, true)] // March is enough
    [InlineData(BillingQuarter.Autumn, 1, false)] // the other half-year
    public void A_quarter_item_is_billed_in_full_when_its_quarter_is_touched(
        BillingQuarter quarter,
        int startMonth,
        bool billed
    )
    {
        var lines = HalfYearBilling.Lines(
            new[] { Seasonal(quarter) },
            new BillingPeriod(2026, 1, startMonth, 6)
        );

        if (billed)
        {
            var line = Assert.Single(lines);
            Assert.Equal(40.00m, line.UnitPrice);
            Assert.Equal($"Bepflanzung {quarter}", line.Description);
        }
        else
        {
            Assert.Empty(lines);
        }
    }

    [Fact]
    public void The_end_half_year_bills_the_quarters_it_touches()
    {
        var period = HalfYearBilling.PeriodIn(M(2026, 5), M(2030, 8), 2030, 2)!;
        var lines = HalfYearBilling.Bill(
            new[]
            {
                Care(),
                Seasonal(BillingQuarter.Autumn, 30m),
                Seasonal(BillingQuarter.Winter, 25m),
            },
            period
        );

        Assert.Equal(new[] { 0, 1 }, lines.Select(l => l.LinkIndex));
        Assert.Equal(new[] { 20.00m, 30.00m }, lines.Select(l => l.Line.UnitPrice));
    }

    [Fact]
    public void Without_a_start_only_the_full_half_years_are_examples()
    {
        Assert.Equal(
            new[] { BillingExampleKind.FullFirstHalf, BillingExampleKind.FullSecondHalf },
            HalfYearBilling.Examples(null, null, EarlierHalf).Select(e => e.Kind)
        );
    }

    [Fact]
    public void Examples_of_an_open_ended_term()
    {
        var examples = HalfYearBilling.Examples(M(2026, 5), null, EarlierHalf);

        Assert.Equal(
            new[]
            {
                BillingExampleKind.Start,
                BillingExampleKind.FullFirstHalf,
                BillingExampleKind.FullSecondHalf,
            },
            examples.Select(e => e.Kind)
        );
        Assert.Equal(new BillingPeriod(2026, 1, 5, 6), examples[0].Period);
    }

    [Fact]
    public void Examples_of_a_term_with_an_end()
    {
        var examples = HalfYearBilling.Examples(M(2026, 5), M(2030, 8), EarlierHalf);

        Assert.Equal(
            new[]
            {
                BillingExampleKind.Start,
                BillingExampleKind.FullFirstHalf,
                BillingExampleKind.FullSecondHalf,
                BillingExampleKind.End,
            },
            examples.Select(e => e.Kind)
        );
        Assert.Equal(new BillingPeriod(2030, 2, 7, 8), examples[3].Period);
    }

    [Fact]
    public void Examples_of_a_term_without_a_full_half_year()
    {
        var examples = HalfYearBilling.Examples(M(2026, 5), M(2026, 8), EarlierHalf);

        Assert.Equal(
            new[] { BillingExampleKind.Start, BillingExampleKind.End },
            examples.Select(e => e.Kind)
        );
    }

    [Fact]
    public void Examples_of_a_term_inside_one_half_year()
    {
        var example = Assert.Single(HalfYearBilling.Examples(M(2026, 2), M(2026, 4), EarlierHalf));

        Assert.Equal(BillingExampleKind.StartAndEnd, example.Kind);
        Assert.Equal(new BillingPeriod(2026, 1, 2, 4), example.Period);
    }

    [Fact]
    public void Examples_list_only_the_full_half_years_that_occur()
    {
        // H2 2026 lies entirely inside, no H1 does
        var examples = HalfYearBilling.Examples(M(2026, 7), M(2027, 3), EarlierHalf);

        Assert.Equal(
            new[]
            {
                BillingExampleKind.Start,
                BillingExampleKind.FullSecondHalf,
                BillingExampleKind.End,
            },
            examples.Select(e => e.Kind)
        );
        Assert.True(examples[0].Period.IsFull);
    }

    [Fact]
    public void HalfYearOf_names_the_half_of_a_moment()
    {
        Assert.Equal((2026, 1), HalfYearBilling.HalfYearOf(new DateTime(2026, 6, 30, 23, 0, 0)));
        Assert.Equal((2026, 2), HalfYearBilling.HalfYearOf(new DateTime(2026, 10, 6, 12, 0, 0)));
    }

    [Theory]
    [InlineData(2025, 2)]
    [InlineData(2026, 1)]
    [InlineData(2026, 2)]
    [InlineData(2027, 1)]
    public void Examples_keep_a_start_that_can_still_be_generated(int year, int half)
    {
        // the clock stands in H2 2026: H2 2025 is the earliest half-year generation reaches
        var from = M(year, half == 1 ? 2 : 8);

        Assert.Equal(
            BillingExampleKind.Start,
            HalfYearBilling.Examples(from, null, (2026, 2))[0].Kind
        );
        Assert.Equal(
            BillingExampleKind.StartAndEnd,
            Assert.Single(HalfYearBilling.Examples(from, from, (2026, 2))).Kind
        );
    }

    [Fact]
    public void Examples_leave_out_a_start_before_the_earliest_generatable_half_year()
    {
        Assert.Equal(
            new[] { BillingExampleKind.FullFirstHalf, BillingExampleKind.FullSecondHalf },
            HalfYearBilling.Examples(M(2025, 5), null, (2026, 2)).Select(e => e.Kind)
        );
        Assert.Empty(HalfYearBilling.Examples(M(2025, 2), M(2025, 4), (2026, 2)));
    }

    [Fact]
    public void The_earliest_generatable_half_year_is_two_back()
    {
        Assert.Equal((2025, 2), HalfYearBilling.EarliestGeneratable((2026, 2)));
        Assert.Equal((2025, 1), HalfYearBilling.EarliestGeneratable((2026, 1)));
        Assert.Equal((2024, 2), HalfYearBilling.EarliestGeneratable((2025, 2)));
    }

    [Fact]
    public void Examples_keep_the_end_of_a_term_even_when_it_started_long_ago()
    {
        var examples = HalfYearBilling.Examples(M(2020, 3), M(2026, 8), (2026, 2));

        Assert.Equal(BillingExampleKind.End, examples[^1].Kind);
    }

    [Fact]
    public void Labels_name_the_invoice()
    {
        var examples = HalfYearBilling.Examples(M(2026, 5), M(2030, 8), EarlierHalf);

        Assert.Equal(
            new[] { "Anfang H1 2026", "Volles H1", "Volles H2", "Ende H2 2030" },
            examples.Select(HalfYearBilling.Label)
        );
        Assert.Equal(
            "Anfang und Ende H1 2026",
            HalfYearBilling.Label(HalfYearBilling.Examples(M(2026, 2), M(2026, 4), EarlierHalf)[0])
        );
    }

    [Fact]
    public void An_end_before_the_start_is_refused()
    {
        Assert.Throws<ArgumentException>(() =>
            HalfYearBilling.Examples(M(2026, 5), M(2026, 4), EarlierHalf)
        );
    }

    [Fact]
    public void ExampleFor_names_the_block_of_a_concrete_half_year()
    {
        var start = HalfYearBilling.ExampleFor(M(2026, 5), M(2030, 8), 2026, 1)!;
        Assert.Equal(BillingExampleKind.Start, start.Kind);
        Assert.Equal(2026, start.Period.Year);

        var full = HalfYearBilling.ExampleFor(M(2026, 5), M(2030, 8), 2027, 2)!;
        Assert.Equal(BillingExampleKind.FullSecondHalf, full.Kind);
        Assert.Null(full.Period.Year);

        Assert.Equal(
            BillingExampleKind.End,
            HalfYearBilling.ExampleFor(M(2026, 5), M(2030, 8), 2030, 2)!.Kind
        );
        Assert.Null(HalfYearBilling.ExampleFor(M(2026, 5), M(2030, 8), 2025, 2));
        Assert.Equal(
            BillingExampleKind.StartAndEnd,
            HalfYearBilling.ExampleFor(M(2026, 2), M(2026, 4), 2026, 1)!.Kind
        );
    }
}
