using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Burkard.Invoicing.Api.Migrations
{
    /// <inheritdoc />
    public partial class VatIncludedItemPrices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "PriceIncludesVat",
                table: "InvoiceItems",
                type: "INTEGER",
                nullable: false,
                defaultValue: false
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "PriceIncludesVat", table: "InvoiceItems");
        }
    }
}
