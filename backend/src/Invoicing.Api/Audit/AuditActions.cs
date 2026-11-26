namespace Burkard.Invoicing.Api.Audit;

/// <summary>The keys an entry's action is stored under. The German labels
/// live in the frontend's i18n catalogue.</summary>
public static class AuditActions
{
    public const string GraveCreated = nameof(GraveCreated);
    public const string GraveEdited = nameof(GraveEdited);
    public const string GraveHandedOver = nameof(GraveHandedOver);
    public const string GraveDeleted = nameof(GraveDeleted);
    public const string GraveRestored = nameof(GraveRestored);
    public const string GraveImported = nameof(GraveImported);
    public const string ItemPriceIncreased = nameof(ItemPriceIncreased);
    public const string ItemDefaultPriceChanged = nameof(ItemDefaultPriceChanged);
    public const string GraveInvoicesGenerated = nameof(GraveInvoicesGenerated);
    public const string GraveInvoiceRevised = nameof(GraveInvoiceRevised);
    public const string InvoiceCreated = nameof(InvoiceCreated);
    public const string InvoiceCreatedFromQuote = nameof(InvoiceCreatedFromQuote);
    public const string InvoiceRevised = nameof(InvoiceRevised);
    public const string InvoicePaid = nameof(InvoicePaid);
    public const string InvoiceUnpaid = nameof(InvoiceUnpaid);

    /// <summary>A change no action declared, recorded by the guard in
    /// <see cref="AuditGuardMode.Record"/> rather than refused.</summary>
    public const string Unattributed = nameof(Unattributed);
}
