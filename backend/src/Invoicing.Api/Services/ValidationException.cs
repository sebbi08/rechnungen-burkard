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
