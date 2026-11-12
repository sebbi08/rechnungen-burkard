using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Burkard.Invoicing.Api.Migrations
{
    /// <inheritdoc />
    public partial class Quotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "QuoteId",
                table: "Invoices",
                type: "INTEGER",
                nullable: true
            );

            migrationBuilder.CreateTable(
                name: "QuoteNumberCounters",
                columns: table => new
                {
                    Year = table.Column<int>(type: "INTEGER", nullable: false),
                    LastSequence = table.Column<int>(type: "INTEGER", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuoteNumberCounters", x => x.Year);
                }
            );

            migrationBuilder.CreateTable(
                name: "Quotes",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RootId = table.Column<long>(type: "INTEGER", nullable: false),
                    RevisionNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    IsLatestRevision = table.Column<bool>(type: "INTEGER", nullable: false),
                    Year = table.Column<int>(type: "INTEGER", nullable: false),
                    Sequence = table.Column<int>(type: "INTEGER", nullable: false),
                    QuoteNumber = table.Column<string>(type: "TEXT", nullable: false),
                    State = table.Column<string>(type: "TEXT", nullable: false),
                    InvoiceId = table.Column<long>(type: "INTEGER", nullable: true),
                    RecipientId = table.Column<long>(type: "INTEGER", nullable: false),
                    RecipientName = table.Column<string>(type: "TEXT", nullable: false),
                    RecipientAddress = table.Column<string>(type: "TEXT", nullable: false),
                    RecipientNumber = table.Column<string>(type: "TEXT", nullable: true),
                    EndingText = table.Column<string>(type: "TEXT", nullable: false),
                    GeneratedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Quotes", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "QuoteDocuments",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    QuoteId = table.Column<long>(type: "INTEGER", nullable: false),
                    FileName = table.Column<string>(type: "TEXT", nullable: false),
                    Content = table.Column<byte[]>(type: "BLOB", nullable: false),
                    GeneratedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuoteDocuments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QuoteDocuments_Quotes_QuoteId",
                        column: x => x.QuoteId,
                        principalTable: "Quotes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "QuoteLines",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    QuoteId = table.Column<long>(type: "INTEGER", nullable: false),
                    ItemId = table.Column<long>(type: "INTEGER", nullable: true),
                    ItemNumber = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    Count = table.Column<int>(type: "INTEGER", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "TEXT", nullable: false),
                    VatRate = table.Column<decimal>(type: "TEXT", nullable: false),
                    PriceIncludesVat = table.Column<bool>(type: "INTEGER", nullable: false),
                    LineNumber = table.Column<int>(type: "INTEGER", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuoteLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QuoteLines_Quotes_QuoteId",
                        column: x => x.QuoteId,
                        principalTable: "Quotes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_QuoteId",
                table: "Invoices",
                column: "QuoteId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_QuoteDocuments_QuoteId",
                table: "QuoteDocuments",
                column: "QuoteId",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_QuoteLines_QuoteId",
                table: "QuoteLines",
                column: "QuoteId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_Quotes_InvoiceId",
                table: "Quotes",
                column: "InvoiceId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_Quotes_RootId",
                table: "Quotes",
                column: "RootId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_Quotes_Year_Sequence_RevisionNumber",
                table: "Quotes",
                columns: new[] { "Year", "Sequence", "RevisionNumber" },
                unique: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "QuoteDocuments");

            migrationBuilder.DropTable(name: "QuoteLines");

            migrationBuilder.DropTable(name: "QuoteNumberCounters");

            migrationBuilder.DropTable(name: "Quotes");

            migrationBuilder.DropIndex(name: "IX_Invoices_QuoteId", table: "Invoices");

            migrationBuilder.DropColumn(name: "QuoteId", table: "Invoices");
        }
    }
}
