using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CardVault.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCollectionsMutationTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ContactAttempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DelinquencyRecordId = table.Column<Guid>(type: "uuid", nullable: false),
                    Channel = table.Column<int>(type: "integer", nullable: false),
                    Outcome = table.Column<int>(type: "integer", nullable: false),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    AttemptedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    AttemptedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContactAttempts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DelinquencyNotes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DelinquencyRecordId = table.Column<Guid>(type: "uuid", nullable: false),
                    Content = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    CreatedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DelinquencyNotes", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ContactAttempts_DelinquencyRecordId",
                table: "ContactAttempts",
                column: "DelinquencyRecordId");

            migrationBuilder.CreateIndex(
                name: "IX_DelinquencyNotes_DelinquencyRecordId",
                table: "DelinquencyNotes",
                column: "DelinquencyRecordId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ContactAttempts");

            migrationBuilder.DropTable(
                name: "DelinquencyNotes");
        }
    }
}
