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
        b.EntitySet<Cemetery>("Cemeteries");
        b.EntitySet<Grave>("Graves");
        b.EntitySet<GraveItem>("GraveItems");
        b.EntitySet<GraveRecipient>("GraveRecipients");
        b.EntitySet<GraveInvoiceRun>("GraveInvoiceRuns");
        b.EntitySet<Invoice>("Invoices");
        b.EntitySet<Document>("Documents");
        // The PDF bytes are served only by DocumentDownloadController's $value
        // route (a plain EF read, independent of the EDM). Leaving Content on the
        // EDM would let one $expand=Documents pull every document's bytes through
        // the query pipeline in a single response.
        b.EntityType<Document>().Ignore(d => d.Content);
        // corrections are stored for generation, never read through OData
        b.EntityType<Grave>().Ignore(g => g.Corrections);
        // a quote's lines are reached as Quotes(key)/Lines; its PDF only through
        // QuoteDownloadController, and the number counters never through OData
        b.EntitySet<Quote>("Quotes");
        b.EntityType<Quote>().Ignore(q => q.Document);
        b.EntitySet<Job>("Jobs");
        b.Singleton<GlobalSettings>("Settings");
        b.Action("CreateInvoice").ReturnsFromEntitySet<Invoice>("Invoices");
        b.Action("CreateRevision").ReturnsFromEntitySet<Invoice>("Invoices");
        b.Action("CreateGraveInvoiceRevision").ReturnsCollectionFromEntitySet<Invoice>("Invoices");
        b.Action("GenerateGraveInvoices").ReturnsFromEntitySet<Job>("Jobs");
        b.Action("SetInvoicePaid").ReturnsFromEntitySet<Invoice>("Invoices");
        b.Action("CreateQuote").ReturnsFromEntitySet<Quote>("Quotes");
        b.Action("CreateQuoteRevision").ReturnsFromEntitySet<Quote>("Quotes");
        return b.GetEdmModel();
    }
}
