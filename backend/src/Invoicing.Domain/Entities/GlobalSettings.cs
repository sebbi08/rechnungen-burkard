namespace Burkard.Invoicing.Domain.Entities;

public sealed class GlobalSettings
{
    public long Id { get; set; }
    public string EndingText { get; set; } = "";
    public string StampWord { get; set; } = "Kopie";
}
