using System.Text.Json;

namespace Burkard.Invoicing.Api.OData;

/// <summary>Serializer options for the endpoints that build their payload by
/// hand instead of letting the EDM serializer do it. They keep the PascalCase
/// property names the entity payloads use, so the client sees one casing.</summary>
public static class ODataJsonDefaults
{
    public static readonly JsonSerializerOptions PascalCase = new()
    {
        PropertyNamingPolicy = null,
    };
}
