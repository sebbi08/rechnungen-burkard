using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Burkard.Invoicing.Api.Migrations;

/// <inheritdoc />
public partial class AuditEntries : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "AuditEntries",
            columns: table => new
            {
                Id = table
                    .Column<long>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                OccurredAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                Action = table.Column<string>(type: "TEXT", nullable: false),
                DetailsJson = table.Column<string>(type: "TEXT", nullable: true),
                GraveId = table.Column<long>(type: "INTEGER", nullable: true),
                InvoiceRootId = table.Column<long>(type: "INTEGER", nullable: true),
                ChangesJson = table.Column<string>(type: "TEXT", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AuditEntries", x => x.Id);
            }
        );

        migrationBuilder.CreateIndex(
            name: "IX_AuditEntries_GraveId",
            table: "AuditEntries",
            column: "GraveId"
        );

        migrationBuilder.CreateIndex(
            name: "IX_AuditEntries_InvoiceRootId",
            table: "AuditEntries",
            column: "InvoiceRootId"
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "AuditEntries");
    }
}
