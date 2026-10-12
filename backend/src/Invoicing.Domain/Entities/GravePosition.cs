namespace Burkard.Invoicing.Domain.Entities;

/// <summary>What kind of grave it is and whether it lies inside or outside.
/// Stored by name, so the members can be reordered.
///
/// The first six are what the editor offers. The rest come from the old
/// program's data through the import: every kind inside, outside, or with the
/// side not known (the kind's name alone), and <c>Unknown</c> for a kind that
/// is not known either.</summary>
public enum GravePosition
{
    DoubleInside,
    DoubleOutside,
    SingleInside,
    SingleOutside,
    UrnInside,
    UrnOutside,
    Single,
    Double,
    Urn,
    TripleInside,
    TripleOutside,
    Triple,
    QuadrupleInside,
    QuadrupleOutside,
    Quadruple,
    ChildInside,
    ChildOutside,
    Child,
    LawnInside,
    LawnOutside,
    Lawn,
    LawnUrnInside,
    LawnUrnOutside,
    LawnUrn,
    TreeInside,
    TreeOutside,
    Tree,
    HonoraryInside,
    HonoraryOutside,
    Honorary,
    SlabInside,
    SlabOutside,
    Slab,
    UnknownInside,
    UnknownOutside,
    Unknown,
}

public static class GravePositions
{
    /// <summary>The positions a grave can be given in the editor. A grave
    /// imported with another one keeps it while it is saved unchanged.</summary>
    public static IReadOnlySet<GravePosition> Offered { get; } =
        new HashSet<GravePosition>
        {
            GravePosition.DoubleInside,
            GravePosition.DoubleOutside,
            GravePosition.SingleInside,
            GravePosition.SingleOutside,
            GravePosition.UrnInside,
            GravePosition.UrnOutside,
        };
}
