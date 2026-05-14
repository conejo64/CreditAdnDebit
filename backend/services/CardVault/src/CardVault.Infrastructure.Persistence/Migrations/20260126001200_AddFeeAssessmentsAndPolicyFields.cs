using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CardVault.Infrastructure.Persistence.Migrations
{
    public partial class AddFeeAssessmentsAndPolicyFields : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "OverlimitFee",
                table: "CreditPolicies",
                type: "numeric",
                nullable: false,
                defaultValue: 15m);

            migrationBuilder.AddColumn<bool>(
                name: "OverlimitFeeOncePerDay",
                table: "CreditPolicies",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<decimal>(
                name: "AnnualFee",
                table: "CreditPolicies",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "CashAdvanceFeeFixed",
                table: "CreditPolicies",
                type: "numeric",
                nullable: false,
                defaultValue: 5m);

            migrationBuilder.AddColumn<decimal>(
                name: "CashAdvanceFeePercent",
                table: "CreditPolicies",
                type: "numeric",
                nullable: false,
                defaultValue: 0.03m);

            migrationBuilder.CreateTable(
                name: "FeeAssessments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    FeeType = table.Column<int>(type: "integer", nullable: false),
                    BusinessDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric", nullable: false),
                    LedgerEntryId = table.Column<Guid>(type: "uuid", nullable: true),
                    Notes = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    CreatedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table => { table.PrimaryKey("PK_FeeAssessments", x => x.Id); });

            migrationBuilder.CreateIndex(
                name: "IX_FeeAssessments_AccountId_FeeType_BusinessDate",
                table: "FeeAssessments",
                columns: new[] { "AccountId", "FeeType", "BusinessDate" },
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "FeeAssessments");
            migrationBuilder.DropColumn(name: "OverlimitFee", table: "CreditPolicies");
            migrationBuilder.DropColumn(name: "OverlimitFeeOncePerDay", table: "CreditPolicies");
            migrationBuilder.DropColumn(name: "AnnualFee", table: "CreditPolicies");
            migrationBuilder.DropColumn(name: "CashAdvanceFeeFixed", table: "CreditPolicies");
            migrationBuilder.DropColumn(name: "CashAdvanceFeePercent", table: "CreditPolicies");
        }
    }
}
