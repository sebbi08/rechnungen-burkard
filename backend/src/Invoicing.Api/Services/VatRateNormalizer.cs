namespace Burkard.Invoicing.Api.Services;

public static class VatRateNormalizer
{
    // decimal.Normalize is not available in .NET 10; dividing by one with the
    // maximum scale strips trailing zeros the same way (7.0 -> 7, 19.00 -> 19).
    public static decimal Normalize(decimal rate) => rate / 1.000000000000000000000000000000000m;
}
