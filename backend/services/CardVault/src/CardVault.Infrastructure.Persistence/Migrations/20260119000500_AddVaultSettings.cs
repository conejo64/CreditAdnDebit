using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CardVault.Infrastructure.Persistence.Migrations
{
    public partial class AddVaultSettings : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "VaultSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ActiveKeyId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    UpdatedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastReencryptRunOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastReencryptUpdated = table.Column<int>(type: "integer", nullable: false),
                    LastReencryptStatus = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VaultSettings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_VaultSettings_ActiveKeyId",
                table: "VaultSettings",
                column: "ActiveKeyId");

            migrationBuilder.CreateIndex(
                name: "IX_VaultSettings_UpdatedOn",
                table: "VaultSettings",
                column: "UpdatedOn");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "VaultSettings");
        }
    }
}