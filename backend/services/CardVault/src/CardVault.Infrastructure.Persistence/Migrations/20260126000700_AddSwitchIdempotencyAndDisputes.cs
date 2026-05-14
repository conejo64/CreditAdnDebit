using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CardVault.Infrastructure.Persistence.Migrations
{
    public partial class AddSwitchIdempotencyAndDisputes : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DisputeCases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Network = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Rrn = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    ReasonCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    OpenedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ClosedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table => { table.PrimaryKey("PK_DisputeCases", x => x.Id); });

            migrationBuilder.CreateIndex(
                name: "IX_DisputeCases_AccountId",
                table: "DisputeCases",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_DisputeCases_Network_Rrn",
                table: "DisputeCases",
                columns: new[] { "Network", "Rrn" });

            migrationBuilder.CreateTable(
                name: "TxnJournal",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Network = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Mti = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    Stan = table.Column<string>(type: "character varying(6)", maxLength: 6, nullable: false),
                    Rrn = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    TxnType = table.Column<int>(type: "integer", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric", nullable: false),
                    LedgerEntryId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PostedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table => { table.PrimaryKey("PK_TxnJournal", x => x.Id); });

            migrationBuilder.CreateIndex(
                name: "IX_TxnJournal_AccountId",
                table: "TxnJournal",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_TxnJournal_Network_Mti_Stan_Rrn",
                table: "TxnJournal",
                columns: new[] { "Network", "Mti", "Stan", "Rrn" },
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "TxnJournal");
            migrationBuilder.DropTable(name: "DisputeCases");
        }
    }
}
