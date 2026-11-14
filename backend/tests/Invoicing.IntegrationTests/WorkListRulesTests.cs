using System;
using System.Linq;
using Burkard.Invoicing.Domain;
using Burkard.Invoicing.Domain.Entities;
using Xunit;

namespace Burkard.Invoicing.IntegrationTests;

/// <summary>The pure rules under the work lists: which months a period spans,
/// whether a grave runs in it, how grave numbers sort and what an item costs
/// gross.</summary>
public class WorkListRulesTests
{
    private static DateOnly M(int year, int month) => new(year, month, 1);

    [Fact]
    public void Year_spans_january_to_december() =>
        Assert.Equal(new MonthRange(M(2027, 1), M(2027, 12)), WorkListPeriod.Year(2027));

    [Theory]
    [InlineData(BillingQuarter.Spring, 1, 3)]
    [InlineData(BillingQuarter.Summer, 4, 6)]
    [InlineData(BillingQuarter.Autumn, 7, 9)]
    [InlineData(BillingQuarter.Winter, 10, 12)]
    public void Quarter_spans_its_three_months(BillingQuarter quarter, int first, int last) =>
        Assert.Equal(
            new MonthRange(M(2027, first), M(2027, last)),
            WorkListPeriod.Quarter(2027, quarter)
        );

    [Fact]
    public void Contains_is_inclusive_at_both_ends()
    {
        var range = WorkListPeriod.Quarter(2027, BillingQuarter.Summer);
        Assert.True(WorkListPeriod.Contains(range, M(2027, 4)));
        Assert.True(WorkListPeriod.Contains(range, M(2027, 6)));
        Assert.False(WorkListPeriod.Contains(range, M(2027, 3)));
        Assert.False(WorkListPeriod.Contains(range, M(2027, 7)));
    }

    [Fact]
    public void A_grave_runs_in_a_range_it_touches_by_one_month()
    {
        var year = WorkListPeriod.Year(2027);
        Assert.True(WorkListPeriod.Runs(M(2020, 1), M(2027, 1), year));
        Assert.True(WorkListPeriod.Runs(M(2027, 12), null, year));
        Assert.False(WorkListPeriod.Runs(M(2020, 1), M(2026, 12), year));
        Assert.False(WorkListPeriod.Runs(M(2028, 1), null, year));
        Assert.True(WorkListPeriod.Runs(M(2020, 1), null, year));
    }

    [Fact]
    public void A_quarter_touched_by_one_month_counts()
    {
        Assert.True(
            WorkListPeriod.Runs(
                M(2020, 1),
                M(2027, 1),
                WorkListPeriod.Quarter(2027, BillingQuarter.Spring)
            )
        );
        Assert.False(
            WorkListPeriod.Runs(
                M(2020, 1),
                M(2027, 1),
                WorkListPeriod.Quarter(2027, BillingQuarter.Summer)
            )
        );
    }

    [Fact]
    public void GraveNumberOrder_compares_digit_runs_as_numbers()
    {
        var sorted = new[] { "A-10", "A-2", "a-3", "B-1" }.OrderBy(
            s => s,
            GraveNumberOrder.Instance
        );
        Assert.Equal(new[] { "A-2", "a-3", "A-10", "B-1" }, sorted);
        Assert.True(GraveNumberOrder.Instance.Compare("9a", "10") < 0);
    }

    [Fact]
    public void GraveNumberOrder_handles_zeros_letters_and_huge_numbers()
    {
        var order = GraveNumberOrder.Instance;
        Assert.NotEqual(0, order.Compare("A-02", "A-2"));
        Assert.Equal(
            Math.Sign(order.Compare("A-02", "A-2")),
            -Math.Sign(order.Compare("A-2", "A-02"))
        );

        var names = new[] { "ohne", "Ohne", "OHNE" };
        var first = names.OrderBy(s => s, order).ToArray();
        var second = names.Reverse().OrderBy(s => s, order).ToArray();
        Assert.Equal(first, second);

        Assert.True(order.Compare("1" + new string('0', 30), "999") > 0);
    }

    [Fact]
    public void GrossOf_adds_the_vat_to_a_net_price_and_rounds_to_the_cent()
    {
        Assert.Equal(48.15m, VatInclusivePrice.GrossOf(45.00m, 7m));
        Assert.Equal(11.90m, VatInclusivePrice.GrossOf(10.00m, 19m));
        Assert.Equal(0.13m, VatInclusivePrice.GrossOf(0.125m, 0m));
    }
}
