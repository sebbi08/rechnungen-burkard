using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Burkard.Invoicing.Api.Migrations
{
    /// <inheritdoc />
    public partial class GraveItemPriceRecipientNumberAndLanguage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Number",
                table: "Recipients",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            // Every existing recipient needs its own number before the unique
            // index goes on, and the generated numbers must line up with the
            // "E-0001" sequence RecipientNumberService continues.
            migrationBuilder.Sql(
                "UPDATE Recipients SET Number = 'E-' || printf('%04d', Id);");

            migrationBuilder.AddColumn<string>(
                name: "Language",
                table: "Invoices",
                type: "TEXT",
                nullable: false,
                defaultValue: "de");

            // Documents generated before this migration are German, and each
            // invoice keeps the language it was issued in.
            migrationBuilder.Sql("UPDATE Invoices SET Language = 'de';");

            migrationBuilder.AddColumn<decimal>(
                name: "UnitPrice",
                table: "GraveItems",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Language",
                table: "GlobalSettings",
                type: "TEXT",
                nullable: false,
                defaultValue: "de");

            migrationBuilder.Sql("UPDATE GlobalSettings SET Language = 'de';");

            migrationBuilder.CreateIndex(
                name: "IX_Recipients_Number",
                table: "Recipients",
                column: "Number",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Recipients_Number",
                table: "Recipients");

            migrationBuilder.DropColumn(
                name: "Number",
                table: "Recipients");

            migrationBuilder.DropColumn(
                name: "Language",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "UnitPrice",
                table: "GraveItems");

            migrationBuilder.DropColumn(
                name: "Language",
                table: "GlobalSettings");
        }
    }
}
