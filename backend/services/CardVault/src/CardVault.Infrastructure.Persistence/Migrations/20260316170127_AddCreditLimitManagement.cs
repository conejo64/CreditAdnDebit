using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CardVault.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCreditLimitManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "AutoIncreaseMinOnTimeRatio",
                table: "CreditPolicies",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "AutoIncreaseMinStatements",
                table: "CreditPolicies",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "AutoIncreaseMinUtilization",
                table: "CreditPolicies",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "AutoIncreasePercent",
                table: "CreditPolicies",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "OverlimitBufferAmount",
                table: "CreditPolicies",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "CreditLimitProposals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    CurrentLimit = table.Column<decimal>(type: "numeric", nullable: false),
                    ProposedIncreaseAmount = table.Column<decimal>(type: "numeric", nullable: false),
                    ProposedLimit = table.Column<decimal>(type: "numeric", nullable: false),
                    OnTimePaymentRatio = table.Column<decimal>(type: "numeric", nullable: false),
                    AverageUtilizationRatio = table.Column<decimal>(type: "numeric", nullable: false),
                    StatementsReviewed = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    DecisionReason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AppliedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CreditLimitProposals", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OverlimitEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    HoldId = table.Column<Guid>(type: "uuid", nullable: true),
                    ApprovedAmount = table.Column<decimal>(type: "numeric", nullable: false),
                    AvailableCreditBefore = table.Column<decimal>(type: "numeric", nullable: false),
                    OverlimitAmount = table.Column<decimal>(type: "numeric", nullable: false),
                    TraceId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OverlimitEvents", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CreditLimitProposals_AccountId_Status_CreatedOn",
                table: "CreditLimitProposals",
                columns: new[] { "AccountId", "Status", "CreatedOn" });

            migrationBuilder.CreateIndex(
                name: "IX_OverlimitEvents_AccountId_CreatedOn",
                table: "OverlimitEvents",
                columns: new[] { "AccountId", "CreatedOn" });

            migrationBuilder.CreateIndex(
                name: "IX_OverlimitEvents_HoldId",
                table: "OverlimitEvents",
                column: "HoldId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CreditLimitProposals");

            migrationBuilder.DropTable(
                name: "OverlimitEvents");

            migrationBuilder.DropColumn(
                name: "AutoIncreaseMinOnTimeRatio",
                table: "CreditPolicies");

            migrationBuilder.DropColumn(
                name: "AutoIncreaseMinStatements",
                table: "CreditPolicies");

            migrationBuilder.DropColumn(
                name: "AutoIncreaseMinUtilization",
                table: "CreditPolicies");

            migrationBuilder.DropColumn(
                name: "AutoIncreasePercent",
                table: "CreditPolicies");

            migrationBuilder.DropColumn(
                name: "OverlimitBufferAmount",
                table: "CreditPolicies");
        }
    }
}
