using Burkard.Invoicing.Domain;
using Xunit;

namespace Burkard.Invoicing.IntegrationTests;

public class VatInclusivePriceTests
{
    [Fact]
    public void An_unflagged_price_is_already_net_and_is_returned_untouched()
    {
        Assert.Equal(50.42m, VatInclusivePrice.NetOf(50.42m, 19m, false));
    }

    // Decimal literals rather than InlineData: an attribute can only hold a
    // double, and since .NET 11 a double converts to decimal with all its
    // binary digits, so 100.84 arrives as 100.84000000000000341060513165.
    public static readonly TheoryData<decimal, decimal, decimal> FlaggedPrices = new()
    {
        { 120.00m, 19m, 100.84m },
        { 100.00m, 19m, 84.03m },
        { 107.00m, 7m, 100.00m },
        { 119.00m, 7m, 111.21m },
    };

    [Theory]
    [MemberData(nameof(FlaggedPrices))]
    public void A_flagged_price_gives_up_its_vat_at_two_decimals(
        decimal gross,
        decimal rate,
        decimal net
    )
    {
        Assert.Equal(net, VatInclusivePrice.NetOf(gross, rate, true));
    }

    [Fact]
    public void A_rate_of_zero_leaves_the_price_alone()
    {
        Assert.Equal(120.00m, VatInclusivePrice.NetOf(120.00m, 0m, true));
    }

    [Fact]
    public void A_gross_below_the_cent_is_rounded_to_the_cent_before_it_is_divided()
    {
        // a gross is what someone pays, so it is a cent amount; 120.005 is
        // billed as 120.01 rather than as a price that shifts on reopening
        Assert.Equal(
            VatInclusivePrice.NetOf(120.01m, 19m, true),
            VatInclusivePrice.NetOf(120.005m, 19m, true)
        );
    }

    [Theory]
    [InlineData(7)]
    [InlineData(19)]
    public void Nothing_is_added_to_the_net_here(decimal rate)
    {
        // A grave whose prices then do not divide is corrected openly, with the
        // user's confirmation -- see GraveBillingCorrectionTests -- rather than
        // rounded into something else here. So this only ever divides and
        // rounds, and the result is always the plain two-decimal quotient.
        for (var cents = 1; cents <= 4000; cents++)
        {
            var gross = cents / 100m;
            var net = VatInclusivePrice.NetOf(gross, rate, true);

            Assert.Equal(net, decimal.Round(net, 2));
            Assert.Equal(
                decimal.Round(gross / (1m + rate / 100m), 2, MidpointRounding.AwayFromZero),
                net
            );
        }
    }

    // The round trip the revision dialogs depend on: the dialog shows
    // round(net × (1 + rate/100), 2) and sends it back, and the same net must
    // come out — otherwise every save subtracts the VAT again.
    public static readonly TheoryData<decimal, decimal> DisplayedGrosses = new()
    {
        { 0.01m, 19m },
        { 1.00m, 19m },
        { 33.33m, 19m },
        { 120.00m, 19m },
        { 999.99m, 19m },
        { 0.01m, 7m },
        { 33.33m, 7m },
        { 119.00m, 7m },
        { 999.99m, 7m },
    };

    [Theory]
    [MemberData(nameof(DisplayedGrosses))]
    public void The_displayed_gross_converts_back_to_the_same_net(decimal gross, decimal rate)
    {
        var net = VatInclusivePrice.NetOf(gross, rate, true);
        var displayed = decimal.Round(net * (1m + rate / 100m), 2, MidpointRounding.AwayFromZero);

        Assert.Equal(net, VatInclusivePrice.NetOf(displayed, rate, true));
    }
}
