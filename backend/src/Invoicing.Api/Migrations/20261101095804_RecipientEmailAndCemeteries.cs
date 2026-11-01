using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Burkard.Invoicing.Api.Migrations
{
    /// <inheritdoc />
    public partial class RecipientEmailAndCemeteries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // first, so that a database which already ran the unreleased
            // RecipientEmail migration this one replaces stops here, unchanged
            migrationBuilder.AddColumn<string>(
                name: "Email",
                table: "Recipients",
                type: "TEXT",
                nullable: true
            );

            migrationBuilder.CreateTable(
                name: "Cemeteries",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Cemeteries", x => x.Id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_Cemeteries_Name",
                table: "Cemeteries",
                column: "Name",
                unique: true
            );

            // Every grave needs a cemetery, and the ones already stored have
            // theirs only as part of the site name. They get one to stand in
            // until the editor sets the real one; an empty database gets none.
            migrationBuilder.Sql(
                "INSERT INTO Cemeteries (Name) SELECT 'Unbekannt' "
                    + "WHERE EXISTS (SELECT 1 FROM Graves);"
            );

            migrationBuilder.AddColumn<long>(
                name: "CemeteryId",
                table: "Graves",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L
            );

            migrationBuilder.Sql(
                "UPDATE Graves SET CemeteryId = "
                    + "(SELECT Id FROM Cemeteries WHERE Name = 'Unbekannt');"
            );

            migrationBuilder.CreateIndex(
                name: "IX_Graves_CemeteryId",
                table: "Graves",
                column: "CemeteryId"
            );

            migrationBuilder.AddForeignKey(
                name: "FK_Graves_Cemeteries_CemeteryId",
                table: "Graves",
                column: "CemeteryId",
                principalTable: "Cemeteries",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Graves_Cemeteries_CemeteryId",
                table: "Graves"
            );

            migrationBuilder.DropIndex(name: "IX_Graves_CemeteryId", table: "Graves");

            migrationBuilder.DropColumn(name: "CemeteryId", table: "Graves");

            migrationBuilder.DropTable(name: "Cemeteries");

            migrationBuilder.DropColumn(name: "Email", table: "Recipients");
        }
    }
}
