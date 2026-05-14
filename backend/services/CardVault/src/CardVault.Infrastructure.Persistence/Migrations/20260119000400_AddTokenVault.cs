using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CardVault.Infrastructure.Persistence.Migrations
{
    public partial class AddTokenVault : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TokenVaultEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Token = table.Column<string>(type: "text", nullable: false),
                    KeyId = table.Column<string>(type: "text", nullable: false),
                    NonceB64 = table.Column<string>(type: "text", nullable: false),
                    CiphertextB64 = table.Column<string>(type: "text", nullable: false),
                    TagB64 = table.Column<string>(type: "text", nullable: false),
                    MaskedPan = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Bin = table.Column<string>(type: "character varying(6)", maxLength: 6, nullable: true),
                    CreatedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastAccessedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TokenVaultEntries", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TokenVaultEntries_Token",
                table: "TokenVaultEntries",
                column: "Token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TokenVaultEntries_Bin",
                table: "TokenVaultEntries",
                column: "Bin");

            migrationBuilder.CreateIndex(
                name: "IX_TokenVaultEntries_CreatedOn",
                table: "TokenVaultEntries",
                column: "CreatedOn");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "TokenVaultEntries");
        }
    }
}