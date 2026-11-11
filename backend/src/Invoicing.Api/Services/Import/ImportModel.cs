using Burkard.Invoicing.Domain.Entities;

namespace Burkard.Invoicing.Api.Services.Import;

/// <summary>One problem found before importing, located precisely enough to fix
/// it without guessing: which file, which row, and why (FR-059). <c>Message</c>
/// is English; <c>MessageKey</c> names the sentence and <c>MessageArgs</c>
/// carries its values as written in the archive, so the frontend can say it in
/// German. <c>Code</c> groups problems of a kind.</summary>
public sealed record ImportProblem(
    string File,
    int? Row,
    string Code,
    string Message,
    string? MessageKey = null,
    IReadOnlyList<string>? MessageArgs = null
);

/// <summary>Counts per file after a successful import (FR-062), and how many of
/// the graves needed a rounding correction.</summary>
public sealed record ImportSummary(
    int Recipients,
    int Items,
    int Graves,
    int ItemLinks,
    int RecipientLinks,
    int CorrectedGraves
);

public sealed record RecipientRow(
    int Row,
    string Number,
    string Name,
    string Address,
    string? Email
);

public sealed record ItemRow(
    int Row,
    string Number,
    string Description,
    decimal VatRate,
    decimal? Price,
    BillingQuarter? Quarter
);

public sealed record GraveRow(
    int Row,
    string Number,
    string FieldNumber,
    string SiteName,
    string Cemetery,
    GravePosition Position,
    DateOnly StartDate,
    DateOnly? EndDate,
    bool Cooperative,
    string? ContractNumber
);

public sealed record ItemLinkRow(
    int Row,
    string GraveNumber,
    string ItemNumber,
    int Count,
    bool PriceIncludesVat,
    decimal? UnitPrice
);

public sealed record RecipientLinkRow(int Row, string GraveNumber, string RecipientNumber);

/// <summary>Everything a valid archive contains, ready to be written.</summary>
public sealed class ImportData
{
    public List<RecipientRow> Recipients { get; } = new();
    public List<ItemRow> Items { get; } = new();
    public List<GraveRow> Graves { get; } = new();
    public List<ItemLinkRow> ItemLinks { get; } = new();
    public List<RecipientLinkRow> RecipientLinks { get; } = new();

    // Every identifier a file named, including on rows that were rejected for
    // another reason. A mapping row that points at such a row is not reported as
    // an unknown reference — the row it points at already carries its own
    // problem, and repeating it would only bury the real one.
    public HashSet<string> DeclaredRecipients { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> DeclaredItems { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> DeclaredGraves { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The stored cemeteries' ids by name, ignoring case: a grave row
    /// names its cemetery, which the import does not create.</summary>
    public Dictionary<string, long> CemeteryIds { get; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>Either the problems that block the import, or the data to write.</summary>
public sealed record ImportValidationResult(IReadOnlyList<ImportProblem> Problems, ImportData? Data)
{
    public bool IsValid => Problems.Count == 0 && Data is not null;
}
