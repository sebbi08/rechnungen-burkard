using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Burkard.Invoicing.Api.Migrations
{
    /// <inheritdoc />
    public partial class GraveSiteFieldsAndGermanOnly : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "Language", table: "Invoices");

            migrationBuilder.DropColumn(name: "Language", table: "GlobalSettings");

            migrationBuilder.AddColumn<string>(
                name: "GraveFieldNumber",
                table: "Invoices",
                type: "TEXT",
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "GraveNumber",
                table: "Invoices",
                type: "TEXT",
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "GravePosition",
                table: "Invoices",
                type: "TEXT",
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "GraveSiteName",
                table: "Invoices",
                type: "TEXT",
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "RecipientNumber",
                table: "Invoices",
                type: "TEXT",
                nullable: true
            );

            migrationBuilder.AlterColumn<DateOnly>(
                name: "EndDate",
                table: "Graves",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(DateOnly),
                oldType: "TEXT"
            );

            migrationBuilder.AddColumn<string>(
                name: "FieldNumber",
                table: "Graves",
                type: "TEXT",
                nullable: false,
                defaultValue: ""
            );

            migrationBuilder.AddColumn<string>(
                name: "Position",
                table: "Graves",
                type: "TEXT",
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "SiteName",
                table: "Graves",
                type: "TEXT",
                nullable: false,
                defaultValue: ""
            );

            migrationBuilder.AddColumn<string>(
                name: "Quarter",
                table: "InvoiceItems",
                type: "TEXT",
                nullable: true
            );

            migrationBuilder.CreateTable(
                name: "GraveBillingCorrections",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    GraveId = table.Column<long>(type: "INTEGER", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", nullable: false),
                    Year = table.Column<int>(type: "INTEGER", nullable: true),
                    Half = table.Column<int>(type: "INTEGER", nullable: false),
                    VatRate = table.Column<decimal>(type: "TEXT", nullable: false),
                    ItemId = table.Column<long>(type: "INTEGER", nullable: false),
                    EnteredPrice = table.Column<decimal>(type: "TEXT", nullable: false),
                    CorrectedPrice = table.Column<decimal>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GraveBillingCorrections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GraveBillingCorrections_Graves_GraveId",
                        column: x => x.GraveId,
                        principalTable: "Graves",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                    table.ForeignKey(
                        name: "FK_GraveBillingCorrections_InvoiceItems_ItemId",
                        column: x => x.ItemId,
                        principalTable: "InvoiceItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict
                    );
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_GraveBillingCorrections_GraveId_Kind_VatRate",
                table: "GraveBillingCorrections",
                columns: new[] { "GraveId", "Kind", "VatRate" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_GraveBillingCorrections_ItemId",
                table: "GraveBillingCorrections",
                column: "ItemId"
            );

            // A grave's term is a month now. DateOnly is stored as 'yyyy-MM-dd'
            // text, so the first eight characters are the month and '01' makes
            // it its first day. Not undone by Down: the day is gone.
            migrationBuilder.Sql(
                "UPDATE Graves SET StartDate = substr(StartDate, 1, 8) || '01', "
                    + "EndDate = CASE WHEN EndDate IS NULL THEN NULL "
                    + "ELSE substr(EndDate, 1, 8) || '01' END;"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "GraveBillingCorrections");

            migrationBuilder.DropColumn(name: "Quarter", table: "InvoiceItems");

            migrationBuilder.DropColumn(name: "GraveFieldNumber", table: "Invoices");

            migrationBuilder.DropColumn(name: "GraveNumber", table: "Invoices");

            migrationBuilder.DropColumn(name: "GravePosition", table: "Invoices");

            migrationBuilder.DropColumn(name: "GraveSiteName", table: "Invoices");

            migrationBuilder.DropColumn(name: "RecipientNumber", table: "Invoices");

            migrationBuilder.DropColumn(name: "FieldNumber", table: "Graves");

            migrationBuilder.DropColumn(name: "Position", table: "Graves");

            migrationBuilder.DropColumn(name: "SiteName", table: "Graves");

            migrationBuilder.AddColumn<string>(
                name: "Language",
                table: "Invoices",
                type: "TEXT",
                nullable: false,
                defaultValue: ""
            );

            migrationBuilder.AlterColumn<DateOnly>(
                name: "EndDate",
                table: "Graves",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1),
                oldClrType: typeof(DateOnly),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "Language",
                table: "GlobalSettings",
                type: "TEXT",
                nullable: false,
                defaultValue: ""
            );
        }
    }
}
