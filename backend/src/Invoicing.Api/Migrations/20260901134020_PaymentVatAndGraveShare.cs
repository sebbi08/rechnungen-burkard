using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Burkard.Invoicing.Api.Migrations
{
    /// <inheritdoc />
    public partial class PaymentVatAndGraveShare : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Invoices_GraveId_IsLatestRevision",
                table: "Invoices"
            );

            migrationBuilder.AddColumn<DateOnly>(
                name: "PaidOn",
                table: "Invoices",
                type: "TEXT",
                nullable: true
            );

            migrationBuilder.AddColumn<int>(
                name: "RecipientCount",
                table: "Invoices",
                type: "INTEGER",
                nullable: true
            );

            migrationBuilder.AddColumn<bool>(
                name: "PriceIncludesVat",
                table: "InvoiceLines",
                type: "INTEGER",
                nullable: false,
                defaultValue: false
            );

            migrationBuilder.AddColumn<bool>(
                name: "PriceIncludesVat",
                table: "GraveItems",
                type: "INTEGER",
                nullable: false,
                defaultValue: false
            );

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_GraveId_IsLatestRevision_PaidOn",
                table: "Invoices",
                columns: new[] { "GraveId", "IsLatestRevision", "PaidOn" }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Invoices_GraveId_IsLatestRevision_PaidOn",
                table: "Invoices"
            );

            migrationBuilder.DropColumn(name: "PaidOn", table: "Invoices");

            migrationBuilder.DropColumn(name: "RecipientCount", table: "Invoices");

            migrationBuilder.DropColumn(name: "PriceIncludesVat", table: "InvoiceLines");

            migrationBuilder.DropColumn(name: "PriceIncludesVat", table: "GraveItems");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_GraveId_IsLatestRevision",
                table: "Invoices",
                columns: new[] { "GraveId", "IsLatestRevision" }
            );
        }
    }
}
