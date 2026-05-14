using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CardVault.Infrastructure.Persistence.Migrations
{
    public partial class AddMinimumPaymentPolicies : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MinimumPaymentPolicies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    FloorAmount = table.Column<decimal>(type: "numeric", nullable: false),
                    PrincipalPercent = table.Column<decimal>(type: "numeric", nullable: false),
                    CeilingAmount = table.Column<decimal>(type: "numeric", nullable: true),
                    IncludeInterest = table.Column<bool>(type: "boolean", nullable: false),
                    IncludeFees = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table => { table.PrimaryKey("PK_MinimumPaymentPolicies", x => x.Id); });

            migrationBuilder.CreateIndex(
                name: "IX_MinimumPaymentPolicies_Code",
                table: "MinimumPaymentPolicies",
                column: "Code",
                unique: true);

            // Seed DEFAULT
            migrationBuilder.InsertData(
                table: "MinimumPaymentPolicies",
                columns: new[] { "Id", "Code", "IsDefault", "FloorAmount", "PrincipalPercent", "CeilingAmount", "IncludeInterest", "IncludeFees", "CreatedOn" },
                values: new object[] { Guid.Parse("22222222-2222-2222-2222-222222222222"), "DEFAULT", true, 10m, 0.05m, null, true, true, DateTimeOffset.UtcNow }
            );
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "MinimumPaymentPolicies");
        }
    }
}
