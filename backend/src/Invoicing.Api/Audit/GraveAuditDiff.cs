namespace Burkard.Invoicing.Api.Audit;

/// <summary>What changed on a grave between two snapshots, in domain terms:
/// items are paired by item, recipients by recipient and corrections by their
/// block, so a link SaveGrave replaced with the same values is no change, and a
/// different count is "count 1 → 2" rather than "removed, added".</summary>
public static class GraveAuditDiff
{
    /// <summary>The changes from <paramref name="before"/> to
    /// <paramref name="after"/>; with no before (a created or imported grave),
    /// every value that is set. Corrections are compared only from
    /// <paramref name="currentHalf"/> on: those for earlier half-years fall away on
    /// every save by themselves.</summary>
    public static IReadOnlyList<AuditChange> Diff(
        GraveSnapshot? before,
        GraveSnapshot after,
        (int Year, int Half) currentHalf
    )
    {
        var changes = new List<AuditChange>();

        void Field(string field, string? old, string? @new, string type)
        {
            old = string.IsNullOrEmpty(old) ? null : old;
            @new = string.IsNullOrEmpty(@new) ? null : @new;
            if (old != @new)
            {
                changes.Add(new AuditChange(field, null, old, @new, type));
            }
        }

        Field("graveNumber", before?.GraveNumber, after.GraveNumber, "text");
        Field("fieldNumber", before?.FieldNumber, after.FieldNumber, "text");
        Field("siteName", before?.SiteName, after.SiteName, "text");
        Field("cemetery", before?.CemeteryName, after.CemeteryName, "text");
        Field("position", before?.Position?.ToString(), after.Position?.ToString(), "position");
        Field(
            "start",
            before is null ? null : AuditValues.Month(before.StartDate),
            AuditValues.Month(after.StartDate),
            "month"
        );
        Field("end", AuditValues.Month(before?.EndDate), AuditValues.Month(after.EndDate), "month");
        // a new grave that is not the cooperative's says nothing about it
        Field(
            "cooperative",
            before is null ? null : AuditValues.Bool(before.IsCooperative),
            before is null && !after.IsCooperative ? null : AuditValues.Bool(after.IsCooperative),
            "bool"
        );
        Field("contractNumber", before?.ContractNumber, after.ContractNumber, "text");

        DiffItems(before?.Items ?? [], after.Items, changes);
        DiffRecipients(before?.Recipients ?? [], after.Recipients, changes);
        DiffCorrections(before?.Corrections ?? [], after.Corrections, currentHalf, changes);
        return changes;
    }

    private static void DiffItems(
        IReadOnlyList<GraveItemSnapshot> before,
        IReadOnlyList<GraveItemSnapshot> after,
        List<AuditChange> changes
    )
    {
        var old = before.ToDictionary(i => i.ItemId);
        foreach (var item in after)
        {
            if (!old.TryGetValue(item.ItemId, out var was))
            {
                changes.Add(new AuditChange("itemAdded", item.Label, null, null, "text"));
                changes.Add(
                    new AuditChange(
                        "itemCount",
                        item.Label,
                        null,
                        AuditValues.Int(item.Count),
                        "int"
                    )
                );
                if (item.EffectivePrice is not null)
                {
                    changes.Add(
                        new AuditChange(
                            "itemPrice",
                            item.Label,
                            null,
                            AuditValues.Money(item.EffectivePrice),
                            "money"
                        )
                    );
                }

                continue;
            }

            if (was.Count != item.Count)
            {
                changes.Add(
                    new AuditChange(
                        "itemCount",
                        item.Label,
                        AuditValues.Int(was.Count),
                        AuditValues.Int(item.Count),
                        "int"
                    )
                );
            }

            if (was.EffectivePrice != item.EffectivePrice)
            {
                changes.Add(
                    new AuditChange(
                        "itemPrice",
                        item.Label,
                        AuditValues.Money(was.EffectivePrice),
                        AuditValues.Money(item.EffectivePrice),
                        "money"
                    )
                );
            }
            else if ((was.OwnPrice is null) != (item.OwnPrice is null))
            {
                changes.Add(
                    new AuditChange(
                        "itemPriceSource",
                        item.Label,
                        PriceSource(was),
                        PriceSource(item),
                        "priceSource"
                    )
                );
            }
        }

        var remaining = after.Select(i => i.ItemId).ToHashSet();
        foreach (var item in before.Where(i => !remaining.Contains(i.ItemId)))
        {
            changes.Add(new AuditChange("itemRemoved", item.Label, null, null, "text"));
        }
    }

    private static string PriceSource(GraveItemSnapshot item) =>
        item.OwnPrice is null ? "default" : "own";

    private static void DiffRecipients(
        IReadOnlyList<GraveRecipientSnapshot> before,
        IReadOnlyList<GraveRecipientSnapshot> after,
        List<AuditChange> changes
    )
    {
        var old = before.Select(r => r.RecipientId).ToHashSet();
        var current = after.Select(r => r.RecipientId).ToHashSet();
        foreach (var recipient in after.Where(r => !old.Contains(r.RecipientId)))
        {
            changes.Add(new AuditChange("recipientAdded", null, null, recipient.Label, "text"));
        }

        foreach (var recipient in before.Where(r => !current.Contains(r.RecipientId)))
        {
            changes.Add(new AuditChange("recipientRemoved", null, recipient.Label, null, "text"));
        }
    }

    private static void DiffCorrections(
        IReadOnlyList<GraveCorrectionSnapshot> before,
        IReadOnlyList<GraveCorrectionSnapshot> after,
        (int Year, int Half) currentHalf,
        List<AuditChange> changes
    )
    {
        bool Compared(GraveCorrectionSnapshot c) =>
            c.Year is not { } year || year * 2 + c.Half >= currentHalf.Year * 2 + currentHalf.Half;

        static (string, int?, int, decimal, long) Key(GraveCorrectionSnapshot c) =>
            (c.Kind, c.Year, c.Half, c.VatRate, c.ItemId);

        var previous = before.Where(Compared).ToList();
        var old = previous.ToDictionary(Key);
        var current = after.Where(Compared).ToList();
        foreach (var correction in current)
        {
            old.TryGetValue(Key(correction), out var was);
            if (was?.CorrectedPrice != correction.CorrectedPrice)
            {
                changes.Add(Change(correction, was?.CorrectedPrice, correction.CorrectedPrice));
            }
        }

        var remaining = current.Select(Key).ToHashSet();
        foreach (var correction in previous.Where(c => !remaining.Contains(Key(c))))
        {
            changes.Add(Change(correction, correction.CorrectedPrice, null));
        }
    }

    private static AuditChange Change(
        GraveCorrectionSnapshot correction,
        decimal? old,
        decimal? @new
    ) =>
        new(
            "correction",
            correction.ItemLabel,
            AuditValues.Money(old),
            AuditValues.Money(@new),
            "money",
            $"{correction.Kind}:{AuditValues.Int(correction.Year) ?? "-"}-{AuditValues.Int(correction.Half)}"
        );
}
