using Burkard.Invoicing.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Burkard.Invoicing.Api.Services.Backup;

/// <summary>Names the data the app is showing. A restore gives it a new name, and a
/// page that was loaded before can tell, because it still sends the old one
/// (<see cref="DatabaseGateMiddleware"/>). Kept in the database file's
/// <c>user_version</c>, so it survives a restart, and a database that was never
/// restored simply has 0.</summary>
public sealed class DataGeneration
{
    private volatile int _current;

    public int Current => _current;

    public void Load(AppDbContext db)
    {
        var connection = db.Database.GetDbConnection();
        var wasClosed = connection.State != System.Data.ConnectionState.Open;
        if (wasClosed)
        {
            connection.Open();
        }

        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version";
            _current = Convert.ToInt32(command.ExecuteScalar());
        }
        finally
        {
            if (wasClosed)
            {
                connection.Close();
            }
        }
    }

    /// <summary>Writes a new generation into the database, then makes it the
    /// current one. If the write fails, the current one stays.</summary>
    public void Renew(AppDbContext db)
    {
        int next;
        do
        {
            next = Random.Shared.Next(1, int.MaxValue);
        } while (next == _current);

        // an integer, so nothing to inject; PRAGMA takes no parameters
#pragma warning disable EF1002
        db.Database.ExecuteSqlRaw($"PRAGMA user_version = {next}");
#pragma warning restore EF1002
        _current = next;
    }
}
