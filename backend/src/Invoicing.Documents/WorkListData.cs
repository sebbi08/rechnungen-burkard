using Burkard.Invoicing.Domain.Entities;

namespace Burkard.Invoicing.Documents;

/// <summary>Which list, and for the care list which variant: graves only
/// (two columns), or every grave with its services under it. The planting
/// list names one quarter's services.</summary>
public enum WorkListKind
{
    CareGravesOnly,
    CareWithServices,
    Planting,
}

/// <summary>A care or planting list, ready to print: cemeteries and graves
/// in the order they are printed in, every value and flag already decided.
/// <paramref name="Quarter"/> is the planting list's quarter, null on the care
/// list. <paramref name="MadeOn"/> is printed as "Stand:" and fixes the
/// document's dates, so the same data gives the same bytes.</summary>
public sealed record WorkListData(
    WorkListKind Kind,
    int Year,
    BillingQuarter? Quarter,
    DateOnly MadeOn,
    IReadOnlyList<WorkListCemetery> Cemeteries
);

/// <summary>A cemetery's graves: a stack of pages of its own.</summary>
public sealed record WorkListCemetery(string Name, IReadOnlyList<WorkListGrave> Graves);

/// <summary>A grave on a list. <paramref name="StartInPeriod"/> and
/// <paramref name="EndInPeriod"/> print the month bold; <paramref name="Lines"/>
/// are its services, unused on the graves-only care list.</summary>
public sealed record WorkListGrave(
    string GraveNumber,
    string SiteName,
    string? FieldNumber,
    GravePosition? Position,
    DateOnly Start,
    DateOnly? End,
    bool StartInPeriod,
    bool EndInPeriod,
    string? ContractNumber,
    IReadOnlyList<WorkListLine> Lines
);

/// <summary>A service on a grave. <paramref name="Quarter"/> is printed on
/// the care list ("(Frühling)"); <paramref name="Amount"/> is count × unit
/// gross, null for a service without a price.</summary>
public sealed record WorkListLine(
    int Count,
    string ItemNumber,
    string Description,
    BillingQuarter? Quarter,
    decimal? Amount
);
