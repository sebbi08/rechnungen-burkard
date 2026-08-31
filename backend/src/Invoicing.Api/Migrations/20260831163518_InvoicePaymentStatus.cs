using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Burkard.Invoicing.Api.Migrations
{
    /// <inheritdoc />
    public partial class InvoicePaymentStatus : Migration
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

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_GraveId_IsLatestRevision",
                table: "Invoices",
                columns: new[] { "GraveId", "IsLatestRevision" }
            );
        }
    }
}
