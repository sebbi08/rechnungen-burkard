using System;
using System.Linq;
using Burkard.Invoicing.Domain;
using Burkard.Invoicing.Domain.Entities;
using Xunit;

namespace Burkard.Invoicing.IntegrationTests;

/// <summary>A block that does not split evenly is fixed by raising one line of
/// the failing VAT rate by the smallest amount that works.</summary>
public class GraveBillingCorrectionTests
{
    private static readonly DateOnly March2026 = new(2026, 3, 1);
    private static readonly DateOnly October2028 = new(2028, 10, 1);
    private static readonly (int Year, int Half) EarlierHalf = (2025, 2);

    private static BillableLink AllYear(
        long id,
        decimal price,
        int count = 1,
        decimal rate = 19m,
        bool gross = false
    ) => new(id, $"{id}", $"Leistung {id}", rate, null, count, price, gross);

    private static BillableLink Seasonal(
        long id,
        BillingQuarter quarter,
        decimal price,
        decimal rate = 19m
    ) => new(id, $"{id}", $"Leistung {id}", rate, quarter, 1, price, false);

    [Fact]
    public void A_block_that_divides_needs_no_correction()
    {
        var evaluation = GraveBillingCheck.Evaluate([AllYear(1, 60m)], null, null, 2, EarlierHalf);

        Assert.True(evaluation.Ok);
        Assert.Empty(evaluation.CorrectionKeys);
    }

    [Fact]
    public void The_g102_example_raises_the_care_in_the_start_and_end_blocks()
    {
        // 3 recipients; start H1 2026 bills 40,00 + 45,00 = 85,00 and end H2 2028
        // bills 40,00 — both raised to 40,02; the full halves divide
        var links = new[] { AllYear(100, 60m), Seasonal(200, BillingQuarter.Spring, 45m) };

        var evaluation = GraveBillingCheck.Evaluate(links, March2026, October2028, 3, EarlierHalf);

        Assert.True(evaluation.Ok);
        Assert.Equal(
            new[]
            {
                new CorrectionKey(BillingExampleKind.Start, 2026, 1, 19m, 100, 40.02m),
                new CorrectionKey(BillingExampleKind.End, 2028, 2, 19m, 100, 40.02m),
            },
            evaluation.CorrectionKeys
        );
        var start = evaluation.Invoices[0];
        var correction = Assert.Single(start.Corrections);
        Assert.Equal(40.00m, correction.EnteredPrice);
        Assert.Equal(40.02m, start.Lines[0].Price);
        Assert.Contains("anteilig 03–06/2026", start.Lines[0].Line.Description);
        Assert.Equal(45.00m, start.Lines[1].Price);
    }

    [Fact]
    public void The_all_year_line_is_raised_before_a_seasonal_one_listed_first()
    {
        // full H1: 40,01 + 60,00 = 100,01 over 2 — the seasonal line comes first
        // in the list, but the all-year line takes the cent
        var links = new[] { Seasonal(1, BillingQuarter.Summer, 40.01m), AllYear(2, 60m) };

        var evaluation = GraveBillingCheck.Evaluate(links, null, null, 2, EarlierHalf);

        var key = Assert.Single(evaluation.CorrectionKeys);
        Assert.Equal(2, key.ItemId);
        Assert.Equal(60.01m, key.CorrectedPrice);
    }

    [Fact]
    public void The_next_line_takes_the_correction_when_the_first_cannot()
    {
        // all-year line with count 2 moves the subtotal in steps of 2 cents, and
        // the other line leaves an odd cent: over 2 recipients only the second
        // all-year line can fix it
        var links = new[] { AllYear(1, 30m, count: 2), AllYear(2, 10.01m) };

        var evaluation = GraveBillingCheck.Evaluate(links, null, null, 2, EarlierHalf);

        Assert.True(evaluation.Ok);
        var key = Assert.Single(
            evaluation.CorrectionKeys,
            k => k.Kind == BillingExampleKind.FullFirstHalf
        );
        Assert.Equal(2, key.ItemId);
        Assert.Equal(10.02m, key.CorrectedPrice);
    }

    [Fact]
    public void Each_rate_is_corrected_on_its_own()
    {
        var links = new[] { AllYear(1, 60.01m, rate: 19m), AllYear(2, 25.01m, rate: 7m) };

        var evaluation = GraveBillingCheck.Evaluate(links, null, null, 2, EarlierHalf);

        var full = evaluation.Invoices[0];
        Assert.Equal(new[] { 7m, 19m }, full.Corrections.Select(c => c.VatRate).OrderBy(r => r));
        Assert.True(full.Ok);
    }

    [Fact]
    public void A_gross_line_is_raised_as_a_gross()
    {
        // 100,25 gross at 19 % over 2 does not add up (100,24 collected)
        var evaluation = GraveBillingCheck.Evaluate(
            [AllYear(1, 100.25m, gross: true)],
            null,
            null,
            2,
            EarlierHalf
        );

        var correction = Assert.Single(evaluation.Invoices[0].Corrections);
        Assert.True(correction.CorrectedPrice > 100.25m);
        Assert.True(correction.CorrectedPrice - 100.25m <= 1.00m);
        var line = evaluation.Invoices[0].Lines[0].Line;
        Assert.Equal(correction.CorrectedPrice, line.EnteredGross);
        Assert.Equal(VatInclusivePrice.NetOf(correction.CorrectedPrice, 19m, true), line.UnitPrice);
        Assert.True(GraveShare.IsEven([line], 2));
    }

    [Fact]
    public void A_quarter_line_takes_the_correction_when_no_all_year_line_can()
    {
        // full H1: 2 × 10,00 + 2 × 10,00 + 0,01 = 40,01 over 2. Both all-year
        // lines move the subtotal in steps of 2 cents, so only the summer line
        // can take the cent
        var links = new[]
        {
            AllYear(1, 10m, count: 2),
            AllYear(2, 10m, count: 2),
            Seasonal(3, BillingQuarter.Summer, 0.01m),
        };

        var evaluation = GraveBillingCheck.Evaluate(links, null, null, 2, EarlierHalf);

        var key = Assert.Single(evaluation.CorrectionKeys);
        Assert.Equal(BillingExampleKind.FullFirstHalf, key.Kind);
        Assert.Equal(3, key.ItemId);
        Assert.Equal(0.02m, key.CorrectedPrice);
    }

    [Fact]
    public void ApplyCorrections_bills_the_stored_price_for_the_item_and_rate()
    {
        var links = new[] { AllYear(100, 60m) };
        var billed = HalfYearBilling.Bill(links, new BillingPeriod(2026, 1, 3, 6));

        var corrected = GraveBillingCheck.ApplyCorrections(links, billed, [(100L, 19m, 40.02m)]);

        Assert.Equal(40.02m, corrected[0].Price);
        Assert.Equal(billed[0].Line.Description, corrected[0].Line.Description);
    }

    [Fact]
    public void SameCorrections_ignores_order_and_decimal_scale()
    {
        var a = new[]
        {
            new CorrectionKey(BillingExampleKind.Start, 2026, 1, 19m, 1, 40.02m),
            new CorrectionKey(BillingExampleKind.End, 2028, 2, 19m, 1, 40.02m),
        };
        var b = new[]
        {
            new CorrectionKey(BillingExampleKind.End, 2028, 2, 19.0m, 1, 40.020m),
            new CorrectionKey(BillingExampleKind.Start, 2026, 1, 19m, 1, 40.02m),
        };

        Assert.True(GraveBillingCheck.SameCorrections(a, b));
        Assert.False(GraveBillingCheck.SameCorrections(a, b.Take(1)));
    }
}
