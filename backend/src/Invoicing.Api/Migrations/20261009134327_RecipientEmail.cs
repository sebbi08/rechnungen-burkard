using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Burkard.Invoicing.Api.Migrations
{
    /// <inheritdoc />
    public partial class RecipientEmail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Email",
                table: "Recipients",
                type: "TEXT",
                nullable: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "Email", table: "Recipients");
        }
    }
}
