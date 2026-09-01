namespace Burkard.Invoicing.Domain;

/// <summary>The net price a line is billed at, given the price entered for it.
/// Prices in this application are net, except for a line whose price was
/// entered as including VAT: there the entered price is the gross the customer
/// pays, and the VAT comes back out of it here.</summary>
public static class VatInclusivePrice
{
    /// <summary>The net keeps three decimals — the precision
    /// <see cref="GraveInvoiceSplitter"/> already stores and the document already
    /// prints. Two would lose a cent as soon as a line has a count above one:
    /// three at 100,00 € gross bill 299,99 € from a net of 84,03 and 300,00 €
    /// from 84,034.
    ///
    /// The gross is rounded to the cent before it is divided, because a gross is
    /// an amount someone pays. That also makes the conversion reversible: the
    /// rounded net is at most 0,0005 from the exact quotient, so multiplying the
    /// rate back on lands inside the half cent of the entered price for any rate
    /// below 900 %. A revision dialog can therefore show the gross, take it back
    /// untouched, and store the same net — without it, every save would subtract
    /// the VAT a second time.</summary>
    public static decimal NetOf(decimal price, decimal vatRate, bool priceIncludesVat)
    {
        if (!priceIncludesVat)
        {
            return price;
        }

        var gross = decimal.Round(price, 2, MidpointRounding.AwayFromZero);
        return decimal.Round(gross / (1m + vatRate / 100m), 3, MidpointRounding.AwayFromZero);
    }
}
