using Burkard.Invoicing.Api.Services;
using Burkard.Invoicing.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Burkard.Invoicing.Api.Audit;

/// <summary>Refuses, in an armed <see cref="AuditScope"/>, a save that changes a
/// grave or an invoice no audit action declared: with no action open, or to a
/// grave the open action does not watch. In <see cref="AuditGuardMode.Record"/>
/// it logs a warning and writes an <see cref="AuditActions.Unattributed"/> entry
/// per grave and invoice chain instead. The invoices an open action saves are
/// handed to it, so <see cref="AuditAction.CompleteAsync"/> can check that each
/// chain was recorded.
///
/// Scoped: <see cref="AuditScope"/> is resolved from the scope at save time, as
/// it depends on the <c>AppDbContext</c> this interceptor is part of.</summary>
public sealed class AuditGuardInterceptor : SaveChangesInterceptor
{
    private readonly IServiceProvider _services;
    private readonly AuditGuardMode _mode;
    private readonly IClock _clock;
    private readonly ILogger<AuditGuardInterceptor> _logger;

    // rows whose subject has a key the database is still to generate: their
    // unattributed entries are written once the save has run
    private List<EntityEntry>? _deferred;
    private HashSet<AuditSubject>? _written;

    public AuditGuardInterceptor(
        IServiceProvider services,
        AuditGuardMode mode,
        IClock clock,
        ILogger<AuditGuardInterceptor> logger
    )
    {
        _services = services;
        _mode = mode;
        _clock = clock;
        _logger = logger;
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result
    )
    {
        Inspect(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default
    )
    {
        Inspect(eventData.Context);
        return ValueTask.FromResult(result);
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        if (TakeDeferred(eventData.Context))
        {
            eventData.Context!.SaveChanges();
        }

        return result;
    }

    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default
    )
    {
        if (TakeDeferred(eventData.Context))
        {
            await eventData.Context!.SaveChangesAsync(cancellationToken);
        }

        return result;
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData) => Forget();

    public override Task SaveChangesFailedAsync(
        DbContextErrorEventData eventData,
        CancellationToken cancellationToken = default
    )
    {
        Forget();
        return Task.CompletedTask;
    }

    public override void SaveChangesCanceled(DbContextEventData eventData) => Forget();

    public override Task SaveChangesCanceledAsync(
        DbContextEventData eventData,
        CancellationToken cancellationToken = default
    )
    {
        Forget();
        return Task.CompletedTask;
    }

    private void Inspect(DbContext? context)
    {
        Forget();
        if (context is null || _services.GetService<AuditScope>() is not { IsArmed: true } scope)
        {
            return;
        }

        context.ChangeTracker.DetectChanges();
        var audited = context.ChangeTracker.Entries().Where(IsAudited).ToList();
        if (audited.Count == 0)
        {
            return;
        }

        var action = scope.Current;
        if (action is null)
        {
            Refuse(
                context,
                audited,
                $"Audited change without a declared audit action: {TypeNames(audited)}"
            );
            return;
        }

        foreach (var entry in audited)
        {
            if (entry.Entity is Invoice invoice)
            {
                action.Touched(invoice);
            }
            else if (entry.Entity is InvoiceLine)
            {
                var invoiceId = Value(entry, nameof(InvoiceLine.InvoiceId));
                if (TrackedInvoice(context, invoiceId) is { } tracked)
                {
                    action.Touched(tracked);
                }
                else
                {
                    action.TouchedInvoiceId(invoiceId);
                }
            }
        }

        // a new grave's key, and its rows' foreign keys, are temporary values
        // until the save: they are matched through the tracked entry
        var newGraves = context
            .ChangeTracker.Entries<Grave>()
            .Where(e => action.IsNewGrave(e.Entity))
            .Select(e => e.Property(g => g.Id).CurrentValue)
            .ToHashSet();
        bool Watched(long graveId) => action.IsWatched(graveId) || newGraves.Contains(graveId);
        var unwatched = audited
            .Where(entry =>
                entry.Entity switch
                {
                    Grave grave when entry.State == EntityState.Added => !action.IsNewGrave(grave),
                    Grave => !Watched(Value(entry, nameof(Grave.Id))),
                    GraveItem or GraveRecipient or GraveBillingCorrection => !Watched(
                        Value(entry, nameof(GraveItem.GraveId))
                    ),
                    _ => false,
                }
            )
            .ToList();
        if (unwatched.Count > 0)
        {
            Refuse(
                context,
                unwatched,
                $"Audited change to a grave the audit action '{action.Name}' does not watch: {TypeNames(unwatched)}"
            );
        }
    }

    private static bool IsAudited(EntityEntry entry) =>
        entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted
        && entry.Entity switch
        {
            Grave or GraveItem or GraveRecipient or GraveBillingCorrection => true,
            Invoice or InvoiceLine => true,
            InvoiceItem => entry.State == EntityState.Modified
                && entry.Property(nameof(InvoiceItem.DefaultPrice)).IsModified,
            _ => false,
        };

    private void Refuse(DbContext context, IReadOnlyList<EntityEntry> entries, string message)
    {
        if (_mode == AuditGuardMode.Throw)
        {
            throw new InvalidOperationException(message);
        }

        _logger.LogWarning("{Message}; recorded as {Action}", message, AuditActions.Unattributed);
        foreach (var entry in entries)
        {
            if (entry.Properties.Any(p => p.IsTemporary))
            {
                (_deferred ??= new List<EntityEntry>()).Add(entry);
                continue;
            }

            Write(context, SubjectsOf(context, entry));
        }
    }

    private bool TakeDeferred(DbContext? context)
    {
        var deferred = _deferred;
        _deferred = null;
        if (context is null || deferred is null)
        {
            return false;
        }

        return Write(context, deferred.SelectMany(entry => SubjectsOf(context, entry)).ToList());
    }

    /// <summary>Adds an unattributed entry per subject not written yet in this
    /// save; whether there was one.</summary>
    private bool Write(DbContext context, IEnumerable<AuditSubject> subjects)
    {
        _written ??= new HashSet<AuditSubject>();
        var now = _clock.GetUtcNow();
        var added = false;
        foreach (var subject in subjects)
        {
            if (_written.Add(subject))
            {
                context.Add(AuditAction.Entry(subject, AuditActions.Unattributed, null, [], now));
                added = true;
            }
        }

        return added;
    }

    private void Forget()
    {
        _deferred = null;
        _written = null;
    }

    /// <summary>The graves and invoice chains a row belongs to; an item's
    /// default price belongs to neither.</summary>
    private static IEnumerable<AuditSubject> SubjectsOf(DbContext context, EntityEntry entry)
    {
        switch (entry.Entity)
        {
            case Grave:
                yield return new AuditSubject.Grave(Value(entry, nameof(Grave.Id)));
                break;
            case GraveItem or GraveRecipient or GraveBillingCorrection:
                yield return new AuditSubject.Grave(Value(entry, nameof(GraveItem.GraveId)));
                break;
            case Invoice invoice:
                if (invoice.GraveId is { } graveId)
                {
                    yield return new AuditSubject.Grave(graveId);
                }

                yield return new AuditSubject.Invoice(
                    invoice.RootId != 0 ? invoice.RootId : Value(entry, nameof(Invoice.Id))
                );
                break;
            case InvoiceLine:
                var invoiceId = Value(entry, nameof(InvoiceLine.InvoiceId));
                var owner = TrackedInvoice(context, invoiceId) is { } tracked
                    ? new
                    {
                        tracked.Id,
                        tracked.RootId,
                        tracked.GraveId,
                    }
                    : context
                        .Set<Invoice>()
                        .AsNoTracking()
                        .Where(i => i.Id == invoiceId)
                        .Select(i => new
                        {
                            i.Id,
                            i.RootId,
                            i.GraveId,
                        })
                        .SingleOrDefault();
                if (owner is null)
                {
                    break;
                }

                if (owner.GraveId is { } ownerGraveId)
                {
                    yield return new AuditSubject.Grave(ownerGraveId);
                }

                yield return new AuditSubject.Invoice(owner.RootId != 0 ? owner.RootId : owner.Id);
                break;
        }
    }

    private static Invoice? TrackedInvoice(DbContext context, long invoiceId) =>
        context
            .ChangeTracker.Entries<Invoice>()
            .FirstOrDefault(e => e.Property(i => i.Id).CurrentValue == invoiceId)
            ?.Entity;

    // through the entry rather than the entity: a key the database is still to
    // generate is only there, as a temporary value
    private static long Value(EntityEntry entry, string property) =>
        (long)entry.Property(property).CurrentValue!;

    private static string TypeNames(IEnumerable<EntityEntry> entries) =>
        string.Join(", ", entries.Select(e => e.Entity.GetType().Name).Distinct().Order());
}
