using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IsoSwitch.Infrastructure.Persistence.Migrations
{
    public partial class TransactionsInDoubt : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "InDoubt",
                table: "Transactions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ReversalAttemptedOn",
                table: "Transactions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ReversalScheduledOn",
                table: "Transactions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReversalStatus",
                table: "Transactions",
                type: "text",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "InDoubt", table: "Transactions");
            migrationBuilder.DropColumn(name: "ReversalAttemptedOn", table: "Transactions");
            migrationBuilder.DropColumn(name: "ReversalScheduledOn", table: "Transactions");
            migrationBuilder.DropColumn(name: "ReversalStatus", table: "Transactions");
        }
    }
}