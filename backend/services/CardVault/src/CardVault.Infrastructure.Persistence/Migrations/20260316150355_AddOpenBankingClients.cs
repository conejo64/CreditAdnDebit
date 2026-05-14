using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CardVault.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOpenBankingClients : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OpenBankingClients",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    SecretHash = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    AllowedScopes = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    AllowAllAccounts = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastTokenIssuedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpenBankingClients", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OpenBankingClientAccountAccesses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientEntityId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpenBankingClientAccountAccesses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OpenBankingClientAccountAccesses_OpenBankingClients_ClientE~",
                        column: x => x.ClientEntityId,
                        principalTable: "OpenBankingClients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OpenBankingClientAccountAccesses_AccountId",
                table: "OpenBankingClientAccountAccesses",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_OpenBankingClientAccountAccesses_ClientEntityId_AccountId",
                table: "OpenBankingClientAccountAccesses",
                columns: new[] { "ClientEntityId", "AccountId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OpenBankingClients_ClientId",
                table: "OpenBankingClients",
                column: "ClientId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OpenBankingClientAccountAccesses");

            migrationBuilder.DropTable(
                name: "OpenBankingClients");
        }
    }
}
