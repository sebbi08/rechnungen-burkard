using Burkard.Invoicing.Api.OData;
using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Api.Services;
using Burkard.Invoicing.Domain;
using Burkard.Invoicing.Domain.Entities;
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

        var (items, refusal) = await LoadItemsAsync(request.Lines);
        if (refusal is not null)
        {
            return refusal;
        }

        var lines = request
            .Lines.Select(l =>
                GraveBilling.LineFor(items![l.ItemId], l.Count, l.UnitPrice, l.PriceIncludesVat)
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

    /// <summary>The items the lines name, or the refusal that stops the check.</summary>
    private async Task<(
        Dictionary<long, InvoiceItem>? Items,
        IActionResult? Refusal
    )> LoadItemsAsync(List<ValidateGraveBillingLine> lines)
    {
        if (lines.Any(l => l.Count <= 0))
        {
            return (
                null,
                BadRequest(Error("InvalidCount", "Every line's count must be positive."))
            );
        }

        var itemIds = lines.Select(l => l.ItemId).Distinct().ToList();
        var items = await _db
            .InvoiceItems.AsNoTracking()
            .Where(i => itemIds.Contains(i.Id))
            .ToDictionaryAsync(i => i.Id, HttpContext.RequestAborted);

        if (itemIds.Any(id => !items.ContainsKey(id)))
        {
            return (
                null,
                BadRequest(Error("ItemNotFound", "A line refers to an item that does not exist."))
            );
        }

        var unpriced = lines.FirstOrDefault(l =>
            (l.UnitPrice ?? items[l.ItemId].DefaultPrice) is null
        );
        if (unpriced is not null)
        {
            return (
                null,
                BadRequest(
                    Error(
                        "PriceRequired",
                        $"A price is required for item '{items[unpriced.ItemId].Number}'."
                    )
                )
            );
        }

        var beyondCents = lines.FirstOrDefault(l =>
            l.UnitPrice is { } price && !GraveBilling.IsWholeCents(price)
        );
        if (beyondCents is not null)
        {
            var number = items[beyondCents.ItemId].Number;
            return (
                null,
                BadRequest(
                    Error(
                        "InvalidPrice",
                        $"The price for item '{number}' has more than two decimals."
                    )
                )
            );
        }

        return (items, null);
    }

    /// <summary>Every invoice the grave's term will produce, as the editor shows
    /// them while the grave is being typed: the lines each bills, the rounding
    /// corrections that make a block divide, and whether anything is left
    /// beyond their reach. The same rule decides the save, so the preview
    /// cannot promise what the save then rejects.</summary>
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
                Error("NoRecipients", "A grave without recipients has nothing to divide.")
            );
        }

        if (request.Recipients > GraveBilling.MaxRecipients)
        {
            return BadRequest(
                Error(
                    "TooManyRecipients",
                    $"A grave can have at most {GraveBilling.MaxRecipients} recipients."
                )
            );
        }

        DateOnly? start = null;
        DateOnly? end = null;
        if (!string.IsNullOrWhiteSpace(request.StartMonth))
        {
            if (!GraveTerm.TryParseMonth(request.StartMonth, out var parsed))
            {
                return BadRequest(Error("InvalidDate", "Start and end are months (yyyy-MM)."));
            }

            start = parsed;
        }

        if (!string.IsNullOrWhiteSpace(request.EndMonth))
        {
            if (!GraveTerm.TryParseMonth(request.EndMonth, out var parsed))
            {
                return BadRequest(Error("InvalidDate", "Start and end are months (yyyy-MM)."));
            }

            end = parsed;
        }

        if (start is not null && end < start)
        {
            return BadRequest(
                Error("DateRangeInvalid", "The end month must not be before the start month.")
            );
        }

        var (items, refusal) = await LoadItemsAsync(request.Lines);
        if (refusal is not null)
        {
            return refusal;
        }

        var links = request
            .Lines.Select(l =>
                GraveBilling.LinkFor(items![l.ItemId], l.Count, l.UnitPrice, l.PriceIncludesVat)
            )
            .ToList();
        var evaluation = GraveBillingCheck.EvaluateWithCorrections(
            links,
            start,
            end,
            request.Recipients
        );

        return new JsonResult(
            new
            {
                evaluation.Ok,
                request.Recipients,
                Invoices = evaluation
                    .Invoices.Select(invoice =>
                    {
                        var collected =
                            invoice.Ok && invoice.Lines.Count > 0
                                ? GraveShare.CollectedCents(invoice.GraveLines, request.Recipients)
                                    / 100m
                                : (decimal?)null;
                        return new
                        {
                            Kind = invoice.Example.Kind.ToString(),
                            invoice.Example.Period.Year,
                            invoice.Example.Period.Half,
                            invoice.Example.Period.Months,
                            invoice.Ok,
                            NetTotal = invoice.Lines.Sum(b => NetTotal(b.Line)),
                            GrossTotal = collected,
                            PerRecipient = collected / request.Recipients,
                            Problems = invoice
                                .Problems.Select(p => new
                                {
                                    p.VatRate,
                                    Kind = p.Kind.ToString(),
                                    Amount = p.Amount / 100m,
                                    // a block that does not divide is corrected,
                                    // so only a rate beyond the correction's reach
                                    // is left, and no figure fixes that
                                    Suggestions = Array.Empty<decimal>(),
                                })
                                .ToList(),
                            Corrections = invoice
                                .Corrections.Select(c => new
                                {
                                    Kind = invoice.Example.Kind.ToString(),
                                    invoice.Example.Period.Year,
                                    invoice.Example.Period.Half,
                                    LineIndex = c.LinkIndex,
                                    c.VatRate,
                                    c.ItemId,
                                    c.ItemNumber,
                                    c.EnteredPrice,
                                    c.CorrectedPrice,
                                })
                                .ToList(),
                            Lines = invoice
                                .Lines.Select(b => new
                                {
                                    LineIndex = b.LinkIndex,
                                    b.Line.ItemNumber,
                                    b.Line.Description,
                                    b.Line.Count,
                                    b.Line.VatRate,
                                    b.Line.PriceIncludesVat,
                                    b.Price,
                                    EnteredPrice = invoice
                                        .Corrections.FirstOrDefault(c => c.LinkIndex == b.LinkIndex)
                                        ?.EnteredPrice
                                        ?? b.Price,
                                    Corrected = invoice.Corrections.Any(c =>
                                        c.LinkIndex == b.LinkIndex
                                    ),
                                    b.Line.UnitPrice,
                                    NetTotal = NetTotal(b.Line),
                                })
                                .ToList(),
                        };
                    })
                    .ToList(),
            },
            ODataJsonDefaults.PascalCase
        );
    }

    private static decimal NetTotal(GraveLine line) =>
        decimal.Round(line.Count * line.UnitPrice, 2, MidpointRounding.AwayFromZero);

    private static object Error(string code, string message) =>
        new { error = new { code, message } };

    public sealed class PreviewGraveBillingRequest
    {
        public int Recipients { get; set; }

        /// <summary>"yyyy-MM"; without it only the full half-years are shown.</summary>
        public string? StartMonth { get; set; }

        /// <summary>"yyyy-MM"; without it the term runs indefinitely.</summary>
        public string? EndMonth { get; set; }

        public List<ValidateGraveBillingLine> Lines { get; set; } = new();
    }

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
