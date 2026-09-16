using Burkard.Invoicing.Api.OData;
using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Api.Services;
using Burkard.Invoicing.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using Microsoft.EntityFrameworkCore;

namespace Burkard.Invoicing.Api.OData.Controllers;

/// <summary>Whether a grave's prices can be split evenly across its recipients,
/// and what they would be billed.
///
/// A grave is billed by giving every recipient the same share of each VAT-rate
/// subtotal, so the prices have to divide. Nothing adjusts them: a grave that
/// does not come out even is refused when it is saved, and this is what the
/// dialog asks before saving so it can say which rate is wrong and what would
/// work instead.
///
/// It is answered here rather than in the browser because the same rule decides
/// what is actually billed. A second copy of it in TypeScript would be free to
/// disagree with the one that refuses the save.</summary>
[Route("odata")]
public class GraveBillingValidationController : ODataController
{
    private readonly AppDbContext _db;

    public GraveBillingValidationController(AppDbContext db) => _db = db;

    [HttpPost("ValidateGraveBilling")]
    public async Task<IActionResult> Validate([FromBody] ValidateGraveBillingRequest? request)
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
                Error("NoRecipients", "A grave without recipients has nothing to divide.")
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

        // the save refuses a line with no price at all; a check that said yes to
        // one would be judging a 0.00 nobody entered
        var unpriced = request.Lines.FirstOrDefault(l =>
            (l.UnitPrice ?? items[l.ItemId].DefaultPrice) is null
        );
        if (unpriced is not null)
        {
            return BadRequest(
                Error(
                    "PriceRequired",
                    $"A price is required for item '{items[unpriced.ItemId].Number}'."
                )
            );
        }

        var beyondCents = request.Lines.FirstOrDefault(l =>
            l.UnitPrice is { } price && !GraveBilling.IsWholeCents(price)
        );
        if (beyondCents is not null)
        {
            var number = items[beyondCents.ItemId].Number;
            return BadRequest(
                Error("InvalidPrice", $"The price for item '{number}' has more than two decimals.")
            );
        }

        var lines = request
            .Lines.Select(l =>
                GraveBilling.LineFor(items[l.ItemId], l.Count, l.UnitPrice, l.PriceIncludesVat)
            )
            .ToList();
        var problems = GraveShare.Problems(lines, request.Recipients);

        return new JsonResult(
            new
            {
                Ok = problems.Count == 0,
                // what the recipients collect between them, which is the grave's
                // worth: VAT is rounded once per recipient, so adding VAT to the
                // whole grave instead gives a figure nobody ever pays
                GrossTotal = problems.Count == 0
                    ? GraveShare.CollectedCents(lines, request.Recipients) / 100m
                    : (decimal?)null,
                Problems = problems
                    .Select(p => new
                    {
                        p.VatRate,
                        Kind = p.Kind.ToString(),
                        Amount = p.Amount / 100m,
                        Suggestions = p.Suggestions.Select(c => c / 100m).ToList(),
                    })
                    .ToList(),
                Lines = lines
                    .Select(line => new
                    {
                        line.ItemId,
                        line.ItemNumber,
                        line.Description,
                        line.Count,
                        line.VatRate,
                        line.PriceIncludesVat,
                        line.UnitPrice,
                        NetTotal = decimal.Round(
                            line.Count * line.UnitPrice,
                            2,
                            MidpointRounding.AwayFromZero
                        ),
                    })
                    .ToList(),
            },
            ODataJsonDefaults.PascalCase
        );
    }

    private static object Error(string code, string message) =>
        new { error = new { code, message } };

    public sealed class ValidateGraveBillingRequest
    {
        public int Recipients { get; set; }
        public List<ValidateGraveBillingLine> Lines { get; set; } = new();
    }

    public sealed class ValidateGraveBillingLine
    {
        public long ItemId { get; set; }
        public int Count { get; set; }

        /// <summary>The grave's own price for the item; null falls back to the
        /// item's default, as billing does.</summary>
        public decimal? UnitPrice { get; set; }
        public bool PriceIncludesVat { get; set; }
    }
}
