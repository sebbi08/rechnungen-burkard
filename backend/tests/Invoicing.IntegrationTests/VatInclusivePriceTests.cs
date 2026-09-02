using Burkard.Invoicing.Domain;
using Xunit;

namespace Burkard.Invoicing.IntegrationTests;

public class VatInclusivePriceTests
{
    [Fact]
    public void An_unflagged_price_is_already_net_and_is_returned_untouched()
    {
        Assert.Equal(50.42m, VatInclusivePrice.NetOf(50.42m, 19m, false, 1));
    }

    [Theory]
    [InlineData(120.00, 19, 100.84)]
    [InlineData(100.00, 19, 84.03)]
    [InlineData(107.00, 7, 100.00)]
    [InlineData(119.00, 7, 111.22)]
    public void A_flagged_price_gives_up_its_vat_at_two_decimals(
        decimal gross,
        decimal rate,
        decimal net
    )
    {
        Assert.Equal(net, VatInclusivePrice.NetOf(gross, rate, true, 1));
    }

    [Fact]
    public void A_rate_of_zero_leaves_the_price_alone()
    {
        Assert.Equal(120.00m, VatInclusivePrice.NetOf(120.00m, 0m, true, 1));
    }

    [Fact]
    public void A_gross_below_the_cent_is_rounded_to_the_cent_before_it_is_divided()
    {
        // a gross is what someone pays, so it is a cent amount; 120.005 is
        // billed as 120.01 rather than as a price that shifts on reopening
        Assert.Equal(
            VatInclusivePrice.NetOf(120.01m, 19m, true, 1),
            VatInclusivePrice.NetOf(120.005m, 19m, true, 1)
        );
    }

    [Fact]
    public void A_count_above_one_raises_the_net_rather_than_undercharging()
    {
        // 100,25 gross at 19 % is a net of 84,2437. Rounded down to 84,24 the
        // three of them bill 300,74 against the 300,75 that was entered, so the
        // net goes up a cent instead. The line then bills 300,77 — over, which
        // is the direction a rounding rule is allowed to go.
        Assert.Equal(84.24m, VatInclusivePrice.NetOf(100.25m, 19m, true, 1));
        Assert.Equal(84.25m, VatInclusivePrice.NetOf(100.25m, 19m, true, 3));
    }

    [Theory]
    [InlineData(7)]
    [InlineData(19)]
    public void A_line_is_never_billed_under_the_gross_it_was_entered_as(decimal rate)
    {
        foreach (var count in new[] { 1, 2, 3, 5, 10 })
        {
            for (var cents = 1; cents <= 4000; cents++)
            {
                var gross = cents / 100m;
                var net = VatInclusivePrice.NetOf(gross, rate, true, count);

                Assert.Equal(net, decimal.Round(net, 2));

                var lineNet = decimal.Round(net * count, 2, MidpointRounding.AwayFromZero);
                var billed =
                    lineNet
                    + decimal.Round(lineNet * rate / 100m, 2, MidpointRounding.AwayFromZero);
                Assert.True(
                    billed >= gross * count,
                    $"{gross} x {count} at {rate} % billed {billed}, under {gross * count}"
                );
            }
        }
    }

    // The round trip the revision dialogs depend on: the dialog shows
    // round(net × (1 + rate/100), 2) and sends it back, and the same net must
    // come out — otherwise every save subtracts the VAT again.
    //
    // The displayed gross is not always the one that was typed. A two-decimal
    // net cannot reach every gross — no whole-cent net bills 119,00 at 7 %, so
    // entering it shows 119,01 afterwards — but whatever it settles on is a
    // fixed point, which is what the dialog needs.
    [Theory]
    [InlineData(0.01, 19)]
    [InlineData(1.00, 19)]
    [InlineData(33.33, 19)]
    [InlineData(120.00, 19)]
    [InlineData(999.99, 19)]
    [InlineData(0.01, 7)]
    [InlineData(33.33, 7)]
    [InlineData(119.00, 7)]
    [InlineData(999.99, 7)]
    public void The_displayed_gross_converts_back_to_the_same_net(decimal gross, decimal rate)
    {
        var net = VatInclusivePrice.NetOf(gross, rate, true, 1);
        var displayed = decimal.Round(net * (1m + rate / 100m), 2, MidpointRounding.AwayFromZero);

        Assert.True(displayed >= gross);
        Assert.Equal(net, VatInclusivePrice.NetOf(displayed, rate, true, 1));
    }
}
