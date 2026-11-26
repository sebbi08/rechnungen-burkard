using System.Text.Encodings.Web;
using System.Text.Json;
using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Api.Services;
using Burkard.Invoicing.Domain;
using Burkard.Invoicing.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Burkard.Invoicing.Api.Audit;

/// <summary>One declared action, open from <see cref="AuditScope.Begin"/> until it
/// is disposed. It may span several SaveChanges calls. Graves are watched:
/// snapshotted before the first write and diffed on completion. Everything
/// else is recorded explicitly by the code that knows the old and new values.
/// <see cref="CompleteAsync"/> writes one entry per subject and action in the
/// transaction of the change, so a refused action leaves none.</summary>
public sealed class AuditAction : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly AuditScope _scope;
    private readonly AppDbContext _db;
    private readonly IClock _clock;
    private readonly AuditGuardMode _mode;
    private readonly ILogger _logger;
    private readonly IReadOnlyDictionary<string, string>? _details;

    // in the order they were first named, so entries are written in that order
    private readonly List<GraveWatch> _watches = new();
    private readonly List<NewGraveWatch> _newGraves = new();
    private readonly List<PendingEntry> _pending = new();

    // what the guard saw this action save; their roots are known only after it
    private readonly HashSet<Invoice> _touchedInvoices = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<long> _touchedInvoiceIds = new();
    private bool _completed;

    internal AuditAction(
        AuditScope scope,
        AppDbContext db,
        IClock clock,
        AuditGuardMode mode,
        ILogger logger,
        string action,
        IReadOnlyDictionary<string, string>? details
    )
    {
        _scope = scope;
        _db = db;
        _clock = clock;
        _mode = mode;
        _logger = logger;
        Name = action;
        _details = details;
    }

    /// <summary>The action's key; subjects take it unless they name their own.</summary>
    public string Name { get; }

    /// <summary>Snapshots the graves as they are now. Call it before the first
    /// write to them; a grave already watched keeps its first snapshot.</summary>
    public async Task WatchGravesAsync(
        IEnumerable<long> graveIds,
        CancellationToken ct,
        string? action = null,
        IReadOnlyDictionary<string, string>? details = null
    )
    {
        // a grave this action created is recorded whole already
        var ids = graveIds
            .Distinct()
            .Where(id => !IsWatched(id) && !_newGraves.Any(n => n.Grave.Id == id))
            .ToList();
        var before = await GraveSnapshots.LoadAsync(_db, ids, ct);
        foreach (var id in ids)
        {
            _watches.Add(new GraveWatch(id, before.GetValueOrDefault(id), action ?? Name, details));
        }
    }

    /// <summary>A grave this action creates: on completion, its every value is
    /// recorded.</summary>
    public void WatchNewGrave(
        Grave grave,
        string? action = null,
        IReadOnlyDictionary<string, string>? details = null
    )
    {
        if (!IsNewGrave(grave))
        {
            _newGraves.Add(new NewGraveWatch(grave, action ?? Name, details));
        }
    }

    /// <summary>Adds changes to the subject's entry for the action; the entry is
    /// written even when <paramref name="changes"/> is empty.</summary>
    public void Record(
        AuditSubject subject,
        IReadOnlyList<AuditChange> changes,
        string? action = null,
        IReadOnlyDictionary<string, string>? details = null
    )
    {
        var entry = Pending(subject, action ?? Name);
        entry.Changes.AddRange(changes);
        entry.MergeDetails(details);
    }

    /// <summary>Writes the entries; call it inside the transaction, right before
    /// committing. Watched graves that did not change get none.</summary>
    public async Task CompleteAsync(CancellationToken ct)
    {
        if (_completed)
        {
            throw new InvalidOperationException(
                $"The audit action '{Name}' has completed already."
            );
        }

        if (_db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException(
                $"The audit action '{Name}' must complete inside the transaction of its changes."
            );
        }

        // the snapshots read the database, so what is still pending goes first;
        // it would be saved together with the entries anyway
        if (_db.ChangeTracker.HasChanges())
        {
            await _db.SaveChangesAsync(ct);
        }

        var currentHalf = HalfYearBilling.HalfYearOf(_clock.GetLocalNow());
        var after = await GraveSnapshots.LoadAsync(
            _db,
            _watches.Select(w => w.GraveId).Concat(_newGraves.Select(n => n.Grave.Id)).ToList(),
            ct
        );
        foreach (var watch in _watches)
        {
            if (after.TryGetValue(watch.GraveId, out var snapshot))
            {
                AddDiff(
                    watch.GraveId,
                    watch.Action,
                    watch.Details,
                    GraveAuditDiff.Diff(watch.Before, snapshot, currentHalf)
                );
            }
        }

        foreach (var created in _newGraves)
        {
            if (after.TryGetValue(created.Grave.Id, out var snapshot))
            {
                AddDiff(
                    created.Grave.Id,
                    created.Action,
                    created.Details,
                    GraveAuditDiff.Diff(null, snapshot, currentHalf)
                );
            }
        }

        var now = _clock.GetUtcNow();
        foreach (var pending in _pending)
        {
            _db.AuditEntries.Add(
                Entry(pending.Subject, pending.Action, pending.Details, pending.Changes, now)
            );
        }

        await _db.SaveChangesAsync(ct);
        await CheckInvoicesRecordedAsync(now, ct);
        _completed = true;
    }

    /// <summary>Closes the action in its scope.</summary>
    public void Dispose() => _scope.End(this);

    internal bool IsWatched(long graveId) => _watches.Any(w => w.GraveId == graveId);

    internal bool IsNewGrave(Grave grave) => _newGraves.Any(n => ReferenceEquals(n.Grave, grave));

    internal void Touched(Invoice invoice) => _touchedInvoices.Add(invoice);

    internal void TouchedInvoiceId(long invoiceId) => _touchedInvoiceIds.Add(invoiceId);

    internal static AuditEntry Entry(
        AuditSubject subject,
        string action,
        IReadOnlyDictionary<string, string>? details,
        IReadOnlyList<AuditChange> changes,
        DateTime occurredAt
    ) =>
        new()
        {
            OccurredAt = occurredAt,
            Action = action,
            DetailsJson = details is { Count: > 0 }
                ? JsonSerializer.Serialize(details, Json)
                : null,
            GraveId = (subject as AuditSubject.Grave)?.GraveId,
            InvoiceRootId = (subject as AuditSubject.Invoice)?.RootId,
            ChangesJson = JsonSerializer.Serialize(changes, Json),
        };

    private void AddDiff(
        long graveId,
        string action,
        IReadOnlyDictionary<string, string>? details,
        IReadOnlyList<AuditChange> changes
    )
    {
        var subject = new AuditSubject.Grave(graveId);
        var existing = _pending.Find(p => p.Subject == subject && p.Action == action);
        if (existing is null && changes.Count == 0)
        {
            return;
        }

        var entry = existing ?? Pending(subject, action);
        entry.Changes.InsertRange(0, changes);
        entry.MergeDetails(details);
    }

    private PendingEntry Pending(AuditSubject subject, string action)
    {
        var entry = _pending.Find(p => p.Subject == subject && p.Action == action);
        if (entry is null)
        {
            entry = new PendingEntry(subject, action);
            // the details given to Begin qualify the action itself, not the
            // subjects that name an action of their own
            if (action == Name)
            {
                entry.MergeDetails(_details);
            }

            _pending.Add(entry);
        }

        return entry;
    }

    /// <summary>Every invoice chain this action changed needs an entry of its
    /// own: the interceptor cannot diff invoices, so the code that writes them
    /// records them.</summary>
    private async Task CheckInvoicesRecordedAsync(DateTime now, CancellationToken ct)
    {
        var roots = _touchedInvoices
            .Select(i => i.RootId != 0 ? i.RootId : i.Id)
            .Where(id => id > 0)
            .ToHashSet();
        if (_touchedInvoiceIds.Count > 0)
        {
            var ids = _touchedInvoiceIds.ToList();
            roots.UnionWith(
                await _db
                    .Invoices.AsNoTracking()
                    .Where(i => ids.Contains(i.Id))
                    .Select(i => i.RootId != 0 ? i.RootId : i.Id)
                    .ToListAsync(ct)
            );
        }

        var recorded = _pending
            .Select(p => p.Subject)
            .OfType<AuditSubject.Invoice>()
            .Select(s => s.RootId)
            .ToHashSet();
        var missing = roots.Where(root => !recorded.Contains(root)).Order().ToList();
        if (missing.Count == 0)
        {
            return;
        }

        var message =
            $"The audit action '{Name}' changed invoices it recorded nothing for: roots {string.Join(", ", missing)}";
        if (_mode == AuditGuardMode.Throw)
        {
            throw new InvalidOperationException(message);
        }

        _logger.LogWarning("{Message}; recorded as {Action}", message, AuditActions.Unattributed);
        foreach (var root in missing)
        {
            _db.AuditEntries.Add(
                Entry(new AuditSubject.Invoice(root), AuditActions.Unattributed, null, [], now)
            );
        }

        await _db.SaveChangesAsync(ct);
    }

    private sealed record GraveWatch(
        long GraveId,
        GraveSnapshot? Before,
        string Action,
        IReadOnlyDictionary<string, string>? Details
    );

    private sealed record NewGraveWatch(
        Grave Grave,
        string Action,
        IReadOnlyDictionary<string, string>? Details
    );

    private sealed class PendingEntry(AuditSubject subject, string action)
    {
        public AuditSubject Subject { get; } = subject;
        public string Action { get; } = action;
        public List<AuditChange> Changes { get; } = new();
        public Dictionary<string, string>? Details { get; private set; }

        public void MergeDetails(IReadOnlyDictionary<string, string>? details)
        {
            if (details is null)
            {
                return;
            }

            Details ??= new Dictionary<string, string>();
            foreach (var (key, value) in details)
            {
                Details[key] = value;
            }
        }
    }
}
