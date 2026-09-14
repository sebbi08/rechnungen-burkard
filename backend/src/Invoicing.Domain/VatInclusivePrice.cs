namespace Burkard.Invoicing.Domain;

/// <summary>The net price a line is billed at, given the price entered for it.
/// Prices in this application are net, except for a line whose price was
/// entered as including VAT: there the entered price is the gross the customer
/// pays, and the VAT comes back out of it here.</summary>
public static class VatInclusivePrice
{
    /// <summary>The net has two decimals, like every other price this
    /// application prints, and nothing is added to it: a gross that cannot be
    /// billed exactly is refused when the grave is saved rather than quietly
    /// rounded into something else.
    ///
    /// The gross is rounded to the cent before it is divided, because a gross is
    /// an amount someone pays.</summary>
    public static decimal NetOf(decimal price, decimal vatRate, bool priceIncludesVat)
    {
        if (!priceIncludesVat)
        {
            return price;
        }

        var gross = decimal.Round(price, 2, MidpointRounding.AwayFromZero);
        return decimal.Round(gross / (1m + vatRate / 100m), 2, MidpointRounding.AwayFromZero);
    }
}
