using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Domain;
using Burkard.Invoicing.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Burkard.Invoicing.Api.Services;

/// <summary>Turns the lines a user entered into snapshotted, validated lines:
/// each item's number, description and VAT rate as they are now, the price
/// entered or else the item's default, kept as the gross where it was entered
/// VAT-included. Invoices and quotes take their input through the same rules, so
/// a quote accepts exactly what an invoice would.</summary>
public sealed class LineBuilder
{
    private readonly AppDbContext _db;

    public LineBuilder(AppDbContext db) => _db = db;

    public async Task<List<InvoiceLine>> BuildAsync(
        IReadOnlyList<InvoiceLineInput> inputs,
        CancellationToken ct
    )
    {
        if (inputs.Count == 0)
        {
            throw new ValidationException("NoLines", "At least one line is required.");
        }

        var itemIds = inputs.Select(l => l.ItemId).ToArray();
        var items = await _db
            .InvoiceItems.Where(i => itemIds.Contains(i.Id))
            .ToDictionaryAsync(i => i.Id, ct);

        var result = new List<InvoiceLine>(inputs.Count);
        foreach (var (input, index) in inputs.Select((l, i) => (l, i)))
        {
            if (!items.TryGetValue(input.ItemId, out var item))
            {
                throw new ValidationException(
                    $"ItemNotFound:{input.ItemId}",
                    $"The item {input.ItemId} does not exist."
                );
            }

            if (input.Count <= 0)
            {
                throw new ValidationException("InvalidCount", "The count must be positive.");
            }

            var unitPrice = input.UnitPrice ?? item.DefaultPrice;
            if (unitPrice is null)
            {
                throw new ValidationException(
                    "PriceRequired",
                    $"A price is required for item '{item.Number}'."
                );
            }

            // the document prints the price to the cent and multiplies the
            // unrounded one, so 3 x 10,125 showed 10,13 and a total of 30,38
            if (input.UnitPrice is { } typed && !GraveBilling.IsWholeCents(typed))
            {
                throw new ValidationException(
                    "InvalidPrice",
                    $"The price for item '{item.Number}' has more than two decimals."
                );
            }

            result.Add(
                new InvoiceLine
                {
                    ItemId = item.Id,
                    ItemNumber = item.Number,
                    Description = item.Description,
                    Count = input.Count,
                    // a line priced VAT-included is entered as the gross the
                    // customer pays, and that gross is what is stored and billed.
                    // UnitPrice then only holds the per-unit net for readers that
                    // want one; the calculation does not use it
                    UnitPrice = VatInclusivePrice.NetOf(
                        unitPrice.Value,
                        item.VatRate,
                        input.PriceIncludesVat
                    ),
                    GrossUnitPrice = input.PriceIncludesVat ? unitPrice.Value : null,
                    VatRate = item.VatRate,
                    PriceIncludesVat = input.PriceIncludesVat,
                    Comment = CleanText(input.Comment),
                    LineNumber = index + 1,
                }
            );
        }

        return result;
    }

    /// <summary>A free text or line comment as it is kept: without the blank
    /// lines and spaces around it, and empty when nothing but those was
    /// entered. The line breaks inside it stay.</summary>
    public static string CleanText(string? text) =>
        string.IsNullOrWhiteSpace(text) ? "" : text.Trim();
}
