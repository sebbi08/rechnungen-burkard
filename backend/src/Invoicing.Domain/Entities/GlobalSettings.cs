namespace Burkard.Invoicing.Domain.Entities;

public sealed class GlobalSettings
{
    public long Id { get; set; }
    public string EndingText { get; set; } = "";
    public string StampWord { get; set; } = "Kopie";

    /// <summary>Language the generated documents are written in; each invoice
    /// snapshots it so existing documents stay unchanged when it is switched.</summary>
    public string Language { get; set; } = "de";
}
