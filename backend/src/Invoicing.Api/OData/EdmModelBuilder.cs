using Burkard.Invoicing.Domain.Entities;
using Microsoft.OData.Edm;
using Microsoft.OData.ModelBuilder;

namespace Burkard.Invoicing.Api.OData;

public static class EdmModelBuilder
{
    public static IEdmModel Build()
    {
        var b = new ODataConventionModelBuilder();
        b.EntitySet<VatRate>("VatRates");
        b.EntitySet<InvoiceItem>("InvoiceItems");
        b.EntitySet<Recipient>("Recipients");
        b.EntitySet<Grave>("Graves");
        b.EntitySet<GraveItem>("GraveItems");
        b.EntitySet<GraveRecipient>("GraveRecipients");
        b.EntitySet<Invoice>("Invoices");
        b.EntitySet<Document>("Documents");
        b.EntitySet<Job>("Jobs");
        b.Singleton<GlobalSettings>("Settings");
        return b.GetEdmModel();
    }
}
