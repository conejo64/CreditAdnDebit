using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CardVault.Infrastructure.Persistence.Migrations
{
    public partial class AddRefundRecordsAndDisputeEvents : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RefundRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Network = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Rrn = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric", nullable: false),
                    Stan = table.Column<string>(type: "character varying(6)", maxLength: 6, nullable: false),
                    LedgerEntryId = table.Column<Guid>(type: "uuid", nullable: true),
                    PostedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table => { table.PrimaryKey("PK_RefundRecords", x => x.Id); });

            migrationBuilder.CreateIndex(
                name: "IX_RefundRecords_AccountId",
                table: "RefundRecords",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_RefundRecords_Network_Rrn_Stan",
                table: "RefundRecords",
                columns: new[] { "Network", "Rrn", "Stan" },
                unique: true);

            migrationBuilder.CreateTable(
                name: "DisputeEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DisputeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Action = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Notes = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    CreatedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table => { table.PrimaryKey("PK_DisputeEvents", x => x.Id); });

            migrationBuilder.CreateIndex(
                name: "IX_DisputeEvents_DisputeId_CreatedOn",
                table: "DisputeEvents",
                columns: new[] { "DisputeId", "CreatedOn" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "DisputeEvents");
            migrationBuilder.DropTable(name: "RefundRecords");
        }
    }
}
