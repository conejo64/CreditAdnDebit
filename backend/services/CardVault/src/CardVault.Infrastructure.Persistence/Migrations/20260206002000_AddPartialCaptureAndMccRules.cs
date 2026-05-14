using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CardVault.Infrastructure.Persistence.Migrations
{
    public partial class AddPartialCaptureAndMccRules : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "CapturedAmount",
                table: "AuthorizationHolds",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "MerchantId",
                table: "AuthorizationHolds",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MerchantCategory",
                table: "AuthorizationHolds",
                type: "character varying(8)",
                maxLength: 8,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "FloorLimit",
                table: "CreditPolicies",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<bool>(
                name: "AllowOverlimit",
                table: "CreditPolicies",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "MccRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Mcc = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    IsBlocked = table.Column<bool>(type: "boolean", nullable: false),
                    PerTxnLimit = table.Column<decimal>(type: "numeric", nullable: true),
                    Description = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true)
                },
                constraints: table => { table.PrimaryKey("PK_MccRules", x => x.Id); });

            migrationBuilder.CreateIndex(
                name: "IX_MccRules_Mcc",
                table: "MccRules",
                column: "Mcc",
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "MccRules");
            migrationBuilder.DropColumn(name: "CapturedAmount", table: "AuthorizationHolds");
            migrationBuilder.DropColumn(name: "MerchantId", table: "AuthorizationHolds");
            migrationBuilder.DropColumn(name: "MerchantCategory", table: "AuthorizationHolds");
            migrationBuilder.DropColumn(name: "FloorLimit", table: "CreditPolicies");
            migrationBuilder.DropColumn(name: "AllowOverlimit", table: "CreditPolicies");
        }
    }
}
