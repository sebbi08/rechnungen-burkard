namespace Burkard.Invoicing.Domain.Entities;

/// <summary>The quarter an item is billed in, in calendar order: Spring is
/// January to March and Summer April to June (both in the first half-year),
/// Autumn July to September and Winter October to December (both in the
/// second). An item without one is billed all year. Stored by name, like
/// <see cref="GravePosition"/>, so the members can be reordered.</summary>
public enum BillingQuarter
{
    Spring = 1,
    Summer = 2,
    Autumn = 3,
    Winter = 4,
}
