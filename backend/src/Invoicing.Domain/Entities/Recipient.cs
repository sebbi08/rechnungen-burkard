namespace Burkard.Invoicing.Domain.Entities;

public sealed class Recipient
{
    public long Id { get; set; }

    /// <summary>Stable business identifier the import mapping file references.
    /// Assigned automatically when a client does not supply one.</summary>
    public string Number { get; set; } = "";
    public string Name { get; set; } = "";
    public string Address { get; set; } = "";
}
