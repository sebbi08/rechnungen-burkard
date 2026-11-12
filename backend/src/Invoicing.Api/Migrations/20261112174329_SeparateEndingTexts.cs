using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Burkard.Invoicing.Api.Migrations
{
    /// <summary>The one ending text becomes four: ordinary invoices keep it, and
    /// quotes and the grave invoices of each half-year start with a copy of it,
    /// so every document reads as before until a text is changed.</summary>
    public partial class SeparateEndingTexts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "EndingText",
                table: "GlobalSettings",
                newName: "InvoiceEndingText"
            );

            migrationBuilder.AddColumn<string>(
                name: "QuoteEndingText",
                table: "GlobalSettings",
                type: "TEXT",
                nullable: false,
                defaultValue: ""
            );

            migrationBuilder.AddColumn<string>(
                name: "GraveFirstHalfEndingText",
                table: "GlobalSettings",
                type: "TEXT",
                nullable: false,
                defaultValue: ""
            );

            migrationBuilder.AddColumn<string>(
                name: "GraveSecondHalfEndingText",
                table: "GlobalSettings",
                type: "TEXT",
                nullable: false,
                defaultValue: ""
            );

            migrationBuilder.Sql(
                "UPDATE GlobalSettings SET QuoteEndingText = InvoiceEndingText, "
                    + "GraveFirstHalfEndingText = InvoiceEndingText, "
                    + "GraveSecondHalfEndingText = InvoiceEndingText"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "QuoteEndingText", table: "GlobalSettings");

            migrationBuilder.DropColumn(name: "GraveFirstHalfEndingText", table: "GlobalSettings");

            migrationBuilder.DropColumn(name: "GraveSecondHalfEndingText", table: "GlobalSettings");

            migrationBuilder.RenameColumn(
                name: "InvoiceEndingText",
                table: "GlobalSettings",
                newName: "EndingText"
            );
        }
    }
}
