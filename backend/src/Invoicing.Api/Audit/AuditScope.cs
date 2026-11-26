using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Api.Services;

namespace Burkard.Invoicing.Api.Audit;

/// <summary>The audit action open in this DI scope, if any. Every write path
/// that changes a grave or an invoice opens one around its work
/// (<c>using var audit = scope.Begin(...)</c>); once the scope is armed,
/// <see cref="AuditGuardInterceptor"/> refuses an audited change that none
/// declared.</summary>
public sealed class AuditScope
{
    private readonly AppDbContext _db;
    private readonly IClock _clock;
    private readonly AuditGuardMode _mode;
    private readonly ILogger<AuditScope> _logger;

    public AuditScope(
        AppDbContext db,
        IClock clock,
        AuditGuardMode mode,
        ILogger<AuditScope> logger
    )
    {
        _db = db;
        _clock = clock;
        _mode = mode;
        _logger = logger;
    }

    /// <summary>Whether the guard checks this scope's saves. Requests are armed;
    /// a scope a test seeds through is not.</summary>
    public bool IsArmed { get; private set; }

    public void Arm() => IsArmed = true;

    public AuditAction? Current { get; private set; }

    /// <summary>Opens <paramref name="action"/>; disposing it closes it again.
    /// One action at a time: a second one while the first is open throws.</summary>
    public AuditAction Begin(string action, IReadOnlyDictionary<string, string>? details = null)
    {
        if (Current is not null)
        {
            throw new InvalidOperationException(
                $"The audit action '{Current.Name}' is still open; '{action}' cannot begin inside it."
            );
        }

        Current = new AuditAction(this, _db, _clock, _mode, _logger, action, details);
        return Current;
    }

    internal void End(AuditAction action)
    {
        if (ReferenceEquals(Current, action))
        {
            Current = null;
        }
    }
}
