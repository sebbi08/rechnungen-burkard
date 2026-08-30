using Burkard.Invoicing.Api.Persistence;

namespace Burkard.Invoicing.Api.Services;

/// <summary>Hands out recipient numbers for clients that do not bring their own.
/// Imported archives always carry explicit numbers; the UI does not, so the
/// generated ones continue the same "E-0001" sequence.</summary>
public sealed class RecipientNumberService
{
    public const string Prefix = "E-";

    private readonly AppDbContext _db;

    public RecipientNumberService(AppDbContext db) => _db = db;

    public string Next()
    {
        var used = _db.Recipients.Select(r => r.Number).ToList();
        return Format(NextSequence(used));
    }

    /// <summary>The first free sequence number after every generated number in
    /// <paramref name="existing"/>; numbers in other formats are ignored.</summary>
    public static int NextSequence(IEnumerable<string> existing)
    {
        var highest = 0;
        foreach (var number in existing)
        {
            if (
                number.StartsWith(Prefix, StringComparison.Ordinal)
                && int.TryParse(number.AsSpan(Prefix.Length), out var sequence)
                && sequence > highest
            )
            {
                highest = sequence;
            }
        }

        return highest + 1;
    }

    public static string Format(int sequence) => $"{Prefix}{sequence:D4}";
}
