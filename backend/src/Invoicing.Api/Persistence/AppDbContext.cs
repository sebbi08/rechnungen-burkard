using Burkard.Invoicing.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Burkard.Invoicing.Api.Persistence;

public sealed class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options) { }

    public DbSet<VatRate> VatRates => Set<VatRate>();
    public DbSet<GlobalSettings> GlobalSettings => Set<GlobalSettings>();
    public DbSet<InvoiceItem> InvoiceItems => Set<InvoiceItem>();
    public DbSet<Recipient> Recipients => Set<Recipient>();
    public DbSet<Cemetery> Cemeteries => Set<Cemetery>();
    public DbSet<Grave> Graves => Set<Grave>();
    public DbSet<GraveItem> GraveItems => Set<GraveItem>();
    public DbSet<GraveRecipient> GraveRecipients => Set<GraveRecipient>();
    public DbSet<GraveBillingCorrection> GraveBillingCorrections => Set<GraveBillingCorrection>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<InvoiceLine> InvoiceLines => Set<InvoiceLine>();
    public DbSet<Document> Documents => Set<Document>();
    public DbSet<GraveInvoiceRun> GraveInvoiceRuns => Set<GraveInvoiceRun>();
    public DbSet<Job> Jobs => Set<Job>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.Entity<VatRate>().HasIndex(v => v.Rate).IsUnique();
        mb.Entity<InvoiceItem>().HasIndex(i => i.Number).IsUnique();
        mb.Entity<Recipient>().HasIndex(r => r.Number).IsUnique();
        // exact duplicates only: SQLite folds ASCII case alone, so the
        // case-insensitive check is CemeteriesController's, in memory
        mb.Entity<Cemetery>().HasIndex(c => c.Name).IsUnique();
        mb.Entity<Invoice>()
            .HasIndex(i => new
            {
                i.Year,
                i.Sequence,
                i.RevisionNumber,
            })
            .IsUnique();
        mb.Entity<Invoice>().HasIndex(i => i.RootId);
        // derived at read time by InvoicesController from the lines and
        // RecipientCount below; never persisted, so no migration for them
        mb.Entity<Invoice>().Ignore(i => i.NetTotal);
        mb.Entity<Invoice>().Ignore(i => i.VatTotal);
        mb.Entity<Invoice>().Ignore(i => i.GrossTotal);
        // the invoice list filters on all three at once; the grave detail page's
        // (GraveId, IsLatestRevision) query still matches the leading columns,
        // so one index serves both
        mb.Entity<Invoice>()
            .HasIndex(i => new
            {
                i.GraveId,
                i.IsLatestRevision,
                i.PaidOn,
            });
        mb.Entity<InvoiceLine>().HasIndex(l => l.InvoiceId);
        mb.Entity<Document>().HasIndex(d => d.InvoiceId);
        mb.Entity<GraveInvoiceRun>()
            .HasIndex(r => new
            {
                r.GraveId,
                r.Year,
                r.Half,
            })
            .IsUnique();
        mb.Entity<Document>().Property(d => d.Kind).HasConversion<int>();
        mb.Entity<Job>().Property(j => j.Status).HasConversion<int>();
        // by name, so a reordered enum never reinterprets stored rows
        mb.Entity<Grave>().Property(g => g.Position).HasConversion<string>();
        mb.Entity<InvoiceItem>().Property(i => i.Quarter).HasConversion<string>();
        mb.Entity<GraveBillingCorrection>().Property(c => c.Kind).HasConversion<string>();
        mb.Entity<Invoice>().Property(i => i.GravePosition).HasConversion<string>();
        // cascade children when their parent goes
        mb.Entity<Grave>()
            .HasMany(g => g.Items)
            .WithOne()
            .HasForeignKey(i => i.GraveId)
            .OnDelete(DeleteBehavior.Cascade);
        mb.Entity<Grave>()
            .HasMany(g => g.Recipients)
            .WithOne()
            .HasForeignKey(r => r.GraveId)
            .OnDelete(DeleteBehavior.Cascade);
        mb.Entity<Invoice>()
            .HasMany(i => i.Lines)
            .WithOne()
            .HasForeignKey(l => l.InvoiceId)
            .OnDelete(DeleteBehavior.Cascade);
        mb.Entity<Invoice>()
            .HasMany(i => i.Documents)
            .WithOne(d => d.Invoice)
            .HasForeignKey(d => d.InvoiceId)
            .OnDelete(DeleteBehavior.Cascade);
        // a grave link must not vanish together with the referenced item/recipient;
        // deleting a referenced row is blocked (handled as a 400 in the controllers)
        mb.Entity<GraveItem>()
            .HasOne(i => i.Item)
            .WithMany()
            .HasForeignKey(i => i.ItemId)
            .OnDelete(DeleteBehavior.Restrict);
        mb.Entity<GraveRecipient>()
            .HasOne(r => r.Recipient)
            .WithMany()
            .HasForeignKey(r => r.RecipientId)
            .OnDelete(DeleteBehavior.Restrict);
        mb.Entity<Grave>()
            .HasOne(g => g.Cemetery)
            .WithMany()
            .HasForeignKey(g => g.CemeteryId)
            .OnDelete(DeleteBehavior.Restrict);
        // one link per (grave, item) / (grave, recipient)
        mb.Entity<GraveItem>().HasIndex(i => new { i.GraveId, i.ItemId }).IsUnique();
        mb.Entity<GraveRecipient>().HasIndex(r => new { r.GraveId, r.RecipientId }).IsUnique();
        // corrections belong to their grave and go with it; the item they raise
        // cannot be deleted while one refers to it
        mb.Entity<Grave>()
            .HasMany(g => g.Corrections)
            .WithOne()
            .HasForeignKey(c => c.GraveId)
            .OnDelete(DeleteBehavior.Cascade);
        mb.Entity<GraveBillingCorrection>()
            .HasOne(c => c.Item)
            .WithMany()
            .HasForeignKey(c => c.ItemId)
            .OnDelete(DeleteBehavior.Restrict);
        mb.Entity<GraveBillingCorrection>()
            .HasIndex(c => new
            {
                c.GraveId,
                c.Kind,
                c.VatRate,
            })
            .IsUnique();
    }
}
