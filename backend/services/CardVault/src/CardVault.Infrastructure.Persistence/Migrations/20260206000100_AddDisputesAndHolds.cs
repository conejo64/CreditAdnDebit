using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CardVault.Infrastructure.Persistence.Migrations
{
    public partial class AddDisputesAndHolds : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DisputeCases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    OriginalTxnJournalId = table.Column<Guid>(type: "uuid", nullable: true),
                    Network = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Stan = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    Rrn = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ReasonCode = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    OriginalAmount = table.Column<decimal>(type: "numeric", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ProvisionalCreditLedgerEntryId = table.Column<Guid>(type: "uuid", nullable: true),
                    OpenedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ResolvedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Notes = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true)
                },
                constraints: table => { table.PrimaryKey("PK_DisputeCases", x => x.Id); });

            migrationBuilder.CreateIndex(
                name: "IX_DisputeCases_AccountId_Network_Rrn_Stan",
                table: "DisputeCases",
                columns: new[] { "AccountId", "Network", "Rrn", "Stan" },
                unique: true);

            migrationBuilder.CreateTable(
                name: "AuthorizationHolds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Network = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Stan = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    Rrn = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    OriginalDataElements90 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Amount = table.Column<decimal>(type: "numeric", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    AuthorizedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CapturedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReleasedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    HoldLedgerEntryId = table.Column<Guid>(type: "uuid", nullable: true),
                    CaptureLedgerEntryId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table => { table.PrimaryKey("PK_AuthorizationHolds", x => x.Id); });

            migrationBuilder.CreateIndex(
                name: "IX_AuthorizationHolds_AccountId_Network_Rrn_Stan",
                table: "AuthorizationHolds",
                columns: new[] { "AccountId", "Network", "Rrn", "Stan" },
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "AuthorizationHolds");
            migrationBuilder.DropTable(name: "DisputeCases");
        }
    }
}
