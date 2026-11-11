using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Burkard.Invoicing.Api.Migrations
{
    /// <inheritdoc />
    public partial class GraveContractNumber : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ContractNumber",
                table: "Graves",
                type: "TEXT",
                nullable: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "ContractNumber", table: "Graves");
        }
    }
}
