using Microsoft.AspNetCore.OData.Deltas;

namespace Burkard.Invoicing.Api.OData;

/// <summary>A row's id is its identity, assigned by the database. A create
/// ignores one in its body -- an existing one collided with its row and ended
/// in a 500 -- and a PATCH that would change it is refused: EF Core cannot
/// change a tracked row's key either, and failed the same way.</summary>
public static class IdReadOnly
{
    public static bool Changes<T>(Delta<T> delta)
        where T : class => delta.GetChangedPropertyNames().Contains("Id");

    public static object Error() =>
        new { error = new { code = "IdReadOnly", message = "A row's id cannot be changed." } };
}
