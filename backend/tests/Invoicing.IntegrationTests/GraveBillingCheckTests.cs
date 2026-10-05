using System;
using System.Linq;
using Burkard.Invoicing.Domain;
using Burkard.Invoicing.Domain.Entities;
using Xunit;

namespace Burkard.Invoicing.IntegrationTests;

/// <summary>A grave is valid when every invoice its term produces splits evenly,
/// after the rounding corrections that make it so.</summary>
public class GraveBillingCheckTests
{
    private static readonly DateOnly May2026 = new(2026, 5, 1);

    private static BillableLink Care(decimal price) =>
        new(1, "100", "Grabpflege", 19m, null, 1, price, false);

    [Fact]
    public void A_grave_whose_every_invoice_divides_is_ok()
    {
        // 60,00 full, 20,00 for May/June: both even over two
        var evaluation = GraveBillingCheck.Evaluate([Care(60m)], May2026, null, 2);

        Assert.True(evaluation.Ok);
        Assert.Equal(3, evaluation.Invoices.Count);
        Assert.All(evaluation.Invoices, i => Assert.Empty(i.Corrections));
    }

    [Fact]
    public void The_start_invoice_is_corrected_while_the_full_ones_need_nothing()
    {
        // 60,02 is 6 002 cents, even; two months of it are 20,01, odd
        var evaluation = GraveBillingCheck.Evaluate([Care(60.02m)], May2026, null, 2);

        Assert.True(evaluation.Ok);
        var correction = Assert.Single(evaluation.Invoices[0].Corrections);
        Assert.Equal(BillingExampleKind.Start, evaluation.Invoices[0].Example.Kind);
        Assert.Equal(20.01m, correction.EnteredPrice);
        Assert.True(correction.CorrectedPrice > correction.EnteredPrice);
        Assert.All(evaluation.Invoices.Skip(1), i => Assert.Empty(i.Corrections));
    }

    [Fact]
    public void An_invoice_with_nothing_to_bill_is_ok()
    {
        var spring = new BillableLink(
            2,
            "200",
            "Frühling",
            19m,
            BillingQuarter.Spring,
            1,
            40.01m,
            false
        );

        // start in May: H1 2026 bills nothing, the full H1 bills 40,01 (odd) and
        // is corrected
        var evaluation = GraveBillingCheck.Evaluate([spring], May2026, null, 2);

        Assert.True(evaluation.Invoices[0].Ok);
        Assert.Empty(evaluation.Invoices[0].Lines);
        Assert.Empty(evaluation.Invoices[0].Corrections);
        Assert.Single(evaluation.Invoices[1].Corrections);
        Assert.True(evaluation.Invoices[1].Ok);
    }

    [Fact]
    public void IsEven_agrees_with_Problems()
    {
        var lines = HalfYearBilling.Lines([Care(47.80m)], BillingPeriod.Full(1));

        Assert.False(GraveShare.IsEven(lines, 3));
        Assert.True(
            GraveShare.IsEven(HalfYearBilling.Lines([Care(47.79m)], BillingPeriod.Full(1)), 3)
        );
    }
}
