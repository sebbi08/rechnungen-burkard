namespace Burkard.Invoicing.Api.Services;

public class ValidationException : Exception
{
    public string Code { get; }

    public string[]? Args { get; protected init; }

    public ValidationException(string code, string message)
        : base(message)
    {
        Code = code;
    }
}

/// <summary>A price increase a grave's rounding cannot absorb: the first grave
/// that fails and the invoice that does not divide.</summary>
public sealed class PriceIncreaseDoesNotDivideException : ValidationException
{
    public PriceIncreaseDoesNotDivideException(string graveNumber, string invoiceLabel)
        : base("PricesDoNotDivide", "A raised price does not divide among the recipients.")
    {
        Args = [graveNumber, invoiceLabel];
    }
}
