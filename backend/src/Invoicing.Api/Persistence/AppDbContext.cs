using Burkard.Invoicing.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Burkard.Invoicing.Api.Persistence;

public sealed class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<VatRate> VatRates => Set<VatRate>();
    public DbSet<GlobalSettings> GlobalSettings => Set<GlobalSettings>();
    public DbSet<InvoiceItem> InvoiceItems => Set<InvoiceItem>();
    public DbSet<Recipient> Recipients => Set<Recipient>();
    public DbSet<Grave> Graves => Set<Grave>();
    public DbSet<GraveItem> GraveItems => Set<GraveItem>();
    public DbSet<GraveRecipient> GraveRecipients => Set<GraveRecipient>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<InvoiceLine> InvoiceLines => Set<InvoiceLine>();
    public DbSet<Document> Documents => Set<Document>();
    public DbSet<GraveInvoiceRun> GraveInvoiceRuns => Set<GraveInvoiceRun>();
    public DbSet<Job> Jobs => Set<Job>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.Entity<VatRate>().HasIndex(v => v.Rate).IsUnique();
        mb.Entity<InvoiceItem>().HasIndex(i => i.Number).IsUnique();
        mb.Entity<Invoice>().HasIndex(i => new { i.Year, i.Sequence, i.RevisionNumber }).IsUnique();
        mb.Entity<Invoice>().HasIndex(i => i.RootId);
        mb.Entity<InvoiceLine>().HasIndex(l => l.InvoiceId);
        mb.Entity<Document>().HasIndex(d => d.InvoiceId);
        mb.Entity<GraveInvoiceRun>().HasIndex(r => new { r.GraveId, r.Year, r.Half }).IsUnique();
        mb.Entity<Document>().Property(d => d.Kind).HasConversion<int>();
        mb.Entity<Job>().Property(j => j.Status).HasConversion<int>();
        // cascade children when their parent goes
        mb.Entity<Grave>().HasMany(g => g.Items).WithOne().HasForeignKey(i => i.GraveId).OnDelete(DeleteBehavior.Cascade);
        mb.Entity<Grave>().HasMany(g => g.Recipients).WithOne().HasForeignKey(r => r.GraveId).OnDelete(DeleteBehavior.Cascade);
        mb.Entity<Invoice>().HasMany(i => i.Lines).WithOne().HasForeignKey(l => l.InvoiceId).OnDelete(DeleteBehavior.Cascade);
        mb.Entity<Invoice>().HasMany(i => i.Documents).WithOne(d => d.Invoice).HasForeignKey(d => d.InvoiceId).OnDelete(DeleteBehavior.Cascade);
    }
}
