using Burkard.Invoicing.Domain;
using Burkard.Invoicing.Domain.Entities;

namespace Burkard.Invoicing.Api.Services;

/// <summary>The rows a grave's rounding corrections are stored as. One place,
/// because a grave save and a price increase have to store exactly the same
/// thing for the same prices.</summary>
public static class GraveCorrections
{
    /// <summary>What a save of the grave with the evaluated prices stores.</summary>
    public static List<GraveBillingCorrection> Build(
        long graveId,
        GraveBillingEvaluation evaluation
    )
    {
        var rows = new List<GraveBillingCorrection>();
        foreach (var invoice in evaluation.Invoices)
        {
            foreach (var correction in invoice.Corrections)
            {
                rows.Add(
                    new GraveBillingCorrection
                    {
                        GraveId = graveId,
                        Kind = invoice.Example.Kind,
                        Year = invoice.Example.Period.Year,
                        Half = invoice.Example.Period.Half,
                        VatRate = correction.VatRate,
                        ItemId = correction.ItemId,
                        EnteredPrice = correction.EnteredPrice,
                        CorrectedPrice = correction.CorrectedPrice,
                    }
                );
            }
        }

        return rows;
    }
}
