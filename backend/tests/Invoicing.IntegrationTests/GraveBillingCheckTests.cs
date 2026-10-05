using System;
using System.Linq;
using Burkard.Invoicing.Domain;
using Burkard.Invoicing.Domain.Entities;
using Xunit;

namespace Burkard.Invoicing.IntegrationTests;

/// <summary>A grave is valid when every invoice its term produces splits evenly,
/// and a suggestion is a price at which all of them do.</summary>
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
        Assert.Empty(evaluation.Suggestions);
    }

    [Fact]
    public void The_start_invoice_can_fail_while_the_full_ones_divide()
    {
        // 60,02 is 6 002 cents, even; two months of it are 20,01, odd
        var evaluation = GraveBillingCheck.Evaluate([Care(60.02m)], May2026, null, 2);

        Assert.False(evaluation.Ok);
        var failing = evaluation.FirstFailing!;
        Assert.Equal(BillingExampleKind.Start, failing.Example.Kind);
        Assert.Equal(2001, Assert.Single(failing.Problems).Amount);
        Assert.True(evaluation.Invoices.Skip(1).All(i => i.Ok));
    }

    [Fact]
    public void A_suggestion_makes_every_invoice_divide()
    {
        // 60,01: the full ones are odd. 60,00 works everywhere; 60,02 and 60,04
        // fix the full ones but leave May/June odd (20,01); 60,06 works.
        var evaluation = GraveBillingCheck.Evaluate([Care(60.01m)], May2026, null, 2);

        Assert.Equal(new[] { 60.00m, 60.06m }, evaluation.Suggestions[0]);
        foreach (var price in evaluation.Suggestions[0])
        {
            Assert.True(GraveBillingCheck.Evaluate([Care(price)], May2026, null, 2).Ok);
        }
    }

    [Fact]
    public void A_line_that_cannot_fix_its_invoice_gets_no_suggestion()
    {
        // both full half-years fail on the 19 % care line. The 7 % Herbst line
        // sits on the failing H2 too, but no price of its own can fix a 19 %
        // subtotal, so it gets no entry
        var links = new[]
        {
            Care(60.01m),
            new BillableLink(2, "300", "Herbst", 7m, BillingQuarter.Autumn, 1, 30m, false),
        };

        var evaluation = GraveBillingCheck.Evaluate(links, null, null, 2);

        Assert.Equal(new[] { 0 }, evaluation.Suggestions.Keys);
    }

    [Fact]
    public void Suggestions_can_be_left_out()
    {
        var evaluation = GraveBillingCheck.Evaluate(
            [Care(60.01m)],
            May2026,
            null,
            2,
            withSuggestions: false
        );

        Assert.False(evaluation.Ok);
        Assert.Empty(evaluation.Suggestions);
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

        // start in May: H1 2026 bills nothing, the full H1 bills 40,01 (odd)
        var evaluation = GraveBillingCheck.Evaluate([spring], May2026, null, 2);

        Assert.True(evaluation.Invoices[0].Ok);
        Assert.Empty(evaluation.Invoices[0].Lines);
        Assert.False(evaluation.Invoices[1].Ok);
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
