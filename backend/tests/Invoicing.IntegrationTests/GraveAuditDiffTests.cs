using Burkard.Invoicing.Api.Audit;
using Burkard.Invoicing.Domain;
using Burkard.Invoicing.Domain.Entities;
using Xunit;

namespace Burkard.Invoicing.IntegrationTests;

/// <summary>What a grave's Verlauf says changed between two snapshots: in
/// domain terms, however the rows were written.</summary>
public class GraveAuditDiffTests
{
    private static readonly (int Year, int Half) CurrentHalf = (2026, 2);

    private static GraveItemSnapshot Grabpflege(
        int count = 1,
        decimal? ownPrice = null,
        decimal? effectivePrice = 100m
    ) => new(11, "1001 Grabpflege", count, ownPrice, effectivePrice);

    private static GraveItemSnapshot Blumen(int count = 2, decimal? ownPrice = 12.5m) =>
        new(12, "2002 Blumenschmuck", count, ownPrice, ownPrice);

    private static GraveRecipientSnapshot Anna() => new(21, "E-0001 Anna Beispiel");

    private static GraveSnapshot Snapshot(
        IReadOnlyList<GraveItemSnapshot>? items = null,
        IReadOnlyList<GraveRecipientSnapshot>? recipients = null,
        IReadOnlyList<GraveCorrectionSnapshot>? corrections = null,
        DateOnly? endDate = null
    ) =>
        new(
            "G-500",
            "F-1",
            "Ostseite",
            "Bergfriedhof",
            GravePosition.SingleInside,
            new DateOnly(2026, 1, 1),
            endDate ?? new DateOnly(2040, 12, 1),
            false,
            null,
            items ?? [Grabpflege(), Blumen()],
            recipients ?? [Anna()],
            corrections ?? []
        );

    [Fact]
    public void Equal_snapshots_give_no_change()
    {
        Assert.Empty(GraveAuditDiff.Diff(Snapshot(), Snapshot(), CurrentHalf));
    }

    [Fact]
    public void A_replaced_item_with_equal_values_is_no_change()
    {
        // SaveGrave deletes and inserts the links: the snapshot compares the
        // item, its count and its price, not the row
        var before = Snapshot(items: [Grabpflege(), Blumen()]);
        var after = Snapshot(items: [Blumen(), Grabpflege()]);

        Assert.Empty(GraveAuditDiff.Diff(before, after, CurrentHalf));
    }

    [Fact]
    public void Count_and_price_changes_are_reported_per_item()
    {
        var before = Snapshot(items: [Grabpflege(count: 1), Blumen(ownPrice: 12.5m)]);
        var after = Snapshot(items: [Grabpflege(count: 2), Blumen(ownPrice: 15m)]);

        Assert.Equal(
            [
                new AuditChange("itemCount", "1001 Grabpflege", "1", "2", "int"),
                new AuditChange("itemPrice", "2002 Blumenschmuck", "12.50", "15.00", "money"),
            ],
            GraveAuditDiff.Diff(before, after, CurrentHalf)
        );
    }

    [Fact]
    public void Added_and_removed_items_and_recipients_are_reported()
    {
        var before = Snapshot(items: [Grabpflege()], recipients: [Anna()]);
        var after = Snapshot(
            items: [Blumen(count: 3, ownPrice: 9m)],
            recipients: [new GraveRecipientSnapshot(22, "E-0002 Bernd Beispiel")]
        );

        Assert.Equal(
            [
                new AuditChange("itemAdded", "2002 Blumenschmuck", null, null, "text"),
                new AuditChange("itemCount", "2002 Blumenschmuck", null, "3", "int"),
                new AuditChange("itemPrice", "2002 Blumenschmuck", null, "9.00", "money"),
                new AuditChange("itemRemoved", "1001 Grabpflege", null, null, "text"),
                new AuditChange("recipientAdded", null, null, "E-0002 Bernd Beispiel", "text"),
                new AuditChange("recipientRemoved", null, "E-0001 Anna Beispiel", null, "text"),
            ],
            GraveAuditDiff.Diff(before, after, CurrentHalf)
        );
    }

    [Fact]
    public void Equal_effective_price_from_another_source_reports_the_source()
    {
        var before = Snapshot(items: [Grabpflege(ownPrice: null, effectivePrice: 100m)]);
        var after = Snapshot(items: [Grabpflege(ownPrice: 100m, effectivePrice: 100m)]);

        Assert.Equal(
            [
                new AuditChange(
                    "itemPriceSource",
                    "1001 Grabpflege",
                    "default",
                    "own",
                    "priceSource"
                ),
            ],
            GraveAuditDiff.Diff(before, after, CurrentHalf)
        );
    }

    [Fact]
    public void Corrections_before_the_current_half_are_ignored()
    {
        GraveCorrectionSnapshot Correction(
            BillingExampleKind kind,
            int? year,
            int half,
            decimal price
        ) => new(kind.ToString(), year, half, 19m, 11, "1001 Grabpflege", price);

        var before = Snapshot(
            corrections:
            [
                Correction(BillingExampleKind.Start, 2026, 1, 50.01m),
                Correction(BillingExampleKind.FullFirstHalf, null, 1, 100.01m),
                Correction(BillingExampleKind.End, 2040, 2, 100.02m),
            ]
        );
        var after = Snapshot(
            corrections:
            [
                // the start block of H1 2026 fell away on its own: no change
                Correction(BillingExampleKind.FullFirstHalf, null, 1, 100.03m),
                Correction(BillingExampleKind.End, 2040, 2, 100.02m),
                Correction(BillingExampleKind.Start, 2026, 2, 49.99m),
            ]
        );

        Assert.Equal(
            [
                new AuditChange(
                    "correction",
                    "1001 Grabpflege",
                    "100.01",
                    "100.03",
                    "money",
                    "FullFirstHalf:--1"
                ),
                new AuditChange(
                    "correction",
                    "1001 Grabpflege",
                    null,
                    "49.99",
                    "money",
                    "Start:2026-2"
                ),
            ],
            GraveAuditDiff.Diff(before, after, CurrentHalf)
        );
    }

    [Fact]
    public void A_changed_field_is_reported_with_its_type()
    {
        var before = Snapshot(endDate: new DateOnly(2040, 12, 1));
        var after = Snapshot(endDate: new DateOnly(2041, 12, 1));

        Assert.Equal(
            [new AuditChange("end", null, "2040-12", "2041-12", "month")],
            GraveAuditDiff.Diff(before, after, CurrentHalf)
        );
    }

    [Fact]
    public void A_null_before_reports_every_value()
    {
        var after = Snapshot(
            corrections:
            [
                new GraveCorrectionSnapshot("End", 2040, 2, 19m, 11, "1001 Grabpflege", 100.02m),
            ]
        );

        Assert.Equal(
            [
                new AuditChange("graveNumber", null, null, "G-500", "text"),
                new AuditChange("fieldNumber", null, null, "F-1", "text"),
                new AuditChange("siteName", null, null, "Ostseite", "text"),
                new AuditChange("cemetery", null, null, "Bergfriedhof", "text"),
                new AuditChange("position", null, null, "SingleInside", "position"),
                new AuditChange("start", null, null, "2026-01", "month"),
                new AuditChange("end", null, null, "2040-12", "month"),
                new AuditChange("itemAdded", "1001 Grabpflege", null, null, "text"),
                new AuditChange("itemCount", "1001 Grabpflege", null, "1", "int"),
                new AuditChange("itemPrice", "1001 Grabpflege", null, "100.00", "money"),
                new AuditChange("itemAdded", "2002 Blumenschmuck", null, null, "text"),
                new AuditChange("itemCount", "2002 Blumenschmuck", null, "2", "int"),
                new AuditChange("itemPrice", "2002 Blumenschmuck", null, "12.50", "money"),
                new AuditChange("recipientAdded", null, null, "E-0001 Anna Beispiel", "text"),
                new AuditChange(
                    "correction",
                    "1001 Grabpflege",
                    null,
                    "100.02",
                    "money",
                    "End:2040-2"
                ),
            ],
            GraveAuditDiff.Diff(null, after, CurrentHalf)
        );
    }
}
