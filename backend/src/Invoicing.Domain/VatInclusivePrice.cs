namespace Burkard.Invoicing.Domain;

/// <summary>The net price a line is billed at, given the price entered for it.
/// Prices in this application are net, except for a line whose price was
/// entered as including VAT: there the entered price is the gross the customer
/// pays, and the VAT comes back out of it here.</summary>
public static class VatInclusivePrice
{
    /// <summary>The net has two decimals, like every other price this
    /// application prints. Rounding the quotient alone would undercharge as
    /// soon as a line has a count above one — three at 100,25 € gross bill
    /// 300,74 € from a net of 84,24 — so the net is raised by whole cents until
    /// the line covers the gross it was entered as. A price is never reduced.
    ///
    /// A two-decimal unit price times a count puts line totals on a lattice of
    /// `count` cents, so the entered gross is not always exactly reachable and
    /// the line then lands on the first value above it: exact for 88 % of
    /// prices at count 1 and at most a cent over, up to eleven cents over at
    /// count 10. That is the cost of printing two decimals everywhere, and it
    /// is paid in the customer's favour rather than the business's.
    ///
    /// The gross is rounded to the cent before it is divided, because a gross is
    /// an amount someone pays.</summary>
    public static decimal NetOf(decimal price, decimal vatRate, bool priceIncludesVat, int count)
    {
        if (!priceIncludesVat)
        {
            return price;
        }

        if (count <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        var gross = decimal.Round(price, 2, MidpointRounding.AwayFromZero);
        var net = decimal.Round(gross / (1m + vatRate / 100m), 2, MidpointRounding.AwayFromZero);

        // At most a couple of steps -- one cent on the unit price moves the line
        // by `count` cents, so it overtakes the target quickly. Bounded anyway,
        // so a rate that breaks the assumption stops the bill rather than
        // spinning.
        for (var step = 0; step <= count + 2; step++)
        {
            if (LineGross(net, count, vatRate) >= gross * count)
            {
                return net;
            }

            net += 0.01m;
        }

        throw new InvalidOperationException(
            $"No two-decimal net covers {gross} at {vatRate} % over a count of {count}."
        );
    }

    /// <summary>What the line is billed, gross: its net total plus that total's
    /// VAT, each rounded to the cent exactly as the document rounds them.</summary>
    private static decimal LineGross(decimal net, int count, decimal vatRate)
    {
        var lineNet = decimal.Round(net * count, 2, MidpointRounding.AwayFromZero);
        return lineNet + decimal.Round(lineNet * vatRate / 100m, 2, MidpointRounding.AwayFromZero);
    }
}
