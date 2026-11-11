namespace Burkard.Invoicing.Domain.Entities;

public sealed class Grave
{
    public long Id { get; set; }
    public string GraveNumber { get; set; } = "";
    public string FieldNumber { get; set; } = "";
    public string SiteName { get; set; } = "";

    /// <summary>Required; restricted, so a cemetery cannot be deleted while a
    /// grave lies in it.</summary>
    public long CemeteryId { get; set; }
    public Cemetery Cemetery { get; set; } = null!;

    /// <summary>Required when a grave is saved; null only on graves that were
    /// migrated from before the field existed and have not been edited since.</summary>
    public GravePosition? Position { get; set; }

    /// <summary>The term is counted in months: both ends are stored as the first
    /// of their month. No end means the grave runs indefinitely.</summary>
    public DateOnly StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public DateOnly CreatedAt { get; set; }

    /// <summary>A grave the cooperative looks after: it is listed and kept up to
    /// date here, but never invoiced, so it needs no recipient, no price and no
    /// all-year item.</summary>
    public bool IsCooperative { get; set; }

    /// <summary>The cooperative's number for the contract the grave is kept
    /// under; required, trimmed, on a cooperative grave and null on any other.</summary>
    public string? ContractNumber { get; set; }
    public List<GraveItem> Items { get; set; } = new();
    public List<GraveRecipient> Recipients { get; set; } = new();
    public List<GraveBillingCorrection> Corrections { get; set; } = new();
}

public sealed class GraveItem
{
    public long Id { get; set; }
    public long GraveId { get; set; }
    public long ItemId { get; set; }
    public int Count { get; set; }

    // per-grave price override; null falls back to the item's default price
    public decimal? UnitPrice { get; set; }

    /// <summary>True when this grave's price for the item was entered as a
    /// gross. Applies to whichever price is billed — the link's own
    /// <see cref="UnitPrice"/>, or the item's default standing in for it.</summary>
    public bool PriceIncludesVat { get; set; }
    public InvoiceItem Item { get; set; } = null!;
}

public sealed class GraveRecipient
{
    public long Id { get; set; }
    public long GraveId { get; set; }
    public long RecipientId { get; set; }
    public Recipient Recipient { get; set; } = null!;
}
