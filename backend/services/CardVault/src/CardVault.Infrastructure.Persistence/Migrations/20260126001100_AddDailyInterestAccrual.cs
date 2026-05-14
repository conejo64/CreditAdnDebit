using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CardVault.Infrastructure.Persistence.Migrations
{
    public partial class AddDailyInterestAccrual : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "PurchaseApr",
                table: "CreditPolicies",
                type: "numeric",
                nullable: false,
                defaultValue: 0.35m);

            migrationBuilder.AddColumn<decimal>(
                name: "CashAdvanceApr",
                table: "CreditPolicies",
                type: "numeric",
                nullable: false,
                defaultValue: 0.45m);

            migrationBuilder.AddColumn<decimal>(
                name: "PenaltyApr",
                table: "CreditPolicies",
                type: "numeric",
                nullable: false,
                defaultValue: 0.55m);

            migrationBuilder.AddColumn<int>(
                name: "PurchaseGraceDays",
                table: "CreditPolicies",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "InterestAccrualRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccrualDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Segment = table.Column<int>(type: "integer", nullable: false),
                    BalanceBase = table.Column<decimal>(type: "numeric", nullable: false),
                    Apr = table.Column<decimal>(type: "numeric", nullable: false),
                    DailyRate = table.Column<decimal>(type: "numeric", nullable: false),
                    InterestAmount = table.Column<decimal>(type: "numeric", nullable: false),
                    LedgerEntryId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table => { table.PrimaryKey("PK_InterestAccrualRecords", x => x.Id); });

            migrationBuilder.CreateIndex(
                name: "IX_InterestAccrualRecords_AccountId_AccrualDate_Segment",
                table: "InterestAccrualRecords",
                columns: new[] { "AccountId", "AccrualDate", "Segment" },
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "InterestAccrualRecords");
            migrationBuilder.DropColumn(name: "PurchaseApr", table: "CreditPolicies");
            migrationBuilder.DropColumn(name: "CashAdvanceApr", table: "CreditPolicies");
            migrationBuilder.DropColumn(name: "PenaltyApr", table: "CreditPolicies");
            migrationBuilder.DropColumn(name: "PurchaseGraceDays", table: "CreditPolicies");
        }
    }
}
