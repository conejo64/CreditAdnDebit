using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CardVault.Infrastructure.Persistence.Migrations
{
    public partial class AddPaymentAllocationBuckets : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "PrincipalDue",
                table: "Statements",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "InterestDue",
                table: "Statements",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "FeesDue",
                table: "Statements",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "PaidToPrincipal",
                table: "Statements",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "PaidToInterest",
                table: "Statements",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "PaidToFees",
                table: "Statements",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "PaymentAllocationPolicies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Order = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table => { table.PrimaryKey("PK_PaymentAllocationPolicies", x => x.Id); });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentAllocationPolicies_Code",
                table: "PaymentAllocationPolicies",
                column: "Code",
                unique: true);

            // Seed default policy
            migrationBuilder.InsertData(
                table: "PaymentAllocationPolicies",
                columns: new[] { "Id", "Code", "Order", "IsDefault", "CreatedOn" },
                values: new object[] { Guid.Parse("11111111-1111-1111-1111-111111111111"), "DEFAULT", "Interest,Fees,Principal", true, DateTimeOffset.UtcNow }
            );
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "PaymentAllocationPolicies");

            migrationBuilder.DropColumn(name: "PrincipalDue", table: "Statements");
            migrationBuilder.DropColumn(name: "InterestDue", table: "Statements");
            migrationBuilder.DropColumn(name: "FeesDue", table: "Statements");
            migrationBuilder.DropColumn(name: "PaidToPrincipal", table: "Statements");
            migrationBuilder.DropColumn(name: "PaidToInterest", table: "Statements");
            migrationBuilder.DropColumn(name: "PaidToFees", table: "Statements");
        }
    }
}
