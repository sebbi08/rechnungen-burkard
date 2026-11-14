namespace Burkard.Invoicing.Api.Services;

/// <summary>The one rule for what a document line comes to, in cents, shared by
/// the document and the list search so the total a user reads is the total they
/// can search for.</summary>
public static class LineAmounts
{
    /// <summary>A line's net and VAT in cents. A line with an entered gross bills
    /// exactly count x gross, takes its VAT out of that (rounded once, away from
    /// zero) and keeps the rest as net. Any other line rounds count x unit price
    /// to the cent and puts its VAT on that.</summary>
    public static (decimal NetCents, decimal VatCents) Cents(
        int count,
        decimal unitPrice,
        decimal vatRate,
        decimal? grossUnitPrice
    )
    {
        if (grossUnitPrice is { } gross)
        {
            var grossCents = count * gross * 100m;
            var vat = Math.Round(
                grossCents * vatRate / (100m + vatRate),
                0,
                MidpointRounding.AwayFromZero
            );
            return (grossCents - vat, vat);
        }

        var net = Math.Round(count * unitPrice * 100m, 0, MidpointRounding.AwayFromZero);
        return (net, Math.Round(net * vatRate / 100m, 0, MidpointRounding.AwayFromZero));
    }
}
