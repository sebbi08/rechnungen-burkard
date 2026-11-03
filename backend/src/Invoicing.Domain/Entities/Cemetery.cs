namespace Burkard.Invoicing.Domain.Entities;

/// <summary>The cemetery a grave lies in, kept in the settings. Graves refer
/// to it, so a rename reaches every one of them at once; it is not printed on
/// invoices.</summary>
public sealed class Cemetery
{
    public long Id { get; set; }

    /// <summary>Unique ignoring case, umlauts included.</summary>
    public string Name { get; set; } = "";
}
