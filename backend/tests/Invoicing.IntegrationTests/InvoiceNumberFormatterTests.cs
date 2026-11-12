using Burkard.Invoicing.Domain;
using Xunit;

namespace Burkard.Invoicing.IntegrationTests;

public class InvoiceNumberFormatterTests
{
    [Fact]
    public void Original_number_is_year_dash_sequence_padded_to_4()
    {
        Assert.Equal("2026-0001", InvoiceNumberFormatter.ForOriginal(2026, 1));
        Assert.Equal("2026-0042", InvoiceNumberFormatter.ForOriginal(2026, 42));
    }

    [Fact]
    public void Revision_number_appends_R_suffix()
    {
        Assert.Equal("2026-0001-R1", InvoiceNumberFormatter.ForRevision("2026-0001", 1));
        Assert.Equal("2026-0001-R2", InvoiceNumberFormatter.ForRevision("2026-0001", 2));
    }

    [Fact]
    public void Strip_revision_removes_suffix()
    {
        Assert.Equal("2026-0001", InvoiceNumberFormatter.StripRevision("2026-0001-R2"));
        Assert.Equal("2026-0001", InvoiceNumberFormatter.StripRevision("2026-0001"));
    }

    [Fact]
    public void Quote_numbers_are_A_year_dash_four_digits()
    {
        Assert.Equal("A2026-0001", InvoiceNumberFormatter.ForQuote(2026, 1));
        Assert.Equal("A2026-0001-R2", InvoiceNumberFormatter.ForRevision("A2026-0001", 2));
        Assert.Equal("A2026-0001", InvoiceNumberFormatter.StripRevision("A2026-0001-R2"));
    }
}
