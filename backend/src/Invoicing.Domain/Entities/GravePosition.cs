namespace Burkard.Invoicing.Domain.Entities;

/// <summary>What kind of grave it is and whether it lies inside or outside.
/// Stored by name, so the members can be reordered.</summary>
public enum GravePosition
{
    DoubleInside,
    DoubleOutside,
    SingleInside,
    SingleOutside,
    UrnInside,
    UrnOutside,
}
