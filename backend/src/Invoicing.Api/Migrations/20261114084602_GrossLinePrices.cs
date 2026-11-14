using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Burkard.Invoicing.Api.Migrations;

/// <inheritdoc />
public partial class GrossLinePrices : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Grave prices are always net from here on. A link that was flagged as
        // a gross is converted first, so it keeps billing what it billed: the
        // net of its own price, or of the item's default standing in for it,
        // taken from the gross rounded to the cent and rounded to the cent
        // again, as VatInclusivePrice.NetOf does. Decimals are stored as text.
        migrationBuilder.Sql(
            """
            UPDATE GraveItems
            SET UnitPrice = printf(
                '%.2f',
                round(
                    round(
                        CAST(
                            COALESCE(
                                UnitPrice,
                                (SELECT i.DefaultPrice FROM InvoiceItems i WHERE i.Id = GraveItems.ItemId)
                            ) AS REAL
                        ),
                        2
                    )
                    / (
                        1.0
                        + CAST(
                            (SELECT i.VatRate FROM InvoiceItems i WHERE i.Id = GraveItems.ItemId) AS REAL
                        ) / 100.0
                    ),
                    2
                )
            )
            WHERE PriceIncludesVat = 1
              AND COALESCE(
                    UnitPrice,
                    (SELECT i.DefaultPrice FROM InvoiceItems i WHERE i.Id = GraveItems.ItemId)
                  ) IS NOT NULL;
            """
        );

        migrationBuilder.DropColumn(name: "PriceIncludesVat", table: "GraveItems");

        migrationBuilder.AddColumn<decimal>(
            name: "GrossUnitPrice",
            table: "QuoteLines",
            type: "TEXT",
            nullable: true
        );

        migrationBuilder.AddColumn<decimal>(
            name: "GrossUnitPrice",
            table: "InvoiceLines",
            type: "TEXT",
            nullable: true
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "GrossUnitPrice", table: "QuoteLines");

        migrationBuilder.DropColumn(name: "GrossUnitPrice", table: "InvoiceLines");

        migrationBuilder.AddColumn<bool>(
            name: "PriceIncludesVat",
            table: "GraveItems",
            type: "INTEGER",
            nullable: false,
            defaultValue: false
        );
    }
}
