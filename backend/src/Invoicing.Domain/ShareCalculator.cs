namespace Burkard.Invoicing.Domain;

public static class ShareCalculator
{
    /// <summary>Distributes <paramref name="totalCents"/> evenly across
    /// <paramref name="recipients"/> recipients; the remainder is handed out
    /// one cent at a time to the first recipients so the sum always equals
    /// the total.</summary>
    public static long[] SplitCents(long totalCents, int recipients)
    {
        if (recipients <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(recipients));
        }

        var baseShare = totalCents / recipients;
        var remainder = (int)(totalCents % recipients);
        var shares = new long[recipients];
        for (var i = 0; i < recipients; i++)
        {
            shares[i] = baseShare + (i < remainder ? 1 : 0);
        }

        return shares;
    }
}
