using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CardVault.Infrastructure.Persistence.Migrations
{
    public partial class AddInterestAndDelinquency : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "AverageDailyBalance",
                table: "Statements",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "InterestApr",
                table: "Statements",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "InterestDays",
                table: "Statements",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "PaidAmount",
                table: "Statements",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LateFeeAppliedOn",
                table: "Statements",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "LateFeeAmount",
                table: "Statements",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateIndex(
                name: "IX_Statements_DueDate_Status",
                table: "Statements",
                columns: new[] { "DueDate", "Status" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "IX_Statements_DueDate_Status", table: "Statements");

            migrationBuilder.DropColumn(name: "AverageDailyBalance", table: "Statements");
            migrationBuilder.DropColumn(name: "InterestApr", table: "Statements");
            migrationBuilder.DropColumn(name: "InterestDays", table: "Statements");
            migrationBuilder.DropColumn(name: "PaidAmount", table: "Statements");
            migrationBuilder.DropColumn(name: "LateFeeAppliedOn", table: "Statements");
            migrationBuilder.DropColumn(name: "LateFeeAmount", table: "Statements");
        }
    }
}
