using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Burkard.Invoicing.Api.Migrations
{
    /// <inheritdoc />
    public partial class RemoveDocumentLanguage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "Language", table: "Invoices");

            migrationBuilder.DropColumn(name: "Language", table: "GlobalSettings");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Language",
                table: "Invoices",
                type: "TEXT",
                nullable: false,
                defaultValue: "de"
            );

            migrationBuilder.AddColumn<string>(
                name: "Language",
                table: "GlobalSettings",
                type: "TEXT",
                nullable: false,
                defaultValue: "de"
            );
        }
    }
}
