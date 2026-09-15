using Burkard.Invoicing.Domain;
using Burkard.Invoicing.Domain.Entities;

namespace Burkard.Invoicing.Api.Services;

/// <summary>Turns a grave's item links into the lines that are billed.
///
/// One place, because three callers have to agree exactly: the save that
/// refuses a grave whose prices do not divide, the dialog's check that says so
/// before the save is attempted, and the generation that bills them. Any
/// disagreement between those would show up as a grave that saves and then
/// cannot be invoiced.</summary>
public static class GraveBilling
{
    /// <summary>Whether a price is a whole number of cents. Prices are entered,
    /// stored and billed in cents; a third decimal would be judged rounded here
    /// and billed unrounded by generation, so every write refuses one.</summary>
    public static bool IsWholeCents(decimal price) =>
        price == decimal.Round(price, 2, MidpointRounding.AwayFromZero);

    /// <summary>The billed line for one item link. The link's own price wins;
    /// without one the item's default is billed.</summary>
    public static GraveLine LineFor(
        InvoiceItem item,
        int count,
        decimal? unitPrice,
        bool priceIncludesVat
    )
    {
        var entered = decimal.Round(
            unitPrice ?? item.DefaultPrice ?? 0m,
            2,
            MidpointRounding.AwayFromZero
        );

        return new GraveLine(
            item.Id,
            item.Number,
            item.Description,
            count,
            VatInclusivePrice.NetOf(entered, item.VatRate, priceIncludesVat),
            item.VatRate,
            priceIncludesVat,
            // a price entered as a gross is a promise about what the customer
            // pays, and the split has to come to exactly that
            priceIncludesVat ? entered : null
        );
    }
}
