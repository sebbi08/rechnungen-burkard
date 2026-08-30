using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Burkard.Invoicing.Api.Migrations
{
    /// <inheritdoc />
    public partial class LatestRevisionAndGraveUnitPrice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsLatestRevision",
                table: "Invoices",
                type: "INTEGER",
                nullable: false,
                defaultValue: true
            );

            // Existing chains were written before the flag existed: everything is
            // "latest" by the column default, so only the superseded rows need
            // clearing. GraveUnitPrice is deliberately left null on existing rows —
            // GraveInvoiceGroup falls back to reconstructing it for them.
            migrationBuilder.Sql(
                @"
                UPDATE Invoices SET IsLatestRevision = 0
                WHERE EXISTS (
                    SELECT 1 FROM Invoices later
                    WHERE later.RootId = Invoices.RootId
                      AND later.RevisionNumber > Invoices.RevisionNumber);"
            );

            migrationBuilder.AddColumn<decimal>(
                name: "GraveUnitPrice",
                table: "InvoiceLines",
                type: "TEXT",
                nullable: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_GraveId_IsLatestRevision",
                table: "Invoices",
                columns: new[] { "GraveId", "IsLatestRevision" }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Invoices_GraveId_IsLatestRevision",
                table: "Invoices"
            );

            migrationBuilder.DropColumn(name: "IsLatestRevision", table: "Invoices");

            migrationBuilder.DropColumn(name: "GraveUnitPrice", table: "InvoiceLines");
        }
    }
}
