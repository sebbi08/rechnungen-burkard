using Burkard.Invoicing.Api.OData;
using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using Microsoft.EntityFrameworkCore;

namespace Burkard.Invoicing.Api.OData.Controllers;

/// <summary>What a grave's lines would be billed at, for a grave that has not
/// been invoiced yet.
///
/// A price is not always billed as it was entered: converting a VAT-included
/// price to a net rounds it, and <see cref="GraveShare.MakeDivisible"/> then
/// raises unit prices by whole cents until each VAT-rate subtotal divides across
/// the recipients and their shares still cover the bill. The grave dialog warns
/// about that before saving, which means knowing the billed price before any
/// invoice exists.
///
/// It is computed here rather than in the browser on purpose. The rule is the
/// one piece of money arithmetic in this feature, and a second copy of it in
/// TypeScript would be free to drift from the one that actually bills.</summary>
[Route("odata")]
public class GraveBillingPreviewController : ODataController
{
    private readonly AppDbContext _db;

    public GraveBillingPreviewController(AppDbContext db) => _db = db;

    [HttpPost("PreviewGraveBilling")]
    public async Task<IActionResult> Preview([FromBody] PreviewGraveBillingRequest? request)
    {
        if (request is null || request.Lines.Count == 0)
        {
            return BadRequest(
                Error("InvalidRequest", "The request body must carry at least one line.")
            );
        }

        if (request.Recipients <= 0)
        {
            return BadRequest(
                Error("NoRecipients", "A grave without recipients has no billed price yet.")
            );
        }

        if (request.Lines.Any(l => l.Count <= 0))
        {
            return BadRequest(Error("InvalidCount", "Every line's count must be positive."));
        }

        var itemIds = request.Lines.Select(l => l.ItemId).Distinct().ToList();
        var items = await _db
            .InvoiceItems.AsNoTracking()
            .Where(i => itemIds.Contains(i.Id))
            .ToDictionaryAsync(i => i.Id, HttpContext.RequestAborted);

        if (itemIds.Any(id => !items.ContainsKey(id)))
        {
            return BadRequest(
                Error("ItemNotFound", "A line refers to an item that does not exist.")
            );
        }

        // The price the grave would actually bill: the line's own price where it
        // has one, the item's default where it does not -- the same fallback
        // GraveInvoiceGenerator applies.
        var entered = request
            .Lines.Select(line =>
            {
                var item = items[line.ItemId];
                var price = decimal.Round(
                    line.UnitPrice ?? item.DefaultPrice ?? 0m,
                    2,
                    MidpointRounding.AwayFromZero
                );
                return (line, item, price);
            })
            .ToList();

        var graveLines = entered
            .Select(e => new GraveLine(
                e.item.Id,
                e.item.Number,
                e.item.Description,
                e.line.Count,
                VatInclusivePrice.NetOf(
                    e.price,
                    e.item.VatRate,
                    e.line.PriceIncludesVat,
                    e.line.Count
                ),
                e.item.VatRate,
                e.line.PriceIncludesVat,
                e.line.PriceIncludesVat ? e.price : null
            ))
            .ToList();

        var billed = GraveShare.MakeDivisible(graveLines, request.Recipients);

        return new JsonResult(
            new
            {
                Lines = billed
                    .Select(
                        (line, index) =>
                        {
                            var e = entered[index];
                            return new
                            {
                                ItemId = line.ItemId,
                                ItemNumber = line.ItemNumber,
                                line.Description,
                                line.Count,
                                line.VatRate,
                                line.PriceIncludesVat,
                                // both in the unit the price was entered in, so
                                // the dialog can compare them without knowing
                                // which that was
                                EnteredPrice = e.price,
                                BilledPrice = InEnteredUnit(line),
                                // the net that would be stored, whatever unit the
                                // price was entered in
                                UnitPrice = line.UnitPrice,
                            };
                        }
                    )
                    .ToList(),
            },
            ODataJsonDefaults.PascalCase
        );
    }

    /// <summary>A billed line's price in the unit it was entered in: the gross
    /// for a VAT-included line, the net for every other one. The same conversion
    /// the grave pages display, so a comparison against the entered price is
    /// like for like.</summary>
    private static decimal InEnteredUnit(GraveLine line) =>
        line.PriceIncludesVat
            ? decimal.Round(
                line.UnitPrice * (1m + line.VatRate / 100m),
                2,
                MidpointRounding.AwayFromZero
            )
            : line.UnitPrice;

    private static object Error(string code, string message) =>
        new { error = new { code, message } };

    public sealed class PreviewGraveBillingRequest
    {
        public int Recipients { get; set; }
        public List<PreviewGraveBillingLine> Lines { get; set; } = new();
    }

    public sealed class PreviewGraveBillingLine
    {
        public long ItemId { get; set; }
        public int Count { get; set; }

        /// <summary>The grave's own price for the item; null falls back to the
        /// item's default, as billing does.</summary>
        public decimal? UnitPrice { get; set; }
        public bool PriceIncludesVat { get; set; }
    }
}
